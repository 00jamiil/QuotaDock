using System.Collections.ObjectModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using QuotaDock.App.Presentation;
using QuotaDock.App.Runtime;
using QuotaDock.Core.Domain;
using WinRT.Interop;
using Windows.Graphics;

namespace QuotaDock.App;

public sealed partial class MainWindow : Window
{
    private readonly QuotaDockRuntime runtime;
    private readonly ObservableCollection<MetricCardViewModel> metricCards = [];
    private readonly HashSet<string> notifiedMetrics = new(StringComparer.Ordinal);
    private AppWindow? appWindow;
    private OverlappedPresenter? presenter;
    private TrayIconService? trayIcon;
    private bool isPinned;
    private bool closeAllowed;
    private bool initialized;

    public MainWindow(QuotaDockRuntime runtime)
    {
        this.runtime = runtime;
        InitializeComponent();
        MetricList.ItemsSource = metricCards;
        ConfigureNativeWindow();
        WidgetRoot.Loaded += WidgetRoot_Loaded;
        runtime.StateChanged += Runtime_StateChanged;
    }

    internal void AllowClose()
    {
        closeAllowed = true;
        trayIcon?.Dispose();
    }

    internal void ToggleVisibility()
    {
        if (appWindow?.IsVisible == true)
        {
            appWindow.Hide();
        }
        else
        {
            appWindow?.Show();
            Activate();
        }
    }

