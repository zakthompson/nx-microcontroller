#!/usr/bin/env python3
"""
Convert a JSON macro file (from controller-relay) to a C header file
that can be embedded in the firmware for standalone macro playback.

Usage:
    python3 macro_to_c.py macro.json [--loop] > embedded_macro.h
"""

import json
import sys
import argparse
import base64
from typing import List, Dict, Any


def json_to_c_header(json_file: str, loop_macro: bool = False) -> str:
    """
    Convert JSON macro file to C header format.

    Args:
        json_file: Path to the input JSON macro file
        loop_macro: Whether to enable macro looping

    Returns:
        C header file content as a string
    """

    with open(json_file, 'r') as f:
        frames: List[Dict[str, Any]] = json.load(f)

    if not frames:
        print(f"Error: Macro file '{json_file}' contains no frames", file=sys.stderr)
        sys.exit(1)

    if not isinstance(frames, list):
        print(f"Error: Macro file '{json_file}' must contain a JSON array of frames", file=sys.stderr)
        sys.exit(1)

    # Generate header
    output: List[str] = []
    output.append("// Auto-generated from macro JSON file")
    output.append("// DO NOT EDIT MANUALLY")
    output.append("")
    output.append("#ifndef EMBEDDED_MACRO_H")
    output.append("#define EMBEDDED_MACRO_H")
    output.append("")
    output.append("#include <stdint.h>")
    output.append("#include <avr/pgmspace.h>")
    output.append("")

    # Configuration
    output.append("// Macro configuration")
    output.append(f"#define EMBEDDED_MACRO_ENABLED 1")
    output.append(f"#define EMBEDDED_MACRO_LOOP {'1' if loop_macro else '0'}")
    output.append(f"#define EMBEDDED_MACRO_FRAME_COUNT {len(frames)}")
    output.append("")

    # Duration info
    duration_ms = frames[-1]['TimestampMs']
    duration_sec = duration_ms / 1000.0
    output.append(f"// Macro duration: {duration_sec:.2f} seconds ({len(frames)} frames)")
    output.append("")

    # Frame structure
    output.append("typedef struct {")
    output.append("    uint32_t timestamp_ms;  // Timestamp in milliseconds")
    output.append("    uint8_t packet[8];      // 8-byte input report")
    output.append("} EmbeddedMacroFrame_t;")
    output.append("")

    # Frame data in PROGMEM
    output.append("// Macro frames stored in program memory (PROGMEM)")
    output.append("const EmbeddedMacroFrame_t embedded_macro_frames[] PROGMEM = {")

    for i, frame in enumerate(frames):
        timestamp = frame['TimestampMs']
        packet_data = frame['Packet']

        # Decode packet - it might be base64 string or byte array
        if isinstance(packet_data, str):
            # Base64 encoded string from C# serializer
            try:
                packet = base64.b64decode(packet_data)
            except Exception as e:
                raise ValueError(f"Invalid base64 packet at frame {i}: {e}")
        elif isinstance(packet_data, list):
            # Already a list of bytes
            packet = bytes(packet_data)
        else:
            raise ValueError(f"Invalid packet format at frame {i}: expected string or array, got {type(packet_data).__name__}")

        # Format packet bytes as hex
        packet_str = ", ".join(f"0x{b:02X}" for b in packet)

        # Add frame entry
        comma = "," if i < len(frames) - 1 else ""
        output.append(f"    {{ {timestamp}, {{ {packet_str} }} }}{comma}")

    output.append("};")
    output.append("")
    output.append("#endif // EMBEDDED_MACRO_H")

    return "\n".join(output)


def main() -> None:
    """Main entry point for the macro conversion script."""
    parser = argparse.ArgumentParser(
        description='Convert JSON macro file to C header for embedded firmware'
    )
    parser.add_argument('macro_file', help='Path to macro.json file')
    parser.add_argument('--loop', action='store_true',
                       help='Enable macro looping (default: play once)')
    parser.add_argument('-o', '--output',
                       help='Output file (default: stdout)')

    args = parser.parse_args()

    try:
        header_content = json_to_c_header(args.macro_file, args.loop)

        if args.output:
            with open(args.output, 'w') as f:
                f.write(header_content)
            print(f"Generated {args.output}", file=sys.stderr)
        else:
            print(header_content)

    except FileNotFoundError:
        print(f"Error: File '{args.macro_file}' not found", file=sys.stderr)
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON in '{args.macro_file}': {e}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
