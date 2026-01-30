#!/bin/bash
# Build standalone macro firmware for ESP32-S3

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

Build standalone firmware with embedded macro for ESP32-S3.

Arguments:
    <macro.json>    Path to the macro JSON file (required)
    --loop          Enable macro looping (optional, default: play once)

Examples:
    $0 ../../controller-relay/macro.json
    $0 ../../controller-relay/macro.json --loop
    $0 ~/my_macros/farming.json --loop

Requirements:
    - IDF_PATH environment variable must be set
    - ESP-IDF v5.0 or later must be installed
    - ESP32-S3 toolchain must be installed

After building, flash to ESP32-S3:
    idf.py flash monitor

Or use the merged binary:
    esptool.py --chip esp32s3 write_flash 0x0 build/nx_standalone_merged.bin
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

# Check for ESP-IDF
if [ -z "$IDF_PATH" ]; then
    echo "Error: IDF_PATH environment variable not set"
    echo "Please set it to the path of your ESP-IDF installation"
    echo "Example: export IDF_PATH=/path/to/esp-idf"
    exit 1
fi

if [ ! -d "$IDF_PATH" ]; then
    echo "Error: IDF_PATH directory not found: $IDF_PATH"
    exit 1
fi

# Source ESP-IDF environment
echo "Sourcing ESP-IDF environment..."
source "$IDF_PATH/export.sh"

# Build
echo "============================================"
echo "Building ESP32-S3 Standalone Macro Firmware"
echo "============================================"
echo "Macro file: $MACRO_FILE"
echo "Loop mode:  $([ $LOOP_MODE -eq 1 ] && echo "ENABLED" || echo "DISABLED")"
echo "ESP-IDF:    $IDF_PATH"
echo ""

# Generate macro header
echo "Converting macro to C header..."
if [ $LOOP_MODE -eq 1 ]; then
    python3 ../macro_to_c.py "$MACRO_FILE" --loop -o main/embedded_macro.h
else
    python3 ../macro_to_c.py "$MACRO_FILE" -o main/embedded_macro.h
fi

# Set target to ESP32-S3
echo "Setting target to ESP32-S3..."
idf.py set-target esp32s3

# Build firmware
echo "Building firmware..."
idf.py build

# Create merged binary for easy flashing
echo "Creating merged binary..."
esptool.py --chip esp32s3 merge_bin \
    -o build/nx_standalone_merged.bin \
    --flash_mode dio \
    --flash_size 8MB \
    0x0 build/bootloader/bootloader.bin \
    0x8000 build/partition_table/partition-table.bin \
    0x10000 build/nx_standalone.bin

echo ""
echo "============================================"
echo "Build Complete!"
echo "============================================"
echo "Firmware: build/nx_standalone.bin"
echo "Merged:   build/nx_standalone_merged.bin"
echo ""
echo "Next steps:"
echo "  Flash with ESP-IDF:"
echo "    idf.py flash monitor"
echo ""
echo "  Or flash merged binary:"
echo "    esptool.py --chip esp32s3 write_flash 0x0 build/nx_standalone_merged.bin"
echo ""