    private void ConfigureNativeWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        var handle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(360, 560));
        appWindow.Closing += AppWindow_Closing;
        presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        if (!runtime.IsEndToEndMode)
        {
            trayIcon = new TrayIconService(
                handle,
                ToggleVisibility,
                () => _ = RefreshAsync(),
                () => ((App)Application.Current).ShowDetails(),
                () => ((App)Application.Current).ExitApplication());
        }
    }

    private async void WidgetRoot_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        StatusLabel.Text = "Loading local usage…";
        try
        {
            await runtime.InitializeAsync();
            RestoreWindowPlacement();
            RebuildCards();
        }
        catch
        {
            StatusLabel.Text = "Local data could not be opened";
            HealthDot.Fill = ResourceBrush("QuotaDockDangerBrush");
        }
    }

    private void Runtime_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RebuildCards);
    }

    private void RebuildCards()
    {
        var now = TimeProvider.System.GetUtcNow();
        var all = runtime.Snapshots
            .SelectMany(snapshot => snapshot.Metrics.Select(metric =>
                MetricCardViewModel.Create(snapshot, metric, runtime.Settings, now)))
            .ToArray();
        var pinned = runtime.Settings.PinnedMetricIds;
        var selected = pinned.Count > 0
            ? pinned.Select(key => all.FirstOrDefault(metric => metric.Key == key))
                .Where(metric => metric is not null)
                .Cast<MetricCardViewModel>()
            : all.AsEnumerable();

        metricCards.Clear();
        foreach (var metric in selected.Take(4))
        {
            metricCards.Add(metric);
        }

        MetricList.Visibility = metricCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = metricCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var staleCount = runtime.Snapshots.Count(snapshot => snapshot.Health == ConnectionHealth.Stale);
        if (!string.IsNullOrWhiteSpace(runtime.LastError))
        {
            StatusLabel.Text = runtime.LastError;
            HealthDot.Fill = ResourceBrush("QuotaDockWarningBrush");
        }
        else if (metricCards.Count == 0)
        {
            StatusLabel.Text = "Connect a provider to begin";
            HealthDot.Fill = ResourceBrush("QuotaDockMutedBrush");
        }
        else if (staleCount > 0)
        {
            StatusLabel.Text = $"Showing saved values · {staleCount} stale";
            HealthDot.Fill = ResourceBrush("QuotaDockWarningBrush");
        }
        else
        {
            StatusLabel.Text = $"{runtime.Snapshots.Count} account{(runtime.Snapshots.Count == 1 ? string.Empty : "s")} up to date";
            HealthDot.Fill = ResourceBrush("QuotaDockAccentBrush");
        }

        var latest = runtime.Snapshots.OrderByDescending(snapshot => snapshot.CapturedAt).FirstOrDefault();
        FreshnessLabel.Text = latest is null
            ? "No local snapshots"
            : $"Updated {MetricCardViewModel.Create(latest, latest.Metrics[0], runtime.Settings, now).Freshness}";
        EvaluateNotifications();
    }

    private void EvaluateNotifications()
    {
        foreach (var snapshot in runtime.Snapshots)
        {
            foreach (var metric in snapshot.Metrics)
            {
                var key = $"{snapshot.ConnectionId}:{metric.Id}";
                if (!runtime.Settings.Notifications.TryGetValue(key, out var preference) ||
                    !preference.Enabled)
                {
                    notifiedMetrics.Remove(key);
                    continue;
                }

                var limit = metric.Limit;
                if (limit is null && runtime.Settings.SoftBudgets.TryGetValue(key, out var budget))
                {
                    limit = budget;
                }

                if (limit is not > 0m)
                {
                    continue;
                }

                var consumedPercent = metric.Direction == MetricDirection.Used
                    ? metric.Current / limit.Value * 100m
                    : (limit.Value - metric.Current) / limit.Value * 100m;
                if (consumedPercent >= preference.ThresholdPercentage)
                {
                    if (notifiedMetrics.Add(key))
                    {
                        trayIcon?.ShowNotification(
                            "QuotaDock threshold reached",
                            $"{snapshot.Provider} {metric.Label} is at {consumedPercent:0}% used.");
                    }
                }
                else
                {
                    notifiedMetrics.Remove(key);
                }
            }
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusLabel.Text = "Refreshing…";
        HealthDot.Fill = ResourceBrush("QuotaDockWarningBrush");
        try
        {
            var result = await runtime.RefreshManuallyAsync();
            StatusLabel.Text = result.Message;
            if (!result.Started)
            {
                HealthDot.Fill = ResourceBrush("QuotaDockMutedBrush");
            }
        }
        catch
        {
            StatusLabel.Text = "Refresh failed; saved values were kept";
            HealthDot.Fill = ResourceBrush("QuotaDockDangerBrush");
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async void PinButton_Click(object sender, RoutedEventArgs e)
    {
        isPinned = !isPinned;
        if (presenter is not null)
        {
            presenter.IsAlwaysOnTop = isPinned;
        }

        AutomationProperties.SetName(
            PinButton,
            isPinned ? "Unpin widget from always on top" : "Pin widget always on top");
        PinIcon.Foreground = ResourceBrush(isPinned ? "QuotaDockAccentBrush" : "QuotaDockMutedBrush");
        await SaveWindowPlacementAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).ShowDetails();

    private void RestoreWindowPlacement()
    {
        if (appWindow is null)
        {
            return;
        }

        var placement = runtime.Settings.Window;
        isPinned = placement.IsAlwaysOnTop;
        if (presenter is not null)
        {
            presenter.IsAlwaysOnTop = isPinned;
        }

        PinIcon.Foreground = ResourceBrush(isPinned ? "QuotaDockAccentBrush" : "QuotaDockMutedBrush");
        WindowPlacementService.Restore(appWindow, WindowNative.GetWindowHandle(this), placement);
    }

    private async Task SaveWindowPlacementAsync()
    {
        if (appWindow is null || !initialized)
        {
            return;
        }

        var placement = WindowPlacementService.Capture(
            appWindow,
            WindowNative.GetWindowHandle(this),
            isPinned);
        await runtime.SaveSettingsAsync(runtime.Settings with { Window = placement });
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!closeAllowed)
        {
            args.Cancel = true;
            await SaveWindowPlacementAsync();
            sender.Hide();
        }
    }

    private static Brush ResourceBrush(string key) =>
        (Brush)Application.Current.Resources[key];
}
