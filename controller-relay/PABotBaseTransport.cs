using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Transport for PokemonAutomation PABotBase firmware protocol:
    /// CRC32C framing, sequence numbers, 4-command queue, OEM controller support
    /// </summary>
    public class PABotBaseTransport : IControllerTransport
    {
        private SerialPort? _port;
        private uint _nextSeqnum = 1;
        private readonly List<byte> _rxBuffer = new List<byte>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastSendTimeMs;

        // Controller configuration
        private readonly uint _controllerId;
        private readonly bool _useOemFormat;
        private readonly int _sendIntervalMs;
        private readonly ushort _commandDurationMs;

        // In-flight command tracking
        private readonly Queue<uint> _inFlightSeqnums = new Queue<uint>();
        private readonly Dictionary<uint, (byte[] message, long sentAtMs)> _inFlightMessages = new Dictionary<uint, (byte[], long)>();


        public PABotBaseTransport(uint controllerId = PA_CID_NS1_WIRELESS_PRO_CONTROLLER)
        {
            _controllerId = controllerId;
            _useOemFormat = IsOemController(controllerId);
            bool isWireless = (controllerId & 0x80) != 0;
            _sendIntervalMs = isWireless ? PA_SEND_INTERVAL_WIRELESS_MS : PA_SEND_INTERVAL_WIRED_MS;
            _commandDurationMs = isWireless ? PA_COMMAND_DURATION_WIRELESS_MS : PA_COMMAND_DURATION_WIRED_MS;
        }

        public bool Connect(SerialPort port)
        {
            _port = port;

            try
            {
                // Flush any partially-parsed message on the device. The PA protocol
                // skips zero bytes at message boundaries, so sending 64 zeros will
                // fill (and CRC-fail) any in-progress parse, then be harmlessly
                // skipped once the device re-syncs.
                byte[] resync = new byte[PA_MAX_PACKET_SIZE];
                _port.Write(resync, 0, resync.Length);
                System.Threading.Thread.Sleep(50);

                _port.DiscardInBuffer();
                _nextSeqnum = 1;
                _inFlightSeqnums.Clear();
                _inFlightMessages.Clear();

                // Send SEQNUM_RESET (0x40) with seqnum=0
                Console.Write("\n  Sending seqnum reset... ");
                byte[] resetPayload = new byte[4]; // seqnum = 0 (LE)
                byte[] resetMsg = FrameMessage(PA_MSG_SEQNUM_RESET, resetPayload);
                _port.Write(resetMsg, 0, resetMsg.Length);

                if (!WaitForAck(2000))
                {
                    Console.WriteLine("no ACK (seqnum reset failed)");
                    return false;
                }
                Console.WriteLine("OK");

                // Set controller mode
                string controllerName = ControllerTypeName(_controllerId);
                Console.Write($"  Setting controller mode ({controllerName})... ");
                if (!SetControllerMode(_controllerId))
                {
                    Console.WriteLine("no ACK (controller mode change failed)");
                    return false;
                }
                Console.WriteLine("OK");

                // Verify mode change took effect (matches PA's refresh_controller_type)
                Console.Write("  Verifying controller mode... ");
                uint? activeMode = ReadControllerMode();
                if (activeMode == null)
                {
                    Console.WriteLine("no response");
                    return false;
                }
                if (activeMode.Value != _controllerId)
                {
                    Console.WriteLine($"MISMATCH: requested 0x{_controllerId:X4}, got 0x{activeMode.Value:X4}");
                    return false;
                }
                Console.WriteLine($"OK (0x{activeMode.Value:X4})");

                // Clear any residual commands on the device
                Console.Write("  Stopping all commands... ");
                if (!StopAllCommands())
                {
                    Console.WriteLine("no ACK");
                    return false;
                }
                Console.WriteLine("OK");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  Connect exception: {ex.Message}");
                return false;
            }
        }

        public void SendState(ControllerState state)
        {
            if (_port == null || !_port.IsOpen) return;

            long now = _clock.ElapsedMilliseconds;

            // Rate-limit to configured interval
            if (now - _lastSendTimeMs < _sendIntervalMs) return;

            // Only send when queue has space
            if (_inFlightSeqnums.Count >= PA_QUEUE_SIZE) return;

            uint seqnum = _nextSeqnum++;
            byte[] payload;
            byte msgType;

            if (_useOemFormat)
            {
                payload = BuildOemControllerPayload(seqnum, state);
                msgType = PA_MSG_OEM_CONTROLLER_BUTTONS;
            }
            else
            {
                payload = BuildWiredControllerPayload(seqnum, state);
                msgType = PA_MSG_WIRED_CONTROLLER_STATE;
            }

            byte[] message = FrameMessage(msgType, payload);
            _port.Write(message, 0, message.Length);
            _inFlightSeqnums.Enqueue(seqnum);
            _inFlightMessages[seqnum] = (message, now);
            _lastSendTimeMs = now;
        }

        public void Update()
        {
            if (_port == null) return;

            ReadIntoBuffer();

            // Process all available messages
            while (true)
            {
                var parsed = TryParseMessage();
                if (parsed == null) break;

                HandleMessage(parsed.Value.type, parsed.Value.payload);
            }

            RetransmitTimedOut();
        }

        public void Dispose()
        {
            _port = null;
            _inFlightSeqnums.Clear();
            _inFlightMessages.Clear();
        }

        private uint? ReadControllerMode()
        {
            if (_port == null) return null;

            uint seqnum = _nextSeqnum++;
            byte[] payload = BitConverter.GetBytes(seqnum);
            byte[] message = FrameMessage(PA_MSG_READ_CONTROLLER_MODE, payload);
            _port.Write(message, 0, message.Length);

            return WaitForAckI32(2000);
        }

        /// <summary>
        /// Waits for an ACK_REQUEST_I32 (0x14) response and extracts the uint32 data.
        /// </summary>
        private uint? WaitForAckI32(int timeoutMs)
        {
            int start = Environment.TickCount;
            while (Environment.TickCount - start < timeoutMs)
            {
                ReadIntoBuffer();
                var response = TryParseMessage();
                if (response != null)
                {
                    if (response.Value.type == 0x14 && response.Value.payload.Length >= 8)
                    {
                        // ACK_REQUEST_I32: payload = seqnum(4) + data(4)
                        return BitConverter.ToUInt32(response.Value.payload, 4);
                    }
                    if (IsAck(response.Value.type))
                        return null; // Got an ACK but not I32 type

                    // ACK any COMMAND_FINISHED that arrives during handshake
                    if (response.Value.type == PA_MSG_COMMAND_FINISHED && response.Value.payload.Length >= 4)
                    {
                        uint finSeq = BitConverter.ToUInt32(response.Value.payload, 0);
                        SendAckRequest(finSeq);
                        continue;
                    }

                    if (!IsInfo(response.Value.type))
                        Console.Write($"[got 0x{response.Value.type:X2}] ");
                }

                System.Threading.Thread.Sleep(2);
            }
            return null;
        }

        private bool StopAllCommands()
        {
            if (_port == null) return false;

            uint seqnum = _nextSeqnum++;
            byte[] payload = BitConverter.GetBytes(seqnum);
            byte[] message = FrameMessage(PA_MSG_REQUEST_STOP, payload);
            _port.Write(message, 0, message.Length);

            return WaitForAck(2000);
        }

        private bool SetControllerMode(uint controllerId)
        {
            if (_port == null) return false;

            uint seqnum = _nextSeqnum++;
            byte[] payload = new byte[8];
            BitConverter.GetBytes(seqnum).CopyTo(payload, 0);
            BitConverter.GetBytes(controllerId).CopyTo(payload, 4);

            byte[] message = FrameMessage(PA_MSG_CHANGE_CONTROLLER_MODE, payload);
            _port.Write(message, 0, message.Length);

            return WaitForAck(2000);
        }

        private static bool IsAck(byte type) => type >= 0x10 && type <= 0x1f;
        private static bool IsInfo(byte type) => type >= 0x20 && type <= 0x3f;

        private bool WaitForAck(int timeoutMs)
        {
            int start = Environment.TickCount;
            while (Environment.TickCount - start < timeoutMs)
            {
                ReadIntoBuffer();
                var response = TryParseMessage();
                if (response != null)
                {
                    if (IsAck(response.Value.type))
                        return true;

                    // ACK any COMMAND_FINISHED that arrives during handshake
                    if (response.Value.type == PA_MSG_COMMAND_FINISHED && response.Value.payload.Length >= 4)
                    {
                        uint finSeq = BitConverter.ToUInt32(response.Value.payload, 0);
                        SendAckRequest(finSeq);
                        Console.Write($"[acked 0x4A] ");
                        continue;
                    }

                    // Skip info messages silently; log anything else
                    if (!IsInfo(response.Value.type))
                        Console.Write($"[got 0x{response.Value.type:X2}] ");
                }

                System.Threading.Thread.Sleep(2);
            }
            return false;
        }

        private void HandleMessage(byte type, byte[] payload)
        {
            if (IsAck(type))
            {
                // Any ack type (0x10-0x1f) — no action needed during main loop
            }
            else if (type == PA_MSG_COMMAND_FINISHED && payload.Length >= 4)
            {
                // Device tells us a command finished; we must ACK it
                uint finishedSeqnum = BitConverter.ToUInt32(payload, 0);
                SendAckRequest(finishedSeqnum);

                if (payload.Length >= 8)
                {
                    uint originalSeqnum = BitConverter.ToUInt32(payload, 4);
                    RemoveInFlight(originalSeqnum);
                }
            }
            else if (type == PA_MSG_COMMAND_DROPPED && payload.Length >= 4)
            {
                uint droppedSeqnum = BitConverter.ToUInt32(payload, 0);
                RemoveInFlight(droppedSeqnum);
            }
            // Info messages (0x20-0x3f) and errors (0x00-0x0f) are silently ignored
        }

        private void SendAckRequest(uint seqnum)
        {
            if (_port == null || !_port.IsOpen) return;

            byte[] payload = BitConverter.GetBytes(seqnum);
            byte[] message = FrameMessage(PA_MSG_ACK_REQUEST, payload);
            _port.Write(message, 0, message.Length);
        }

        private void RemoveInFlight(uint seqnum)
        {
            _inFlightMessages.Remove(seqnum);
            int count = _inFlightSeqnums.Count;
            for (int i = 0; i < count; i++)
            {
                uint s = _inFlightSeqnums.Dequeue();
                if (s != seqnum)
                    _inFlightSeqnums.Enqueue(s);
            }
        }

        private void RetransmitTimedOut()
        {
            if (_port == null || !_port.IsOpen) return;

            long now = _clock.ElapsedMilliseconds;
            foreach (var kvp in _inFlightMessages)
            {
                if (now - kvp.Value.sentAtMs > PA_RETRANSMIT_MS)
                {
                    _port.Write(kvp.Value.message, 0, kvp.Value.message.Length);
                    _inFlightMessages[kvp.Key] = (kvp.Value.message, now);
                    break; // One per cycle
                }
            }
        }

        private byte[] BuildOemControllerPayload(uint seqnum, ControllerState state)
        {
            byte[] oemState = state.ToOemState(); // 10 bytes
            byte[] payload = new byte[4 + 2 + oemState.Length]; // seqnum + ms + state
            BitConverter.GetBytes(seqnum).CopyTo(payload, 0);
            BitConverter.GetBytes(_commandDurationMs).CopyTo(payload, 4);
            Array.Copy(oemState, 0, payload, 6, oemState.Length);
            return payload;
        }

        private byte[] BuildWiredControllerPayload(uint seqnum, ControllerState state)
        {
            // 13 bytes: seqnum(4) + milliseconds(2) + buttons0 + buttons1 + dpad + LX + LY + RX + RY
            byte[] payload = new byte[13];
            BitConverter.GetBytes(seqnum).CopyTo(payload, 0);
            BitConverter.GetBytes(_commandDurationMs).CopyTo(payload, 4);
            payload[6] = state.Buttons0;
            payload[7] = state.Buttons1;
            payload[8] = state.Dpad;
            payload[9] = state.LX;
            payload[10] = state.LY;
            payload[11] = state.RX;
            payload[12] = state.RY;
            return payload;
        }

        private static byte[] FrameMessage(byte type, byte[] payload)
        {
            // Wire format: [~totalLength] [type] [payload] [CRC32C x4]
            int totalLength = 1 + 1 + payload.Length + 4;
            byte[] message = new byte[totalLength];
            message[0] = (byte)~totalLength;
            message[1] = type;
            Array.Copy(payload, 0, message, 2, payload.Length);

            uint crc = Crc32C(message, 0, totalLength - 4);
            BitConverter.GetBytes(crc).CopyTo(message, totalLength - 4);

            return message;
        }

        private void ReadIntoBuffer()
        {
            if (_port == null) return;

            int available = _port.BytesToRead;
            if (available <= 0) return;

            byte[] buf = new byte[available];
            int read = _port.Read(buf, 0, available);
            for (int i = 0; i < read; i++)
                _rxBuffer.Add(buf[i]);
        }

        private (byte type, byte[] payload)? TryParseMessage()
        {
            while (_rxBuffer.Count > 0)
            {
                // Skip zero bytes (re-sync markers)
                if (_rxBuffer[0] == 0x00)
                {
                    _rxBuffer.RemoveAt(0);
                    continue;
                }

                byte lengthByte = _rxBuffer[0];
                int totalLength = (byte)~lengthByte;

                if (totalLength < PA_PROTOCOL_OVERHEAD || totalLength > PA_MAX_PACKET_SIZE)
                {
                    _rxBuffer.RemoveAt(0);
                    continue;
                }

                if (_rxBuffer.Count < totalLength)
                    return null;

                byte[] msgBytes = new byte[totalLength];
                _rxBuffer.CopyTo(0, msgBytes, 0, totalLength);

                uint expectedCrc = BitConverter.ToUInt32(msgBytes, totalLength - 4);
                uint actualCrc = Crc32C(msgBytes, 0, totalLength - 4);

                if (expectedCrc != actualCrc)
                {
                    _rxBuffer.RemoveAt(0);
                    continue;
                }

                _rxBuffer.RemoveRange(0, totalLength);

                byte type = msgBytes[1];
                int payloadLen = totalLength - PA_PROTOCOL_OVERHEAD;
                byte[] payload = new byte[payloadLen];
                Array.Copy(msgBytes, 2, payload, 0, payloadLen);

                return (type, payload);
            }

            return null;
        }

        private static uint Crc32C(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                crc = Crc32CTable[(byte)crc ^ data[i]] ^ (crc >> 8);
            }
            return crc;
        }

        private static readonly uint[] Crc32CTable = new uint[]
        {
            0x00000000, 0xf26b8303, 0xe13b70f7, 0x1350f3f4, 0xc79a971f, 0x35f1141c, 0x26a1e7e8, 0xd4ca64eb,
            0x8ad958cf, 0x78b2dbcc, 0x6be22838, 0x9989ab3b, 0x4d43cfd0, 0xbf284cd3, 0xac78bf27, 0x5e133c24,
            0x105ec76f, 0xe235446c, 0xf165b798, 0x030e349b, 0xd7c45070, 0x25afd373, 0x36ff2087, 0xc494a384,
            0x9a879fa0, 0x68ec1ca3, 0x7bbcef57, 0x89d76c54, 0x5d1d08bf, 0xaf768bbc, 0xbc267848, 0x4e4dfb4b,
            0x20bd8ede, 0xd2d60ddd, 0xc186fe29, 0x33ed7d2a, 0xe72719c1, 0x154c9ac2, 0x061c6936, 0xf477ea35,
            0xaa64d611, 0x580f5512, 0x4b5fa6e6, 0xb93425e5, 0x6dfe410e, 0x9f95c20d, 0x8cc531f9, 0x7eaeb2fa,
            0x30e349b1, 0xc288cab2, 0xd1d83946, 0x23b3ba45, 0xf779deae, 0x05125dad, 0x1642ae59, 0xe4292d5a,
            0xba3a117e, 0x4851927d, 0x5b016189, 0xa96ae28a, 0x7da08661, 0x8fcb0562, 0x9c9bf696, 0x6ef07595,
            0x417b1dbc, 0xb3109ebf, 0xa0406d4b, 0x522bee48, 0x86e18aa3, 0x748a09a0, 0x67dafa54, 0x95b17957,
            0xcba24573, 0x39c9c670, 0x2a993584, 0xd8f2b687, 0x0c38d26c, 0xfe53516f, 0xed03a29b, 0x1f682198,
            0x5125dad3, 0xa34e59d0, 0xb01eaa24, 0x42752927, 0x96bf4dcc, 0x64d4cecf, 0x77843d3b, 0x85efbe38,
            0xdbfc821c, 0x2997011f, 0x3ac7f2eb, 0xc8ac71e8, 0x1c661503, 0xee0d9600, 0xfd5d65f4, 0x0f36e6f7,
            0x61c69362, 0x93ad1061, 0x80fde395, 0x72966096, 0xa65c047d, 0x5437877e, 0x4767748a, 0xb50cf789,
            0xeb1fcbad, 0x197448ae, 0x0a24bb5a, 0xf84f3859, 0x2c855cb2, 0xdeeedfb1, 0xcdbe2c45, 0x3fd5af46,
            0x7198540d, 0x83f3d70e, 0x90a324fa, 0x62c8a7f9, 0xb602c312, 0x44694011, 0x5739b3e5, 0xa55230e6,
            0xfb410cc2, 0x092a8fc1, 0x1a7a7c35, 0xe811ff36, 0x3cdb9bdd, 0xceb018de, 0xdde0eb2a, 0x2f8b6829,
            0x82f63b78, 0x709db87b, 0x63cd4b8f, 0x91a6c88c, 0x456cac67, 0xb7072f64, 0xa457dc90, 0x563c5f93,
            0x082f63b7, 0xfa44e0b4, 0xe9141340, 0x1b7f9043, 0xcfb5f4a8, 0x3dde77ab, 0x2e8e845f, 0xdce5075c,
            0x92a8fc17, 0x60c37f14, 0x73938ce0, 0x81f80fe3, 0x55326b08, 0xa759e80b, 0xb4091bff, 0x466298fc,
            0x1871a4d8, 0xea1a27db, 0xf94ad42f, 0x0b21572c, 0xdfeb33c7, 0x2d80b0c4, 0x3ed04330, 0xccbbc033,
            0xa24bb5a6, 0x502036a5, 0x4370c551, 0xb11b4652, 0x65d122b9, 0x97baa1ba, 0x84ea524e, 0x7681d14d,
            0x2892ed69, 0xdaf96e6a, 0xc9a99d9e, 0x3bc21e9d, 0xef087a76, 0x1d63f975, 0x0e330a81, 0xfc588982,
            0xb21572c9, 0x407ef1ca, 0x532e023e, 0xa145813d, 0x758fe5d6, 0x87e466d5, 0x94b49521, 0x66df1622,
            0x38cc2a06, 0xcaa7a905, 0xd9f75af1, 0x2b9cd9f2, 0xff56bd19, 0x0d3d3e1a, 0x1e6dcdee, 0xec064eed,
            0xc38d26c4, 0x31e6a5c7, 0x22b65633, 0xd0ddd530, 0x0417b1db, 0xf67c32d8, 0xe52cc12c, 0x1747422f,
            0x49547e0b, 0xbb3ffd08, 0xa86f0efc, 0x5a048dff, 0x8ecee914, 0x7ca56a17, 0x6ff599e3, 0x9d9e1ae0,
            0xd3d3e1ab, 0x21b862a8, 0x32e8915c, 0xc083125f, 0x144976b4, 0xe622f5b7, 0xf5720643, 0x07198540,
            0x590ab964, 0xab613a67, 0xb831c993, 0x4a5a4a90, 0x9e902e7b, 0x6cfbad78, 0x7fab5e8c, 0x8dc0dd8f,
            0xe330a81a, 0x115b2b19, 0x020bd8ed, 0xf0605bee, 0x24aa3f05, 0xd6c1bc06, 0xc5914ff2, 0x37faccf1,
            0x69e9f0d5, 0x9b8273d6, 0x88d28022, 0x7ab90321, 0xae7367ca, 0x5c18e4c9, 0x4f48173d, 0xbd23943e,
            0xf36e6f75, 0x0105ec76, 0x12551f82, 0xe03e9c81, 0x34f4f86a, 0xc69f7b69, 0xd5cf889d, 0x27a40b9e,
            0x79b737ba, 0x8bdcb4b9, 0x988c474d, 0x6ae7c44e, 0xbe2da0a5, 0x4c4623a6, 0x5f16d052, 0xad7d5351
        };
    }
}
