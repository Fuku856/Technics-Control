namespace TechnicsControl.Core.Technics;

/// <summary>イヤホンの主側（Agent、スマホや PC とつながっている側）と相方（Partner）。</summary>
public enum EarbudRole : byte
{
    Agent = 0,
    Partner = 1,
}

/// <summary>外音コントロールのモード（0x000A／0x000B の mode）。</summary>
public enum OutsideMode : byte
{
    Off = 0,
    NoiseCanceling = 1,
    Ambient = 2,
}

/// <summary>外音取り込みの種類（0x0021／0x0022 の 1 バイト目）。</summary>
public enum AmbientType : byte
{
    Transparent = 0,
    Attention = 1,
}

/// <summary>
/// 外音コントロール。強さはイヤホン側にモードごとに保存されていて、どちらも 1〜100。
/// </summary>
public sealed record OutsideControl(OutsideMode Mode, byte NoiseCancelingLevel, byte AmbientLevel)
{
    public const byte MinLevel = 1;
    public const byte MaxLevel = 100;

    public static OutsideControl FromPayload(ReadOnlySpan<byte> data) => new((OutsideMode)data[0], data[1], data[2]);

    public byte[] ToPayload()
    {
        if (!Enum.IsDefined(Mode))
            throw new ArgumentOutOfRangeException(nameof(Mode));
        if (NoiseCancelingLevel is < MinLevel or > MaxLevel || AmbientLevel is < MinLevel or > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(NoiseCancelingLevel), "強さは 1〜100 で指定してください。");
        return [(byte)Mode, NoiseCancelingLevel, AmbientLevel];
    }

    /// <summary>今のモードで調整する強さ（OFF のときは null）。</summary>
    public byte? CurrentLevel => Mode switch
    {
        OutsideMode.NoiseCanceling => NoiseCancelingLevel,
        OutsideMode.Ambient => AmbientLevel,
        _ => null,
    };
}

/// <summary>アンビエントモードの設定。StopMusic は「外音取り込み中は音楽を止める」。</summary>
public sealed record AmbientSetting(AmbientType Type, bool StopMusic)
{
    public static AmbientSetting FromPayload(ReadOnlySpan<byte> data) => new((AmbientType)data[0], data[1] != 0);

    public byte[] ToPayload() => [(byte)Type, (byte)(StopMusic ? 1 : 0)];
}

/// <summary>
/// ノイズキャンセリングの最適化。Level は 0 が標準、20〜40 が -6.0〜+4.0dB（0.5dB 刻み、32 が 0dB）。
/// </summary>
public sealed record NoiseCancelingAdjust(byte Level)
{
    public const byte Default = 0;
    public const byte MinLevel = 20;
    public const byte MaxLevel = 40;

    public double? Decibel => Level == Default ? null : (Level - 32) * 0.5;

    public static bool IsValid(byte level) => level == Default || level is >= MinLevel and <= MaxLevel;
}

/// <summary>NC 強度の調整を送るときの扱い（公式アプリの NOISE_CANCELING_ADJUST_MODE_*）。</summary>
public enum NoiseCancelingAdjustMode : byte
{
    Temporary = 0,
    Execute = 1,
    Save = 2,
}

/// <summary>タッチで外音コントロールを切り替えるときに巡回するモード（ビットの組み合わせ）。</summary>
[Flags]
public enum OutsideToggle : byte
{
    None = 0,
    Off = 1,
    NoiseCanceling = 2,
    Ambient = 4,
}

/// <summary>A2DP コーデックの種類（0x0012 の 1 バイト目）。</summary>
public enum CodecType : byte
{
    Unknown = 0,
    Sbc = 1,
    Aac = 2,
    AptX = 3,
    AptXLowLatency = 4,
    AptXHd = 5,
    Ldac = 6,
    UsbAudio = 32,
    AnalogAudio = 33,
}

public sealed record CodecInfo(CodecType Type, byte SamplingFrequencyCode, byte ChannelModeCode)
{
    private static readonly int[] Frequencies = [0, 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000];

    public int? SamplingFrequency => SamplingFrequencyCode < Frequencies.Length && SamplingFrequencyCode > 0
        ? Frequencies[SamplingFrequencyCode]
        : null;

    public static CodecInfo FromPayload(ReadOnlySpan<byte> data) => new((CodecType)data[0], data[1], data[2]);
}

public sealed record BatteryLevel(EarbudRole Role, byte Percent);

/// <summary>使用時間（分）。AgentMinutes は主側として動いていた時間。</summary>
public sealed record UsageTime(EarbudRole Role, uint TotalMinutes, uint AgentMinutes);

/// <summary>充電エラーの回数（連続回数と累計回数）。</summary>
public sealed record ChargeError(EarbudRole Role, byte Consecutive, ushort Total);

/// <summary>左右それぞれの値と、両者が一致しているか。</summary>
public sealed record SyncedValue<T>(T Agent, T? Partner, bool PartnerPresent)
{
    public bool Matches => !PartnerPresent || EqualityComparer<T?>.Default.Equals(Agent, Partner);
}

/// <summary>左右を揃える設定の結果。</summary>
public sealed record SyncResult<T>(SyncedValue<T> Value, int Attempts, bool UsedPartnerRelay);
