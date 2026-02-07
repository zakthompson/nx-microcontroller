import type { APIRoute } from 'astro';
import { readFileSync, readdirSync, existsSync } from 'fs';
import { join } from 'path';
import { parseMacroFrontmatter } from '../../lib/macro-frontmatter';

function parseInstructionsFromMacro(content: string): string | null {
  const lines = content.split('\n');
  const instructionLines: string[] = [];
  let inInstructionsBlock = false;

  for (const line of lines) {
    const trimmed = line.trim();

    if (trimmed === '# Instructions:') {
      inInstructionsBlock = true;
      continue;
    }

    if (trimmed === '# End Instructions') {
      break;
    }

    if (inInstructionsBlock) {
      // Stop at first non-comment line if no End marker
      if (!trimmed.startsWith('#')) {
        break;
      }

      // Remove leading '# ' or '#' from the line
      const instructionLine = trimmed.replace(/^#\s?/, '');
      instructionLines.push(instructionLine);
    }
  }

  const instructions = instructionLines.join('\n').trim();
  return instructions || null;
}

export const GET: APIRoute = async ({ request }) => {
  const url = new URL(request.url);
  const game = url.searchParams.get('game');
  const bot = url.searchParams.get('bot');
  const filename = url.searchParams.get('filename');

  if (!game || !bot || !filename) {
    return new Response(
      JSON.stringify({
        error: 'Game, bot, and filename parameters are required',
      }),
      {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      }
    );
  }

  try {
    // Macros are stored in the macros directory
    // In dev: process.cwd() = .../site, so we need ../macros
    // In Docker: process.cwd() = /app, macros are copied to /app/macros
    const macrosDir = existsSync(join(process.cwd(), 'macros'))
      ? join(process.cwd(), 'macros')
      : join(process.cwd(), '..', 'macros');
    const macroPath = join(macrosDir, game, bot, filename);
    const rawContent = readFileSync(macroPath, 'utf-8');
    const { config, body } = parseMacroFrontmatter(rawContent);

    // Try to parse instructions from the macro body (frontmatter-stripped)
    let instructions = parseInstructionsFromMacro(body);

    // If no instructions in macro, fall back to bot-level README.md
    if (!instructions) {
      const readmePath = join(macrosDir, game, bot, 'README.md');
      if (existsSync(readmePath)) {
        instructions = readFileSync(readmePath, 'utf-8');
      }
    }

    // Load .macro files from the game-level includes/ directory so the
    // compile endpoint can resolve @../includes/... references
    const includeMacros: Record<string, string> = {};
    const includesDir = join(macrosDir, game, 'includes');
    if (existsSync(includesDir)) {
      const files = readdirSync(includesDir).filter((f) =>
        f.endsWith('.macro')
      );
      for (const file of files) {
        const name = file.replace(/\.macro$/, '');
        includeMacros[name] = readFileSync(join(includesDir, file), 'utf-8');
      }
    }

    return new Response(
      JSON.stringify({
        content: body,
        instructions: instructions || '',
        config,
        includeMacros,
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }
    );
  } catch (error) {
    console.error('Error reading macro:', error);
    return new Response(
      JSON.stringify({ error: 'Failed to read macro', details: String(error) }),
      {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      }
    );
  }
};
