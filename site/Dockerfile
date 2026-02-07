FROM node:22-bookworm-slim AS base

# Install build dependencies and all toolchains
RUN apt-get update && apt-get install -y \
    # AVR toolchain (existing)
    gcc-avr \
    avr-libc \
    binutils-avr \
    # ARM toolchain (for Pico)
    gcc-arm-none-eabi \
    libnewlib-arm-none-eabi \
    # ESP-IDF dependencies
    git \
    wget \
    flex \
    bison \
    gperf \
    python3 \
    python3-pip \
    python3-venv \
    cmake \
    ninja-build \
    ccache \
    libffi-dev \
    libssl-dev \
    libusb-1.0-0 \
    # Build tools
    make \
    && rm -rf /var/lib/apt/lists/*

# Install Pico SDK
RUN git clone --depth 1 --branch master https://github.com/raspberrypi/pico-sdk.git /opt/pico-sdk && \
    cd /opt/pico-sdk && \
    git submodule update --init --depth 1
ENV PICO_SDK_PATH=/opt/pico-sdk

# Install ESP-IDF for ESP32-S3
RUN git clone --depth 1 --branch v5.3.1 https://github.com/espressif/esp-idf.git /opt/esp-idf && \
    cd /opt/esp-idf && \
    git submodule update --init --recursive --depth 1 && \
    ./install.sh esp32s3
ENV IDF_PATH=/opt/esp-idf

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
