#!/bin/bash
# Build standalone macro firmware for Raspberry Pi Pico

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Default values
MACRO_FILE=""
LOOP_MODE=0

# Parse arguments
show_usage() {
    cat << EOF
Usage: $0 <macro.json> [--loop]

Build standalone firmware with embedded macro for Raspberry Pi Pico.

Arguments:
    <macro.json>    Path to the macro JSON file (required)
    --loop          Enable macro looping (optional, default: play once)

Examples:
    $0 ../../controller-relay/macro.json
    $0 ../../controller-relay/macro.json --loop
    $0 ~/my_macros/farming.json --loop

Requirements:
    - PICO_SDK_PATH environment variable must be set
    - Pico SDK and toolchain must be installed

After building, flash to Pico:
    1. Hold BOOTSEL button while plugging in Pico
    2. Drag build/nx_standalone.uf2 onto the USB drive that appears
    3. Pico will reboot and start running the macro
EOF
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help)
            show_usage
            exit 0
            ;;
        --loop)
            LOOP_MODE=1
            shift
            ;;
        *)
            if [ -z "$MACRO_FILE" ]; then
                MACRO_FILE="$1"
            else
                echo "Error: Unexpected argument '$1'"
                echo ""
                show_usage
                exit 1
            fi
            shift
            ;;
    esac
done

# Validate macro file
if [ -z "$MACRO_FILE" ]; then
    echo "Error: Macro file required"
    echo ""
    show_usage
    exit 1
fi

if [ ! -f "$MACRO_FILE" ]; then
    echo "Error: Macro file not found: $MACRO_FILE"
    exit 1
fi

# Check for Pico SDK
if [ -z "$PICO_SDK_PATH" ]; then
    echo "Error: PICO_SDK_PATH environment variable not set"
    echo "Please set it to the path of your Pico SDK installation"
    echo "Example: export PICO_SDK_PATH=/path/to/pico-sdk"
    exit 1
fi

if [ ! -d "$PICO_SDK_PATH" ]; then
    echo "Error: PICO_SDK_PATH directory not found: $PICO_SDK_PATH"
    exit 1
fi

# Build
echo "============================================"
echo "Building Pico Standalone Macro Firmware"
echo "============================================"
echo "Macro file: $MACRO_FILE"
echo "Loop mode:  $([ $LOOP_MODE -eq 1 ] && echo "ENABLED" || echo "DISABLED")"
echo "Pico SDK:   $PICO_SDK_PATH"
echo ""

# Generate macro header
echo "Converting macro to C header..."
if [ $LOOP_MODE -eq 1 ]; then
    python3 ../macro_to_c.py "$MACRO_FILE" --loop -o embedded_macro.h
else
    python3 ../macro_to_c.py "$MACRO_FILE" -o embedded_macro.h
fi

# Create build directory if it doesn't exist
mkdir -p build
cd build

# Configure CMake
echo "Configuring build..."
cmake .. -DCMAKE_BUILD_TYPE=Release

# Build
echo "Building firmware..."
make -j$(nproc) nx_standalone

echo ""
echo "============================================"
echo "Build Complete!"
echo "============================================"
echo "Firmware: build/nx_standalone.uf2"
echo ""
echo "Next steps:"
echo "  1. Hold BOOTSEL button while plugging in Pico"
echo "  2. Drag nx_standalone.uf2 onto the USB drive"
echo "  3. Pico will reboot and run your macro"
echo ""
