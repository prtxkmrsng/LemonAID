using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LemonAID.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace LemonAID;

public partial class App : Application
{
    private static readonly string SingleInstanceKey = $"LemonAID_Instance_{Environment.UserName}";
    private static readonly string SingleInstanceMutexName = $"Local\\LemonAID_{Environment.UserName}_InstanceMutex_v2";
    private static readonly string ShowDashboardEventName = $"Local\\LemonAID_{Environment.UserName}_ShowEvent_v2";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showDashboardEvent;
    private MainWindow? _window;

    private static string GetLogFilePath()
    {
        try
        {
            string tempDir = Path.GetTempPath();
            return Path.Combine(tempDir, "lemonaid.log");
        }
        catch
        {
            return "lemonaid.log";
        }
    }

    private static void LogError(string context, Exception? ex)
    {
        try
        {
            string path = GetLogFilePath();
            string msg = $"[{DateTime.UtcNow:O}] {context}: {ex?.Message}\n{ex}\n{ex?.StackTrace}\n\n";
            File.AppendAllText(path, msg);
        }
        catch { }
    }

    public App()
    {
        UnhandledException += (s, e) =>
        {
            LogError($"UnhandledException ({e.Message})", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogError("AppDomain UnhandledException", e.ExceptionObject as Exception);
        };
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            bool isFirstInstance = true;

            // 1. Try Windows App SDK AppInstance registration
            try
            {
                var mainInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
                if (!mainInstance.IsCurrent)
                {
                    var current = AppInstance.GetCurrent();
                    var activatedArgs = current.GetActivatedEventArgs();
                    await mainInstance.RedirectActivationToAsync(activatedArgs);
                    Environment.Exit(0);
                    return;
                }

                mainInstance.Activated += (sender, eventArgs) =>
                {
                    _window?.DispatcherQueue?.TryEnqueue(() => _window.ShowDashboardWindow());
                };
            }
            catch (Exception ex)
            {
                LogError("AppInstance registration failed, using fallback mutex", ex);
            }

            // 2. Secondary fallback via Mutex & EventWaitHandle
            try
            {
                _instanceMutex = new Mutex(true, SingleInstanceMutexName, out isFirstInstance);
            }
            catch (AbandonedMutexException)
            {
                isFirstInstance = true;
            }
            catch (Exception ex)
            {
                LogError("Mutex creation threw exception, proceeding as first instance", ex);
                isFirstInstance = true;
            }

            try
            {
                _showDashboardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowDashboardEventName);
            }
            catch (Exception ex)
            {
                LogError("EventWaitHandle creation threw exception", ex);
            }

            if (!isFirstInstance)
            {
                try
                {
                    _showDashboardEvent?.Set();
                }
                catch { }
                Environment.Exit(0);
                return;
            }

            string[] cmdArgs = Environment.GetCommandLineArgs();
            bool cliHeadless = cmdArgs.Any(a =>
                a.Equals("--headless", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--background", StringComparison.OrdinalIgnoreCase));

            // Start headless ONLY if explicitly requested via command line flags (e.g. at Windows startup)
            bool startHeadless = cliHeadless;

            _window = new MainWindow(startHeadless);

            // Listen for second-instance launch signals to wake up the dashboard from headless mode
            if (_showDashboardEvent != null)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        while (true)
                        {
                            _showDashboardEvent.WaitOne();
                            _window?.DispatcherQueue?.TryEnqueue(() => _window.ShowDashboardWindow());
                        }
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            LogError("Fatal error in OnLaunched", ex);
        }
    }
}
