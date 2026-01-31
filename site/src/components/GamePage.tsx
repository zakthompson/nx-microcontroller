import { useState, useEffect, useRef } from 'react';
import BotSelector from './BotSelector';
import MacroSelector from './MacroSelector';
import PlatformSelector from './PlatformSelector';
import InstructionsDisplay from './InstructionsDisplay';
import Header from './Header';
import type { MacroConfigOption } from '../lib/macro-frontmatter';

interface GamePageProps {
  gameId: string;
  gameTitle: string;
}

interface BotOption {
  id: string;
  displayName: string;
}

interface MacroOption {
  filename: string;
  displayName: string;
}

interface MacroData {
  content: string;
  instructions: string;
  config: MacroConfigOption[];
  includeMacros: Record<string, string>;
}

export default function GamePage({ gameId, gameTitle }: GamePageProps) {
  const [bots, setBots] = useState<BotOption[]>([]);
  const [selectedBot, setSelectedBot] = useState<string | null>(null);
  const [macros, setMacros] = useState<MacroOption[]>([]);
  const [selectedMacro, setSelectedMacro] = useState<string | null>(null);
  const [macroData, setMacroData] = useState<MacroData | null>(null);
  const [platform, setPlatform] = useState('avr');
  const [mcu, setMcu] = useState('atmega16u2');
  const [isCompiling, setIsCompiling] = useState(false);
  const [configValues, setConfigValues] = useState<
    Record<string, string | number>
  >({});
  const [isLoading, setIsLoading] = useState(true);
  const [shouldAutoSelect, setShouldAutoSelect] = useState(false);

  // Track the current bot+macro pair to detect changes
  const selectionRef = useRef({ bot: selectedBot, macro: selectedMacro });
  selectionRef.current = { bot: selectedBot, macro: selectedMacro };

  // Load available bots on mount
  useEffect(() => {
    const loadBots = async () => {
      try {
        const response = await fetch(`/api/list-macros?game=${gameId}`);
        const data = await response.json();

        if (data.bots) {
          const botOptions = data.bots.map((botId: string) => ({
            id: botId,
            displayName: botId
              .replace(/-/g, ' ')
              .replace(/\b\w/g, (l: string) => l.toUpperCase()),
          }));
          setBots(botOptions);

          // Auto-select first bot
          if (botOptions.length > 0) {
            setSelectedBot(botOptions[0].id);
          }
        }
      } catch (error) {
        console.error('Failed to load bots:', error);
      } finally {
        setIsLoading(false);
      }
    };

    loadBots();
  }, [gameId]);

  // Load macros when bot is selected
  useEffect(() => {
    if (!selectedBot) return;

    // Clear selection when bot changes
    setSelectedMacro(null);
    setMacroData(null);
    setMacros([]);
    setConfigValues({});

    const loadMacros = async () => {
      try {
        const response = await fetch(
          `/api/list-macros?game=${gameId}&bot=${selectedBot}`
        );
        const data = await response.json();

        if (data.macros) {
          setMacros(data.macros);
          setShouldAutoSelect(data.shouldAutoSelect ?? false);

          // Auto-select if shouldAutoSelect is true
          if (data.shouldAutoSelect && data.macros.length === 1) {
            setSelectedMacro(data.macros[0].filename);
          }
        }
      } catch (error) {
        console.error('Failed to load macros:', error);
      }
    };

    loadMacros();
  }, [gameId, selectedBot]);

  // Load macro data when selection changes
  useEffect(() => {
    if (!selectedMacro) {
      setMacroData(null);
      return;
    }

    // Don't fetch until the macros list has loaded and contains this macro
    const macroExists = macros.some((m) => m.filename === selectedMacro);
    if (!macroExists) {
      setMacroData(null);
      return;
    }

    // Read bot from ref — avoids stale fetches when bot changes but
    // selectedMacro/macros haven't flushed to null/[] yet
    const bot = selectionRef.current.bot;
    if (!bot) return;

    const abortController = new AbortController();

    const loadMacroData = async () => {
      try {
        const response = await fetch(
          `/api/get-macro?game=${gameId}&bot=${bot}&filename=${selectedMacro}`,
          { signal: abortController.signal }
        );
        const data = await response.json();

        // Only update if not aborted and selection hasn't changed
        if (
          !abortController.signal.aborted &&
          selectionRef.current.bot === bot &&
          selectionRef.current.macro === selectedMacro &&
          data.content &&
          data.instructions !== undefined
        ) {
          setMacroData(data);

          // Initialize config defaults
          const configOptions: MacroConfigOption[] = data.config ?? [];
          if (configOptions.length > 0) {
            const defaults: Record<string, string | number> = {};
            for (const opt of configOptions) {
              if (opt.type === 'number') {
                defaults[opt.id] = opt.default;
              } else if (opt.type === 'dropdown') {
                defaults[opt.id] = opt.options[0].value;
              }
            }
            setConfigValues(defaults);
          } else {
            setConfigValues({});
          }
        }
      } catch (error: unknown) {
        if (error instanceof Error && error.name === 'AbortError') {
          return;
        }

        if (
          selectionRef.current.bot === bot &&
          selectionRef.current.macro === selectedMacro
        ) {
          console.error('Failed to load macro data:', error);
        }
      }
    };

    loadMacroData();

    return () => {
      abortController.abort();
    };
  }, [selectedMacro, gameId, macros]);

  const handleDownload = async () => {
    if (!macroData || !selectedMacro) return;

    setIsCompiling(true);

    try {
      const response = await fetch('/api/compile-macro', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          macroText: macroData.content,
          macroName: selectedMacro.replace('.macro', ''),
          platform,
          mcu: platform === 'avr' || platform === 'pico' ? mcu : undefined,
          loop: true,
          savedMacros: macroData.includeMacros ?? {},
          config: macroData.config,
          configValues,
        }),
      });

      if (!response.ok) {
        const error = await response.json();
        alert(`Compilation failed: ${error.error || 'Unknown error'}`);
        return;
      }

      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;

      // Determine file extension based on platform
      const ext =
        platform === 'avr' ? '.hex' : platform === 'pico' ? '.uf2' : '.bin';
      a.download = selectedMacro.replace('.macro', ext);

      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
    } catch (error) {
      console.error('Download failed:', error);
      alert('Failed to download firmware');
    } finally {
      setIsCompiling(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-gray-900 via-slate-800 to-gray-900">
        <div className="text-xl text-white">Loading...</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-900 via-slate-800 to-gray-900">
      <Header />
      <div className="container mx-auto px-4 py-8">
        {/* Header */}
        <header className="mb-8">
          <a
            href="/"
            className="mb-4 inline-block text-purple-400 hover:text-purple-300"
          >
            ← Back to Games
          </a>
          <h1 className="text-4xl font-bold text-white">{gameTitle}</h1>
        </header>

        {/* Main Content */}
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-4">
          {/* Sidebar */}
          <aside className="lg:col-span-1">
            <div className="space-y-6 rounded-lg bg-gray-800 p-6">
              <BotSelector
                bots={bots}
                selectedBot={selectedBot}
                onSelectBot={setSelectedBot}
              />
              <PlatformSelector
                selectedPlatform={platform}
                onSelectPlatform={setPlatform}
                selectedMcu={mcu}
                onSelectMcu={setMcu}
              />
            </div>
          </aside>

          {/* Main Content Area */}
          <main className="lg:col-span-3">
            {selectedBot && (
              <div className="space-y-6">
                {!shouldAutoSelect && (
                  <div className="rounded-lg bg-gray-800 p-6">
                    <h2 className="mb-4 text-xl font-semibold text-white">
                      Select Macro
                    </h2>
                    <MacroSelector
                      macros={macros}
                      selectedMacro={selectedMacro}
                      onSelectMacro={setSelectedMacro}
                      isSingleOption={shouldAutoSelect}
                    />
                  </div>
                )}

                {macroData && (
                  <InstructionsDisplay
                    instructions={macroData.instructions}
                    macroContent={macroData.content}
                    platform={platform}
                    mcu={mcu}
                    onDownload={handleDownload}
                    isCompiling={isCompiling}
                    config={macroData.config ?? []}
                    configValues={configValues}
                    onConfigChange={setConfigValues}
                  />
                )}

                {!macroData && selectedBot && (
                  <div className="rounded-lg bg-gray-800 p-8 text-center">
                    <p className="text-gray-400">
                      {shouldAutoSelect
                        ? 'Loading...'
                        : 'Select a macro to view instructions and download'}
                    </p>
                  </div>
                )}
              </div>
            )}
          </main>
        </div>
      </div>
    </div>
  );
}
