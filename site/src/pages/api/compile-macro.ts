import type { APIRoute } from 'astro';
import { exec } from 'child_process';
import { promisify } from 'util';
import { writeFile, readFile, mkdir, rm, cp } from 'fs/promises';
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
  let tempBuildDir: string | null = null;

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

    // Get source firmware directory
    const isDocker = process.cwd() === '/app';
    const sourceFirmwareDir = isDocker
      ? join(process.cwd(), 'firmware')
      : join(process.cwd(), '..', 'firmware');

    // Create isolated temporary build directory for this request
    const tempId = randomBytes(16).toString('hex');
    tempBuildDir = join(tmpdir(), `nx-controller-build-${tempId}`);
    await mkdir(tempBuildDir, { recursive: true });

    // Create macros subdirectory for included macros
    const macrosDir = join(tempBuildDir, 'macros');
    await mkdir(macrosDir, { recursive: true });

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

      // Save included macro to macros subdirectory
      const includePath = join(macrosDir, `${includeName}.macro`);
      await writeFile(includePath, includedMacro, 'utf-8');

      // Replace the reference with relative path
      const replacement = loopCount
        ? `@${includePath},*${loopCount}`
        : `@${includePath}`;
      resolvedMacro = resolvedMacro.replace(fullMatch, replacement);
    }

    // Write main macro file
    const macroPath = join(tempBuildDir, 'macro.macro');
    await writeFile(macroPath, resolvedMacro, 'utf-8');

    // Copy all necessary firmware files to isolated build directory
    console.log('Copying firmware files to isolated build directory...');
    const filesToCopy = [
      'Makefile',
      'macro_to_c.py',
      'JoystickStandalone.c',
      'Joystick.c',
      'Joystick.h',
      'Descriptors.c',
      'Descriptors.h',
      'EmulatedSPI.c',
      'EmulatedSPI.h',
      'Response.c',
      'Response.h',
      'avr.h',
      'datatypes.h',
    ];

    for (const file of filesToCopy) {
      await cp(
        join(sourceFirmwareDir, file),
        join(tempBuildDir, file),
        { recursive: false }
      );
    }

    // Copy LUFA and Config directories recursively
    await cp(
      join(sourceFirmwareDir, 'LUFA'),
      join(tempBuildDir, 'LUFA'),
      { recursive: true }
    );
    await cp(
      join(sourceFirmwareDir, 'Config'),
      join(tempBuildDir, 'Config'),
      { recursive: true }
    );

    console.log('Firmware files copied successfully');

    // Run macro_to_c.py to validate and convert
    const pythonCmd = `python3 "${join(tempBuildDir, 'macro_to_c.py')}" "${macroPath}" ${loop ? '--loop' : ''} -o "${join(tempBuildDir, 'embedded_macro.h')}"`;

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

    // Build standalone firmware in isolated directory
    // Set BOARD parameter based on MCU for proper LED configuration
    const board = mcu === 'atmega16u2' ? 'UNO' : 'NONE';

    // Pass CC_FLAGS with EMBEDDED_MACRO_ENABLED as a compiler flag
    // This is required because JoystickStandalone.c checks #ifdef EMBEDDED_MACRO_ENABLED before including embedded_macro.h
    const makeCmd = `make -C "${tempBuildDir}" TARGET=JoystickStandalone MCU=${mcu} BOARD=${board} CC_FLAGS="-DUSE_LUFA_CONFIG_HEADER -IConfig/ -DEMBEDDED_MACRO_ENABLED=1" all`;

    console.log('Build command:', makeCmd);

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
      console.error('Build dir:', tempBuildDir);
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

    // Read the compiled hex file from isolated build directory
    const hexPath = join(tempBuildDir, 'JoystickStandalone.hex');
    const hexContent = await readFile(hexPath, 'utf-8');

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
    // Clean up isolated build directory
    if (tempBuildDir) {
      try {
        await rm(tempBuildDir, { recursive: true, force: true });
      } catch (error) {
        console.error('Failed to clean up build directory:', error);
      }
    }
  }
};
