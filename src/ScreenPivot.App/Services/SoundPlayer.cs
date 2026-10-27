using System.Runtime.InteropServices;
using ScreenPivot.Core.Diagnostics;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.App.Services;

/// <summary>Plays <see cref="SoundCatalog"/> ids asynchronously; a missing sound stays silent instead of beeping.</summary>
internal static class SoundPlayer
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_ALIAS = 0x00010000;
    private const uint SND_FILENAME = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    /// <returns>False when there is nothing to play or Windows could not play it.</returns>
    public static bool Play(string id)
    {
        var target = SoundCatalog.Target(id);
        if (target.Length == 0) return false;

        bool ok;
        if (SoundCatalog.IsFile(id))
            ok = File.Exists(target) && PlaySound(target, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
        else
            ok = PlaySound(target, IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT);

        if (!ok) DiagnosticsLog.Write($"sound failed: {(SoundCatalog.IsFile(id) ? "file" : target)}");
        return ok;
    }
}
