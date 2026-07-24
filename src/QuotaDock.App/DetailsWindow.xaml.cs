using System.Globalization;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuotaDock.App.Runtime;
using QuotaDock.Connectors.OpenAI;
using QuotaDock.Core.Configuration;
using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;
using WinRT.Interop;
using Windows.Graphics;

namespace QuotaDock.App;

public sealed partial class DetailsWindow : Window
{
    private const string CodexInstallUrl = "https://developers.openai.com/codex/cli/";
    private const string ClaudeInstallUrl = "https://docs.claude.com/en/docs/claude-code/setup";
    private readonly QuotaDockRuntime runtime;
    private readonly CodexCliLocator codexCliLocator = new();
    private bool rebuilding;

    public DetailsWindow(QuotaDockRuntime runtime)
    {
        this.runtime = runtime;
        InitializeComponent();
        var handle = WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        AppWindow.GetFromWindowId(id).Resize(new SizeInt32(780, 780));
        runtime.StateChanged += Runtime_StateChanged;
        Closed += (_, _) => runtime.StateChanged -= Runtime_StateChanged;
        DetailsRoot.Loaded += DetailsRoot_Loaded;
    }

    private async void DetailsRoot_Loaded(object sender, RoutedEventArgs e)
    {
        await runtime.InitializeAsync();
        Rebuild();
    }

    private void Runtime_StateChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(Rebuild);

    // ---- Tab construction -------------------------------------------------

    private void Rebuild()
    {
        rebuilding = true;
        try
        {
            var selectedTag = (ProviderTabs.SelectedItem as TabViewItem)?.Tag as string;
            ProviderTabs.TabItems.Clear();

            foreach (var provider in ConnectedProviders())
            {
                ProviderTabs.TabItems.Add(BuildProviderTab(provider));
            }

            ProviderTabs.TabItems.Add(BuildConnectTab());

            var restore = ProviderTabs.TabItems
                .OfType<TabViewItem>()
                .FirstOrDefault(item => (item.Tag as string) == selectedTag);
            ProviderTabs.SelectedItem = restore ?? ProviderTabs.TabItems.FirstOrDefault();
        }
        finally
        {
            rebuilding = false;
        }
    }

    private IReadOnlyList<ProviderKind> ConnectedProviders()
    {
        return runtime.Connections
            .Select(connection => connection.Provider)
            .Distinct()
            .OrderBy(provider => provider)
            .ToArray();
    }

