using System.Collections.ObjectModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuotaDock.App.Presentation;
using QuotaDock.App.Runtime;
using QuotaDock.Core.Domain;
using WinRT.Interop;
using Windows.Graphics;
using Windows.UI;

namespace QuotaDock.App;

public sealed partial class MainWindow : Window
{
    private const string HomeTab = "home";
    private static readonly ProviderKind[] TabProviders =
    [
        ProviderKind.OpenAI,
        ProviderKind.Anthropic,
        ProviderKind.Xai,
        ProviderKind.Moonshot
    ];

    private readonly QuotaDockRuntime runtime;
    private readonly ObservableCollection<MetricCardViewModel> activeCards = [];
    private readonly HashSet<string> notifiedMetrics = new(StringComparer.Ordinal);
    private AppWindow? appWindow;
    private OverlappedPresenter? presenter;
    private TrayIconService? trayIcon;
    private string selectedTab = HomeTab;
    private bool isPinned;
    private bool closeAllowed;
    private bool initialized;
    private readonly WrapLayout wrapLayout = new()
    {
        DesiredColumnWidth = 280,
        ColumnSpacing = 8,
        RowSpacing = 8
    };

    public MainWindow(QuotaDockRuntime runtime)
    {
        this.runtime = runtime;
        InitializeComponent();
        MetricRepeater.ItemsSource = activeCards;
        MetricRepeater.Layout = wrapLayout;
        ConfigureNativeWindow();
        WindowStyleHelper.Apply(this, useMica: false);
        WidgetRoot.Loaded += WidgetRoot_Loaded;
        runtime.StateChanged += Runtime_StateChanged;
    }

    internal void AllowClose()
    {
        closeAllowed = true;
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
        appWindow.Resize(new SizeInt32(420, 640));
        appWindow.Closing += AppWindow_Closing;
        appWindow.Changed += AppWindow_Changed;
        presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            // Keep the native frame (so DWM can round the corners and the window
            // stays resizable) but drop the system title bar entirely. WinUI 3
            // draws its caption buttons as an overlay whenever the title bar is
            // present, and that overlay is what was landing on top of the app's
            // own header buttons — turning the title bar off removes it for good.
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = false;
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

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Keep the widget usable at any size: clamp to a sensible minimum so the
        // adaptive grid always has room to render at least one card column.
        if (!args.DidSizeChange)
        {
            return;
        }

        var width = sender.Size.Width;
        var height = sender.Size.Height;
        if (width < 320 || height < 440)
        {
            sender.Resize(new SizeInt32(Math.Max(width, 320), Math.Max(height, 440)));
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
            BuildTabStrip();
            RestoreWindowPlacement();
            ApplyTheme();
            RebuildContent();
        }
        catch
        {
            StatusLabel.Text = "Local data could not be opened";
            HealthDot.Fill = ResourceBrush("QuotaDockDangerBrush");
        }
    }

