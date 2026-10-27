using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ScreenPivot.Core.Model;

namespace ScreenPivot.App.Pages;

/// <summary>x:Bind helpers for page templates.</summary>
public static class Format
{
    public static readonly string[] PriorityNames = { "Ignorar", "Baja", "Normal", "Alta", "Crítica" };

    public static string PriorityLabel(Priority p) => PriorityNames[(int)p];

    public static Brush PriorityBrush(Priority p) => new SolidColorBrush(p switch
    {
        Priority.Critical => ColorHelper.FromArgb(255, 232, 17, 35),
        Priority.High => ColorHelper.FromArgb(255, 247, 99, 12),
        Priority.Normal => ColorHelper.FromArgb(255, 0, 120, 212),
        _ => Colors.Gray,
    });

    public static string Time(DateTimeOffset t) =>
        t.LocalDateTime.Date == DateTime.Today ? t.LocalDateTime.ToString("HH:mm") : t.LocalDateTime.ToString("dd/MM HH:mm");

    public static string Count(int n) => n == 1 ? "1 aviso" : $"{n} avisos";

    public static Visibility VisibleIfText(string text) => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
}
