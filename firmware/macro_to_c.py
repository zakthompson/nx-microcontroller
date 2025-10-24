#!/usr/bin/env python3
"""
Convert a macro file (JSON or text format from controller-relay) to a C header file
that can be embedded in the firmware for standalone macro playback.

Usage:
    python3 macro_to_c.py macro.macro [--loop] > embedded_macro.h
    python3 macro_to_c.py macro.json [--loop] > embedded_macro.h
"""

import json
import sys
import argparse
import base64
import re
import os
from typing import List, Dict, Any, Optional, Tuple


# Button bit masks (matching C# SwitchControllerConstants)
BUTTONS = {
    'Y': 1 << 0, 'B': 1 << 1, 'A': 1 << 2, 'X': 1 << 3,
    'L': 1 << 4, 'R': 1 << 5, 'ZL': 1 << 6, 'ZR': 1 << 7,
    'MINUS': 1 << 8, 'PLUS': 1 << 9,
    'LCLICK': 1 << 10, 'RCLICK': 1 << 11,
    'HOME': 1 << 12, 'CAPTURE': 1 << 13
}

# D-Pad HAT values
DPAD = {
    'UP': 0x00, 'UPRIGHT': 0x01, 'RIGHT': 0x02, 'DOWNRIGHT': 0x03,
    'DOWN': 0x04, 'DOWNLEFT': 0x05, 'LEFT': 0x06, 'UPLEFT': 0x07,
    'NEUTRAL': 0x08
}

# Stick cardinal positions
LEFT_STICK = {
    'LUP': (128, 255), 'LDOWN': (128, 0), 'LLEFT': (0, 128), 'LRIGHT': (255, 128),
    'LUPLEFT': (0, 255), 'LUPRIGHT': (255, 255), 'LDOWNLEFT': (0, 0), 'LDOWNRIGHT': (255, 0)
}

RIGHT_STICK = {
    'RUP': (128, 255), 'RDOWN': (128, 0), 'RLEFT': (0, 128), 'RRIGHT': (255, 128),
    'RUPLEFT': (0, 255), 'RUPRIGHT': (255, 255), 'RDOWNLEFT': (0, 0), 'RDOWNRIGHT': (255, 0)
}

NEUTRAL_KEYWORDS = {'WAIT', 'NOTHING', 'NEUTRAL'}

MAX_INCLUDE_DEPTH = 10


