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
    private readonly QuotaDockRuntime runtime;
    private readonly CodexCliLocator codexCliLocator = new();
    private bool rebuilding;
    private AppearanceSettings workingAppearance = AppearanceSettings.Default;
    private bool syncingAppearance;
    private bool appearanceDirty;
    private TabViewItem? appearanceTab;
    private TabView? modelTabs;
    private Button? applyAppearanceButton;
    private ColorPicker? cpBackground;
    private ColorPicker? cpText;
    private ColorPicker? cpForeground;
    private ColorPicker? cpAccent;
    private ComboBox? themeCombo;
    private ComboBox? presetCombo;
    private Button? darkModeButton;
    private Button? lightModeButton;

    public DetailsWindow(QuotaDockRuntime runtime)
    {
        this.runtime = runtime;
        InitializeComponent();
        WindowStyleHelper.Apply(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        var handle = WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        AppWindow.GetFromWindowId(id).Resize(new SizeInt32(780, 780));
        runtime.StateChanged += Runtime_StateChanged;
        Closed += (_, _) => OnClosedAsync();
        DetailsRoot.Loaded += DetailsRoot_Loaded;
    }

    private async void OnClosedAsync()
    {
        runtime.StateChanged -= Runtime_StateChanged;

        // Appearance edits are only persisted through Apply. Anything left
        // unapplied is a live preview, so closing the window rolls the other
        // windows back to the saved look instead of silently keeping it.
        if (appearanceDirty && !Equals(runtime.Settings.Appearance, workingAppearance))
        {
            try
            {
                var saved = runtime.Settings.Appearance;
                workingAppearance = saved;
                appearanceDirty = false;
                ThemeApplier.ApplyBrushes(saved);
                ThemeApplier.ApplyToAll(saved);
            }
            catch
            {
                // The window is closing; a failed rollback must never throw.
            }
        }

        await Task.CompletedTask;
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
        syncingAppearance = true;
        try
        {
            // Never clobber an unapplied appearance edit with the saved value:
            // a background refresh must not throw away what the user is tuning.
            if (!appearanceDirty)
            {
                workingAppearance = runtime.Settings.Appearance;
            }

            var selectedTag = (ProviderTabs.SelectedItem as TabViewItem)?.Tag as string;
            var selectedModelTag = (modelTabs?.SelectedItem as TabViewItem)?.Tag as string;
            ProviderTabs.TabItems.Clear();

            appearanceTab = BuildAppearanceTab();
            ProviderTabs.TabItems.Add(appearanceTab);
            ProviderTabs.TabItems.Add(BuildModelsTab(selectedModelTag));

            var restore = ProviderTabs.TabItems
                .OfType<TabViewItem>()
                .FirstOrDefault(item => (item.Tag as string) == selectedTag);
            ProviderTabs.SelectedItem = restore ?? ProviderTabs.TabItems.FirstOrDefault();
            ApplyTheme();
        }
        catch
        {
            // A malformed connection or snapshot must never crash the details
            // window. The next StateChanged event will retry the rebuild.
        }
        finally
        {
            rebuilding = false;
            syncingAppearance = false;
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
        var content = BuildProviderContent(provider);

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

    /// <summary>
    /// The "Models" tab: an inner tab strip with one tab per connected provider
    /// plus a persistent "Connect" tab, so settings stay two top-level tabs.
    /// </summary>
    private TabViewItem BuildModelsTab(string? selectedInnerTag)
    {
        modelTabs = new TabView
        {
            IsAddTabButtonVisible = false,
            CanReorderTabs = false,
            CanDragTabs = false,
            TabWidthMode = TabViewWidthMode.Equal
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(modelTabs, "ModelTabs");

        foreach (var provider in ConnectedProviders())
        {
            modelTabs.TabItems.Add(BuildProviderTab(provider));
        }

        modelTabs.TabItems.Add(BuildConnectTab());

        var restore = modelTabs.TabItems
            .OfType<TabViewItem>()
            .FirstOrDefault(item => (item.Tag as string) == selectedInnerTag);
        modelTabs.SelectedItem = restore ?? modelTabs.TabItems.FirstOrDefault();

        return new TabViewItem
        {
            Header = "Models",
            Tag = "models",
            IsClosable = false,
            IconSource = new FontIconSource { Glyph = "\uE9D9" },
            Content = modelTabs
        };
    }

    private StackPanel BuildProviderContent(ProviderKind provider)
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

        return content;
    }

    private TabViewItem BuildConnectTab()
    {
        var content = new StackPanel { Spacing = 14, Padding = new Thickness(6, 14, 6, 20) };

        content.Children.Add(SectionLabel("AUTO-DETECT"));
        var autoCard = new StackPanel { Spacing = 10 };
        autoCard.Children.Add(new TextBlock
        {
            Text = "Scan this PC for signed-in AI tools and connect them automatically. QuotaDock reads Codex, Claude, Grok, and Kimi usage locally — no keys, no copy/paste.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brush("QuotaDockMutedBrush")
        });
        var autoButton = new Button
        {
            Content = "Auto-detect providers & models",
            Background = Brush("QuotaDockAccentBrush"),
            Foreground = Brush("QuotaDockOnAccentBrush"),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(4)
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
        content.Children.Add(ConnectActionCard(
            "Connect Grok subscription",
            "Reads Grok Build credits automatically from your local sign-in.",
            "Connect", ConnectGrok_Click));
        content.Children.Add(ConnectActionCard(
            "Connect Kimi subscription",
            "Reads Kimi Code session & weekly limits automatically from your local sign-in.",
            "Connect", ConnectKimi_Click));

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

    // ---- Appearance -------------------------------------------------------

    private TabViewItem BuildAppearanceTab()
    {
        var content = new StackPanel { Spacing = 14, Padding = new Thickness(6, 14, 6, 20) };

        content.Children.Add(SectionLabel("THEME"));
        themeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Header = "Window style" };
        foreach (var option in new (string Label, ThemeKind Kind)[]
                 {
                     ("Default — solid", ThemeKind.Default),
                     ("Glassy — frosted glass", ThemeKind.Glassy),
                     ("Mica — subtle material", ThemeKind.Mica)
                 })
        {
            themeCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Kind });
        }
        SelectThemeCombo(workingAppearance.Theme);
        themeCombo.SelectionChanged += OnThemeChanged;
        content.Children.Add(Card(themeCombo));

        content.Children.Add(SectionLabel("MODE"));
        darkModeButton = ModeButton("Dark", ColorMode.Dark);
        lightModeButton = ModeButton("Light", ColorMode.Light);
        var modeRow = new Grid { ColumnSpacing = 8 };
        modeRow.ColumnDefinitions.Add(new ColumnDefinition());
        modeRow.ColumnDefinitions.Add(new ColumnDefinition());
        modeRow.Children.Add(darkModeButton);
        Grid.SetColumn(lightModeButton, 1);
        modeRow.Children.Add(lightModeButton);
        UpdateModeButtons();
        content.Children.Add(Card(modeRow));

        content.Children.Add(SectionLabel("COLOR PRESET"));
        presetCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Header = "Palette" };
        presetCombo.Items.Add(new ComboBoxItem { Content = "Custom", Tag = "Custom" });
        foreach (var preset in AppearancePresets.All)
        {
            presetCombo.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset.Name });
        }
        SelectPresetCombo(workingAppearance.Preset);
        presetCombo.SelectionChanged += OnPresetChanged;
        content.Children.Add(Card(presetCombo));

        content.Children.Add(SectionLabel("COLORS"));
        cpBackground = MakePicker(workingAppearance.Background);
        cpText = MakePicker(workingAppearance.Text);
        cpForeground = MakePicker(workingAppearance.Foreground);
        cpAccent = MakePicker(workingAppearance.Accent);
        cpBackground.ColorChanged += (s, _) => OnPickerColor(s, "bg");
        cpText.ColorChanged += (s, _) => OnPickerColor(s, "text");
        cpForeground.ColorChanged += (s, _) => OnPickerColor(s, "fg");
        cpAccent.ColorChanged += (s, _) => OnPickerColor(s, "accent");
        var bgField = PickerField("Background", cpBackground);
        var textField = PickerField("Text", cpText);
        var fgField = PickerField("Foreground", cpForeground);
        var accentField = PickerField("Accent", cpAccent);
        var colorGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition());
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition());
        colorGrid.RowDefinitions.Add(new RowDefinition());
        colorGrid.RowDefinitions.Add(new RowDefinition());
        colorGrid.Children.Add(bgField);
        Grid.SetColumn(textField, 1);
        colorGrid.Children.Add(textField);
        Grid.SetRow(fgField, 1);
        colorGrid.Children.Add(fgField);
        Grid.SetRow(accentField, 1);
        Grid.SetColumn(accentField, 1);
        colorGrid.Children.Add(accentField);
        content.Children.Add(Card(colorGrid));

        // Apply is the only thing that writes to disk. Everything above is a
        // live preview across every window, so the user can judge a palette
        // before committing it.
        applyAppearanceButton = new Button
        {
            Content = "Apply",
            Padding = new Thickness(20, 8, 20, 8),
            CornerRadius = new CornerRadius(4),
            Background = Brush("QuotaDockAccentBrush"),
            Foreground = Brush("QuotaDockOnAccentBrush")
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
            applyAppearanceButton, "ApplyAppearanceButton");
        applyAppearanceButton.Click += ApplyAppearance_Click;

        var reset = new Button
        {
            Content = "Reset appearance",
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(4)
        };
        reset.Click += ResetAppearance_Click;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        actions.Children.Add(applyAppearanceButton);
        actions.Children.Add(reset);
        content.Children.Add(actions);
        UpdateApplyButton();

        return new TabViewItem
        {
            Header = "Appearance",
            Tag = "appearance",
            IsClosable = false,
            IconSource = new FontIconSource { Glyph = "\uE790" },
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            }
        };
    }

    private ColorPicker MakePicker(string hex) => new()
    {
        Width = 240,
        HorizontalAlignment = HorizontalAlignment.Left,
        IsMoreButtonVisible = false,
        IsAlphaEnabled = false,
        IsAlphaSliderVisible = false,
        IsAlphaTextInputVisible = false,
        IsColorSliderVisible = true,
        IsColorChannelTextInputVisible = false,
        IsHexInputVisible = true,
        IsColorPreviewVisible = true,
        Color = ToWinColor(hex)
    };

    private static StackPanel PickerField(string label, ColorPicker picker)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Brush("QuotaDockMutedBrush")
        });
        stack.Children.Add(picker);
        return stack;
    }

    private Button ModeButton(string label, ColorMode mode)
    {
        var button = new Button
        {
            Content = label,
            Tag = mode,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(0)
        };
        button.Click += (_, _) => OnModeClick(mode);
        return button;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingAppearance || themeCombo is null)
        {
            return;
        }

        if (themeCombo.SelectedItem is ComboBoxItem { Tag: ThemeKind kind } && kind != workingAppearance.Theme)
        {
            workingAppearance = workingAppearance with { Theme = kind };
            CommitWorking(syncPickers: false);
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingAppearance || presetCombo is null)
        {
            return;
        }

        if (presetCombo.SelectedItem is ComboBoxItem { Tag: string name } &&
            !string.Equals(name, workingAppearance.Preset, StringComparison.OrdinalIgnoreCase) &&
            AppearancePresets.FindByName(name) is { } preset)
        {
            workingAppearance = preset.Appearance with { Theme = workingAppearance.Theme, Preset = name };
            CommitWorking(syncPickers: true);
        }
    }

    private void OnModeClick(ColorMode mode)
    {
        if (mode == workingAppearance.Mode)
        {
            return;
        }

        var baseAppearance = (mode == ColorMode.Dark
                ? AppearancePresets.FindByName("Default")
                : AppearancePresets.FindByName("Light"))?.Appearance ?? workingAppearance;
        workingAppearance = workingAppearance with
        {
            Mode = mode,
            Background = baseAppearance.Background,
            Text = baseAppearance.Text,
            Foreground = baseAppearance.Foreground,
            Preset = "Custom"
        };
        CommitWorking(syncPickers: true);
    }

    private void OnPickerColor(ColorPicker picker, string which)
    {
        if (syncingAppearance)
        {
            return;
        }

        var hex = HexOf(picker.Color);
        var current = which switch
        {
            "bg" => workingAppearance.Background,
            "text" => workingAppearance.Text,
            "fg" => workingAppearance.Foreground,
            _ => workingAppearance.Accent
        };
        if (string.Equals(hex, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        workingAppearance = which switch
        {
            "bg" => workingAppearance with { Background = hex, Preset = "Custom" },
            "text" => workingAppearance with { Text = hex, Preset = "Custom" },
            "fg" => workingAppearance with { Foreground = hex, Preset = "Custom" },
            _ => workingAppearance with { Accent = hex, Preset = "Custom" }
        };
        CommitWorking(syncPickers: false);
    }

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        workingAppearance = AppearanceSettings.Default with { Theme = workingAppearance.Theme };
        CommitWorking(syncPickers: true);
    }

    private void CommitWorking(bool syncPickers)
    {
        ThemeApplier.ApplyBrushes(workingAppearance);
        ThemeApplier.ApplyToAll(workingAppearance);
        if (syncPickers)
        {
            SyncAppearanceControls();
        }

        appearanceDirty = !Equals(runtime.Settings.Appearance, workingAppearance);
        UpdateApplyButton();
    }

    private async void ApplyAppearance_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await runtime.SaveSettingsAsync(runtime.Settings with { Appearance = workingAppearance });
            appearanceDirty = false;
            ThemeApplier.ApplyBrushes(workingAppearance);
            ThemeApplier.ApplyToAll(workingAppearance);
            UpdateApplyButton();
            SetStatus("Appearance applied and saved for every window.", InfoBarSeverity.Success);
        }
        catch
        {
            SetStatus("Appearance could not be saved.", InfoBarSeverity.Error);
        }
    }

    private void UpdateApplyButton()
    {
        if (applyAppearanceButton is null)
        {
            return;
        }

        applyAppearanceButton.Content = appearanceDirty ? "Apply changes" : "Applied";
        applyAppearanceButton.IsEnabled = appearanceDirty;
    }

    private void SyncAppearanceControls()
    {
        syncingAppearance = true;
        try
        {
            if (cpBackground is not null)
            {
                cpBackground.Color = ToWinColor(workingAppearance.Background);
            }

            if (cpText is not null)
            {
                cpText.Color = ToWinColor(workingAppearance.Text);
            }

            if (cpForeground is not null)
            {
                cpForeground.Color = ToWinColor(workingAppearance.Foreground);
            }

            if (cpAccent is not null)
            {
                cpAccent.Color = ToWinColor(workingAppearance.Accent);
            }

            SelectPresetCombo(workingAppearance.Preset);
            UpdateModeButtons();
        }
        finally
        {
            syncingAppearance = false;
        }
    }

    private void UpdateModeButtons()
    {
        StyleModeButton(darkModeButton, workingAppearance.Mode == ColorMode.Dark);
        StyleModeButton(lightModeButton, workingAppearance.Mode == ColorMode.Light);
    }

    private void StyleModeButton(Button? button, bool selected)
    {
        if (button is null)
        {
            return;
        }

        button.Background = selected
            ? Brush("QuotaDockAccentBrush")
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        button.Foreground = selected ? Brush("QuotaDockOnAccentBrush") : Brush("QuotaDockMutedBrush");
    }

    private void SelectThemeCombo(ThemeKind theme)
    {
        if (themeCombo is null)
        {
            return;
        }

        themeCombo.SelectedItem = themeCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is ThemeKind tag && tag == theme);
    }

    private void SelectPresetCombo(string name)
    {
        if (presetCombo is null)
        {
            return;
        }

        presetCombo.SelectedItem = presetCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, name, StringComparison.OrdinalIgnoreCase))
            ?? presetCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private void ApplyTheme()
    {
        ThemeApplier.ApplyBrushes(workingAppearance);
        ThemeApplier.ApplyToWindow(this, workingAppearance);
    }

    private static Windows.UI.Color ToWinColor(string hex)
    {
        var c = ThemePalette.ParseHex(hex, new Argb(255, 98, 214, 181));
        return Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    private static string HexOf(Windows.UI.Color color) =>
        ThemePalette.ToHex(new Argb(color.A, color.R, color.G, color.B));

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
            CornerRadius = new CornerRadius(4)
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
            DataSourceKind.LocalCli => "Local sign-in",
            _ => connection.Source.ToString()
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        // When the local sign-in is missing or expired, the app fixes it itself:
        // one click runs the provider CLI's own browser login, then reconnects.
        if (latest is { Health: ConnectionHealth.AuthenticationRequired } &&
            connection.Source == DataSourceKind.LocalCli)
        {
            var signIn = new Button
            {
                Content = "Sign in",
                Tag = connection.Provider,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(4),
                Background = Brush("QuotaDockAccentBrush"),
                Foreground = Brush("QuotaDockOnAccentBrush")
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                signIn, $"Sign in to {connection.AccountLabel}");
            signIn.Click += SignIn_Click;
            actions.Children.Add(signIn);
        }

        var disconnect = new Button
        {
            Content = "Disconnect",
            Tag = connection.Id,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(4)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(disconnect, $"Disconnect {connection.AccountLabel}");
        disconnect.Click += Disconnect_Click;
        actions.Children.Add(disconnect);

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
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
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

        // Show/hide controls whether the card appears in the widget at all.
        var show = new CheckBox
        {
            Content = "Show",
            Tag = key,
            IsChecked = !runtime.Settings.HiddenMetricIds.Contains(key, StringComparer.Ordinal),
            VerticalAlignment = VerticalAlignment.Center
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(show, $"Show {metric.Label} in the widget");
        show.Click += ShowCard_Click;

        var toggles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggles.Children.Add(show);
        toggles.Children.Add(pin);

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
                CornerRadius = new CornerRadius(4),
                Minimum = 0,
                Maximum = 100,
                Value = (double)(fraction * 100m),
                Foreground = Brush("QuotaDockAccentBrush"),
                Background = Brush("QuotaDockTrackBrush")
            });
        }

        var pace = UsagePace.Calculate(metric, snapshot.CapturedAt, DateTimeOffset.Now);
        if (pace.Status != PaceStatus.Unknown)
        {
            labelStack.Children.Add(PaceChip(pace));
        }

        header.Children.Add(labelStack);
        Grid.SetColumn(toggles, 1);
        header.Children.Add(toggles);

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
                CornerRadius = new CornerRadius(4)
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

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProviderKind provider } button)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            await RunProviderSignInAsync(provider);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task RunProviderSignInAsync(ProviderKind provider)
    {
        var providerName = provider switch
        {
            ProviderKind.Anthropic => "Claude",
            ProviderKind.Xai => "Grok",
            ProviderKind.Moonshot => "Kimi",
            _ => "Codex"
        };
        SetStatus($"Opening your browser for the {providerName} sign-in… Finish the login there and QuotaDock will pick it up automatically.",
            InfoBarSeverity.Informational);

        var outcome = provider switch
        {
            ProviderKind.Anthropic => await runtime.SignInClaudeAsync(),
            ProviderKind.Xai => await runtime.SignInGrokAsync(),
            ProviderKind.Moonshot => await runtime.SignInKimiAsync(),
            _ => await runtime.SignInCodexAsync()
        };

        if (outcome.Succeeded)
        {
            SetStatus($"{providerName} is signed in and up to date.", InfoBarSeverity.Success);
            return;
        }

        if (outcome.CliMissing && outcome.InstallUrl is not null)
        {
            await ShowInstallDialogAsync(providerName, outcome.Message, outcome.InstallUrl);
            return;
        }

        SetStatus(outcome.Message, InfoBarSeverity.Error);
    }

    private async Task ShowInstallDialogAsync(string providerName, string message, string installUrl)
    {
        var dialog = new ContentDialog
        {
            Title = $"Install {providerName} first",
            Content = new TextBlock
            {
                Text = message + "\n\nAfter installation, come back and press Sign in — QuotaDock handles the login for you.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Open installation guide",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            OpenDefaultBrowser(installUrl);
            SetStatus($"The official {providerName} installation guide opened in your browser.",
                InfoBarSeverity.Informational);
        }
    }

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
        if (result.IsValid)
        {
            SetStatus("Codex connected.", InfoBarSeverity.Success);
            return;
        }

        // The CLI is installed but could not report usage — most often because
        // it is signed out. Offer the app-driven browser login.
        var dialog = new ContentDialog
        {
            Title = "Sign in to Codex",
            Content = new TextBlock
            {
                Text = result.Message + "\n\nQuotaDock can sign you in now: your default browser opens on the official OpenAI login, and QuotaDock connects automatically once you finish.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Sign in with browser",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunProviderSignInAsync(ProviderKind.OpenAI);
        }
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

        // The local sign-in is missing or expired. Offer the app-driven login:
        // QuotaDock runs Claude Code's own browser sign-in and reconnects.
        var dialog = new ContentDialog
        {
            Title = "Sign in to Claude",
            Content = new TextBlock
            {
                Text = result.Message + "\n\nQuotaDock can sign you in now: your default browser opens on the official Claude login, and QuotaDock connects automatically once you finish.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Sign in with browser",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunProviderSignInAsync(ProviderKind.Anthropic);
        }
    }

    private async void ConnectGrok_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Reading your local Grok sign-in…", InfoBarSeverity.Informational);
        var result = await runtime.ConnectAsync("grok-subscription", "Grok account", null, null);
        if (result.IsValid)
        {
            SetStatus("Grok connected. Credits will update automatically.", InfoBarSeverity.Success);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Sign in to Grok",
            Content = new TextBlock
            {
                Text = result.Message + "\n\nQuotaDock can sign you in now: your default browser opens on the official xAI login, and QuotaDock connects automatically once you finish.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Sign in with browser",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunProviderSignInAsync(ProviderKind.Xai);
        }
    }

    private async void ConnectKimi_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Reading your local Kimi sign-in…", InfoBarSeverity.Informational);
        var result = await runtime.ConnectAsync("kimi-subscription", "Kimi account", null, null);
        if (result.IsValid)
        {
            SetStatus("Kimi connected. Session & weekly limits will update automatically.", InfoBarSeverity.Success);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Sign in to Kimi",
            Content = new TextBlock
            {
                Text = result.Message + "\n\nQuotaDock can sign you in now: your default browser opens on the official Kimi login, and QuotaDock connects automatically once you finish.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Sign in with browser",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = DetailsRoot.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunProviderSignInAsync(ProviderKind.Moonshot);
        }
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
            pins.Add(key);
        }
        else if (checkBox.IsChecked != true)
        {
            pins.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
        }

        await runtime.SaveSettingsAsync(runtime.Settings with { PinnedMetricIds = pins });
    }

    private async void ShowCard_Click(object sender, RoutedEventArgs e)
    {
        if (rebuilding || sender is not CheckBox { Tag: string key } checkBox)
        {
            return;
        }

        var hidden = runtime.Settings.HiddenMetricIds.ToList();
        if (checkBox.IsChecked == true)
        {
            hidden.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
        }
        else if (!hidden.Contains(key, StringComparer.Ordinal))
        {
            hidden.Add(key);
        }

        await runtime.SaveSettingsAsync(runtime.Settings with { HiddenMetricIds = hidden });
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
        BorderBrush = Brush("QuotaDockBorderBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
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
        ProviderKind.OpenAI => "Codex",
        ProviderKind.Anthropic => "Claude",
        ProviderKind.Xai => "Grok",
        ProviderKind.Moonshot => "Kimi",
        _ => provider.ToString()
    };

    private static string ProviderGlyph(ProviderKind provider) => provider switch
    {
        ProviderKind.OpenAI => "\uE9D9",
        ProviderKind.Anthropic => "\uE8BD",
        ProviderKind.Xai => "\uE7C3",
        ProviderKind.Moonshot => "\uE735",
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
            CornerRadius = new CornerRadius(4),
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
