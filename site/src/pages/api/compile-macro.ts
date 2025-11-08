import type { APIRoute } from 'astro';
import { exec } from 'child_process';
import { promisify } from 'util';
import { writeFile, readFile, mkdir, rm } from 'fs/promises';
import { join } from 'path';
import { tmpdir } from 'os';
import { randomBytes } from 'crypto';

const execAsync = promisify(exec);

interface CompileRequest {
  macroText: string;
  macroName: string;
  mcu: string;
  loop: boolean;
  savedMacros: Record<string, string>;
}

export const POST: APIRoute = async ({ request }) => {
  let tempDir: string | null = null;

  try {
    const body: CompileRequest = await request.json();
    const { macroText, macroName, mcu, loop, savedMacros } = body;

    if (!macroText || !macroName) {
      return new Response(
        JSON.stringify({ message: 'Macro text and name are required' }),
        { status: 400, headers: { 'Content-Type': 'application/json' } }
      );
    }

    // Validate MCU
    const validMCUs = ['atmega16u2', 'at90usb1286', 'atmega32u4'];
    if (!validMCUs.includes(mcu)) {
      return new Response(JSON.stringify({ message: 'Invalid MCU type' }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    // Create temporary directory
    const tempId = randomBytes(16).toString('hex');
    tempDir = join(tmpdir(), `nx-controller-${tempId}`);
    await mkdir(tempDir, { recursive: true });

    // Resolve includes by replacing @macroname with actual macro content
    let resolvedMacro = macroText;
    const includePattern = /@([a-zA-Z0-9_-]+)(?:,\*(\d+))?/g;
    const matches = Array.from(macroText.matchAll(includePattern));

    for (const match of matches) {
      const [fullMatch, includeName, loopCount] = match;
      const includedMacro = savedMacros[includeName];

      if (!includedMacro) {
        return new Response(
          JSON.stringify({
            message: `Referenced macro "${includeName}" not found in saved macros`,
          }),
          { status: 400, headers: { 'Content-Type': 'application/json' } }
        );
      }

      // Save included macro to temp file
      const includePath = join(tempDir, `${includeName}.macro`);
      await writeFile(includePath, includedMacro, 'utf-8');

      // Replace the reference with relative path
      const replacement = loopCount
        ? `@${includePath},*${loopCount}`
        : `@${includePath}`;
      resolvedMacro = resolvedMacro.replace(fullMatch, replacement);
    }

    // Write main macro file
    const macroPath = join(tempDir, 'macro.macro');
    await writeFile(macroPath, resolvedMacro, 'utf-8');

    // Get firmware directory
    // In development: process.cwd() is /path/to/site, so we go up one level
    // In production (Docker): process.cwd() is /app, and firmware is at /app/firmware
    // Check if we're in Docker by looking for /app directory
    const isDocker = process.cwd() === '/app';
    const firmwareDir = isDocker
      ? join(process.cwd(), 'firmware')
      : join(process.cwd(), '..', 'firmware');

    // Debug logging
    console.log('Environment:', process.env.NODE_ENV);
    console.log('process.cwd():', process.cwd());
    console.log('isDocker:', isDocker);
    console.log('firmwareDir:', firmwareDir);
    console.log('macro_to_c.py path:', join(firmwareDir, 'macro_to_c.py'));

    // Run macro_to_c.py to validate and convert
    const pythonCmd = `python3 "${join(firmwareDir, 'macro_to_c.py')}" "${macroPath}" ${loop ? '--loop' : ''} -o "${join(tempDir, 'embedded_macro.h')}"`;

    console.log('Python command:', pythonCmd);

    try {
      await execAsync(pythonCmd);
      // If we get here, validation succeeded
    } catch (error) {
      const err = error as {
        stderr?: string;
        stdout?: string;
        message?: string;
      };
      // Python script outputs errors to stderr
      const errorOutput =
        err.stderr || err.message || 'Unknown validation error';

      // Filter out informational messages (like "Generated ...")
      const errorLines = errorOutput.split('\n').filter((line) => {
        const trimmed = line.trim();
        return (
          trimmed &&
          !trimmed.startsWith('Generated ') &&
          !trimmed.startsWith('Traceback') &&
          trimmed !== 'None'
        );
      });

      const cleanError =
        errorLines.length > 0
          ? errorLines.join('\n')
          : 'Macro validation failed. Please check your syntax.';

      return new Response(
        JSON.stringify({
          message: `Macro validation error:\n${cleanError}`,
        }),
        { status: 400, headers: { 'Content-Type': 'application/json' } }
      );
    }

    // Copy embedded_macro.h to firmware directory
    // Add the EMBEDDED_MACRO_ENABLED define at the top of the file
    const embeddedMacroContent = await readFile(
      join(tempDir, 'embedded_macro.h'),
      'utf-8'
    );
    const contentWithDefine = `#ifndef EMBEDDED_MACRO_ENABLED
#define EMBEDDED_MACRO_ENABLED 1
#endif

${embeddedMacroContent}`;
    await writeFile(
      join(firmwareDir, 'embedded_macro.h'),
      contentWithDefine,
      'utf-8'
    );

    // Build standalone firmware
    // We build the JoystickStandalone target directly since we already generated embedded_macro.h
    // Don't override CC_FLAGS - let the Makefile use its existing flags for LUFA compatibility
    const makeCmd = `make -C "${firmwareDir}" TARGET=JoystickStandalone MCU=${mcu} all`;

    try {
      await execAsync(makeCmd);
      // Build succeeded
    } catch (error) {
      const err = error as {
        stderr?: string;
        stdout?: string;
        message?: string;
        code?: number;
      };
      const buildOutput =
        err.stderr || err.stdout || err.message || 'Unknown build error';

      // Log full output for debugging (server-side only)
      console.error('Build failed. Full output:', buildOutput);
      console.error('Firmware dir:', firmwareDir);
      console.error('Command:', makeCmd);

      // Split into lines and look for meaningful error context
      const lines = buildOutput.split('\n');
      const errorLines: string[] = [];

      // Find lines with actual error information (user errors)
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const lower = line.toLowerCase();

        // Look for compiler errors that indicate user syntax issues
        if (
          lower.includes('error:') ||
          lower.includes('undefined reference') ||
          (lower.includes('error') && lower.includes('.c:')) ||
          lower.includes('fatal error')
        ) {
          // Include this line and potentially the next few for context
          errorLines.push(line);
          if (i + 1 < lines.length && lines[i + 1].trim()) {
            errorLines.push(lines[i + 1]);
          }
        }
      }

      // Check if this looks like a system/setup issue vs user error
      // System errors include: missing tools, firmware code issues, library mismatches
      // User errors are in embedded_macro.h (the generated file from their macro)
      const isSystemError =
        errorLines.length === 0 ||
        buildOutput.includes('No such file or directory') ||
        buildOutput.includes('command not found') ||
        buildOutput.includes('Permission denied') ||
        buildOutput.includes('conflicting types') ||
        buildOutput.includes('too few arguments') ||
        buildOutput.includes('too many arguments') ||
        buildOutput.includes('JoystickStandalone.c:') ||
        buildOutput.includes('Joystick.c:') ||
        buildOutput.includes('Descriptors.c:') ||
        (!buildOutput.includes('embedded_macro.h:') &&
          buildOutput.includes('.c:'));

      if (isSystemError) {
        // Server-side issue, not user's fault
        return new Response(
          JSON.stringify({
            message:
              'Oops! Something went wrong on our side. Please try again later.',
          }),
          { status: 500, headers: { 'Content-Type': 'application/json' } }
        );
      }

      // User error - show the actual compiler errors
      const cleanError = errorLines.slice(0, 15).join('\n');

      return new Response(
        JSON.stringify({
          message: `Compilation error:\n${cleanError}`,
        }),
        { status: 400, headers: { 'Content-Type': 'application/json' } }
      );
    }

    // Read the compiled hex file
    const hexPath = join(firmwareDir, 'JoystickStandalone.hex');
    const hexContent = await readFile(hexPath, 'utf-8');

    // Clean up firmware directory
    try {
      await rm(join(firmwareDir, 'embedded_macro.h'));
    } catch {
      // Ignore cleanup errors
    }

    // Return the hex file
    return new Response(hexContent, {
      status: 200,
      headers: {
        'Content-Type': 'application/octet-stream',
        'Content-Disposition': `attachment; filename="${macroName}-${mcu}.hex"`,
      },
    });
  } catch (error) {
    console.error('Compilation error:', error);
    return new Response(
      JSON.stringify({
        message: error instanceof Error ? error.message : 'Unknown error',
      }),
      { status: 500, headers: { 'Content-Type': 'application/json' } }
    );
  } finally {
    // Clean up temp directory
    if (tempDir) {
      try {
        await rm(tempDir, { recursive: true, force: true });
      } catch (error) {
        console.error('Failed to clean up temp directory:', error);
      }
    }
  }
};
