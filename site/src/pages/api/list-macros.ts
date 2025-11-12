import type { APIRoute } from 'astro';
import { readdirSync, statSync } from 'fs';
import { join } from 'path';

export const GET: APIRoute = async ({ request }) => {
  const url = new URL(request.url);
  const game = url.searchParams.get('game');
  const bot = url.searchParams.get('bot');

  if (!game) {
    return new Response(JSON.stringify({ error: 'Game parameter is required' }), {
      status: 400,
      headers: { 'Content-Type': 'application/json' },
    });
  }

  try {
    // Macros are stored in the top-level macros directory
    const macrosDir = join(process.cwd(), '..', 'macros', game);

    // If no bot specified, list all bots for the game
    if (!bot) {
      const bots = readdirSync(macrosDir).filter((file) => {
        const stat = statSync(join(macrosDir, file));
        return stat.isDirectory();
      });

      return new Response(JSON.stringify({ bots }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    // List all macros for the specified bot
    const botDir = join(macrosDir, bot);
    const files = readdirSync(botDir).filter((file) => file.endsWith('.macro'));

    // Convert filenames to display names (remove .macro, replace hyphens with spaces, titleize)
    const macros = files.map((file) => {
      const name = file
        .replace('.macro', '')
        .replace(/-/g, ' ')
        .replace(/\b\w/g, (char) => char.toUpperCase());
      return {
        filename: file,
        displayName: name,
      };
    });

    // Check if we should auto-select: single macro AND filename matches directory name
    const shouldAutoSelect = files.length === 1 && files[0].replace('.macro', '') === bot;

    return new Response(JSON.stringify({ macros, shouldAutoSelect }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });
  } catch (error) {
    console.error('Error listing macros:', error);
    return new Response(
      JSON.stringify({ error: 'Failed to list macros', details: String(error) }),
      {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      },
    );
  }
};
