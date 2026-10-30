namespace TechnicsControl.Core.Technics;

/// <summary>
/// サウンドモード（イコライザーのプリセット）。値は PEQ のグループ番号（公式アプリの SOUNDE_MODE_*）。
/// カスタムの音質は公式アプリで保存したものが使われる。
/// </summary>
public enum SoundMode : byte
{
    Off = 0,
    BassEnhancer = 1,
    ClearVoice = 2,
    Custom = 3,
    TrebleBoost = 4,
    Dynamic = 5,
    BassEnhancerPlus = 9,
    Custom2 = 10,
    Custom3 = 11,
}

/// <summary>
/// 機種 ID（0x0000）の応答。公式アプリは 4 バイト目の各ビットで、設定項目を表示するかを決めている
/// （DeviceMmiAiroha2.getByteToBitIntArray: 上位ビットから順に並べた配列の添字）。
/// </summary>
public sealed record DeviceCapabilities(ushort ModelId, byte Flags)
{
    public bool MusicVideoBuffer => Bit(0);
    public bool KeyCustomize => Bit(2);
    public bool KeyEnable => Bit(3);
    public bool OutsideToggle => Bit(4);
    public bool LedFlash => Bit(5);

    /// <summary>値部分（ステータスの後ろ）から作る。AZ80 は 70 00 03 FB 19 02。</summary>
    public static DeviceCapabilities FromPayload(ReadOnlySpan<byte> data) =>
        new((ushort)(data[0] | data[1] << 8), data.Length > 3 ? data[3] : (byte)0);

    private bool Bit(int indexFromMsb) => (Flags >> (7 - indexFromMsb) & 1) != 0;
}

/// <summary>マルチポイントの接続台数（0x0032／0x0033）。</summary>
public enum MultiPointMode : byte
{
    Off = 0,
    TwoDevices = 1,
    ThreeDevices = 2,
}

/// <summary>接続モードと LDAC（0x0010／0x0011）。ビットの組み合わせ。</summary>
[Flags]
public enum A2dpOption : byte
{
    /// <summary>接続優先（音質優先のビットなし）。</summary>
    ConnectionPriority = 0,
    SoundQualityPriority = 0x01,
    Ldac = 0x10,
}

/// <summary>通話相手からのノイズを抑える強さ（0x0034／0x0035）。</summary>
public enum NoiseReduction : byte
{
    Normal = 0,
    High = 1,
}

/// <summary>
/// ヘッドホン装着検知（拡張版 0x004D／0x004E）。
/// PauseMusic: 外したら音楽を止める。TouchWhenNotWorn: 外しているときもタッチ操作を受け付ける。
/// ResumeMusic: 着け直したら音楽を再開する。
/// </summary>
public sealed record WearingDetection(bool Enabled, bool PauseMusic, bool TouchWhenNotWorn, bool ResumeMusic)
{
    public static WearingDetection FromPayload(ReadOnlySpan<byte> data) =>
        new(data[0] != 0, data[1] != 0, data[2] != 0, data.Length > 3 && data[3] != 0);

    public byte[] ToPayload() => [B(Enabled), B(PauseMusic), B(TouchWhenNotWorn), B(ResumeMusic)];

    private static byte B(bool value) => (byte)(value ? 1 : 0);
}

/// <summary>オートパワーオフ（0x0006／0x0007）。分は 5／10／30／60。</summary>
public sealed record AutoPowerOff(bool Enabled, byte Minutes)
{
    public static readonly byte[] AllowedMinutes = [5, 10, 30, 60];

    public static AutoPowerOff FromPayload(ReadOnlySpan<byte> data) => new(data[0] != 0, data[1]);

    public byte[] ToPayload()
    {
        if (!AllowedMinutes.Contains(Minutes))
            throw new ArgumentOutOfRangeException(nameof(Minutes), "オートパワーオフの時間は 5／10／30／60 分です。");
        return [(byte)(Enabled ? 1 : 0), Minutes];
    }
}

/// <summary>キー割り当ての左右（0x0019／0x001A の 1 バイト目）。主側・相方とは関係なく、0 が左。</summary>
public enum KeySide : byte
{
    Left = 0,
    Right = 1,
}

