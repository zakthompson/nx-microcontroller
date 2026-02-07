interface PlatformSelectorProps {
  selectedPlatform: string;
  onSelectPlatform: (platform: string) => void;
  selectedMcu?: string;
  onSelectMcu?: (mcu: string) => void;
}

const PLATFORM_OPTIONS = [
  {
    value: 'avr',
    label: 'Arduino / Teensy (AVR)',
    description: 'For Arduino UNO R3, Leonardo, Micro, Teensy++ 2.0',
    mcuOptions: [
      { value: 'atmega16u2', label: 'ATmega16U2 (Arduino UNO R3)' },
      { value: 'atmega32u4', label: 'ATmega32U4 (Arduino Leonardo, Micro)' },
      { value: 'at90usb1286', label: 'AT90USB1286 (Teensy++ 2.0)' },
    ],
    flashingInstructions: 'Flash the .hex file using dfu-programmer or FLIP. Put your Arduino in DFU mode first.',
  },
  {
    value: 'pico',
    label: 'Raspberry Pi Pico',
    description: 'For Raspberry Pi Pico, Pico 2, and W variants',
    mcuOptions: [
      { value: 'pico', label: 'Pico (RP2040)' },
      { value: 'pico_w', label: 'Pico W (RP2040)' },
      { value: 'pico2', label: 'Pico 2 (RP2350)' },
      { value: 'pico2_w', label: 'Pico 2 W (RP2350)' },
    ],
    flashingInstructions: 'Hold BOOTSEL while plugging in your Pico, then drag the .uf2 file onto the USB drive that appears.',
  },
  {
    value: 'esp32s3',
    label: 'ESP32-S3',
    description: 'For ESP32-S3 DevKit and compatible boards (native USB required)',
    mcuOptions: null,
    flashingInstructions: 'Flash using esptool: esptool.py --chip esp32s3 write_flash 0x0 firmware.bin',
  },
];

export default function PlatformSelector({
  selectedPlatform,
  onSelectPlatform,
  selectedMcu,
  onSelectMcu,
}: PlatformSelectorProps) {
  const currentPlatform = PLATFORM_OPTIONS.find((p) => p.value === selectedPlatform);
  const showMcuSelector = currentPlatform?.mcuOptions && onSelectMcu;

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <label htmlFor="platform-select" className="block text-sm font-medium text-gray-300">
          Select Your Platform
        </label>
        <select
          id="platform-select"
          value={selectedPlatform}
          onChange={(e) => {
            const newPlatform = e.target.value;
            onSelectPlatform(newPlatform);

            // Auto-select first MCU if switching to AVR
            const platform = PLATFORM_OPTIONS.find((p) => p.value === newPlatform);
            if (platform?.mcuOptions && onSelectMcu) {
              onSelectMcu(platform.mcuOptions[0].value);
            }
          }}
          className="w-full rounded-lg border border-gray-600 bg-gray-700 px-4 py-2 text-white focus:border-purple-500 focus:outline-none focus:ring-2 focus:ring-purple-500"
        >
          {PLATFORM_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        {currentPlatform && (
          <p className="text-sm text-gray-400">{currentPlatform.description}</p>
        )}
      </div>

      {showMcuSelector && currentPlatform.mcuOptions && selectedMcu && (
        <div className="space-y-2">
          <label htmlFor="mcu-select" className="block text-sm font-medium text-gray-300">
            {selectedPlatform === 'pico' ? 'Select Your Board' : 'Select Your Microcontroller'}
          </label>
          <select
            id="mcu-select"
            value={selectedMcu}
            onChange={(e) => onSelectMcu(e.target.value)}
            className="w-full rounded-lg border border-gray-600 bg-gray-700 px-4 py-2 text-white focus:border-purple-500 focus:outline-none focus:ring-2 focus:ring-purple-500"
          >
            {currentPlatform.mcuOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
      )}

      {currentPlatform && (
        <div className="rounded-lg border border-blue-500/30 bg-blue-500/10 p-4">
          <h4 className="mb-2 text-sm font-semibold text-blue-400">Flashing Instructions</h4>
          <p className="text-sm text-gray-300">{currentPlatform.flashingInstructions}</p>
        </div>
      )}
    </div>
  );
}

// Export platform options for use in other components
export { PLATFORM_OPTIONS };
