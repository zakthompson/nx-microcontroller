#!/bin/bash
# Convenience script for building standalone macro firmware

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

Build standalone firmware with embedded macro for Nintendo Switch.

Arguments:
    <macro.json>    Path to the macro JSON file (required)
    --loop          Enable macro looping (optional, default: play once)

Examples:
    $0 ../controller-relay/macro.json
    $0 ../controller-relay/macro.json --loop
    $0 ~/my_macros/farming.json --loop

After building, flash with:
    make flash-standalone

Or manually:
    sudo dfu-programmer atmega16u2 erase
    sudo dfu-programmer atmega16u2 flash JoystickStandalone.hex
    sudo dfu-programmer atmega16u2 reset
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

# Build
echo "============================================"
echo "Building Standalone Macro Firmware"
echo "============================================"
echo "Macro file: $MACRO_FILE"
echo "Loop mode:  $([ $LOOP_MODE -eq 1 ] && echo "ENABLED" || echo "DISABLED")"
echo ""

if [ $LOOP_MODE -eq 1 ]; then
    make standalone MACRO="$MACRO_FILE" LOOP=1
else
    make standalone MACRO="$MACRO_FILE"
fi

echo ""
echo "============================================"
echo "Build Complete!"
echo "============================================"
echo "Firmware: JoystickStandalone.hex"
echo ""
echo "Next steps:"
echo "  1. Put Arduino into DFU mode (short RESET and GND)"
echo "  2. Flash firmware: make flash-standalone"
echo "  3. Plug Arduino into Nintendo Switch"
echo ""
