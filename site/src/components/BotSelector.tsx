interface BotOption {
  id: string;
  displayName: string;
}

interface BotSelectorProps {
  bots: BotOption[];
  selectedBot: string | null;
  onSelectBot: (botId: string) => void;
}

export default function BotSelector({
  bots,
  selectedBot,
  onSelectBot,
}: BotSelectorProps) {
  return (
    <div className="space-y-2">
      <h2 className="text-lg font-semibold text-white">Select Bot</h2>
      <div className="space-y-1">
        {bots.map((bot) => (
          <button
            key={bot.id}
            onClick={() => onSelectBot(bot.id)}
            className={`w-full rounded-lg px-4 py-3 text-left text-sm font-medium transition-colors ${
              selectedBot === bot.id
                ? 'bg-purple-600 text-white'
                : 'bg-gray-700 text-gray-300 hover:bg-gray-600'
            }`}
          >
            {bot.displayName}
          </button>
        ))}
      </div>
    </div>
  );
}
