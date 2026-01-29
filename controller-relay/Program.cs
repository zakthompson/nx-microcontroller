using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SwitchController
{
    /// <summary>
    /// Main program entry point for the controller relay GUI
    /// </summary>
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Check for headless mode flag
            if (args.Contains("--headless") || args.Contains("-h"))
            {
                RunHeadless();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// Runs the relay in headless mode using saved configuration
        /// </summary>
        private static void RunHeadless()
        {
            Console.WriteLine("Controller Relay - Headless Mode");
            Console.WriteLine("================================");
            Console.WriteLine();

            // Load configuration
            var config = Configuration.Load();

            if (string.IsNullOrEmpty(config.ComPort))
            {
                Console.WriteLine("Error: No COM port configured. Please run the GUI once to configure the COM port.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Using COM port: {config.ComPort}");
            Console.WriteLine($"Firmware type: {config.FirmwareType}");
            Console.WriteLine($"Controller type: {config.ControllerType}");
            Console.WriteLine();
            Console.WriteLine("Starting relay session...");
            Console.WriteLine("Press Ctrl+C to stop, or use the quit hotkey combo on your controller.");
            Console.WriteLine();

            // Create and start relay session
            var session = new RelaySession(config, config.ComPort);
            session.StatusUpdate += Console.WriteLine;

            bool sessionEnded = false;
            session.SessionStopped += () => { sessionEnded = true; };

            // Handle Ctrl+C
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nStopping relay session...");
                session.Stop();
            };

            session.Start();

            // Wait for session to end
            while (!sessionEnded)
            {
                Thread.Sleep(100);
            }

            Console.WriteLine("Relay session ended.");
        }
    }
}