def parse_text_macro(file_path: str, include_chain: Optional[set] = None, depth: int = 0) -> List[Dict[str, Any]]:
    """
    Parse a human-readable text macro file into frame format with include support.

    Args:
        file_path: Path to the .macro text file
        include_chain: Set of already included files (for circular dependency detection)
        depth: Current include depth (for max depth checking)

    Returns:
        List of frames in the same format as JSON: [{'TimestampMs': int, 'Packet': [bytes]}]
    """
    # Initialize include chain if this is the top-level call
    if include_chain is None:
        include_chain = set()

    # Get absolute path for this file
    abs_file_path = os.path.abspath(file_path)
    base_directory = os.path.dirname(abs_file_path)

    # Check recursion depth
    if depth > MAX_INCLUDE_DEPTH:
        print(f"Error: Maximum include depth of {MAX_INCLUDE_DEPTH} exceeded in {file_path}", file=sys.stderr)
        sys.exit(1)

    # Add this file to the include chain
    if abs_file_path in include_chain:
        chain_str = ' -> '.join(include_chain) + ' -> ' + abs_file_path
        print(f"Error: Circular include detected: {chain_str}", file=sys.stderr)
        sys.exit(1)

    include_chain = include_chain | {abs_file_path}

    frames = []
    current_timestamp = 0
    prev_state = None

    with open(file_path, 'r') as f:
        for line_num, line in enumerate(f, 1):
            line = line.strip()

            # Skip empty lines and comments
            if not line or line.startswith('#') or line.startswith('//'):
                continue

            # Remove inline comments
            if '#' in line:
                line = line[:line.index('#')].strip()
            if '//' in line:
                line = line[:line.index('//')].strip()

            # Handle include directive
            if line.startswith('@'):
                include_path = line[1:].strip()

                try:
                    # Resolve path relative to base directory
                    if os.path.isabs(include_path):
                        full_include_path = include_path
                    else:
                        full_include_path = os.path.abspath(os.path.join(base_directory, include_path))

                    # Check if file exists
                    if not os.path.exists(full_include_path):
                        print(f"Error on line {line_num}: Include file not found: {full_include_path}", file=sys.stderr)
                        sys.exit(1)

                    # Recursively parse included file
                    included_frames = parse_text_macro(full_include_path, include_chain, depth + 1)

                    # Merge included frames into current frames, adjusting timestamps
                    for included_frame in included_frames:
                        # Skip the final neutral frame from included file
                        if included_frame == included_frames[-1]:
                            continue

                        # Adjust timestamp to be relative to current position
                        adjusted_timestamp = current_timestamp + included_frame['TimestampMs']
                        frames.append({
                            'TimestampMs': adjusted_timestamp,
                            'Packet': included_frame['Packet']
                        })

                    # Update current timestamp based on the last included frame's timestamp
                    if len(included_frames) > 1:
                        # Get the duration from the included macro (last timestamp before neutral frame)
                        last_included_timestamp = included_frames[-2]['TimestampMs'] if len(included_frames) > 1 else 0
                        # Calculate duration from last included frame
                        if len(included_frames) > 1:
                            included_duration = included_frames[-1]['TimestampMs']
                            current_timestamp += included_duration

                except Exception as e:
                    print(f"Error on line {line_num} processing include: {e}", file=sys.stderr)
                    sys.exit(1)

                continue

            # Parse line: inputs,duration
            # Need to handle commas inside L(x,y) or R(x,y)
            try:
                # Find the last comma that's not inside parentheses
                paren_depth = 0
                split_pos = -1
                for i, char in enumerate(line):
                    if char == '(':
                        paren_depth += 1
                    elif char == ')':
                        paren_depth -= 1
                    elif char == ',' and paren_depth == 0:
                        split_pos = i

                if split_pos == -1:
                    raise ValueError("Missing comma separator between inputs and duration")

                inputs_part = line[:split_pos].strip()
                duration_part = line[split_pos+1:].strip()

                # Parse duration (support hex with 0x prefix)
                if duration_part.lower().startswith('0x'):
                    duration = int(duration_part, 16)
                else:
                    duration = int(duration_part)

                if duration < 0:
                    raise ValueError(f"Duration cannot be negative")

                # Build packet
                packet = build_packet(inputs_part, prev_state, line_num)

                # Add frame
                frames.append({
                    'TimestampMs': current_timestamp,
                    'Packet': list(packet)
                })

                current_timestamp += duration
                prev_state = packet

            except ValueError as e:
                print(f"Error on line {line_num}: {e}", file=sys.stderr)
                sys.exit(1)

    # Add final neutral frame to release all inputs
    if frames:
        neutral_packet = [0, 0, DPAD['NEUTRAL'], 128, 128, 128, 128, 0]
        frames.append({
            'TimestampMs': current_timestamp,
            'Packet': neutral_packet
        })

    return frames


