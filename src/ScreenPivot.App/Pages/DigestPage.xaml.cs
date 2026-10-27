using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ScreenPivot.App.Services;
using ScreenPivot.Core.Digest;
using ScreenPivot.Core.Model;

namespace ScreenPivot.App.Pages;

public sealed partial class DigestPage : Page
{
    private AppServices? _services;

    public DigestPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _services = (AppServices)e.Parameter;
        _services.StateChanged += OnStateChanged;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_services is not null) _services.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e) => Refresh();

    private void OnFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => Refresh();

    private void OnMarkReviewed(object sender, RoutedEventArgs e) => _services?.MarkReviewed();

    private void OnReturnToRemote(object sender, RoutedEventArgs e) => _services?.ReturnToRemote();

    private void OnMinimizeRemote(object sender, RoutedEventArgs e) => _services?.MinimizeRemote();

    private void OnOpenApp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AppGroup group } && group.Latest.Count > 0)
            _services?.OpenEventApp(group.Latest[0]);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _services?.Digest.Clear();
        Refresh();
    }

    private void Refresh()
    {
        if (_services is null) return;
        var hub = _services.Hub;

        IReadOnlyList<HostEvent> events;
        if (Filter.SelectedItem == AllFilter)
            events = _services.Digest.All();
        else if (_services.IsDemo)
            events = _services.LastSession; // the sample session, even if a real remote session is open
        else if (hub.RemoteSince is { } since)
            events = _services.Digest.Since(since).Where(x => x.ReceivedWhileRemote).ToList();
        else
            events = _services.LastSession;

        var summary = RulesSummarizer.Summarize(events);
        Headline.Text = summary.Headline;
        Groups.ItemsSource = summary.Groups;
        EmptyText.Visibility = summary.Total == 0 ? Visibility.Visible : Visibility.Collapsed;

        var pending = hub.Pending().Count;
        var state = _services.Settings.Paused ? "Avisos en pausa."
            : hub.IsRemote ? $"Cloud PC a pantalla completa desde las {Format.Time(hub.RemoteSince!.Value)}."
            : "Trabajando en el equipo.";
        StatusText.Text = pending == 0 ? state : $"{state} {Format.Count(pending)} sin revisar.";

        // Footer: both switch buttons, only "back" once the session is minimized, nothing without a session.
        bool hasSession = _services.HasSession;
        bool canMinimize = hasSession && !_services.IsParked;
        Footer.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        MinimizeButton.Visibility = canMinimize ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(ReturnButton, canMinimize ? 1 : 0);
        Grid.SetColumnSpan(ReturnButton, canMinimize ? 1 : 2);
        NoticeBar.Message = _services.Notice;
        NoticeBar.IsOpen = _services.Notice.Length > 0;
    }
}
