using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.Core.Overlay;

/// <summary>Where the docked panel goes, in physical pixels.</summary>
public static class PanelLayout
{
    /// <summary>Width of a left/right panel, in DIPs.</summary>
    public const int SideWidth = 420;

    /// <summary>Height of a top/bottom panel, in DIPs.</summary>
    public const int BandHeight = 440;

    /// <summary>A top/bottom panel is centred and no wider than this, in DIPs, so cards stay readable.</summary>
    public const int BandMaxWidth = 980;

    /// <param name="area">
    /// The whole monitor when the remote session covers it (its own taskbar is hidden), else the work area so the
    /// host taskbar stays visible.
    /// </param>
    public static Rect Compute(Rect area, PanelEdge edge, double scale)
    {
        int side = Math.Min((int)Math.Round(SideWidth * scale), area.Width);
        int band = Math.Min((int)Math.Round(BandHeight * scale), area.Height);
        int bandWidth = Math.Min((int)Math.Round(BandMaxWidth * scale), area.Width);
        int bandLeft = area.Left + (area.Width - bandWidth) / 2;

        return edge switch
        {
            PanelEdge.Left => new Rect(area.Left, area.Top, area.Left + side, area.Bottom),
            PanelEdge.Top => new Rect(bandLeft, area.Top, bandLeft + bandWidth, area.Top + band),
            PanelEdge.Bottom => new Rect(bandLeft, area.Bottom - band, bandLeft + bandWidth, area.Bottom),
            _ => new Rect(area.Right - side, area.Top, area.Right, area.Bottom),
        };
    }
}
