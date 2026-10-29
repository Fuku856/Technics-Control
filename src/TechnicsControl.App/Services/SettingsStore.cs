using System.Text.Json;

namespace TechnicsControl.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>本体色（公式アプリの画像の色名）。</summary>
public enum BodyColor
{
    Black,
    Silver,
    MidnightBlue,
}

/// <summary>アプリの設定。%LOCALAPPDATA%\TechnicsControl\settings.json に保存する。</summary>
public sealed class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechnicsControl", "settings.json");

    public AppTheme Theme { get; set; } = AppTheme.System;

    public BodyColor BodyColor
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            BodyColorChanged?.Invoke();
        }
    } = BodyColor.Black;

    public event Action? BodyColorChanged;

    /// <summary>
    /// ウィンドウを閉じているあいだは接続を切る（スマホの公式アプリと併用するため）。
    /// RACE チャネルは同時に 1 台しか使えない。
    /// </summary>
    public bool DisconnectWhenHidden { get; set; }

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.SettingsStore) ?? new SettingsStore();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new SettingsStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJsonContext.Default.SettingsStore));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(SettingsStore))]
internal sealed partial class SettingsJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
