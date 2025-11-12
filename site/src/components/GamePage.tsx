import { useState, useEffect, useRef } from 'react';
import BotSelector from './BotSelector';
import MacroSelector from './MacroSelector';
import McuSelector from './McuSelector';
import InstructionsDisplay from './InstructionsDisplay';
import Header from './Header';

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
}

export default function GamePage({ gameId, gameTitle }: GamePageProps) {
  const [bots, setBots] = useState<BotOption[]>([]);
  const [selectedBot, setSelectedBot] = useState<string | null>(null);
  const [macros, setMacros] = useState<MacroOption[]>([]);
  const [selectedMacro, setSelectedMacro] = useState<string | null>(null);
  const [macroData, setMacroData] = useState<MacroData | null>(null);
  const [mcu, setMcu] = useState('atmega16u2');
  const [isCompiling, setIsCompiling] = useState(false);
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
            displayName: botId.replace(/-/g, ' ').replace(/\b\w/g, (l: string) => l.toUpperCase()),
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

    const loadMacros = async () => {
      try {
        const response = await fetch(
          `/api/list-macros?game=${gameId}&bot=${selectedBot}`,
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
    if (!selectedBot || !selectedMacro) {
      setMacroData(null);
      return;
    }

    const abortController = new AbortController();

    const loadMacroData = async () => {
      const botAtStart = selectedBot;
      const macroAtStart = selectedMacro;

      // Wait a tick to let the macros state update
      await new Promise(resolve => setTimeout(resolve, 0));

      // Check if selection changed while we waited
      if (selectionRef.current.bot !== botAtStart || selectionRef.current.macro !== macroAtStart) {
        return;
      }

      // Now verify with the current macros array
      if (macros.length === 0) {
        return;
      }

      const macroExists = macros.some((m) => m.filename === macroAtStart);
      if (!macroExists) {
        setMacroData(null);
        return;
      }

      try {
        const response = await fetch(
          `/api/get-macro?game=${gameId}&bot=${botAtStart}&filename=${macroAtStart}`,
          { signal: abortController.signal }
        );
        const data = await response.json();

        // Only update if not aborted and selection hasn't changed
        if (
          !abortController.signal.aborted &&
          selectionRef.current.bot === botAtStart &&
          selectionRef.current.macro === macroAtStart &&
          data.content &&
          data.instructions
        ) {
          setMacroData(data);
        }
      } catch (error: unknown) {
        // Ignore abort errors
        if (error instanceof Error && error.name === 'AbortError') {
          return;
        }

        // Only log if the selection is still the same (not a stale request)
        if (
          selectionRef.current.bot === botAtStart &&
          selectionRef.current.macro === macroAtStart
        ) {
          console.error('Failed to load macro data:', error);
        }
      }
    };

    loadMacroData();

    // Cleanup: abort the fetch if the selection changes
    return () => {
      abortController.abort();
    };
  }, [selectedBot, selectedMacro, gameId, macros]);

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
          mcu,
          loop: true,
          savedMacros: {},
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
      a.download = selectedMacro.replace('.macro', '.hex');
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
              <McuSelector selectedMcu={mcu} onSelectMcu={setMcu} />
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
                    mcu={mcu}
                    onDownload={handleDownload}
                    isCompiling={isCompiling}
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