    private TabViewItem BuildProviderTab(ProviderKind provider)
    {
        var content = new StackPanel { Spacing = 14, Padding = new Thickness(6, 14, 6, 20) };

        var connections = runtime.Connections.Where(c => c.Provider == provider).ToArray();
        var snapshots = runtime.Snapshots.Where(s => s.Provider == provider).ToArray();

        // Accounts section.
        content.Children.Add(SectionLabel("ACCOUNTS"));
        foreach (var connection in connections)
        {
            content.Children.Add(CreateConnectionCard(connection));
        }

        // Metrics section.
        content.Children.Add(SectionLabel("METRICS"));
        var metricCount = 0;
        foreach (var snapshot in snapshots.OrderBy(s => s.AccountLabel))
        {
            foreach (var metric in snapshot.Metrics)
            {
                content.Children.Add(CreateMetricCard(snapshot, metric));
                metricCount++;
            }
        }

        if (metricCount == 0)
        {
            content.Children.Add(MutedText("Metrics appear here after a successful refresh."));
        }

        // Local spend (per provider currency).
        var spend = SpendEstimator.Summarize(snapshots, DateTimeOffset.Now);
        if (spend.HasData)
        {
            content.Children.Add(SectionLabel("LOCAL SPEND"));
            content.Children.Add(SpendCard("Last 7 days", spend.LastSevenDays));
            content.Children.Add(SpendCard("Last 30 days", spend.LastThirtyDays));
        }

        return new TabViewItem
        {
            Header = ProviderDisplayName(provider),
            Tag = $"provider:{provider}",
            IsClosable = false,
            IconSource = new FontIconSource { Glyph = ProviderGlyph(provider) },
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            }
        };
    }

    private TabViewItem BuildConnectTab()
    {
        var content = new StackPanel { Spacing = 14, Padding = new Thickness(6, 14, 6, 20) };

        content.Children.Add(SectionLabel("AUTO-DETECT"));
        var autoCard = new StackPanel { Spacing = 10 };
        autoCard.Children.Add(new TextBlock
        {
            Text = "Scan this PC for signed-in AI tools and connect them automatically. QuotaDock reads Codex and Claude Code usage locally — no keys, no copy/paste.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brush("QuotaDockMutedBrush")
        });
        var autoButton = new Button
        {
            Content = "Auto-detect providers & models",
            Background = Brush("QuotaDockAccentBrush"),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 34, 29)),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(999)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(autoButton, "ConnectTabAutoDetectButton");
        autoButton.Click += AutoDetect_Click;
        autoCard.Children.Add(autoButton);
        content.Children.Add(Card(autoCard));

        content.Children.Add(SectionLabel("LOCAL SUBSCRIPTIONS"));
        content.Children.Add(ConnectActionCard(
            "Connect local Codex",
            "Reads your installed Codex CLI usage automatically.",
            "Connect", ConnectCodex_Click));
        content.Children.Add(ConnectActionCard(
            "Connect Claude subscription",
            "Reads Claude Code session & weekly limits automatically from your local sign-in.",
            "Connect", ConnectClaude_Click));

        content.Children.Add(SectionLabel("OPENAI-COMPATIBLE"));
        content.Children.Add(ConnectActionCard(
            "Add OpenAI-compatible provider",
            "OpenRouter, DeepSeek, Groq, Mistral, xAI, Together, Ollama and more — or any custom endpoint.",
            "Add", ConnectCompatible_Click));

        content.Children.Add(SectionLabel("ORGANIZATION APIs"));
        content.Children.Add(ConnectActionCard(
            "OpenAI organization API",
            "Admin key · month-to-date tokens, requests and cost.",
            "Connect", ConnectOpenAi_Click));
        content.Children.Add(ConnectActionCard(
            "Anthropic organization API",
            "Admin key · message usage and cost reports.",
            "Connect", ConnectAnthropic_Click));
        content.Children.Add(ConnectActionCard(
            "Alibaba Token Plan International",
            "Isolated console reader · team plan credits and resets.",
            "Connect", ConnectAlibaba_Click));

        content.Children.Add(BuildStartupCard());

        return new TabViewItem
        {
            Header = "Connect",
            Tag = "connect",
            IsClosable = false,
            IconSource = new FontIconSource { Glyph = "\uE710" },
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            }
        };
    }

    private Border BuildStartupCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Start with Windows",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("QuotaDockTextBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Launch the portable app when you sign in.",
            FontSize = 11,
            Foreground = Brush("QuotaDockMutedBrush")
        });

        var toggle = new ToggleSwitch { IsOn = runtime.Settings.StartWithWindows };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "StartupToggle");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, "Start QuotaDock with Windows");
        toggle.Toggled += StartupToggle_Toggled;

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(stack);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return Card(grid);
    }

    private Border ConnectActionCard(string title, string subtitle, string actionText, RoutedEventHandler handler)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("QuotaDockTextBrush")
        });
        text.Children.Add(new TextBlock
        {
            Text = subtitle,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("QuotaDockMutedBrush")
        });

        var button = new Button
        {
            Content = actionText,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(16, 7, 16, 7),
            CornerRadius = new CornerRadius(999)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
        button.Click += handler;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return Card(grid);
    }

    // ---- Cards ------------------------------------------------------------

    private UIElement SpendCard(string title, IReadOnlyList<SpendTotal> totals)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("QuotaDockTextBrush")
        });

        if (totals.Count == 0)
        {
            content.Children.Add(MutedText("No spend in this window."));
            return Card(content);
        }

        foreach (var total in totals)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{total.Amount:0.##} {total.Currency}",
                FontSize = 12,
                Foreground = Brush("QuotaDockMutedBrush")
            });
        }

        return Card(content);
    }

    private Border CreateConnectionCard(ConnectorConnection connection)
    {
        var latest = runtime.Snapshots.FirstOrDefault(snapshot => snapshot.ConnectionId == connection.Id);
        var health = latest?.StatusMessage ?? latest?.Health.ToString() ?? "Waiting for first refresh";
        var source = connection.Source switch
        {
            DataSourceKind.OfficialApi => "Official API",
            DataSourceKind.LocalCli when connection.Provider == ProviderKind.Anthropic => "Local Claude sign-in",
            DataSourceKind.LocalCli => "Local CLI",
            DataSourceKind.DashboardReader => "Isolated dashboard reader",
            _ => connection.Source.ToString()
        };

        var disconnect = new Button
        {
            Content = "Disconnect",
            Tag = connection.Id,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(999)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(disconnect, $"Disconnect {connection.AccountLabel}");
        disconnect.Click += Disconnect_Click;

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = connection.AccountLabel,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("QuotaDockTextBrush")
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{source} · {health}",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            Foreground = Brush("QuotaDockMutedBrush")
        });

        if (latest is { Health: ConnectionHealth.Stale or ConnectionHealth.RateLimited
            or ConnectionHealth.AuthenticationRequired or ConnectionHealth.Unavailable
            or ConnectionHealth.FormatChanged })
        {
            var (label, brush) = IncidentBadge(latest.Health);
            text.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(brush)
            });
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(disconnect, 1);
        grid.Children.Add(disconnect);
        return Card(grid);
    }

    private Border CreateMetricCard(UsageSnapshot snapshot, UsageMetric metric)
    {
        var key = $"{snapshot.ConnectionId}:{metric.Id}";
        var title = new TextBlock
        {
            Text = metric.Label,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("QuotaDockTextBrush")
        };
        var direction = metric.Direction == MetricDirection.Used ? "used" : "remaining";
        var resetText = metric.ResetsAt is { } reset
            ? $" · {ResetCountdown.Format(reset, DateTimeOffset.Now)}"
            : string.Empty;
        var value = new TextBlock
        {
            Text = $"{metric.Current:0.##} {metric.Unit} {direction} · {snapshot.AccountLabel}{resetText}",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            Foreground = Brush("QuotaDockMutedBrush")
        };
        var pin = new CheckBox
        {
            Content = "Pin",
            Tag = key,
            IsChecked = runtime.Settings.PinnedMetricIds.Contains(key, StringComparer.Ordinal),
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pin, $"Pin {metric.Label} to widget");
        pin.Click += Pin_Click;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelStack = new StackPanel();
        labelStack.Children.Add(title);
        labelStack.Children.Add(value);

        // Quota metrics with a real limit get a rounded progress bar.
        if (metric.ProgressFraction is { } fraction)
        {
            labelStack.Children.Add(new ProgressBar
            {
                Margin = new Thickness(0, 8, 0, 0),
                Height = 6,
                CornerRadius = new CornerRadius(999),
                Minimum = 0,
                Maximum = 100,
                Value = (double)(fraction * 100m),
                Foreground = Brush("QuotaDockAccentBrush"),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 56, 72))
            });
        }

        var pace = UsagePace.Calculate(metric, snapshot.CapturedAt, DateTimeOffset.Now);
        if (pace.Status != PaceStatus.Unknown)
        {
            labelStack.Children.Add(PaceChip(pace));
        }

        header.Children.Add(labelStack);
        Grid.SetColumn(pin, 1);
        header.Children.Add(pin);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(header);

        if (metric.Limit is null)
        {
            var budgetBox = new NumberBox
            {
                Width = 150,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Minimum = 0,
                Value = runtime.Settings.SoftBudgets.TryGetValue(key, out var budget) ? (double)budget : double.NaN,
                PlaceholderText = "No soft budget",
                Tag = key
            };
            var saveBudget = new Button
            {
                Content = "Save soft budget",
                Tag = budgetBox,
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(999)
            };
            saveBudget.Click += SaveBudget_Click;
            var budgetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            budgetRow.Children.Add(budgetBox);
            budgetRow.Children.Add(new TextBlock
            {
                Text = metric.Unit,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("QuotaDockMutedBrush")
            });
            budgetRow.Children.Add(saveBudget);
            content.Children.Add(budgetRow);
        }

        var preference = runtime.Settings.Notifications.TryGetValue(key, out var existing)
            ? existing
            : new NotificationPreference(false, 80m);
        var notify = new ToggleSwitch
        {
            Header = "Notify at threshold",
            IsOn = preference.Enabled,
            Tag = key,
            OnContent = $"{preference.ThresholdPercentage:0}%",
            OffContent = "Off"
        };
        notify.Toggled += Notification_Toggled;
        content.Children.Add(notify);
        return Card(content);
    }

    // ---- Connect actions --------------------------------------------------

    private async void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Scanning for local AI tools…", InfoBarSeverity.Informational);
        try
        {
            var outcome = await runtime.AutoDetectAsync();
            var detail = outcome.Notes.Count > 0 ? " " + string.Join(" ", outcome.Notes) : string.Empty;
            SetStatus(
                outcome.Summary + detail,
                outcome.Added > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
        }
        catch
        {
            SetStatus("Auto-detect could not complete. Try connecting a provider manually.", InfoBarSeverity.Error);
        }
    }

    private async void ConnectCodex_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Checking the installed Codex CLI…", InfoBarSeverity.Informational);
        var executable = await codexCliLocator.FindAsync();
        if (executable is null)
        {
            await ShowCodexSetupAsync();
            return;
        }

        await ConnectCodexExecutableAsync(executable);
    }

    private async Task ShowCodexSetupAsync()
    {
        var executablePath = new TextBox
        {
            Header = "Path to codex.exe",
            PlaceholderText = @"C:\path\to\codex.exe",
            MaxLength = 1024
        };
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(new TextBlock
        {
            Text = "QuotaDock could not find a launchable Codex CLI. Install the official CLI or paste the full path to codex.exe.",
            TextWrapping = TextWrapping.Wrap
        });
        fields.Children.Add(executablePath);

        var dialog = new ContentDialog
        {
            Title = "Set up local Codex",
            Content = fields,
            PrimaryButtonText = "Validate and connect",
            SecondaryButtonText = "Open installation guide",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Secondary)
        {
            OpenDefaultBrowser(CodexInstallUrl);
            SetStatus("The official Codex CLI installation guide opened in your default browser.",
                InfoBarSeverity.Informational);
            return;
        }

        if (choice != ContentDialogResult.Primary)
        {
            return;
        }

        var executable = await codexCliLocator.FindAsync(executablePath.Text);
        if (executable is null)
        {
            SetStatus("That file is not a launchable codex.exe. Check the path or install the official CLI.",
                InfoBarSeverity.Error);
            return;
        }

        await ConnectCodexExecutableAsync(executable);
    }

    private async Task ConnectCodexExecutableAsync(string executable)
    {
        var result = await runtime.ConnectAsync(
            "codex-personal",
            "Codex account",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["executable"] = executable });
        SetStatus(result.IsValid ? "Codex connected." : result.Message!,
            result.IsValid ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async void ConnectClaude_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Reading your local Claude sign-in…", InfoBarSeverity.Informational);
        var result = await runtime.ConnectAsync("claude-subscription", "Claude account", null, null);
        if (result.IsValid)
        {
            SetStatus("Claude connected. Session & weekly limits will update automatically.", InfoBarSeverity.Success);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Connect Claude subscription",
            Content = new TextBlock
            {
                Text = result.Message + "\n\nClaude Code stores a local sign-in that QuotaDock reads to show your session and weekly limits automatically. Install Claude Code and run it once, then try again.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Open Claude Code setup",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            OpenDefaultBrowser(ClaudeInstallUrl);
        }
    }

    private async void ConnectOpenAi_Click(object sender, RoutedEventArgs e) =>
        await ConnectAdminApiAsync("openai-organization", "OpenAI organization", "OpenAI Admin API key");

    private async void ConnectAnthropic_Click(object sender, RoutedEventArgs e) =>
        await ConnectAdminApiAsync("anthropic-organization", "Anthropic organization", "Anthropic Admin API key");

    private void ConnectAlibaba_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).OpenDashboardReader(ProviderKind.Alibaba);

    private async void ConnectCompatible_Click(object sender, RoutedEventArgs e)
    {
        var presetBox = new ComboBox
        {
            Header = "Preset",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        presetBox.Items.Add(new ComboBoxItem { Content = "Custom endpoint", Tag = string.Empty });
        foreach (var preset in OpenAiCompatiblePresets.All)
        {
            presetBox.Items.Add(new ComboBoxItem { Content = preset.DisplayName, Tag = preset.Id });
        }
        presetBox.SelectedIndex = 0;

        var account = new TextBox { Header = "Provider label", Text = "Custom provider", MaxLength = 80 };
        var baseUrl = new TextBox { Header = "OpenAI-compatible base URL", PlaceholderText = "https://provider.example/v1", MaxLength = 2048 };
        var model = new TextBox { Header = "Model ID", PlaceholderText = "provider-model-id", MaxLength = 256 };
        var key = new PasswordBox { Header = "API key (optional for local providers)", PasswordChar = "●", MaxLength = 4096 };
        var usageUrl = new TextBox { Header = "Aggregate usage URL (optional, same origin)", PlaceholderText = "https://provider.example/admin/usage", MaxLength = 2048 };

        presetBox.SelectionChanged += (_, _) =>
        {
            if (presetBox.SelectedItem is ComboBoxItem { Tag: string id } && !string.IsNullOrEmpty(id) &&
                OpenAiCompatiblePresets.FindById(id) is { } preset)
            {
                account.Text = preset.DisplayName;
                baseUrl.Text = preset.BaseUrl;
                model.Text = preset.DefaultModel;
            }
        };

        var fields = new StackPanel { Spacing = 10 };
        fields.Children.Add(presetBox);
        fields.Children.Add(account);
        fields.Children.Add(baseUrl);
        fields.Children.Add(model);
        fields.Children.Add(key);
        fields.Children.Add(usageUrl);
        fields.Children.Add(new TextBlock
        {
            Text = "Model access is validated through /v1/models. Without a compatible usage URL, QuotaDock monitors availability only and never invents usage values. HTTPS is required except for localhost.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brush("QuotaDockMutedBrush")
        });

        var dialog = new ContentDialog
        {
            Title = "Add OpenAI-compatible provider",
            Content = new ScrollViewer { Content = fields, MaxHeight = 520 },
            PrimaryButtonText = "Validate and connect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            key.Password = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(account.Text) ||
            string.IsNullOrWhiteSpace(baseUrl.Text) ||
            string.IsNullOrWhiteSpace(model.Text))
        {
            key.Password = string.Empty;
            SetStatus("Provider label, base URL, and model ID are required.", InfoBarSeverity.Error);
            return;
        }

        SetStatus("Validating model access…", InfoBarSeverity.Informational);
        var result = await runtime.ConnectAsync(
            "openai-compatible",
            account.Text,
            key.Password,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [OpenAiCompatibleConnector.BaseUrlSetting] = baseUrl.Text,
                [OpenAiCompatibleConnector.ModelSetting] = model.Text,
                [OpenAiCompatibleConnector.UsageUrlSetting] = usageUrl.Text
            });
        key.Password = string.Empty;
        SetStatus(
            result.IsValid
                ? string.IsNullOrWhiteSpace(usageUrl.Text)
                    ? "Provider connected. Model availability is monitored; aggregate usage is not configured."
                    : "Provider connected with aggregate usage tracking."
                : result.Message!,
            result.IsValid ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async Task ConnectAdminApiAsync(string connectorId, string defaultLabel, string keyLabel)
    {
        var account = new TextBox { Header = "Account label", Text = defaultLabel, MaxLength = 80 };
        var key = new PasswordBox { Header = keyLabel, PasswordChar = "●", MaxLength = 256 };
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(account);
        fields.Children.Add(key);
        fields.Children.Add(new TextBlock
        {
            Text = "Validation performs a read-only usage request. Invalid keys are immediately removed from Windows Credential Manager.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brush("QuotaDockMutedBrush")
        });

        var dialog = new ContentDialog
        {
            Title = $"Connect {defaultLabel}",
            Content = fields,
            PrimaryButtonText = "Validate and connect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(key.Password))
        {
            SetStatus("An admin API key is required.", InfoBarSeverity.Error);
            return;
        }

        SetStatus("Validating the provider connection…", InfoBarSeverity.Informational);
        var result = await runtime.ConnectAsync(connectorId, account.Text, key.Password);
        key.Password = string.Empty;
        SetStatus(result.IsValid ? $"{defaultLabel} connected." : result.Message!,
            result.IsValid ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string connectionId })
        {
            await runtime.DisconnectAsync(connectionId);
            SetStatus("Connection and its stored credential were removed.", InfoBarSeverity.Success);
        }
    }

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (rebuilding || sender is not CheckBox { Tag: string key } checkBox)
        {
            return;
        }

        var pins = runtime.Settings.PinnedMetricIds.ToList();
        if (checkBox.IsChecked == true && !pins.Contains(key, StringComparer.Ordinal))
        {
            if (pins.Count >= 4)
            {
                checkBox.IsChecked = false;
                SetStatus("The widget can show up to four pinned metrics.", InfoBarSeverity.Warning);
                return;
            }

            pins.Add(key);
        }
        else if (checkBox.IsChecked != true)
        {
            pins.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
        }

        await runtime.SaveSettingsAsync(runtime.Settings with { PinnedMetricIds = pins });
    }

    private async void SaveBudget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NumberBox { Tag: string key } box })
        {
            return;
        }

        var budgets = new Dictionary<string, decimal>(runtime.Settings.SoftBudgets, StringComparer.Ordinal);
        if (double.IsNaN(box.Value) || box.Value <= 0d)
        {
            budgets.Remove(key);
        }
        else
        {
            budgets[key] = Convert.ToDecimal(box.Value, CultureInfo.InvariantCulture);
        }

        await runtime.SaveSettingsAsync(runtime.Settings with { SoftBudgets = budgets });
        SetStatus("Local soft budget saved.", InfoBarSeverity.Success);
    }

    private async void Notification_Toggled(object sender, RoutedEventArgs e)
    {
        if (rebuilding || sender is not ToggleSwitch { Tag: string key } toggle)
        {
            return;
        }

        var notifications = new Dictionary<string, NotificationPreference>(
            runtime.Settings.Notifications,
            StringComparer.Ordinal)
        {
            [key] = new NotificationPreference(toggle.IsOn, 80m)
        };
        await runtime.SaveSettingsAsync(runtime.Settings with { Notifications = notifications });
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (rebuilding || sender is not ToggleSwitch toggle)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(toggle.IsOn);
            await runtime.SaveSettingsAsync(runtime.Settings with { StartWithWindows = toggle.IsOn });
            SetStatus("Startup preference saved.", InfoBarSeverity.Success);
        }
        catch
        {
            rebuilding = true;
            toggle.IsOn = !toggle.IsOn;
            rebuilding = false;
            SetStatus("Windows startup could not be changed.", InfoBarSeverity.Error);
        }
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        var outcome = await runtime.RefreshManuallyAsync();
        SetStatus(outcome.Message, outcome.Started ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static bool OpenDefaultBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---- Shared helpers ---------------------------------------------------

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        CharacterSpacing = 110,
        Foreground = Brush("QuotaDockMutedBrush")
    };

    private static Border Card(UIElement content) => new()
    {
        Padding = new Thickness(16),
        Background = Brush("QuotaDockSurfaceBrush"),
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 51, 65)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Child = content
    };

    private static TextBlock MutedText(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Brush("QuotaDockMutedBrush")
    };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static string ProviderDisplayName(ProviderKind provider) => provider switch
    {
        ProviderKind.OpenAI => "OpenAI",
        ProviderKind.Anthropic => "Anthropic",
        ProviderKind.Alibaba => "Alibaba",
        _ => provider.ToString()
    };

    private static string ProviderGlyph(ProviderKind provider) => provider switch
    {
        ProviderKind.OpenAI => "\uE9D9",
        ProviderKind.Anthropic => "\uE8BD",
        ProviderKind.Alibaba => "\uE909",
        _ => "\uE7C3"
    };

    private static (string Label, string Brush) IncidentBadge(ConnectionHealth health) => health switch
    {
        ConnectionHealth.Stale => ("Showing saved values", "QuotaDockWarningBrush"),
        ConnectionHealth.RateLimited => ("Rate-limited by provider", "QuotaDockWarningBrush"),
        ConnectionHealth.AuthenticationRequired => ("Sign-in required", "QuotaDockWarningBrush"),
        ConnectionHealth.FormatChanged => ("Provider page changed", "QuotaDockWarningBrush"),
        ConnectionHealth.Unavailable => ("Provider unavailable", "QuotaDockDangerBrush"),
        _ => (health.ToString(), "QuotaDockMutedBrush")
    };

    private UIElement PaceChip(PaceResult pace)
    {
        var brush = pace.Status switch
        {
            PaceStatus.Exceeds => "QuotaDockDangerBrush",
            PaceStatus.Watch => "QuotaDockWarningBrush",
            _ => "QuotaDockAccentBrush"
        };

        var text = pace.Status == PaceStatus.OnTrack
            ? "On track for this window"
            : pace.Status == PaceStatus.Watch
                ? "Watch — close to the limit at this pace"
                : "Over pace — projected to exceed before reset";

        return new Border
        {
            Margin = new Thickness(0, 5, 0, 0),
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(999),
            BorderBrush = Brush(brush),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(brush)
            }
        };
    }
}
