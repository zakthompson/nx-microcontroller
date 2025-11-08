import { useState, useEffect } from 'react';
import MacroLegend from './MacroLegend';

const MCU_OPTIONS = [
  { value: 'atmega16u2', label: 'atmega16u2 (UNO R3)' },
  { value: 'at90usb1286', label: 'at90usb1286 (Teensy 2.0++)' },
  { value: 'atmega32u4', label: 'atmega32u4 (Arduino Micro/Teensy 2.0)' },
];

const STORAGE_KEY = 'nx-controller-macros';

interface SavedMacros {
  [name: string]: string;
}

export default function MacroEditor() {
  const [macroName, setMacroName] = useState('');
  const [macroText, setMacroText] = useState('');
  const [mcu, setMcu] = useState('atmega16u2');
  const [loop, setLoop] = useState(true);
  const [savedMacros, setSavedMacros] = useState<SavedMacros>({});
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);
  const [isCompiling, setIsCompiling] = useState(false);
  const [showLegend, setShowLegend] = useState(true);

  // Load saved macros from localStorage on mount
  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      try {
        setSavedMacros(JSON.parse(stored));
      } catch (e) {
        console.error('Failed to load saved macros:', e);
      }
    }
  }, []);

  // Track unsaved changes (only if text/name actually differs from saved state)
  useEffect(() => {
    // Don't mark as unsaved if both are empty (initial state)
    if (!macroText && !macroName) {
      setHasUnsavedChanges(false);
      return;
    }

    // Check if current state matches saved state
    const currentSavedContent = macroName ? savedMacros[macroName] : undefined;
    const matchesSaved = currentSavedContent === macroText;
    setHasUnsavedChanges(!matchesSaved);
  }, [macroText, macroName, savedMacros]);

  const handleNew = () => {
    if (
      hasUnsavedChanges &&
      !confirm(
        'You have unsaved changes. Are you sure you want to create a new macro?'
      )
    ) {
      return;
    }
    setMacroName('');
    setMacroText('');
    setValidationErrors([]);
    setHasUnsavedChanges(false);
  };

  const handleSave = () => {
    if (!macroName.trim()) {
      alert('Please enter a macro name before saving.');
      return;
    }

    // Validate macro name (no spaces or special characters)
    if (!/^[a-zA-Z0-9_-]+$/.test(macroName)) {
      alert(
        'Macro name can only contain letters, numbers, hyphens, and underscores.\nSpaces and special characters are not allowed.'
      );
      return;
    }

    const updated = { ...savedMacros, [macroName]: macroText };
    setSavedMacros(updated);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
    setHasUnsavedChanges(false);
  };

  const handleLoad = (name: string) => {
    if (
      hasUnsavedChanges &&
      !confirm('You have unsaved changes. Load this macro anyway?')
    ) {
      return;
    }

    setMacroName(name);
    setMacroText(savedMacros[name] || '');
    setValidationErrors([]);
    setHasUnsavedChanges(false);
  };

  const handleDelete = (name: string) => {
    if (!confirm(`Delete macro "${name}"?`)) {
      return;
    }

    const updated = { ...savedMacros };
    delete updated[name];
    setSavedMacros(updated);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));

    if (macroName === name) {
      handleNew();
    }
  };

  const validateMacro = (text: string): string[] => {
    const errors: string[] = [];
    const lines = text.split('\n');

    lines.forEach((line, index) => {
      const trimmed = line.trim();
      if (!trimmed || trimmed.startsWith('#') || trimmed.startsWith('//')) {
        return;
      }

      // Check include directives
      if (trimmed.startsWith('@')) {
        const match = trimmed.match(/^@([a-zA-Z0-9_-]+)(?:,\*(\d+))?$/);
        if (!match) {
          errors.push(`Line ${index + 1}: Invalid include syntax`);
          return;
        }
        const [, includeName] = match;
        if (!savedMacros[includeName]) {
          errors.push(
            `Line ${index + 1}: Macro "${includeName}" not found in saved macros`
          );
        }
        return;
      }

      // Check basic line format
      if (!trimmed.includes(',')) {
        errors.push(
          `Line ${index + 1}: Missing comma separator between inputs and duration`
        );
        return;
      }

      const parts = trimmed.split(',');
      if (parts.length < 2) {
        errors.push(`Line ${index + 1}: Invalid line format`);
        return;
      }

      // Validate duration
      const duration = parts[parts.length - 1].trim().split(/\s+/)[0];
      if (!/^(0x[0-9a-fA-F]+|\d+)$/.test(duration)) {
        errors.push(`Line ${index + 1}: Invalid duration format`);
        return;
      }

      const durationValue = duration.startsWith('0x')
        ? parseInt(duration, 16)
        : parseInt(duration, 10);
      if (durationValue < 0) {
        errors.push(`Line ${index + 1}: Duration cannot be negative`);
      }
    });

    return errors;
  };

  const handleDownload = async () => {
    // Validate macro name first
    const nameToUse = macroName.trim() || 'custom';
    if (!/^[a-zA-Z0-9_-]+$/.test(nameToUse)) {
      setValidationErrors([
        'Invalid macro name. Use only letters, numbers, hyphens, and underscores.',
      ]);
      return;
    }

    // Validate macro syntax
    const errors = validateMacro(macroText);
    if (errors.length > 0) {
      setValidationErrors(errors);
      return;
    }

    setValidationErrors([]);
    setIsCompiling(true);

    try {
      const response = await fetch('/api/compile-macro', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          macroText,
          macroName: nameToUse,
          mcu,
          loop,
          savedMacros,
        }),
      });

      if (!response.ok) {
        const error = await response.json();
        setValidationErrors([error.message || 'Compilation failed']);
        return;
      }

      // Download the hex file
      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${nameToUse}-${mcu}.hex`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
    } catch (error) {
      setValidationErrors([
        error instanceof Error ? error.message : 'Compilation failed',
      ]);
    } finally {
      setIsCompiling(false);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-900 via-slate-800 to-gray-900 p-4">
      <div className="mx-auto max-w-7xl">
        {/* Header */}
        <div className="mb-6">
          <h1 className="mb-2 text-4xl font-bold text-white">
            Custom Macro Editor
          </h1>
          <a href="/" className="text-gray-400 hover:text-white">
            ← Back to Home
          </a>
        </div>

        {/* Toolbar */}
        <div className="mb-6 grid grid-cols-1 gap-4 rounded-lg bg-gray-800 p-4 md:grid-cols-4">
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-300">
              Microcontroller
            </label>
            <select
              value={mcu}
              onChange={(e) => setMcu(e.target.value)}
              className="w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-white focus:border-blue-500 focus:outline-none"
            >
              {MCU_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium text-gray-300">
              Macro Name
            </label>
            <input
              type="text"
              value={macroName}
              onChange={(e) => setMacroName(e.target.value)}
              placeholder="my-macro"
              pattern="[a-zA-Z0-9_-]+"
              title="Use only letters, numbers, hyphens, and underscores"
              className="w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-white placeholder-gray-400 focus:border-blue-500 focus:outline-none"
            />
            <p className="mt-1 text-xs text-gray-400">
              Letters, numbers, hyphens, underscores only
            </p>
          </div>

          <div className="flex items-start gap-2 pt-5">
            <button
              onClick={handleNew}
              className="rounded bg-gray-600 px-4 py-2 font-medium text-white transition hover:bg-gray-500"
            >
              New
            </button>
            <button
              onClick={handleSave}
              disabled={!macroName.trim()}
              className="rounded bg-green-600 px-4 py-2 font-medium text-white transition hover:bg-green-500 disabled:cursor-not-allowed disabled:opacity-50"
            >
              Save
            </button>
          </div>

          <div className="flex items-start pt-5">
            <button
              onClick={() => setShowLegend(!showLegend)}
              className="w-full rounded bg-blue-600 px-4 py-2 font-medium text-white transition hover:bg-blue-500"
            >
              {showLegend ? 'Hide' : 'Show'} Legend
            </button>
          </div>
        </div>

        {/* Main Content */}
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-4">
          {/* Sidebar */}
          <div className="rounded-lg bg-gray-800 p-4 lg:col-span-1">
            <h2 className="mb-3 text-xl font-bold text-white">Saved Macros</h2>
            <div className="max-h-96 space-y-2 overflow-y-auto">
              {Object.keys(savedMacros).length === 0 ? (
                <p className="text-sm text-gray-400">No saved macros yet</p>
              ) : (
                Object.keys(savedMacros).map((name) => (
                  <div
                    key={name}
                    className="flex items-center justify-between rounded bg-gray-700 p-2"
                  >
                    <button
                      onClick={() => handleLoad(name)}
                      className="flex-1 truncate text-left text-white hover:text-blue-400"
                    >
                      {name}
                    </button>
                    <button
                      onClick={() => handleDelete(name)}
                      className="ml-2 text-red-400 hover:text-red-300"
                      title="Delete"
                    >
                      ✕
                    </button>
                  </div>
                ))
              )}
            </div>
          </div>

          {/* Editor Area */}
          <div className="space-y-4 lg:col-span-3">
            {/* Validation Errors */}
            {validationErrors.length > 0 && (
              <div className="rounded-lg bg-red-900/50 p-4">
                <h3 className="mb-2 font-bold text-red-200">
                  Validation Errors:
                </h3>
                <div className="space-y-2 text-sm text-red-200">
                  {validationErrors.map((error, i) => (
                    <pre key={i} className="font-mono whitespace-pre-wrap">
                      {error}
                    </pre>
                  ))}
                </div>
              </div>
            )}

            {/* Textarea */}
            <textarea
              value={macroText}
              onChange={(e) => setMacroText(e.target.value)}
              placeholder="# Enter your macro here&#10;# Example:&#10;A,5&#10;Wait,125&#10;B,10"
              className="h-96 w-full rounded-lg border border-gray-600 bg-gray-800 p-4 font-mono text-sm text-white placeholder-gray-500 focus:border-blue-500 focus:outline-none"
            />

            {/* Loop Checkbox and Download Button */}
            <div className="space-y-3">
              <label className="flex items-center gap-2 text-white">
                <input
                  type="checkbox"
                  checked={loop}
                  onChange={(e) => setLoop(e.target.checked)}
                  className="h-4 w-4 rounded border-gray-600 bg-gray-700 text-purple-600 focus:ring-2 focus:ring-purple-500"
                />
                <span className="text-sm">Loop macro continuously</span>
              </label>

              <button
                onClick={handleDownload}
                disabled={isCompiling || !macroText.trim()}
                className="w-full rounded-lg bg-purple-600 px-6 py-3 text-lg font-bold text-white transition hover:bg-purple-500 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {isCompiling ? 'Compiling...' : 'Download Compiled Firmware'}
              </button>
            </div>
          </div>
        </div>

        {/* Legend */}
        {showLegend && <MacroLegend />}
      </div>
    </div>
  );
}
