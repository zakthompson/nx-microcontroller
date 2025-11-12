FROM node:22-bookworm-slim AS base

# Install AVR toolchain and build dependencies
RUN apt-get update && apt-get install -y \
    gcc-avr \
    avr-libc \
    binutils-avr \
    make \
    python3 \
    git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy package files
COPY site/package*.json ./

# Install dependencies
RUN npm ci --only=production

# Copy site source code
COPY site/ .

# Copy firmware and macros directories (from repo root context)
COPY firmware/ ./firmware/
COPY macros/ ./macros/

# Build the Astro application
RUN npm run build

# Expose the port
ENV HOST=0.0.0.0
ENV PORT=3000
EXPOSE 3000

# Start the server
CMD ["node", "./dist/server/entry.mjs"]
