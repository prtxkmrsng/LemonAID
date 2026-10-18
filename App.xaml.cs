using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LemonAID.Services;
using Microsoft.UI.Xaml;

namespace LemonAID;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\LemonAID_PowerToys_Mutex";
    private const string ShowDashboardEventName = "Local\\LemonAID_ShowDashboard_Event";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showDashboardEvent;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _showDashboardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowDashboardEventName);
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Signal the running background instance to open its dashboard window and exit this duplicate process
            _showDashboardEvent.Set();
            Environment.Exit(0);
            return;
        }

        var settings = BackgroundSettingsService.Load();
        string[] cmdArgs = Environment.GetCommandLineArgs();
        bool cliHeadless = cmdArgs.Any(a =>
            a.Equals("--headless", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        bool startHeadless = cliHeadless || (settings.OnboardingCompleted && settings.StartMinimizedHeadless);

        _window = new MainWindow(startHeadless);

        // Listen for second-instance launch signals to wake up the dashboard from headless mode
        _ = Task.Run(() =>
        {
            while (true)
            {
                _showDashboardEvent.WaitOne();
                _window?.DispatcherQueue.TryEnqueue(() => _window.ShowDashboardWindow());
            }
        });
    }
}
