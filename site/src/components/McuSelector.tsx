interface McuSelectorProps {
  selectedMcu: string;
  onSelectMcu: (mcu: string) => void;
}

const MCU_OPTIONS = [
  { value: 'atmega16u2', label: 'ATmega16U2 (Arduino UNO R3)' },
  { value: 'atmega32u4', label: 'ATmega32U4 (Arduino Leonardo, Micro)' },
  { value: 'at90usb1286', label: 'AT90USB1286 (Teensy++ 2.0)' },
];

export default function McuSelector({ selectedMcu, onSelectMcu }: McuSelectorProps) {
  return (
    <div className="space-y-2">
      <label htmlFor="mcu-select" className="block text-sm font-medium text-gray-300">
        Select Your Microcontroller
      </label>
      <select
        id="mcu-select"
        value={selectedMcu}
        onChange={(e) => onSelectMcu(e.target.value)}
        className="w-full rounded-lg border border-gray-600 bg-gray-700 px-4 py-2 text-white focus:border-purple-500 focus:outline-none focus:ring-2 focus:ring-purple-500"
      >
        {MCU_OPTIONS.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}