def build_packet(inputs_str: str, prev_packet: Optional[List[int]], line_num: int) -> List[int]:
    """
    Build an 8-byte packet from an input string.

    Args:
        inputs_str: Input string (e.g., "A+B", "LUp", "L(127,255)")
        prev_packet: Previous packet (not used - kept for compatibility)
        line_num: Line number for error reporting

    Returns:
        8-byte packet as list
    """
    # Start with neutral packet
    # Each line specifies the complete state - no inheritance from previous commands
    packet = [0, 0, DPAD['NEUTRAL'], 128, 128, 128, 128, 0]

    # Check for neutral/wait state
    if not inputs_str or inputs_str.upper() in NEUTRAL_KEYWORDS:
        # Reset to neutral
        return [0, 0, DPAD['NEUTRAL'], 128, 128, 128, 128, 0]

    # Parse inputs (separated by +)
    inputs = [inp.strip() for inp in inputs_str.split('+') if inp.strip()]

    buttons = 0
    hat = DPAD['NEUTRAL']
    left_stick_set = False
    right_stick_set = False

    for inp in inputs:
        inp_upper = inp.upper()

        # Check for complex analog: L(x,y) or R(x,y)
        match = re.match(r'([LR])\s*\(\s*(\d+|0x[0-9A-Fa-f]+)\s*,\s*(\d+|0x[0-9A-Fa-f]+)\s*\)', inp, re.IGNORECASE)
        if match:
            stick, x_str, y_str = match.groups()
            x = int(x_str, 16 if x_str.lower().startswith('0x') else 10)
            y = int(y_str, 16 if y_str.lower().startswith('0x') else 10)

            if not (0 <= x <= 255 and 0 <= y <= 255):
                raise ValueError(f"Stick coordinates must be 0-255: {inp}")

            if stick.upper() == 'L':
                packet[3] = x
                packet[4] = y
                left_stick_set = True
            else:
                packet[5] = x
                packet[6] = y
                right_stick_set = True
            continue

        # Check for button
        if inp_upper in BUTTONS:
            buttons |= BUTTONS[inp_upper]
            continue

        # Check for D-Pad
        if inp_upper in DPAD:
            hat = DPAD[inp_upper]
            continue

        # Check for left stick cardinal
        if inp_upper in LEFT_STICK:
            x, y = LEFT_STICK[inp_upper]
            packet[3] = x
            packet[4] = y
            left_stick_set = True
            continue

        # Check for right stick cardinal
        if inp_upper in RIGHT_STICK:
            x, y = RIGHT_STICK[inp_upper]
            packet[5] = x
            packet[6] = y
            right_stick_set = True
            continue

        raise ValueError(f"Unknown input: {inp}")

    # Build final packet
    packet[0] = (buttons >> 8) & 0xFF  # buttons_hi
    packet[1] = buttons & 0xFF          # buttons_lo
    packet[2] = hat

    return packet


def load_macro_file(file_path: str) -> List[Dict[str, Any]]:
    """
    Load a macro file, auto-detecting format (JSON or text).

    Args:
        file_path: Path to macro file

    Returns:
        List of frames in standard format
    """
    # Determine format by extension
    if file_path.endswith('.macro') or file_path.endswith('.txt'):
        return parse_text_macro(file_path)
    elif file_path.endswith('.json'):
        with open(file_path, 'r') as f:
            return json.load(f)
    else:
        # Try JSON first, then text
        try:
            with open(file_path, 'r') as f:
                return json.load(f)
        except json.JSONDecodeError:
            return parse_text_macro(file_path)


def json_to_c_header(macro_file: str, loop_macro: bool = False) -> str:
    """
    Convert macro file (JSON or text format) to C header format.

    Args:
        macro_file: Path to the input macro file (.json, .macro, or .txt)
        loop_macro: Whether to enable macro looping

    Returns:
        C header file content as a string
    """

    frames: List[Dict[str, Any]] = load_macro_file(macro_file)

    if not frames:
        print(f"Error: Macro file '{macro_file}' contains no frames", file=sys.stderr)
        sys.exit(1)

    if not isinstance(frames, list):
        print(f"Error: Macro file '{macro_file}' must contain a list of frames", file=sys.stderr)
        sys.exit(1)

    # Generate header
    output: List[str] = []
    output.append("// Auto-generated from macro file")
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
        description='Convert macro file (JSON or text format) to C header for embedded firmware'
    )
    parser.add_argument('macro_file', help='Path to macro file (.macro, .json, or .txt)')
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
