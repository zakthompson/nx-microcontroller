# NX Controller Web Tool

Web application for the NX-Microcontroller project. Allows users to download precompiled firmware for various games or build custom macros and download compiled hex files.

## Stack

- **Astro 5** - Web framework with SSR
- **React 19** - UI components
- **Tailwind CSS 4** - Styling
- **TypeScript** - Type safety
- **Node.js** - Backend runtime
- **ESLint & Prettier** - Code quality and formatting

## Development

All commands are run from the `site/` directory:

| Command           | Action                               |
| :---------------- | :----------------------------------- |
| `npm install`     | Install dependencies                 |
| `npm run dev`     | Start dev server at `localhost:4321` |
| `npm run build`   | Build production site to `./dist/`   |
| `npm run start`   | Start production server              |
| `npm run preview` | Preview build locally                |
| `npm run lint`    | Run ESLint                           |
| `npm run format`  | Format code with Prettier            |

## Deployment to Fly.io

This project includes AVR toolchain for compiling microcontroller firmware on the server.

**Important:** Deploy from the repository root, not from `site/`:

```bash
cd /path/to/nx-microcontroller
fly deploy --config site/fly.toml
```

The Dockerfile includes:

- Node.js runtime
- AVR-GCC toolchain (gcc-avr, avr-libc, binutils-avr)
- Python 3 (for macro_to_c.py)
- Make and Git

## Project Structure

```
site/
├── src/
│   ├── pages/          # Route pages
│   ├── components/     # React/Astro components
│   └── styles/         # Global styles
├── public/             # Static assets
├── firmware/           # Copied during Docker build
├── Dockerfile          # Production container
└── fly.toml           # Fly.io configuration
```
