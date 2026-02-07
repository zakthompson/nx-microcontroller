import ReactMarkdown from 'react-markdown';
import { PLATFORM_OPTIONS } from './PlatformSelector';
import type { MacroConfigOption } from '../lib/macro-frontmatter';

interface InstructionsDisplayProps {
  instructions: string;
  macroContent: string;
  platform: string;
  mcu: string;
  onDownload: () => void;
  isCompiling: boolean;
  config: MacroConfigOption[];
  configValues: Record<string, string | number>;
  onConfigChange: (values: Record<string, string | number>) => void;
}

function getTargetLabel(platform: string, mcu: string): string {
  const platformOption = PLATFORM_OPTIONS.find((p) => p.value === platform);
  if (!platformOption) return mcu;

  if (!platformOption.mcuOptions) return platformOption.label;

  const mcuOption = platformOption.mcuOptions.find((m) => m.value === mcu);
  return mcuOption?.label ?? mcu;
}

function ConfigField({
  option,
  value,
  onChange,
}: {
  option: MacroConfigOption;
  value: string | number | undefined;
  onChange: (value: string | number) => void;
}) {
  if (option.type === 'number') {
    return (
      <div className="space-y-1">
        <label
          htmlFor={`config-${option.id}`}
          className="block text-sm font-medium text-gray-300"
        >
          {option.label}
        </label>
        <input
          id={`config-${option.id}`}
          type="number"
          min={option.min}
          max={option.max}
          value={value ?? option.default}
          onChange={(e) => {
            const num = Number(e.target.value);
            if (Number.isFinite(num)) {
              onChange(num);
            }
          }}
          className="w-full rounded-lg border border-gray-600 bg-gray-700 px-4 py-2 text-white focus:border-purple-500 focus:ring-2 focus:ring-purple-500 focus:outline-none"
        />
      </div>
    );
  }

  return (
    <div className="space-y-1">
      <label
        htmlFor={`config-${option.id}`}
        className="block text-sm font-medium text-gray-300"
      >
        {option.label}
      </label>
      <select
        id={`config-${option.id}`}
        value={value ?? option.options[0].value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-lg border border-gray-600 bg-gray-700 px-4 py-2 text-white focus:border-purple-500 focus:ring-2 focus:ring-purple-500 focus:outline-none"
      >
        {option.options.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
    </div>
  );
}

export default function InstructionsDisplay({
  instructions,
  macroContent: _macroContent,
  platform,
  mcu,
  onDownload,
  isCompiling,
  config,
  configValues,
  onConfigChange,
}: InstructionsDisplayProps) {
  return (
    <div className="space-y-6">
      {/* Instructions Section */}
      <div className="rounded-lg bg-gray-800 p-6">
        <div className="prose prose-invert max-w-none">
          <ReactMarkdown
            components={{
              h1: ({ children }) => (
                <h1 className="mb-4 text-2xl font-bold text-white">
                  {children}
                </h1>
              ),
              h2: ({ children }) => (
                <h2 className="mt-4 mb-3 text-xl font-semibold text-white">
                  {children}
                </h2>
              ),
              h3: ({ children }) => (
                <h3 className="mt-3 mb-2 text-lg font-semibold text-white">
                  {children}
                </h3>
              ),
              p: ({ children }) => (
                <p className="mb-3 text-gray-300">{children}</p>
              ),
              ul: ({ children }) => (
                <ul className="mb-3 ml-6 list-disc text-gray-300">
                  {children}
                </ul>
              ),
              ol: ({ children }) => (
                <ol className="mb-3 ml-6 list-decimal text-gray-300">
                  {children}
                </ol>
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

      {/* Configuration Section */}
      {config.length > 0 && (
        <div className="rounded-lg bg-gray-800 p-6">
          <h3 className="mb-4 text-lg font-semibold text-white">
            Configuration
          </h3>
          <div className="space-y-4">
            {config.map((option) => (
              <ConfigField
                key={option.id}
                option={option}
                value={configValues[option.id]}
                onChange={(value) =>
                  onConfigChange({ ...configValues, [option.id]: value })
                }
              />
            ))}
          </div>
        </div>
      )}

      {/* Download Section */}
      <div className="rounded-lg bg-gray-800 p-6">
        <h3 className="mb-4 text-lg font-semibold text-white">
          Download Firmware
        </h3>
        <div className="space-y-4">
          <div>
            <label className="mb-2 block text-sm text-gray-300">
              Target:{' '}
              <span className="font-semibold text-white">
                {getTargetLabel(platform, mcu)}
              </span>
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
