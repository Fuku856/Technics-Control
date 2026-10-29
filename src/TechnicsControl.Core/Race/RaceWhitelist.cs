namespace TechnicsControl.Core.Race;

/// <summary>
/// 送信してよい RACE ID の一覧。ここにない ID（ファームウェア更新、Flash／NVKey 書き込み、
/// 初期化、電源 OFF など）は送らない。
/// </summary>
public static class RaceWhitelist
{
    private static readonly HashSet<ushort> Allowed =
    [
        // Panasonic 独自: 取得
        0x0000, 0x0002, 0x0004, 0x0006, 0x0008, 0x000A, 0x0010, 0x0012, 0x0013, 0x0015, 0x0017, 0x0019,
        0x0021, 0x0023, 0x0025, 0x0026, 0x0028, 0x002E, 0x0032, 0x0034, 0x0038, 0x003A, 0x0040,
        0x0043, 0x0049, 0x004A, 0x004C, 0x004D, 0x0053, 0x005B, 0x0067,
        // Panasonic 独自: 設定（計画で実装する項目だけ）
        0x0007, 0x000B, 0x0011, 0x0014, 0x0016, 0x0018, 0x001A, 0x0022, 0x0024, 0x0033, 0x0035, 0x0039, 0x004E,
        // Airoha 標準: 読み取り
        0x0301, // SDK 版
        0x1E08, // ビルド版
        0x0CD4, // 主側が右か
        0x0CD6, // 電池
        0x1C07, // ファームウェア版（FOTA 系の範囲だが読み取り専用）
        0x0D00, // 中継先の一覧
        0x0D01, // 中継（内側のパケットも別途検査する）
    ];

    public static bool IsAllowed(ushort id) => Allowed.Contains(id);

    public static void Ensure(RacePacket packet)
    {
        // 0x0900／0x0901（MMI の設定と取得）は他の機能にも使う汎用コマンドなので、イコライザー（モジュール 0）だけ許可する
        var isPeq = packet.Payload is [0, 0] && packet.Id == PeqIds.GetGroup
                    || packet.Payload is [0, 0, _] && packet.Id == PeqIds.SetGroup;
        if (!IsAllowed(packet.Id) && !isPeq)
            throw new InvalidOperationException($"RACE ID 0x{packet.Id:X4} は送信が許可されていません。");
        if (packet.Id == RelayIds.PassToDst)
        {
            if (packet.Payload.Length < 2 + RacePacket.HeaderSize)
                throw new InvalidOperationException("中継パケットの中身が不正です。");
            Ensure(RacePacket.Parse(packet.Payload.AsSpan(2)));
        }
    }
}

/// <summary>イコライザー（PEQ）のグループ番号の取得と設定（Airoha の MMI。モジュール 0）。</summary>
public static class PeqIds
{
    public const ushort SetGroup = 0x0900;
    public const ushort GetGroup = 0x0901;
}

public static class RelayIds
{
    public const ushort GetAvailableDst = 0x0D00;
    public const ushort PassToDst = 0x0D01;

    /// <summary>中継先の種別: 相方のイヤホン（AWS peer）。</summary>
    public const byte PartnerType = 0x05;
}
