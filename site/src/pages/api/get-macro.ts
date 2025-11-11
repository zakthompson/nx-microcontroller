import type { APIRoute } from 'astro';
import { readFileSync, existsSync } from 'fs';
import { join } from 'path';

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
      JSON.stringify({ error: 'Game, bot, and filename parameters are required' }),
      {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      },
    );
  }

  try {
    // Macros are stored in the top-level macros directory
    const macrosDir = join(process.cwd(), '..', 'macros');
    const macroPath = join(macrosDir, game, bot, filename);
    const content = readFileSync(macroPath, 'utf-8');

    // Try to parse instructions from the macro file
    let instructions = parseInstructionsFromMacro(content);

    // If no instructions in macro, fall back to bot-level README.md
    if (!instructions) {
      const readmePath = join(macrosDir, game, bot, 'README.md');
      if (existsSync(readmePath)) {
        instructions = readFileSync(readmePath, 'utf-8');
      }
    }

    return new Response(
      JSON.stringify({
        content,
        instructions: instructions || '',
      }),
      {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      },
    );
  } catch (error) {
    console.error('Error reading macro:', error);
    return new Response(
      JSON.stringify({ error: 'Failed to read macro', details: String(error) }),
      {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      },
    );
  }
};
