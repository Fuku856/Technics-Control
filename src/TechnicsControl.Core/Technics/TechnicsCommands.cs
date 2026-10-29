namespace TechnicsControl.Core.Technics;

/// <summary>
/// Panasonic 独自の RACE ID（公式アプリの com.airoha.libbase.RaceCommand.packet.pana.RaceCmd* から）と、
/// 使用する Airoha 標準の RACE ID。
/// </summary>
public static class TechnicsCommands
{
    public const ushort GetModelId = 0x0000;
    public const ushort GetColor = 0x0002;
    public const ushort GetAutoPowerOff = 0x0006;
    public const ushort SetAutoPowerOff = 0x0007;
    public const ushort GetOutsideControl = 0x000A;
    public const ushort SetOutsideControl = 0x000B;
    public const ushort GetA2dpOption = 0x0010;
    public const ushort SetA2dpOption = 0x0011;
    public const ushort GetCodecInfo = 0x0012;
    public const ushort GetLedFlash = 0x0013;
    public const ushort SetLedFlash = 0x0014;
    public const ushort GetOutsideToggle = 0x0015;
    public const ushort SetOutsideToggle = 0x0016;
    public const ushort GetKeyEnable = 0x0017;
    public const ushort SetKeyEnable = 0x0018;
    public const ushort GetKeymap = 0x0019;
    public const ushort SetKeymap = 0x001A;
    public const ushort GetAmbientMode = 0x0021;
    public const ushort SetAmbientMode = 0x0022;
    public const ushort GetWearingDetection = 0x0023;
    public const ushort SetWearingDetection = 0x0024;
    public const ushort GetMultiPoint = 0x0032;
    public const ushort SetMultiPoint = 0x0033;
    public const ushort GetNoiseReduction = 0x0034;
    public const ushort SetNoiseReduction = 0x0035;
    public const ushort GetNoiseCancelingAdjust = 0x0038;
    public const ushort SetNoiseCancelingAdjust = 0x0039;
    public const ushort GetCradleBattery = 0x0040;
    public const ushort GetWearingDetection3 = 0x004D;
    public const ushort SetWearingDetection3 = 0x004E;
    public const ushort GetConnectedDevices = 0x0049;
    public const ushort GetUsageTime = 0x004C;
    public const ushort GetChargeError = 0x0053;

    // Airoha 標準
    public const ushort GetSdkVersion = 0x0301;
    public const ushort IsAgentRight = 0x0CD4;
    public const ushort GetBattery = 0x0CD6;
    public const ushort GetFirmwareVersion = 0x1C07;
}
