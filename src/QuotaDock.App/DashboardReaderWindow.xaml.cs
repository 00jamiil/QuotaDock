using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using QuotaDock.App.Runtime;
using QuotaDock.Connectors.Alibaba;
using QuotaDock.Connectors.Dashboard;
using QuotaDock.Core.Domain;
using WinRT.Interop;
using Windows.Graphics;

namespace QuotaDock.App;

public sealed partial class DashboardReaderWindow : Window
{
    private readonly QuotaDockRuntime runtime;
    private readonly ConnectorConnection connection;
    private readonly DashboardNavigationPolicy navigationPolicy;
    private readonly Uri startUri;
    private readonly DispatcherTimer refreshTimer;
    private readonly AppWindow appWindow;
    private bool initialized;
    private bool closeAllowed;

    public DashboardReaderWindow(QuotaDockRuntime runtime, ConnectorConnection connection)
    {
        this.runtime = runtime;
        this.connection = connection;
        InitializeComponent();

        var handle = WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Resize(new SizeInt32(980, 760));
        appWindow.Closing += AppWindow_Closing;

        if (connection.Provider == ProviderKind.Alibaba)
        {
            ReaderTitle.Text = "Alibaba Token Plan International";
            Title = "QuotaDock — Alibaba isolated reader";
            startUri = new Uri("https://modelstudio.console.alibabacloud.com/");
            navigationPolicy = new DashboardNavigationPolicy(["alibabacloud.com", "aliyun.com"]);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(connection), "Only the Alibaba dashboard reader is supported.");
        }

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        refreshTimer.Tick += RefreshTimer_Tick;
        Browser.Loaded += Browser_Loaded;
        Closed += (_, _) => refreshTimer.Stop();
    }

    internal void AllowClose() => closeAllowed = true;

    private async void Browser_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        try
        {
            var profilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuotaDock",
                "WebView2",
                "alibaba");
            Directory.CreateDirectory(profilePath);
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty,
                profilePath,
                new CoreWebView2EnvironmentOptions());
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) ||
                    !navigationPolicy.IsAllowed(destination))
                {
                    args.Cancel = true;
                    ReaderStatus.Severity = InfoBarSeverity.Warning;
                    ReaderStatus.Message = "Navigation outside the provider allowlist was blocked.";
                }
            };
            Browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                ReaderStatus.Severity = InfoBarSeverity.Warning;
                ReaderStatus.Message = "Pop-up navigation was blocked. Continue in this provider window.";
            };
            Browser.CoreWebView2.DownloadStarting += (_, args) =>
            {
                args.Cancel = true;
                ReaderStatus.Severity = InfoBarSeverity.Warning;
                ReaderStatus.Message = "Downloads are disabled in dashboard readers.";
            };
            Browser.CoreWebView2.PermissionRequested += (_, args) =>
            {
                args.State = CoreWebView2PermissionState.Deny;
                args.Handled = true;
            };
            Browser.Source = startUri;
            refreshTimer.Start();
        }
        catch
        {
            ReaderStatus.Severity = InfoBarSeverity.Error;
            ReaderStatus.Message = "The isolated provider browser could not be started.";
        }
    }

    private async void Capture_Click(object sender, RoutedEventArgs e) => await CaptureAsync();

    private async void RefreshTimer_Tick(object? sender, object e)
    {
        if (Browser.CoreWebView2 is null || Browser.Source is null || !navigationPolicy.IsAllowed(Browser.Source))
        {
            return;
        }

        var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            sender.NavigationCompleted -= NavigationCompleted;
            navigation.TrySetResult(args.IsSuccess);
        }

        Browser.CoreWebView2.NavigationCompleted += NavigationCompleted;
        Browser.Reload();
        try
        {
            if (await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                await CaptureAsync();
            }
        }
        catch (TimeoutException)
        {
            Browser.CoreWebView2.NavigationCompleted -= NavigationCompleted;
            ReaderStatus.Severity = InfoBarSeverity.Warning;
            ReaderStatus.Message = "The provider page did not finish refreshing; saved values were kept.";
        }
    }

    private async Task CaptureAsync()
    {
        var current = Browser.Source;
        if (Browser.CoreWebView2 is null || current is null || !navigationPolicy.IsAllowed(current))
        {
            ReaderStatus.Severity = InfoBarSeverity.Warning;
            ReaderStatus.Message = "Open the provider usage page before reading usage.";
            return;
        }

        ReaderStatus.Severity = InfoBarSeverity.Informational;
        ReaderStatus.Message = "Reading visible usage values…";
        try
        {
            var encoded = await Browser.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
            var visibleText = JsonSerializer.Deserialize<string>(encoded) ?? string.Empty;
            if (visibleText.Length > 2_000_000)
            {
                ReaderStatus.Severity = InfoBarSeverity.Error;
                ReaderStatus.Message = "The provider page was unexpectedly large and was not processed.";
                return;
            }

            var result = AlibabaDashboardTextParser.Parse(
                connection.Id,
                visibleText,
                TimeProvider.System.GetUtcNow());

            visibleText = string.Empty;
            await runtime.SaveDashboardResultAsync(connection, result);
            ReaderStatus.Severity = result.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            ReaderStatus.Message = result.IsSuccess
                ? "Usage captured. No raw page content was saved."
                : result.Message ?? "The expected usage page format changed.";
        }
        catch
        {
            ReaderStatus.Severity = InfoBarSeverity.Error;
            ReaderStatus.Message = "Visible usage could not be read. Saved values were kept.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => appWindow.Hide();

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!closeAllowed)
        {
            args.Cancel = true;
            sender.Hide();
        }
    }
}
