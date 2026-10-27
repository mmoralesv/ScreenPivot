using System.Globalization;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenPivot.App.Services;
using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Sources;

namespace ScreenPivot.App.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly ObservableCollection<RuleRow> _rules = new();
    private readonly ObservableCollection<TitleRuleRow> _titleRules = new();
    private readonly List<SoundRow> _soundRows = new();
    private AppServices? _services;
    private bool _loadingStartup;
    private bool _languageChanged;

    public SettingsPage()
    {
        InitializeComponent();
        RulesList.ItemsSource = _rules;
        TitleRulesList.ItemsSource = _titleRules;
        if (App.PrivacyPolicyUrl.Length > 0) PrivacyLink.NavigateUri = new Uri(App.PrivacyPolicyUrl);
        else PrivacyLink.Visibility = Visibility.Collapsed;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _services = (AppServices)e.Parameter;
        _services.StateChanged += OnStateChanged;
        Load(_services.Settings);
        RefreshStatus();

        _loadingStartup = true;
        StartupToggle.IsOn = await StartupService.IsEnabledAsync();
        _loadingStartup = false;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_services is not null) _services.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e) => RefreshStatus();

    private void Load(AppSettings s)
    {
        LanguageBox.SelectedIndex = Math.Max(0, LanguageBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => string.Equals((string)i.Tag, s.Language, StringComparison.OrdinalIgnoreCase)));
        PausedToggle.IsOn = s.Paused;
        SoundToggle.IsOn = s.SoundEnabled;
        DigestToggle.IsOn = s.ShowDigestOnReturn;
        MaximizedToggle.IsOn = s.IncludeMaximized;
        CornerBox.SelectedIndex = (int)s.Corner;
        SwitchModeBox.SelectedIndex = (int)s.SwitchMode;
        QuickAccessToggle.IsOn = s.ShowQuickAccess;
        BannerSecondsBox.Value = s.HighBannerSeconds;
        RetentionBox.Value = s.RetentionHours;
        VipBox.Text = string.Join(Environment.NewLine, s.VipSenders);
        KeywordsBox.Text = string.Join(Environment.NewLine, s.CriticalKeywords);
        MutedBox.Text = string.Join(Environment.NewLine, s.MutedApps);

        var monitors = RemoteSessionDetector.GetMonitors();
        FillMonitors(MonitorBox, monitors, Loc.Get("Monitor_AutoBanner"), s.MonitorIndex);
        FillMonitors(PanelMonitorBox, monitors, Loc.Get("Monitor_AutoPanel"), s.PanelMonitorIndex);
        PanelEdgeBox.SelectedIndex = (int)s.PanelEdge;

        _rules.Clear();
        foreach (var rule in s.Rules) _rules.Add(RuleRow.From(rule));

        SoundRows.Children.Clear();
        _soundRows.Clear();
        SoundStatus.Text = "";
        foreach (var p in new[] { Priority.Critical, Priority.High, Priority.Normal, Priority.Low })
        {
            var row = new SoundRow(p, s.Sounds.For(p), PickWavAsync, text => SoundStatus.Text = text);
            _soundRows.Add(row);
            SoundRows.Children.Add(row.Element);
        }

        FlashToggle.IsOn = s.WatchFlashes;
        TitlesToggle.IsOn = s.WatchTitles;
        _titleRules.Clear();
        foreach (var rule in s.TitleRules) _titleRules.Add(TitleRuleRow.From(rule));
    }

    private AppSettings Read() => new()
    {
        Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
        Paused = PausedToggle.IsOn,
        SoundEnabled = SoundToggle.IsOn,
        ShowDigestOnReturn = DigestToggle.IsOn,
        IncludeMaximized = MaximizedToggle.IsOn,
        Corner = (OverlayCorner)Math.Max(0, CornerBox.SelectedIndex),
        SwitchMode = (SwitchMode)Math.Max(0, SwitchModeBox.SelectedIndex),
        ShowQuickAccess = QuickAccessToggle.IsOn,
        MonitorIndex = MonitorBox.SelectedIndex - 1,
        PanelEdge = (PanelEdge)Math.Max(0, PanelEdgeBox.SelectedIndex),
        PanelMonitorIndex = PanelMonitorBox.SelectedIndex - 1,
        HighBannerSeconds = double.IsNaN(BannerSecondsBox.Value) ? 6 : (int)BannerSecondsBox.Value,
        RetentionHours = double.IsNaN(RetentionBox.Value) ? 24 : (int)RetentionBox.Value,
        VipSenders = Lines(VipBox.Text),
        CriticalKeywords = Lines(KeywordsBox.Text),
        MutedApps = Lines(MutedBox.Text),
        Rules = _rules.Where(r => !r.IsEmpty).Select(r => r.ToRule()).ToList(),
        Sounds = ReadSounds(),
        WatchFlashes = FlashToggle.IsOn,
        WatchTitles = TitlesToggle.IsOn,
        TitleRules = _titleRules.Where(r => !r.IsEmpty).Select(r => r.ToRule()).ToList(),
        OnboardingCompleted = true,
    };

    /// <summary>First entry is "automatic" (-1); then one entry per monitor, so index = SelectedIndex - 1.</summary>
    private static void FillMonitors(ComboBox box, IReadOnlyList<MonitorDesc> monitors, string automatic, int selected)
    {
        box.Items.Clear();
        box.Items.Add(automatic);
        foreach (var m in monitors)
            box.Items.Add(Loc.Format("Monitor_Item", m.Index + 1, m.Primary ? Loc.Get("Monitor_Primary") : "", m.Bounds.Width, m.Bounds.Height));
        box.SelectedIndex = selected + 1 < box.Items.Count ? selected + 1 : 0;
    }

    private PrioritySounds ReadSounds()
    {
        var sounds = new PrioritySounds();
        foreach (var row in _soundRows) sounds.Set(row.Priority, row.Id);
        return sounds;
    }

    private static async Task<string?> PickWavAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".wav");
        // Desktop apps must give WinRT pickers an owner window.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.Current!.PanelHandle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>First invalid title pattern, as a message; empty when all compile.</summary>
    private string ValidateTitleRules()
    {
        foreach (var row in _titleRules.Where(r => !r.IsEmpty))
        {
            if (row.Pattern.Trim().Length == 0) return Loc.Format("Validation_NoPattern", row.Name);
            if (TitleTracker.TryCreate(row.Pattern.Trim(), out var error) is null)
                return Loc.Format("Validation_BadPattern", row.Name, error);
        }
        return "";
    }

    private static List<string> Lines(string text) =>
        text.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    private void RefreshStatus()
    {
        if (_services is null) return;

        var access = _services.Toasts.CheckAccess();
        (AccessBar.Severity, AccessBar.Title, AccessBar.Message, AccessButton.Content, AccessButton.Visibility) = access switch
        {
            ToastAccess.Allowed => (InfoBarSeverity.Success, Loc.Get("Access_AllowedTitle"),
                _services.Toasts.UsingChangeEvent ? Loc.Get("Access_AllowedEvent") : Loc.Get("Access_AllowedPolling"),
                "", Visibility.Collapsed),
            ToastAccess.Denied => (InfoBarSeverity.Error, Loc.Get("Access_DeniedTitle"),
                Loc.Get("Access_DeniedMessage"), Loc.Get("Access_OpenSettings"), Visibility.Visible),
            ToastAccess.Unavailable => (InfoBarSeverity.Error, Loc.Get("Access_UnavailableTitle"),
                Loc.Get("Access_UnavailableMessage"), "", Visibility.Collapsed),
            _ => (InfoBarSeverity.Warning, Loc.Get("Access_NeededTitle"),
                Loc.Get("Access_NeededMessage"),
                Loc.Get("Access_Allow"), Visibility.Visible),
        };

        var d = _services.Detection;
        var covered = d.Monitors.Where(m => m.Index < d.Coverage.Count && d.Coverage[m.Index] != CoverageKind.None)
            .Select(m => Loc.Format(d.Coverage[m.Index] == CoverageKind.FullScreen ? "Detection_MonitorFull" : "Detection_MonitorMaximized", m.Index + 1))
            .ToList();
        DetectionText.Text = d.SessionName.Length == 0
            ? Loc.Get("Detection_NoSession")
            : covered.Count == 0
                ? Loc.Format("Detection_NotCovering", d.SessionName)
                : Loc.Format("Detection_Covering", d.SessionName, string.Join(", ", covered));
    }

    private async void OnAccessClick(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        if (_services.Toasts.Access == ToastAccess.Denied)
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-notifications"));
        else
            await _services.RequestNotificationAccessAsync();
        RefreshStatus();
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingStartup) return;
        var actual = await StartupService.SetEnabledAsync(StartupToggle.IsOn);
        if (actual != StartupToggle.IsOn)
        {
            _loadingStartup = true;
            StartupToggle.IsOn = actual;
            _loadingStartup = false;
        }
    }

    private void OnTestBanner(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        if (ValidateTitleRules().Length == 0) _services.SaveSettings(Read()); // test with the placement currently on screen
        TestHint.Text = Loc.Get("Settings_TestHintNow");
        _services.ShowTestBanner(TimeSpan.FromSeconds(5));
    }

    private void OnAddRule(object sender, RoutedEventArgs e) =>
        _rules.Add(new RuleRow { Name = Loc.Get("Rule_New") });

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RuleRow row }) _rules.Remove(row);
    }

    private void OnAddTitleRule(object sender, RoutedEventArgs e) =>
        _titleRules.Add(new TitleRuleRow { Name = Loc.Get("TitleRule_New") });

    private void OnRemoveTitleRule(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TitleRuleRow row }) _titleRules.Remove(row);
    }

    private void OnRestart(object sender, RoutedEventArgs e) => App.Current?.Restart();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var error = ValidateTitleRules();
        if (error.Length > 0)
        {
            SavedText.Text = Loc.Format("Save_Failed", error);
            return;
        }
        var language = _services?.Settings.Language;
        _services?.SaveSettings(Read());
        // The language only changes after a restart; offer it while the saved choice differs from the running one.
        if (_services is not null && _services.Settings.Language != language) _languageChanged = true;
        RestartPanel.Visibility = _languageChanged ? Visibility.Visible : Visibility.Collapsed;
        App.Current?.RepositionPanel(); // a new edge or monitor applies right away
        SavedText.Text = Loc.Format("Save_Done", DateTime.Now.ToString("T", CultureInfo.CurrentCulture));
    }
}