/// <summary>タッチセンサーの操作（キー割り当ての 4 つの枠の順番）。</summary>
public enum KeyGesture
{
    SingleTap = 0,
    DoubleTap = 1,
    TripleTap = 2,
    LongPress = 3,
}

/// <summary>
/// キーに割り当てる機能。値はイヤホンに送るコード（公式アプリの KeyCustomizeManager.KeyMapping）。
/// </summary>
public enum KeyFunction : byte
{
    None = 0x00,
    PlayPause = 0x10,
    PreviousTrack = 0x11,
    NextTrack = 0x12,
    VolumeDown = 0x13,
    VolumeUp = 0x14,
    NoiseCanceling = 0x20,
    Ambient = 0x21,
    OutsideOff = 0x22,
    OutsideSwitching = 0x23,
    PowerOff = 0x24,
    VoiceAssistant = 0x25,
    AnswerCall = 0x30,
    RejectCall = 0x31,
    EndCall = 0x40,
    Mute = 0x41,
    NoiseReduction = 0x42,
}

/// <summary>
/// 片耳のキー割り当て。音楽再生中・着信時・通話中のそれぞれに、4 つの操作（1 回・2 回・3 回タップ、長押し）の機能を持つ。
/// </summary>
public sealed record Keymap(KeySide Side, KeyFunction[] Music, KeyFunction[] IncomingCall, KeyFunction[] Talking)
{
    /// <summary>音楽再生中に割り当てられる機能（AZ80 で使えるもの。公式アプリの GeneralOperation*）。</summary>
    public static readonly KeyFunction[] MusicChoices =
    [
        KeyFunction.PlayPause, KeyFunction.PreviousTrack, KeyFunction.NextTrack, KeyFunction.VolumeDown, KeyFunction.VolumeUp,
        KeyFunction.VoiceAssistant, KeyFunction.OutsideSwitching, KeyFunction.NoiseCanceling, KeyFunction.Ambient,
        KeyFunction.OutsideOff, KeyFunction.None,
    ];

    /// <summary>長押しだけは電源 OFF も選べる。</summary>
    public static readonly KeyFunction[] MusicLongPressChoices = [.. MusicChoices[..^1], KeyFunction.PowerOff, KeyFunction.None];

    /// <summary>着信時（1 回タップと長押しだけ変更できる）。</summary>
    public static readonly KeyFunction[] IncomingCallChoices = [KeyFunction.AnswerCall, KeyFunction.RejectCall, KeyFunction.None];

    /// <summary>通話中（2 回・3 回タップと長押しだけ変更できる）。</summary>
    public static readonly KeyFunction[] TalkingChoices =
    [
        KeyFunction.Mute, KeyFunction.NoiseReduction, KeyFunction.VolumeDown, KeyFunction.VolumeUp, KeyFunction.OutsideSwitching,
        KeyFunction.NoiseCanceling, KeyFunction.Ambient, KeyFunction.OutsideOff, KeyFunction.EndCall, KeyFunction.None,
    ];

    /// <summary>通知の値部分（[7] 左右, [8] センサー, [9..20] 機能）から作る。</summary>
    public static Keymap FromPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14)
            throw new FormatException("キー割り当ての応答が短すぎます。");
        static KeyFunction[] Read(ReadOnlySpan<byte> s) => [(KeyFunction)s[0], (KeyFunction)s[1], (KeyFunction)s[2], (KeyFunction)s[3]];
        return new Keymap((KeySide)data[0], Read(data[2..6]), Read(data[6..10]), Read(data[10..14]));
    }

    public byte[] ToPayload()
    {
        if (Music.Length != 4 || IncomingCall.Length != 4 || Talking.Length != 4)
            throw new ArgumentException("キー割り当ては 4 つずつ必要です。");
        return [(byte)Side, 0, .. Music.Select(f => (byte)f), .. IncomingCall.Select(f => (byte)f), .. Talking.Select(f => (byte)f)];
    }

    public bool Equals(Keymap? other) =>
        other is not null && Side == other.Side && Music.SequenceEqual(other.Music)
        && IncomingCall.SequenceEqual(other.IncomingCall) && Talking.SequenceEqual(other.Talking);

    public override int GetHashCode() => HashCode.Combine(Side, Music.Length);
}
