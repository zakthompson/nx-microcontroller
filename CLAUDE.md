# NX-Microcontroller

This project contains tools for using a microcontroller, such as an Arduino or Teensy, as a controller for a Nintendo Switch.

Currently, the project has two parts, and a third is planned.

## Firmware

- Found in @firmware
- Written in C (with one python tool)
- Can build firmware either to accept UART input and relay as to a Switch console, OR firmware with an embedded macro that will run when the microcontroller is plugged into the Switch

## Controller Relay

- Found in @controller-relay
- Written in C#
- Listens to XInput controller events and relays them over a serial port to the microcontroller running the UART firmware
- Can optionally launch a companion app when it launches, and will close the app when it exits
- Can optionally send input to the companion app after it launches (such as clicking a fullscreen button in capture software)
- Can record macros using a button combination to start and stop recording, and then execute those macros either once or on a loop

## Web Tool (To Be Implemented)

- To be implemented in @site
- Should be deployable on Fly.io via Docker image
- Should be written in TypeScript (Node back-end, Astro + React front-end)
- Should be styled via Tailwind CSS
- Should be linted and formatted via ESLint and Prettier, including a plugin to enforce Tailwind class order
- Allows users to select a game to download precompiled hex files for that game
- OR, allows users to build their own macros via text editor, and download a compiled hex file

---

## Coding Principles

1. **Aggressive Simplicity**

   - Always prefer the simplest working solution.
   - Complexity should only be introduced when required (performance, maintainability, or feature necessity).
   - Avoid overengineering, speculative abstractions, or clever hacks.
   - Strive for clarity over cleverness.

2. **Don’t Repeat Yourself (DRY)**

   - Avoid duplication — if code is about to be repeated, factor it out into a reusable function or module.
   - Keep shared logic in a single source of truth to reduce maintenance cost.

3. **Prefer Functional Style**

   - Favor pure functions with no side effects where possible.
   - Compose small, focused functions rather than relying on large classes or inheritance.
   - Minimize mutability unless required for clarity or performance.

4. **Zero Tolerance for Broken Code**

   - Code must compile/build without errors.
   - No linting, formatting, or type errors allowed.
   - Work-in-progress commits are acceptable, but merges must meet these standards.

5. **Production-Quality Expectations**
   - Code must be robust, maintainable, and secure.
   - Include appropriate error handling, input validation, and edge-case coverage.
   - Prioritize readability and maintainability — future contributors should be able to easily understand and extend the code.
   - **Use comments sparingly.** Explain _why_ something unusual or non-obvious is being done. Explain _what_ the code does only if it is necessarily complex and cannot be simplified further.

## Critical Tools

- **Context7**: ALWAYS use this MCP when seeking the latest documentation for a library.
