using Microsoft.Win32;
using ScreenPivot.Core.Interop;
using Windows.ApplicationModel;

namespace ScreenPivot.App.Services;

/// <summary>Launch at sign-in: the MSIX startup task when packaged, the HKCU Run key when running unpackaged.</summary>
internal static class StartupService
{
    public const string BackgroundArgument = "--background";
    private const string TaskId = "ScreenPivotStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenPivot";

    public static async Task<bool> IsEnabledAsync()
    {
        if (Win32.HasPackageIdentity())
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    /// <returns>The resulting state; the user or policy can refuse enabling.</returns>
    public static async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (Win32.HasPackageIdentity())
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!enabled)
            {
                task.Disable();
                return false;
            }
            var state = await task.RequestEnableAsync();
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {BackgroundArgument}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        return enabled;
    }
}
