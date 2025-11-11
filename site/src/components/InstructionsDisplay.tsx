import ReactMarkdown from 'react-markdown';

interface InstructionsDisplayProps {
  instructions: string;
  macroContent: string;
  mcu: string;
  onDownload: () => void;
  isCompiling: boolean;
}

export default function InstructionsDisplay({
  instructions,
  macroContent,
  mcu,
  onDownload,
  isCompiling,
}: InstructionsDisplayProps) {
  return (
    <div className="space-y-6">
      {/* Instructions Section */}
      <div className="rounded-lg bg-gray-800 p-6">
        <div className="prose prose-invert max-w-none">
          <ReactMarkdown
            components={{
              h1: ({ children }) => (
                <h1 className="mb-4 text-2xl font-bold text-white">{children}</h1>
              ),
              h2: ({ children }) => (
                <h2 className="mb-3 mt-4 text-xl font-semibold text-white">
                  {children}
                </h2>
              ),
              h3: ({ children }) => (
                <h3 className="mb-2 mt-3 text-lg font-semibold text-white">
                  {children}
                </h3>
              ),
              p: ({ children }) => (
                <p className="mb-3 text-gray-300">{children}</p>
              ),
              ul: ({ children }) => (
                <ul className="mb-3 ml-6 list-disc text-gray-300">{children}</ul>
              ),
              ol: ({ children }) => (
                <ol className="mb-3 ml-6 list-decimal text-gray-300">{children}</ol>
              ),
              li: ({ children }) => <li className="mb-1">{children}</li>,
              strong: ({ children }) => (
                <strong className="font-semibold text-white">{children}</strong>
              ),
              em: ({ children }) => <em className="italic">{children}</em>,
              code: ({ children }) => (
                <code className="rounded bg-gray-700 px-1.5 py-0.5 font-mono text-sm text-purple-300">
                  {children}
                </code>
              ),
              pre: ({ children }) => (
                <pre className="mb-3 overflow-x-auto rounded-lg bg-gray-900 p-4">
                  {children}
                </pre>
              ),
            }}
          >
            {instructions}
          </ReactMarkdown>
        </div>
      </div>

      {/* Download Section */}
      <div className="rounded-lg bg-gray-800 p-6">
        <h3 className="mb-4 text-lg font-semibold text-white">
          Download Firmware
        </h3>
        <div className="space-y-4">
          <div>
            <label className="mb-2 block text-sm text-gray-300">
              Microcontroller: <span className="font-semibold text-white">{mcu}</span>
            </label>
          </div>
          <button
            onClick={onDownload}
            disabled={isCompiling}
            className="w-full rounded-lg bg-purple-600 px-6 py-3 font-semibold text-white transition-colors hover:bg-purple-700 disabled:cursor-not-allowed disabled:bg-gray-600"
          >
            {isCompiling ? 'Compiling...' : 'Download Compiled Firmware'}
          </button>
        </div>
      </div>
    </div>
  );
}
