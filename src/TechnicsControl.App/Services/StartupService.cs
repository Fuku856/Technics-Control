using Microsoft.Win32;

namespace TechnicsControl.App.Services;

/// <summary>Windows 起動時の自動起動（パッケージなしのアプリなのでレジストリの Run キーを使う）。</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TechnicsControl";

    /// <summary>自動起動のときに付ける引数。ウィンドウを開かずトレイにだけ常駐する。</summary>
    public const string BackgroundArgument = "--background";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {BackgroundArgument}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
