namespace TechnicsControl.App.Services;

/// <summary>想定外の例外を %LOCALAPPDATA%\TechnicsControl\error.log に残す。</summary>
public static class ErrorLog
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechnicsControl", "error.log");

    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 例外を記録して握りつぶす。DispatcherQueue のコールバック（トレイアイコンの操作など）で投げられた例外は
    /// Application.UnhandledException を通らずにプロセスを落とすので、そこから呼ぶ処理は必ずこれで包む。
    /// </summary>
    public static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Write(e);
        }
    }

    public static async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            Write(e);
        }
    }
}