    private void Runtime_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RebuildContent);
    }

    // ---- Tabs -------------------------------------------------------------

    private void BuildTabStrip()
    {
        TabStrip.Children.Clear();
        AddTab(HomeTab, "Home");
        foreach (var provider in TabProviders)
        {
            AddTab(ProviderTab(provider), TabLabel(provider));
        }

        UpdateTabStyles();
    }

    private void AddTab(string tag, string label)
    {
        var button = new Button
        {
            Tag = tag,
            Content = label,
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(6),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            BorderThickness = new Thickness(0)
        };
        button.Click += Tab_Click;
        TabStrip.Children.Add(button);
    }

    private void UpdateTabStyles()
    {
        foreach (var tab in TabStrip.Children.OfType<Button>())
        {
            var isSelected = string.Equals(tab.Tag as string, selectedTab, StringComparison.Ordinal);
            tab.Background = isSelected
                ? ResourceBrush("QuotaDockAccentBrush")
                : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            tab.Foreground = isSelected
                ? ResourceBrush("QuotaDockOnAccentBrush")
                : ResourceBrush("QuotaDockMutedBrush");
        }
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            !string.Equals(tag, selectedTab, StringComparison.Ordinal))
        {
            selectedTab = tag;
            UpdateTabStyles();
            RebuildContent();
        }
    }

    private static string ProviderTab(ProviderKind provider) => $"provider:{provider}";

    private static string TabLabel(ProviderKind provider) => provider switch
    {
        ProviderKind.OpenAI => "Codex",
        ProviderKind.Anthropic => "Claude",
        ProviderKind.Xai => "Grok",
        ProviderKind.Moonshot => "Kimi",
        _ => provider.ToString()
    };

    private static ProviderKind? ParseProvider(string tab) =>
        tab.StartsWith("provider:", StringComparison.Ordinal) &&
        Enum.TryParse<ProviderKind>(tab["provider:".Length..], out var kind)
            ? kind
            : null;

    // ---- Content ----------------------------------------------------------

    private void RebuildContent()
    {
        try
        {
            var now = TimeProvider.System.GetUtcNow();
            var all = runtime.Snapshots
                .SelectMany(snapshot => snapshot.Metrics.Select(metric =>
                    MetricCardViewModel.Create(snapshot, metric, runtime.Settings, now)))
                .ToArray();

            var selected = SelectedCards(all);
            activeCards.Clear();
            foreach (var card in selected)
            {
                activeCards.Add(card);
            }

            MetricRepeater.Visibility = activeCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = activeCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateEmptyState();
            UpdateStatus();
            UpdateFreshness(now);
            EvaluateNotifications();
            ApplyTheme();
        }
        catch
        {
            // A malformed snapshot or missing resource must never crash the
            // widget. The next StateChanged event will retry.
            StatusLabel.Text = "Display error — saved values are safe";
            HealthDot.Fill = ResourceBrush("QuotaDockWarningBrush");
        }
    }

    private IEnumerable<MetricCardViewModel> SelectedCards(MetricCardViewModel[] all)
    {
        var hidden = runtime.Settings.HiddenMetricIds;
        var visible = hidden.Count > 0
            ? all.Where(card => !hidden.Contains(card.Key, StringComparer.Ordinal)).ToArray()
            : all;

        if (selectedTab == HomeTab)
        {
            // Home shows the metrics the user pinned, in pin order. Before any
            // pinning happens it falls back to every available metric so the
            // widget is useful from first launch.
            var pinned = runtime.Settings.PinnedMetricIds;
            if (pinned.Count > 0)
            {
                return pinned
                    .Select(key => visible.FirstOrDefault(card => card.Key == key))
                    .Where(card => card is not null)
                    .Cast<MetricCardViewModel>();
            }

            return visible;
        }

        var provider = ParseProvider(selectedTab);
        return provider is { } kind
            ? visible.Where(card => card.ProviderKind == kind)
            : [];
    }

    private void UpdateEmptyState()
    {
        if (selectedTab == HomeTab)
        {
            EmptyIcon.Glyph = "\uE945";
            EmptyTitle.Text = runtime.Snapshots.Count == 0
                ? "Your AI limits, one glance away."
                : "Your home view is empty.";
            EmptySubtitle.Text = runtime.Snapshots.Count == 0
                ? "Connect Codex, Claude, Grok, or Kimi to start tracking usage."
                : "Pin metrics from any provider tab to build your home view.";
            EmptyActionButton.Content = runtime.Snapshots.Count == 0 ? "Connect provider" : "Open details";
        }
        else
        {
            var name = ParseProvider(selectedTab) is { } kind ? TabLabel(kind) : "Provider";
            EmptyIcon.Glyph = "\uE8BD";
            EmptyTitle.Text = $"No {name} usage yet.";
            EmptySubtitle.Text = $"Connect {name} to see its limits here.";
            EmptyActionButton.Content = "Connect provider";
        }
    }

    private void UpdateStatus()
    {
        var staleCount = runtime.Snapshots.Count(snapshot => snapshot.Health == ConnectionHealth.Stale);
        if (!string.IsNullOrWhiteSpace(runtime.LastError))
        {
            StatusLabel.Text = runtime.LastError;
            HealthDot.Fill = ResourceBrush("QuotaDockWarningBrush");
        }
        else if (runtime.Snapshots.Count == 0)
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
    }

    private void UpdateFreshness(DateTimeOffset now)
    {
        var latest = runtime.Snapshots
            .Where(snapshot => snapshot.Metrics.Count > 0)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .FirstOrDefault();
        FreshnessLabel.Text = latest is null
            ? "No local snapshots"
            : $"Updated {MetricCardViewModel.Create(latest, latest.Metrics[0], runtime.Settings, now).Freshness}";
    }

    private async void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        var pins = runtime.Settings.PinnedMetricIds.ToList();
        if (pins.Contains(key, StringComparer.Ordinal))
        {
            pins.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
        }
        else
        {
            pins.Add(key);
        }

        await runtime.SaveSettingsAsync(runtime.Settings with { PinnedMetricIds = pins });
    }

    private async void CollapseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        var collapsed = runtime.Settings.CollapsedMetricIds.ToList();
        if (collapsed.Contains(key, StringComparer.Ordinal))
        {
            collapsed.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
        }
        else
        {
            collapsed.Add(key);
        }

        await runtime.SaveSettingsAsync(runtime.Settings with { CollapsedMetricIds = collapsed });
    }

    private void ApplyTheme()
    {
        ThemeApplier.ApplyBrushes(runtime.Settings.Appearance);
        ThemeApplier.ApplyToWindow(this, runtime.Settings.Appearance);
        UpdateTabStyles();
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).ExitApplication();

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
            return;
        }

        trayIcon?.Dispose();
    }

    private static Brush ResourceBrush(string key) =>
        (Brush)Application.Current.Resources[key];
}
