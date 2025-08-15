using System;
using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;
using SimpleSteamSwitcher.Services;

namespace SimpleSteamSwitcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FreeConsole();

        protected override void OnStartup(StartupEventArgs e)
        {
            // Set up global exception handling FIRST
            SetupGlobalExceptionHandling();
            
            // Allocate a console for this GUI application
            AllocConsole();
            
            System.Console.WriteLine("=== SimpleSteamSwitcher Debug Console ===");
            System.Console.WriteLine("Console logging enabled!");
            System.Console.WriteLine("You will see live debug output here.");
            System.Console.WriteLine("==========================================");
            
            base.OnStartup(e);
        }

        private void SetupGlobalExceptionHandling()
        {
            var logger = new LogService();
            
            // Handle all unhandled exceptions on the UI thread
            this.DispatcherUnhandledException += (sender, args) =>
            {
                logger.LogError("=== UNHANDLED UI THREAD EXCEPTION ===");
                logger.LogError($"Exception: {args.Exception.Message}");
                logger.LogError($"Type: {args.Exception.GetType().Name}");
                logger.LogError($"Stack Trace: {args.Exception.StackTrace}");
                if (args.Exception.InnerException != null)
                {
                    logger.LogError($"Inner Exception: {args.Exception.InnerException.Message}");
                    logger.LogError($"Inner Stack Trace: {args.Exception.InnerException.StackTrace}");
                }
                logger.LogError("=== END UNHANDLED EXCEPTION ===");
                
                // Also log to console
                System.Console.WriteLine($"[CRASH] {args.Exception.Message}");
                System.Console.WriteLine($"[CRASH] {args.Exception.StackTrace}");
                
                // Prevent the application from crashing
                args.Handled = true;
            };
            
            // Handle unhandled exceptions on background threads
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                if (ex != null)
                {
                    logger.LogError("=== UNHANDLED BACKGROUND THREAD EXCEPTION ===");
                    logger.LogError($"Exception: {ex.Message}");
                    logger.LogError($"Type: {ex.GetType().Name}");
                    logger.LogError($"Stack Trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        logger.LogError($"Inner Exception: {ex.InnerException.Message}");
                        logger.LogError($"Inner Stack Trace: {ex.InnerException.StackTrace}");
                    }
                    logger.LogError($"Is Terminating: {args.IsTerminating}");
                    logger.LogError("=== END UNHANDLED EXCEPTION ===");
                    
                    // Also log to console
                    System.Console.WriteLine($"[CRASH] Background thread: {ex.Message}");
                    System.Console.WriteLine($"[CRASH] {ex.StackTrace}");
                }
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Free the console when the application exits
            FreeConsole();
            base.OnExit(e);
        }
    }
} 