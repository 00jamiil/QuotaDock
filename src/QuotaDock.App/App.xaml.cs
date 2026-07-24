using Microsoft.UI.Xaml;
using QuotaDock.App.Runtime;

namespace QuotaDock.App;

public partial class App : Application
{
    private Window? window;
    private QuotaDockRuntime? runtime;
    private DetailsWindow? detailsWindow;
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

    internal async void ExitApplication()
    {
        if (isExiting)
        {
            return;
        }

        isExiting = true;
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
        // Always log the exception so we can diagnose crashes.
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuotaDock",
                "crash.log");
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}] {args.Exception}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never interfere with the app.
        }

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

        // In normal mode, swallow the exception so the app stays alive.
        // A provider refresh or UI rebuild error must never crash the widget.
        args.Handled = true;
    }
}
