using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenPivot.App.Services;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.App.Pages;

/// <summary>
/// One "priority → sound" row: built-in Windows sounds, the chosen WAV file (if any) and a last entry that opens
/// a file picker. Cancelling the picker restores the previous choice.
/// </summary>
internal sealed class SoundRow
{
    private static readonly string PickFileLabel = Loc.Get("Sound_PickFile");

    private readonly Func<Task<string?>> _pickFile;
    private readonly Action<string> _report;
    private readonly ComboBox _box = new() { MinWidth = 160 };
    private readonly List<string> _ids = new();
    private bool _updating;

    public SoundRow(Priority priority, string id, Func<Task<string?>> pickFile, Action<string> report)
    {
        Priority = priority;
        Id = id;
        _pickFile = pickFile;
        _report = report;

        var test = new Button { Content = Loc.Get("Sound_Test") };
        test.Click += (_, _) => Test();
        _box.SelectionChanged += OnSelectionChanged;

        // label | selector (fills the panel width) | test
        var label = new TextBlock { Text = Format.PriorityLabel(priority), VerticalAlignment = VerticalAlignment.Center, Width = 56 };
        _box.HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_box, 1);
        Grid.SetColumn(test, 2);
        grid.Children.Add(label);
        grid.Children.Add(_box);
        grid.Children.Add(test);
        Element = grid;
        Fill();
    }

    public Priority Priority { get; }

    public string Id { get; private set; }

    public UIElement Element { get; }

    private void Fill()
    {
        _updating = true;
        _box.Items.Clear();
        _ids.Clear();
        foreach (var choice in SoundCatalog.Builtin()) Add(choice.Id, choice.Name);
        if (SoundCatalog.IsFile(Id)) Add(Id, SoundCatalog.Describe(Id));
        else if (!_ids.Contains(Id)) Add(Id, SoundCatalog.Describe(Id)); // alias set by hand in settings.json
        _box.Items.Add(PickFileLabel);
        _box.SelectedIndex = _ids.IndexOf(Id);
        _updating = false;
    }

    private void Add(string id, string name)
    {
        _ids.Add(id);
        _box.Items.Add(name);
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _box.SelectedIndex < 0) return;
        if (_box.SelectedIndex < _ids.Count)
        {
            Id = _ids[_box.SelectedIndex];
            return;
        }

        var path = await _pickFile();
        if (path is not null) Id = SoundCatalog.FromFile(path);
        Fill();
        if (path is not null) Test();
    }

    private void Test()
    {
        if (Id.Length == 0)
        {
            _report(Loc.Format("Sound_Silent", Format.PriorityLabel(Priority)));
            return;
        }
        _report(SoundPlayer.Play(Id)
            ? Loc.Format("Sound_Playing", Format.PriorityLabel(Priority), SoundCatalog.Describe(Id))
            : Loc.Format("Sound_Failed", SoundCatalog.Describe(Id)));
    }
}
