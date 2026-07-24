using Microsoft.UI.Xaml;
using QuotaDock.App.Runtime;
using QuotaDock.Core.Domain;

namespace QuotaDock.App;

public partial class App : Application
{
    private Window? window;
    private QuotaDockRuntime? runtime;
    private DetailsWindow? detailsWindow;
    private readonly Dictionary<ProviderKind, DashboardReaderWindow> dashboardWindows = [];
    private bool isExiting;
    private bool isEndToEndMode;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        isEndToEndMode = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--e2e", StringComparison.OrdinalIgnoreCase));
        runtime = new QuotaDockRuntime(isEndToEndMode);
        window = new MainWindow(runtime);
        window.Activate();
    }

    internal void ShowDetails()
    {
        if (runtime is null)
        {
            return;
        }

        detailsWindow ??= new DetailsWindow(runtime);
        detailsWindow.Closed += (_, _) => detailsWindow = null;
        detailsWindow.Activate();
    }

    internal async void OpenDashboardReader(ProviderKind provider)
    {
        if (runtime is null || provider != ProviderKind.Alibaba)
        {
            return;
        }

        if (dashboardWindows.TryGetValue(provider, out var existing))
        {
            existing.Activate();
            return;
        }

        var connection = await runtime.EnsureDashboardConnectionAsync(provider);
        var reader = new DashboardReaderWindow(runtime, connection);
        dashboardWindows[provider] = reader;
        reader.Closed += (_, _) => dashboardWindows.Remove(provider);
        reader.Activate();
    }

    internal async void ExitApplication()
    {
        if (isExiting)
        {
            return;
        }

        isExiting = true;
        foreach (var dashboardWindow in dashboardWindows.Values.ToArray())
        {
            dashboardWindow.AllowClose();
            dashboardWindow.Close();
        }
        dashboardWindows.Clear();
        detailsWindow?.Close();
        if (window is MainWindow mainWindow)
        {
            mainWindow.AllowClose();
            mainWindow.Close();
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync();
        }

        Exit();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        if (isEndToEndMode)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "quotadock-e2e-error.txt"),
                    args.Exception.ToString());
            }
            catch
            {
                // Diagnostics must never interfere with the isolated test run.
            }
        }

        args.Handled = isEndToEndMode;
    }
}
