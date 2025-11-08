export default function MacroLegend() {
  return (
    <div className="mt-6 rounded-lg bg-gray-800 p-6">
      <h2 className="mb-4 text-2xl font-bold text-white">Macro Syntax Guide</h2>

      <div className="grid gap-6 md:grid-cols-2">
        {/* Basic Format */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Basic Format</h3>
          <div className="rounded bg-gray-900 p-3 font-mono text-sm text-gray-300">
            <div>inputs,duration</div>
            <div className="mt-2 text-xs text-gray-500">
              # Example: A button for 5 frames
            </div>
            <div>A,5</div>
          </div>
        </div>

        {/* Timing */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Timing</h3>
          <ul className="space-y-1 text-sm text-gray-300">
            <li>• 1 frame = 8ms (125Hz USB polling)</li>
            <li>• 125 frames = 1 second</li>
            <li>• Minimum safe: 5 frames (40ms)</li>
          </ul>
        </div>

        {/* Face Buttons */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Face Buttons</h3>
          <div className="grid grid-cols-4 gap-2 text-sm text-gray-300">
            <span className="rounded bg-gray-700 px-2 py-1">Y</span>
            <span className="rounded bg-gray-700 px-2 py-1">B</span>
            <span className="rounded bg-gray-700 px-2 py-1">A</span>
            <span className="rounded bg-gray-700 px-2 py-1">X</span>
          </div>
        </div>

        {/* Shoulder Buttons */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Shoulder Buttons</h3>
          <div className="grid grid-cols-4 gap-2 text-sm text-gray-300">
            <span className="rounded bg-gray-700 px-2 py-1">L</span>
            <span className="rounded bg-gray-700 px-2 py-1">R</span>
            <span className="rounded bg-gray-700 px-2 py-1">ZL</span>
            <span className="rounded bg-gray-700 px-2 py-1">ZR</span>
          </div>
        </div>

        {/* System Buttons */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">System Buttons</h3>
          <div className="grid grid-cols-4 gap-2 text-sm text-gray-300">
            <span className="rounded bg-gray-700 px-2 py-1">PLUS</span>
            <span className="rounded bg-gray-700 px-2 py-1">MINUS</span>
            <span className="rounded bg-gray-700 px-2 py-1">HOME</span>
            <span className="rounded bg-gray-700 px-2 py-1">CAPTURE</span>
          </div>
        </div>

        {/* D-Pad */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">D-Pad</h3>
          <div className="grid grid-cols-4 gap-2 text-sm text-gray-300">
            <span className="rounded bg-gray-700 px-2 py-1">UP</span>
            <span className="rounded bg-gray-700 px-2 py-1">DOWN</span>
            <span className="rounded bg-gray-700 px-2 py-1">LEFT</span>
            <span className="rounded bg-gray-700 px-2 py-1">RIGHT</span>
            <span className="rounded bg-gray-700 px-2 py-1 text-xs">
              UPRIGHT
            </span>
            <span className="rounded bg-gray-700 px-2 py-1 text-xs">
              DOWNRIGHT
            </span>
            <span className="rounded bg-gray-700 px-2 py-1 text-xs">
              DOWNLEFT
            </span>
            <span className="rounded bg-gray-700 px-2 py-1 text-xs">
              UPLEFT
            </span>
          </div>
        </div>

        {/* Left Stick */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Left Stick</h3>
          <div className="space-y-1 text-sm text-gray-300">
            <div className="grid grid-cols-4 gap-2">
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">LUP</span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                LDOWN
              </span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                LLEFT
              </span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                LRIGHT
              </span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                LCLICK
              </span>
            </div>
            <div className="rounded bg-gray-900 p-2 font-mono text-xs">
              L(x,y) where x,y = 0-255
              <br />
              L(128,255) - Up full
            </div>
          </div>
        </div>

        {/* Right Stick */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Right Stick</h3>
          <div className="space-y-1 text-sm text-gray-300">
            <div className="grid grid-cols-4 gap-2">
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">RUP</span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                RDOWN
              </span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                RLEFT
              </span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                RRIGHT
              </span>
              <span className="rounded bg-gray-700 px-2 py-1 text-xs">
                RCLICK
              </span>
            </div>
            <div className="rounded bg-gray-900 p-2 font-mono text-xs">
              R(x,y) where x,y = 0-255
              <br />
              R(128,128) - Center
            </div>
          </div>
        </div>

        {/* Multiple Inputs */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Multiple Inputs</h3>
          <div className="rounded bg-gray-900 p-3 font-mono text-sm text-gray-300">
            <div>A+B,10</div>
            <div>L+R+A,5</div>
            <div>LUP+A,20</div>
          </div>
        </div>

        {/* Wait */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Wait</h3>
          <div className="rounded bg-gray-900 p-3 font-mono text-sm text-gray-300">
            <div>WAIT,125 - 1 second wait</div>
          </div>
        </div>

        {/* Include Macros */}
        <div>
          <h3 className="mb-2 font-semibold text-blue-400">Include Macros</h3>
          <div className="rounded bg-gray-900 p-3 font-mono text-sm text-gray-300">
            <div>@macroname</div>
            <div>@macroname,*5 - Repeat 5 times</div>
          </div>
        </div>

        {/* Example Macro */}
        <div className="md:col-span-2">
          <h3 className="mb-2 font-semibold text-blue-400">Complete Example</h3>
          <div className="rounded bg-gray-900 p-3 font-mono text-sm text-gray-300">
            <div>X,5</div>
            <div>WAIT,250</div>
            <div>DOWN,5</div>
            <div>WAIT,10</div>
            <div>A,5</div>
            <div>WAIT,125</div>
            <div>L(64,200),30</div>
          </div>
        </div>
      </div>
    </div>
  );
}
