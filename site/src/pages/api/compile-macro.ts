import type { APIRoute } from 'astro';
import { exec } from 'child_process';
import { promisify } from 'util';
import { writeFile, readFile, mkdir, rm, cp } from 'fs/promises';
import { join } from 'path';
import { tmpdir } from 'os';
import { randomBytes } from 'crypto';
import {
  parseMacroFrontmatter,
  applyMacroConfig,
  type MacroConfigOption,
} from '../../lib/macro-frontmatter';

const execAsync = promisify(exec);

interface CompileRequest {
  macroText: string;
  macroName: string;
  platform: 'avr' | 'pico' | 'esp32s3';
  mcu?: string; // Only for AVR
  loop: boolean;
  savedMacros: Record<string, string>;
  config?: MacroConfigOption[];
  configValues?: Record<string, string | number>;
}

export const POST: APIRoute = async ({ request }) => {
  let tempBuildDir: string | null = null;

  try {
    const body: CompileRequest = await request.json();
    const {
      macroText,
      macroName,
      platform,
      mcu,
      loop,
      savedMacros,
      config: clientConfig,
      configValues,
    } = body;

    if (!macroText || !macroName) {
      return new Response(
        JSON.stringify({ message: 'Macro text and name are required' }),
        { status: 400, headers: { 'Content-Type': 'application/json' } }
      );
    }

    // Validate platform
    const validPlatforms = ['avr', 'pico', 'esp32s3'];
    if (!validPlatforms.includes(platform)) {
      return new Response(JSON.stringify({ message: 'Invalid platform' }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    // Validate MCU/board for platforms that require it
    if (platform === 'avr') {
      const validMCUs = ['atmega16u2', 'at90usb1286', 'atmega32u4'];
      if (!mcu || !validMCUs.includes(mcu)) {
        return new Response(
          JSON.stringify({ message: 'Invalid MCU type for AVR platform' }),
          {
            status: 400,
            headers: { 'Content-Type': 'application/json' },
          }
        );
      }
    } else if (platform === 'pico') {
      const validBoards = ['pico', 'pico_w', 'pico2', 'pico2_w'];
      if (!mcu || !validBoards.includes(mcu)) {
        return new Response(
          JSON.stringify({ message: 'Invalid board type for Pico platform' }),
          {
            status: 400,
            headers: { 'Content-Type': 'application/json' },
          }
        );
      }
    }

    // Get source firmware directory
    const isDocker = process.cwd() === '/app';
    const sourceFirmwareDir = isDocker
      ? join(process.cwd(), 'firmware')
      : join(process.cwd(), '..', 'firmware');

    // Create isolated temporary build directory
    const tempId = randomBytes(16).toString('hex');
    tempBuildDir = join(tmpdir(), `nx-controller-build-${tempId}`);
    await mkdir(tempBuildDir, { recursive: true });

    // Create macros subdirectory for included macros
    const macrosDir = join(tempBuildDir, 'macros');
    await mkdir(macrosDir, { recursive: true });

    // Apply config substitutions if present
    // Prefer client-provided config (frontmatter is already stripped by get-macro),
    // fall back to parsing frontmatter from the text (e.g. custom editor)
    const parsed = parseMacroFrontmatter(macroText);
    const config = clientConfig ?? parsed.config;
    let resolvedMacro = parsed.body;

    if (config.length > 0) {
      try {
        resolvedMacro = applyMacroConfig(
          resolvedMacro,
          config,
          configValues ?? {}
        );
      } catch (error) {
        return new Response(
          JSON.stringify({
            message:
              error instanceof Error
                ? error.message
                : 'Config substitution failed',
          }),
          { status: 400, headers: { 'Content-Type': 'application/json' } }
        );
      }
    }

    // Resolve includes
    const includePattern = /@([a-zA-Z0-9_-]+)(?:\.macro)?(?:,\*(\d+))?/g;
    const matches = Array.from(resolvedMacro.matchAll(includePattern));

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

      const includePath = join(macrosDir, `${includeName}.macro`);
      await writeFile(includePath, includedMacro, 'utf-8');

      const replacement = loopCount
        ? `@${includePath},*${loopCount}`
        : `@${includePath}`;
      resolvedMacro = resolvedMacro.replace(fullMatch, replacement);
    }

    // Write main macro file
    const macroPath = join(tempBuildDir, 'macro.macro');
    await writeFile(macroPath, resolvedMacro, 'utf-8');

    // Platform-specific build logic
    if (platform === 'avr') {
      return await buildAVR(
        tempBuildDir,
        sourceFirmwareDir,
        macroPath,
        macroName,
        mcu!,
        loop
      );
    } else if (platform === 'pico') {
      return await buildPico(
        tempBuildDir,
        sourceFirmwareDir,
        macroPath,
        macroName,
        mcu!,
        loop
      );
    } else if (platform === 'esp32s3') {
      return await buildESP32S3(
        tempBuildDir,
        sourceFirmwareDir,
        macroPath,
        macroName,
        loop
      );
    }

    throw new Error('Invalid platform');
  } catch (error) {
    console.error('Compilation error:', error);
    return new Response(
      JSON.stringify({
        message: error instanceof Error ? error.message : 'Unknown error',
      }),
      { status: 500, headers: { 'Content-Type': 'application/json' } }
    );
  } finally {
    if (tempBuildDir) {
      try {
        await rm(tempBuildDir, { recursive: true, force: true });
      } catch (error) {
        console.error('Failed to clean up build directory:', error);
      }
    }
  }
};

async function buildAVR(
  tempBuildDir: string,
  sourceFirmwareDir: string,
  macroPath: string,
  macroName: string,
  mcu: string,
  loop: boolean
): Promise<Response> {
  console.log('Building for AVR platform...');

  // Copy AVR firmware files
  const avrSourceDir = join(sourceFirmwareDir, 'avr');
  const filesToCopy = [
    'Makefile',
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
    await cp(join(avrSourceDir, file), join(tempBuildDir, file));
  }

  // Copy LUFA and Config directories
  await cp(join(sourceFirmwareDir, 'LUFA'), join(tempBuildDir, 'LUFA'), {
    recursive: true,
  });
  await cp(join(avrSourceDir, 'Config'), join(tempBuildDir, 'Config'), {
    recursive: true,
  });

  // Copy shared directory
  await cp(join(sourceFirmwareDir, 'shared'), join(tempBuildDir, 'shared'), {
    recursive: true,
  });

  // Copy macro_to_c.py from firmware root
  await cp(
    join(sourceFirmwareDir, 'macro_to_c.py'),
    join(tempBuildDir, 'macro_to_c.py')
  );

  // Generate embedded_macro.h
  const pythonCmd = `python3 "${join(tempBuildDir, 'macro_to_c.py')}" "${macroPath}" ${loop ? '--loop' : ''} -o "${join(tempBuildDir, 'embedded_macro.h')}"`;

  try {
    await execAsync(pythonCmd);
  } catch (error) {
    return handleValidationError(error);
  }

  // Build firmware
  const board = mcu === 'atmega16u2' ? 'UNO' : 'NONE';
  const makeCmd = `make -C "${tempBuildDir}" TARGET=JoystickStandalone MCU=${mcu} BOARD=${board} CC_FLAGS="-DUSE_LUFA_CONFIG_HEADER -IConfig/ -Ishared -DEMBEDDED_MACRO_ENABLED=1 -include embedded_macro.h" SRC='JoystickStandalone.c Descriptors.c shared/macro_player.c shared/emulated_spi.c $(LUFA_SRC_USB)' LUFA_PATH="LUFA/LUFA" all`;

  try {
    await execAsync(makeCmd);
  } catch (error) {
    return handleBuildError(error, tempBuildDir, makeCmd);
  }

  // Read and return hex file
  const hexPath = join(tempBuildDir, 'JoystickStandalone.hex');
  const hexContent = await readFile(hexPath, 'utf-8');

  return new Response(hexContent, {
    status: 200,
    headers: {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': `attachment; filename="${macroName}-${mcu}.hex"`,
    },
  });
}

async function buildPico(
  tempBuildDir: string,
  sourceFirmwareDir: string,
  macroPath: string,
  macroName: string,
  board: string,
  loop: boolean
): Promise<Response> {
  console.log(`Building for Pico platform (board: ${board})...`);

  // Copy Pico firmware and shared directories
  await cp(join(sourceFirmwareDir, 'pico'), join(tempBuildDir, 'pico'), {
    recursive: true,
  });
  await cp(join(sourceFirmwareDir, 'shared'), join(tempBuildDir, 'shared'), {
    recursive: true,
  });

  // Copy macro_to_c.py
  await cp(
    join(sourceFirmwareDir, 'macro_to_c.py'),
    join(tempBuildDir, 'macro_to_c.py')
  );

  // Generate embedded_macro.h in pico directory
  const pythonCmd = `python3 "${join(tempBuildDir, 'macro_to_c.py')}" "${macroPath}" ${loop ? '--loop' : ''} -o "${join(tempBuildDir, 'pico', 'embedded_macro.h')}"`;

  try {
    await execAsync(pythonCmd);
  } catch (error) {
    return handleValidationError(error);
  }

  // Build with CMake
  const buildDir = join(tempBuildDir, 'pico', 'build');
  await mkdir(buildDir, { recursive: true });

  const cmakeCmd = `cd "${buildDir}" && cmake .. -DCMAKE_BUILD_TYPE=Release -DPICO_BOARD=${board}`;
  const makeCmd = `make -C "${buildDir}" -j$(nproc) nx_standalone`;

  try {
    await execAsync(cmakeCmd);
    await execAsync(makeCmd);
  } catch (error) {
    return handleBuildError(error, tempBuildDir, `${cmakeCmd} && ${makeCmd}`);
  }

  // Read and return uf2 file
  const uf2Path = join(buildDir, 'nx_standalone.uf2');
  const uf2Content = await readFile(uf2Path);

  return new Response(uf2Content, {
    status: 200,
    headers: {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': `attachment; filename="${macroName}-${board}.uf2"`,
    },
  });
}

async function buildESP32S3(
  tempBuildDir: string,
  sourceFirmwareDir: string,
  macroPath: string,
  macroName: string,
  loop: boolean
): Promise<Response> {
  console.log('Building for ESP32-S3 platform...');

  // Copy ESP32-S3 firmware and shared directories
  await cp(join(sourceFirmwareDir, 'esp32s3'), join(tempBuildDir, 'esp32s3'), {
    recursive: true,
  });
  await cp(join(sourceFirmwareDir, 'shared'), join(tempBuildDir, 'shared'), {
    recursive: true,
  });

  // Copy macro_to_c.py
  await cp(
    join(sourceFirmwareDir, 'macro_to_c.py'),
    join(tempBuildDir, 'macro_to_c.py')
  );

  // Generate embedded_macro.h in esp32s3/main directory
  const pythonCmd = `python3 "${join(tempBuildDir, 'macro_to_c.py')}" "${macroPath}" ${loop ? '--loop' : ''} -o "${join(tempBuildDir, 'esp32s3', 'main', 'embedded_macro.h')}"`;

  try {
    await execAsync(pythonCmd);
  } catch (error) {
    return handleValidationError(error);
  }

  // Build with ESP-IDF
  const esp32Dir = join(tempBuildDir, 'esp32s3');
  const buildCmd = `cd "${esp32Dir}" && . $IDF_PATH/export.sh && idf.py set-target esp32s3 && idf.py build 2>&1`;

  try {
    await execAsync(buildCmd, {
      shell: '/bin/bash',
      maxBuffer: 10 * 1024 * 1024,
    });
  } catch (error) {
    // Try to read ESP-IDF's log files for more detail
    const err = error as { stderr?: string; stdout?: string; message?: string };
    const buildLogDir = join(esp32Dir, 'build', 'log');
    try {
      const { readdir } = await import('fs/promises');
      const logFiles = await readdir(buildLogDir);
      const stderrLog = logFiles.find((f) => f.includes('stderr'));
      if (stderrLog) {
        const logContent = await readFile(
          join(buildLogDir, stderrLog),
          'utf-8'
        );
        if (logContent.trim()) {
          err.stderr = (err.stderr || '') + '\n' + logContent;
        }
      }
    } catch {
      /* log dir may not exist */
    }
    return handleBuildError(error, tempBuildDir, buildCmd);
  }

  // Read the main binary file
  const binPath = join(esp32Dir, 'build', 'nx_standalone.bin');
  const binContent = await readFile(binPath);

  return new Response(binContent, {
    status: 200,
    headers: {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': `attachment; filename="${macroName}-esp32s3.bin"`,
    },
  });
}

function handleValidationError(error: unknown): Response {
  const err = error as { stderr?: string; stdout?: string; message?: string };
  const errorOutput = err.stderr || err.message || 'Unknown validation error';

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
    JSON.stringify({ message: `Macro validation error:\n${cleanError}` }),
    { status: 400, headers: { 'Content-Type': 'application/json' } }
  );
}

function handleBuildError(
  error: unknown,
  tempBuildDir: string,
  cmd: string
): Response {
  const err = error as { stderr?: string; stdout?: string; message?: string };
  const buildOutput =
    err.stderr || err.stdout || err.message || 'Unknown build error';

  console.error('Build failed. Full output:', buildOutput);
  console.error('Build dir:', tempBuildDir);
  console.error('Command:', cmd);

  const lines = buildOutput.split('\n');
  const errorLines: string[] = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const lower = line.toLowerCase();

    if (
      lower.includes('error:') ||
      lower.includes('undefined reference') ||
      (lower.includes('error') && lower.includes('.c:')) ||
      lower.includes('fatal error')
    ) {
      errorLines.push(line);
      if (i + 1 < lines.length && lines[i + 1].trim()) {
        errorLines.push(lines[i + 1]);
      }
    }
  }

  const isSystemError =
    errorLines.length === 0 ||
    buildOutput.includes('No such file or directory') ||
    buildOutput.includes('command not found') ||
    buildOutput.includes('Permission denied');

  if (isSystemError) {
    return new Response(
      JSON.stringify({
        message:
          'Oops! Something went wrong on our side. Please try again later.',
      }),
      { status: 500, headers: { 'Content-Type': 'application/json' } }
    );
  }

  const cleanError = errorLines.slice(0, 15).join('\n');

  return new Response(
    JSON.stringify({ message: `Compilation error:\n${cleanError}` }),
    { status: 400, headers: { 'Content-Type': 'application/json' } }
  );
}
