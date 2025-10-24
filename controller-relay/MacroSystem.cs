using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwitchController
{
    /// <summary>
    /// Represents a single frame in a macro recording
    /// </summary>
    public class MacroFrame
    {
        public long TimestampMs { get; set; }
        public byte[] Packet { get; set; } = new byte[8];
    }

    /// <summary>
    /// Handles macro recording, playback, and persistence
    /// </summary>
    public class MacroSystem
    {
        private readonly string _macroFilePath;
        private List<MacroFrame> _recordedMacro = new List<MacroFrame>();
        private byte[]? _lastRecordedPacket;
        private long _recordingStartTime;
        private long _playbackStartTime;
        private int _playbackIndex;

        public bool IsRecording { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsLooping { get; private set; }
        public int FrameCount => _recordedMacro.Count;
        public int PlaybackIndex => _playbackIndex;

        public MacroSystem(string macroFilePath)
        {
            _macroFilePath = macroFilePath ?? throw new ArgumentNullException(nameof(macroFilePath));
        }

        /// <summary>
        /// Starts recording a new macro
        /// </summary>
        public void StartRecording(long currentTime)
        {
            IsRecording = true;
            _recordedMacro.Clear();
            _playbackIndex = 0;
            _recordingStartTime = currentTime;
            _lastRecordedPacket = null;
            Console.WriteLine("\n[MACRO] Recording started! Press L3 + A again to stop.");
        }

        /// <summary>
        /// Stops recording and saves the macro
        /// </summary>
        public void StopRecording()
        {
            IsRecording = false;
            _lastRecordedPacket = null;
            Console.WriteLine($"\n[MACRO] Recording stopped. {_recordedMacro.Count} unique frames captured.");
            Save();
        }

        /// <summary>
        /// Records a frame, skipping if identical to the last recorded frame
        /// </summary>
        public void RecordFrame(long currentTime, byte[] packet)
        {
            if (!IsRecording)
                return;

            // Skip identical frames to reduce macro size
            if (_lastRecordedPacket != null)
            {
                bool isDifferent = false;
                for (int i = 0; i < 8; i++)
                {
                    if (packet[i] != _lastRecordedPacket[i])
                    {
                        isDifferent = true;
                        break;
                    }
                }

                if (!isDifferent)
                    return;
            }

            // Store frame with relative timestamp
            long relativeTime = currentTime - _recordingStartTime;
            var frame = new MacroFrame
            {
                TimestampMs = relativeTime,
                Packet = new byte[8]
            };
            Array.Copy(packet, frame.Packet, 8);
            _recordedMacro.Add(frame);

            // Update last recorded packet
            _lastRecordedPacket ??= new byte[8];
            Array.Copy(packet, _lastRecordedPacket, 8);
        }

        /// <summary>
        /// Starts macro playback
        /// </summary>
        public void StartPlayback(long currentTime, bool loop)
        {
            IsPlaying = !loop;
            IsLooping = loop;
            _playbackIndex = 0;
            _playbackStartTime = currentTime;

            string mode = loop ? "LOOPING" : "PLAYING ONCE";
            Console.WriteLine($"\n[MACRO] {mode} - {_recordedMacro.Count} frames. " +
                            (loop ? "Press L3 + Y to stop." : ""));
        }

        /// <summary>
        /// Stops macro playback
        /// </summary>
        public void StopPlayback()
        {
            IsPlaying = false;
            IsLooping = false;
            _playbackIndex = 0;
            Console.WriteLine("\n[MACRO] Playback stopped.");
        }

        /// <summary>
        /// Gets the current macro packet based on elapsed time
        /// </summary>
        /// <returns>Current packet, or null if playback is finished</returns>
        public byte[]? GetCurrentPacket(long currentTime)
        {
            if (_recordedMacro.Count == 0)
                return null;

            long elapsedMs = currentTime - _playbackStartTime;

            // Check if playback is finished
            if (_playbackIndex >= _recordedMacro.Count - 1 &&
                elapsedMs >= _recordedMacro[_recordedMacro.Count - 1].TimestampMs)
            {
                return null;
            }

            // Advance to the current frame
            while (_playbackIndex < _recordedMacro.Count - 1 &&
                   elapsedMs >= _recordedMacro[_playbackIndex + 1].TimestampMs)
            {
                _playbackIndex++;
            }

            return _recordedMacro[_playbackIndex].Packet;
        }

        /// <summary>
        /// Restarts macro playback from the beginning
        /// </summary>
        public void RestartPlayback(long currentTime)
        {
            _playbackIndex = 0;
            _playbackStartTime = currentTime;
        }

        /// <summary>
        /// Saves the recorded macro to disk in the appropriate format
        /// </summary>
        public void Save()
        {
            try
            {
                if (_recordedMacro.Count == 0)
                {
                    Console.WriteLine("[MACRO] No frames to save.");
                    return;
                }

                // Determine format based on file extension
                string extension = Path.GetExtension(_macroFilePath).ToLowerInvariant();

                if (extension == ".macro" || extension == ".txt")
                {
                    SaveTextFormat();
                }
                else
                {
                    SaveJsonFormat();
                }

                long durationMs = _recordedMacro[_recordedMacro.Count - 1].TimestampMs;
                double durationSec = durationMs / 1000.0;
                Console.WriteLine($"[MACRO] Saved {_recordedMacro.Count} frames to {Path.GetFileName(_macroFilePath)}");
                Console.WriteLine($"[MACRO] Duration: {durationSec:F2}s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MACRO] Error saving macro: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the macro in JSON format
        /// </summary>
        private void SaveJsonFormat()
        {
            var json = JsonSerializer.Serialize(_recordedMacro, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_macroFilePath, json);
        }

        /// <summary>
        /// Saves the macro in human-readable text format
        /// </summary>
        private void SaveTextFormat()
        {
            MacroWriter.WriteFile(_macroFilePath, _recordedMacro);
        }

        /// <summary>
        /// Loads a macro from disk, auto-detecting format
        /// </summary>
        public void Load()
        {
            try
            {
                // Try to load from the specified file path
                if (File.Exists(_macroFilePath))
                {
                    LoadFromFile(_macroFilePath);
                    return;
                }

                // Try alternate formats
                string directory = Path.GetDirectoryName(_macroFilePath) ?? ".";
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(_macroFilePath);

                // Try .macro format
                string macroPath = Path.Combine(directory, fileNameWithoutExt + ".macro");
                if (File.Exists(macroPath))
                {
                    LoadFromFile(macroPath);
                    return;
                }

                // Try .json format
                string jsonPath = Path.Combine(directory, fileNameWithoutExt + ".json");
                if (File.Exists(jsonPath))
                {
                    LoadFromFile(jsonPath);
                    return;
                }

                Console.WriteLine("[MACRO] No saved macro found.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MACRO] Error loading macro: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a macro from a specific file
        /// </summary>
        private void LoadFromFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            List<MacroFrame>? frames = null;

            if (extension == ".macro" || extension == ".txt")
            {
                // Try text format first
                try
                {
                    frames = MacroParser.ParseFile(filePath);
                }
                catch (MacroParseException ex)
                {
                    Console.WriteLine($"[MACRO] Parse error in {Path.GetFileName(filePath)}:");
                    Console.WriteLine($"[MACRO]   {ex.Message}");
                    return;
                }
            }
            else
            {
                // Try JSON format
                try
                {
                    var json = File.ReadAllText(filePath);
                    frames = JsonSerializer.Deserialize<List<MacroFrame>>(json);
                }
                catch
                {
                    // If JSON fails and the file looks like text, try text format
                    try
                    {
                        frames = MacroParser.ParseFile(filePath);
                    }
                    catch
                    {
                        throw; // Re-throw original JSON error
                    }
                }
            }

            if (frames != null && frames.Count > 0)
            {
                _recordedMacro = frames;
                long durationMs = _recordedMacro[_recordedMacro.Count - 1].TimestampMs;
                double durationSec = durationMs / 1000.0;
                Console.WriteLine($"[MACRO] Loaded {_recordedMacro.Count} frames from {Path.GetFileName(filePath)}");
                Console.WriteLine($"[MACRO] Duration: {durationSec:F2}s");
                Console.WriteLine("[MACRO] Press L3 + X to play once, L3 + Y to loop");
            }
            else
            {
                Console.WriteLine($"[MACRO] No frames found in {Path.GetFileName(filePath)}");
            }
        }
    }
}
