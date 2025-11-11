import { useState, useMemo } from 'react';

interface MacroOption {
  filename: string;
  displayName: string;
}

interface MacroSelectorProps {
  macros: MacroOption[];
  selectedMacro: string | null;
  onSelectMacro: (filename: string) => void;
  isSingleOption?: boolean;
}

export default function MacroSelector({
  macros,
  selectedMacro,
  onSelectMacro,
  isSingleOption = false,
}: MacroSelectorProps) {
  const [searchTerm, setSearchTerm] = useState('');

  // Filter macros based on search term
  const filteredMacros = useMemo(() => {
    if (!searchTerm) return macros;
    const lowerSearch = searchTerm.toLowerCase();
    return macros.filter((macro) =>
      macro.displayName.toLowerCase().includes(lowerSearch),
    );
  }, [macros, searchTerm]);

  // If single option, auto-select it
  if (isSingleOption && macros.length === 1 && !selectedMacro) {
    onSelectMacro(macros[0].filename);
  }

  // Don't show selector if single option (it's auto-selected)
  if (isSingleOption) {
    return null;
  }

  return (
    <div className="space-y-3">
      <div>
        <label htmlFor="macro-search" className="mb-2 block text-sm text-gray-300">
          Search for a macro:
        </label>
        <input
          id="macro-search"
          type="text"
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          placeholder="Type to filter..."
          className="w-full rounded-lg border border-gray-600 bg-gray-700 px-4 py-2 text-white placeholder-gray-400 focus:border-purple-500 focus:outline-none focus:ring-2 focus:ring-purple-500"
        />
      </div>

      <div className="max-h-96 space-y-1 overflow-y-auto rounded-lg border border-gray-600 bg-gray-800 p-2">
        {filteredMacros.length === 0 ? (
          <div className="py-8 text-center text-gray-400">
            No macros found matching "{searchTerm}"
          </div>
        ) : (
          filteredMacros.map((macro) => (
            <button
              key={macro.filename}
              onClick={() => onSelectMacro(macro.filename)}
              className={`w-full rounded px-3 py-2 text-left text-sm transition-colors ${
                selectedMacro === macro.filename
                  ? 'bg-purple-600 text-white'
                  : 'text-gray-300 hover:bg-gray-700'
              }`}
            >
              {macro.displayName}
            </button>
          ))
        )}
      </div>
    </div>
  );
}
