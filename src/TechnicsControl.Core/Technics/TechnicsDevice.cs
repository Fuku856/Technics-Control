using System.Text;
using TechnicsControl.Core.Race;
using TechnicsControl.Core.Transport;

namespace TechnicsControl.Core.Technics;

/// <summary>
/// Technics のイヤホン（EAH-AZ80 で確認）を RACE で操作する。
/// NC と外音取り込みの設定は、左右が必ず同じになるように確認しながら設定する。
/// </summary>
public sealed class TechnicsDevice : IAsyncDisposable
{
    private readonly RaceClient _client;
    private (byte Type, byte Id)? _partnerDst;

    private TechnicsDevice(RaceClient client)
    {
        _client = client;
        _client.IndicationReceived += OnIndication;
        _client.Closed += error => Disconnected?.Invoke(error);
    }

    /// <summary>左右がずれたとき、相方へ中継で同じ設定を送るか（公式アプリは使っていない経路）。</summary>
    public bool AllowPartnerRelaySet { get; set; }

    /// <summary>左右の確認で、不一致のときに読み直すまでの待ち時間。</summary>
    public TimeSpan SyncRetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    public bool HasPartner => _partnerDst is not null;

    public RaceClient Client => _client;

    /// <summary>イヤホン側の操作（タッチなど）で外音コントロールが変わったとき。</summary>
    public event Action<OutsideControl>? OutsideControlChanged;

    public event Action<Exception?>? Disconnected;

    public static async Task<TechnicsDevice> ConnectAsync(ulong bluetoothAddress, CancellationToken cancellationToken = default)
    {
        var transport = await RfcommTransport.ConnectAsync(bluetoothAddress, cancellationToken).ConfigureAwait(false);
        return await CreateAsync(transport, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TechnicsDevice> CreateAsync(IRaceTransport transport, CancellationToken cancellationToken = default)
    {
        var device = new TechnicsDevice(new RaceClient(transport));
        try
        {
            await device.RefreshPartnerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await device.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return device;
    }

    /// <summary>相方のイヤホンがつながっているかを調べ直す（0x0D00）。</summary>
    public async Task<bool> RefreshPartnerAsync(CancellationToken cancellationToken = default)
    {
        // 応答はステータスなしで (種別, ID) の組が並ぶ
        var response = await _client.SendUncheckedAsync(RacePacket.Request(RelayIds.GetAvailableDst), cancellationToken).ConfigureAwait(false);
        _partnerDst = null;
        var data = response.Payload;
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            if (data[i] == RelayIds.PartnerType)
            {
                _partnerDst = (data[i], data[i + 1]);
                break;
            }
        }
        return HasPartner;
    }

    // ── 外音コントロール（NC／外音取り込み） ──────────────────────────────

    public async Task<OutsideControl> GetOutsideControlAsync(EarbudRole role = EarbudRole.Agent, CancellationToken cancellationToken = default) =>
        OutsideControl.FromPayload(await GetAsync(TechnicsCommands.GetOutsideControl, role, cancellationToken).ConfigureAwait(false));

    /// <summary>外音コントロールを設定し、左右が一致するまで確認する。</summary>
    public Task<SyncResult<OutsideControl>> SetOutsideControlAsync(OutsideControl value, CancellationToken cancellationToken = default) =>
        SetSyncedAsync(TechnicsCommands.SetOutsideControl, value.ToPayload(), role => GetOutsideControlAsync(role, cancellationToken), value, cancellationToken);

    /// <summary>
    /// 確認なしで外音コントロールを送る（ダイヤルをドラッグ中の途中経過用）。
    /// 最終値は必ず <see cref="SetOutsideControlAsync"/> で送り直すこと。
    /// </summary>
    public Task SendOutsideControlAsync(OutsideControl value, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetOutsideControl, value.ToPayload(), cancellationToken);

    public async Task<AmbientSetting> GetAmbientSettingAsync(EarbudRole role = EarbudRole.Agent, CancellationToken cancellationToken = default) =>
        AmbientSetting.FromPayload(await GetAsync(TechnicsCommands.GetAmbientMode, role, cancellationToken).ConfigureAwait(false));

    public Task<SyncResult<AmbientSetting>> SetAmbientSettingAsync(AmbientSetting value, CancellationToken cancellationToken = default) =>
        SetSyncedAsync(TechnicsCommands.SetAmbientMode, value.ToPayload(), role => GetAmbientSettingAsync(role, cancellationToken), value, cancellationToken);

    public async Task<NoiseCancelingAdjust> GetNoiseCancelingAdjustAsync(EarbudRole role = EarbudRole.Agent, CancellationToken cancellationToken = default) =>
        new((await GetAsync(TechnicsCommands.GetNoiseCancelingAdjust, role, cancellationToken).ConfigureAwait(false))[0]);

    public Task<SyncResult<NoiseCancelingAdjust>> SetNoiseCancelingAdjustAsync(NoiseCancelingAdjust value, NoiseCancelingAdjustMode mode = NoiseCancelingAdjustMode.Save, CancellationToken cancellationToken = default)
    {
        if (!NoiseCancelingAdjust.IsValid(value.Level))
            throw new ArgumentOutOfRangeException(nameof(value), "NC の最適化の値が範囲外です。");
        return SetSyncedAsync(TechnicsCommands.SetNoiseCancelingAdjust, [value.Level, (byte)mode],
            role => GetNoiseCancelingAdjustAsync(role, cancellationToken), value, cancellationToken);
    }

    public async Task<OutsideToggle> GetOutsideToggleAsync(EarbudRole role = EarbudRole.Agent, CancellationToken cancellationToken = default) =>
        (OutsideToggle)(await GetAsync(TechnicsCommands.GetOutsideToggle, role, cancellationToken).ConfigureAwait(false))[0];

    public Task<SyncResult<OutsideToggle>> SetOutsideToggleAsync(OutsideToggle value, CancellationToken cancellationToken = default)
    {
        // 切り替え順は 2 つ以上のモードを含む必要がある
        if (System.Numerics.BitOperations.PopCount((uint)value) < 2 || (value & ~(OutsideToggle.Off | OutsideToggle.NoiseCanceling | OutsideToggle.Ambient)) != 0)
            throw new ArgumentOutOfRangeException(nameof(value), "切り替え順には 2 つ以上のモードを選んでください。");
        return SetSyncedAsync(TechnicsCommands.SetOutsideToggle, [(byte)value],
            role => GetOutsideToggleAsync(role, cancellationToken), value, cancellationToken);
    }

    // ── 表示用の情報 ─────────────────────────────────────────

    public async Task<BatteryLevel> GetBatteryAsync(EarbudRole role, CancellationToken cancellationToken = default)
    {
        var p = await _client.SendAsync(RacePacket.Request(TechnicsCommands.GetBattery, (byte)role), RaceReply.Indication, cancellationToken).ConfigureAwait(false);
        return new BatteryLevel((EarbudRole)p.At(7), p.At(8));
    }

    public async Task<byte> GetCradleBatteryAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync(TechnicsCommands.GetCradleBattery, EarbudRole.Agent, cancellationToken).ConfigureAwait(false))[0];

    public async Task<CodecInfo> GetCodecInfoAsync(CancellationToken cancellationToken = default) =>
        CodecInfo.FromPayload(await GetAsync(TechnicsCommands.GetCodecInfo, EarbudRole.Agent, cancellationToken).ConfigureAwait(false));

    /// <summary>主側が右のイヤホンか（0x0CD4）。</summary>
    public async Task<bool> IsAgentRightAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync(TechnicsCommands.IsAgentRight, EarbudRole.Agent, cancellationToken).ConfigureAwait(false))[0] != 0;

    public async Task<string> GetFirmwareVersionAsync(EarbudRole role, CancellationToken cancellationToken = default)
    {
        var p = await _client.SendAsync(RacePacket.Request(TechnicsCommands.GetFirmwareVersion, (byte)role), RaceReply.Indication, cancellationToken).ConfigureAwait(false);
        var length = p.At(8);
        return Encoding.ASCII.GetString(p.Payload, 9 - RacePacket.HeaderSize, Math.Min(length, p.Payload.Length - 3)).TrimEnd('\0');
    }

    public async Task<IReadOnlyList<string>> GetConnectedDevicesAsync(CancellationToken cancellationToken = default)
    {
        var data = await GetAsync(TechnicsCommands.GetConnectedDevices, EarbudRole.Agent, cancellationToken).ConfigureAwait(false);
        var count = data[0];
        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var offset = 1 + i * 32;
            if (offset + 32 > data.Length)
                break;
            names.Add(Encoding.UTF8.GetString(data, offset, 32).TrimEnd('\0'));
        }
        return names;
    }

    public async Task<UsageTime> GetUsageTimeAsync(EarbudRole role, CancellationToken cancellationToken = default)
    {
        var p = await _client.SendAsync(RacePacket.Request(TechnicsCommands.GetUsageTime, (byte)role), RaceReply.Indication, cancellationToken).ConfigureAwait(false);
        return new UsageTime((EarbudRole)p.At(7),
            BitConverter.ToUInt32(p.Payload, 8 - RacePacket.HeaderSize),
            BitConverter.ToUInt32(p.Payload, 12 - RacePacket.HeaderSize));
    }

    public async Task<ChargeError> GetChargeErrorAsync(EarbudRole role, CancellationToken cancellationToken = default)
    {
        var p = await _client.SendAsync(RacePacket.Request(TechnicsCommands.GetChargeError, (byte)role), RaceReply.Indication, cancellationToken).ConfigureAwait(false);
        return new ChargeError((EarbudRole)p.At(7), p.At(8), BitConverter.ToUInt16(p.Payload, 9 - RacePacket.HeaderSize));
    }

    // ── 設定ページの項目 ───────────────────────────────────────

    public async Task<MultiPointMode> GetMultiPointAsync(CancellationToken cancellationToken = default) =>
        (MultiPointMode)(await GetAsync(TechnicsCommands.GetMultiPoint, cancellationToken: cancellationToken).ConfigureAwait(false))[0];

    public Task SetMultiPointAsync(MultiPointMode value, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetMultiPoint, [(byte)value], cancellationToken);

    public async Task<A2dpOption> GetA2dpOptionAsync(CancellationToken cancellationToken = default) =>
        (A2dpOption)(await GetAsync(TechnicsCommands.GetA2dpOption, cancellationToken: cancellationToken).ConfigureAwait(false))[0];

    public Task SetA2dpOptionAsync(A2dpOption value, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetA2dpOption, [(byte)value], cancellationToken);

    public async Task<WearingDetection> GetWearingDetectionAsync(CancellationToken cancellationToken = default) =>
        WearingDetection.FromPayload(await GetAsync(TechnicsCommands.GetWearingDetection3, cancellationToken: cancellationToken).ConfigureAwait(false));

    public Task SetWearingDetectionAsync(WearingDetection value, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetWearingDetection3, value.ToPayload(), cancellationToken);

    public async Task<AutoPowerOff> GetAutoPowerOffAsync(CancellationToken cancellationToken = default) =>
        AutoPowerOff.FromPayload(await GetAsync(TechnicsCommands.GetAutoPowerOff, cancellationToken: cancellationToken).ConfigureAwait(false));

    public Task SetAutoPowerOffAsync(AutoPowerOff value, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetAutoPowerOff, value.ToPayload(), cancellationToken);

    public async Task<bool> GetTouchEnabledAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync(TechnicsCommands.GetKeyEnable, cancellationToken: cancellationToken).ConfigureAwait(false))[0] != 0;

    public Task SetTouchEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetKeyEnable, [(byte)(enabled ? 1 : 0)], cancellationToken);

    public async Task<bool> GetLedFlashAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync(TechnicsCommands.GetLedFlash, cancellationToken: cancellationToken).ConfigureAwait(false))[0] != 0;

    public Task SetLedFlashAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetLedFlash, [(byte)(enabled ? 1 : 0)], cancellationToken);

    public async Task<NoiseReduction> GetNoiseReductionAsync(CancellationToken cancellationToken = default) =>
        (NoiseReduction)(await GetAsync(TechnicsCommands.GetNoiseReduction, cancellationToken: cancellationToken).ConfigureAwait(false))[0];

    public Task SetNoiseReductionAsync(NoiseReduction value, CancellationToken cancellationToken = default) =>
        SetAsync(TechnicsCommands.SetNoiseReduction, [(byte)value], cancellationToken);

    /// <summary>片耳のキー割り当て（値は通知で返る）。</summary>
    public async Task<Keymap> GetKeymapAsync(KeySide side, CancellationToken cancellationToken = default)
    {
        var p = await _client.SendAsync(RacePacket.Request(TechnicsCommands.GetKeymap, (byte)side, 0), RaceReply.Indication, cancellationToken).ConfigureAwait(false);
        return Keymap.FromPayload(p.Payload.AsSpan(1));
    }

    /// <summary>片耳のキー割り当てを設定し、読み直した値を返す。</summary>
    public async Task<Keymap> SetKeymapAsync(Keymap value, CancellationToken cancellationToken = default)
    {
        await _client.SendAsync(RacePacket.Request(TechnicsCommands.SetKeymap, value.ToPayload()), RaceReply.Indication, cancellationToken).ConfigureAwait(false);
        return await GetKeymapAsync(value.Side, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>サウンドモード（PEQ のグループ番号）。応答は [モジュール(2), ステータス, 番号]。</summary>
    public async Task<SoundMode> GetSoundModeAsync(EarbudRole role = EarbudRole.Agent, CancellationToken cancellationToken = default)
    {
        var request = RacePacket.Request(PeqIds.GetGroup, 0, 0);
        var response = role == EarbudRole.Agent
            ? await _client.SendUncheckedAsync(request, cancellationToken).ConfigureAwait(false)
            : await SendToPartnerAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Payload is not [0, 0, 0, var index, ..])
            throw new RaceException($"サウンドモードを読めませんでした（{response}）。");
        return (SoundMode)index;
    }

    /// <summary>サウンドモードを設定し、左右が一致するまで確認する。</summary>
    public Task<SyncResult<SoundMode>> SetSoundModeAsync(SoundMode value, CancellationToken cancellationToken = default) =>
        SetSyncedAsync(PeqIds.SetGroup, [0, 0, (byte)value], role => GetSoundModeAsync(role, cancellationToken), value, cancellationToken,
            async (id, payload, ct) =>
            {
                var response = await _client.SendUncheckedAsync(RacePacket.Request(id, payload), ct).ConfigureAwait(false);
                if (response.Payload is not [0, 0, 0, ..])
                    throw new RaceException($"サウンドモードを設定できませんでした（{response}）。");
            });

    private Task<RacePacket> SendToPartnerAsync(RacePacket request, CancellationToken cancellationToken)
    {
        var dst = _partnerDst ?? throw new RaceException("相方のイヤホンがつながっていません。");
        return _client.SendToPartnerAsync(dst.Type, dst.Id, request, cancellationToken: cancellationToken);
    }

    // ── 汎用 ─────────────────────────────────────────────────

    /// <summary>取得系コマンドの値部分（ステータスの後ろ）を返す。相方は中継で読む。</summary>
    public async Task<byte[]> GetAsync(ushort id, EarbudRole role = EarbudRole.Agent, CancellationToken cancellationToken = default)
    {
        var request = RacePacket.Request(id);
        RacePacket response;
        if (role == EarbudRole.Agent)
        {
            response = await _client.SendAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var dst = _partnerDst ?? throw new RaceException("相方のイヤホンがつながっていません。");
            response = await _client.SendToPartnerAsync(dst.Type, dst.Id, request, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.Status != 0)
                throw new RaceException($"相方の 0x{id:X4} がエラーを返しました（status=0x{response.Status:X2}）。");
        }
        return response.Payload[1..];
    }

    /// <summary>設定系コマンドを送る。応答のステータスが 0 以外なら例外。</summary>
    public Task SetAsync(ushort id, byte[] payload, CancellationToken cancellationToken = default) =>
        _client.SendAsync(RacePacket.Request(id, payload), cancellationToken: cancellationToken);

    /// <summary>左右の値を両方読む。相方がいなければ主側だけ。</summary>
    public async Task<SyncedValue<T>> ReadBothAsync<T>(Func<EarbudRole, Task<T>> read)
    {
        var agent = await read(EarbudRole.Agent).ConfigureAwait(false);
        if (!HasPartner)
            return new SyncedValue<T>(agent, default, false);
        try
        {
            return new SyncedValue<T>(agent, await read(EarbudRole.Partner).ConfigureAwait(false), true);
        }
        catch (Exception ex) when (ex is TimeoutException or RaceException)
        {
            // 片耳がケースに入れられた直後など。相方の有無を調べ直す
            if (!await RefreshPartnerAsync().ConfigureAwait(false))
                return new SyncedValue<T>(agent, default, false);
            throw;
        }
    }

    /// <summary>
    /// 左右の値を読み、食い違っていれば少し待ってもう一度読む。
    /// 設定やタッチ操作の直後は、相方への反映が遅れて一瞬だけずれて見えることがあるため。
    /// </summary>
    public async Task<SyncedValue<T>> ReadBothSettledAsync<T>(Func<EarbudRole, Task<T>> read, CancellationToken cancellationToken = default)
    {
        var value = await ReadBothAsync(read).ConfigureAwait(false);
        if (value.Matches)
            return value;
        await Task.Delay(SyncRetryDelay, cancellationToken).ConfigureAwait(false);
        return await ReadBothAsync(read).ConfigureAwait(false);
    }

    /// <summary>
    /// 左右で同じでなければならない設定（キー割り当ては左右で別々に持つので含まない）。
    /// 装着検知は、相方からの読み取りを確認済みの 0x0023 で比べる。
    /// </summary>
    public static readonly IReadOnlyList<(ushort Id, string Name)> PairedSettings =
    [
        (TechnicsCommands.GetOutsideControl, "外音コントロール"),
        (TechnicsCommands.GetAmbientMode, "アンビエントモード"),
        (PeqIds.GetGroup, "サウンドモード"),
        (TechnicsCommands.GetNoiseCancelingAdjust, "NC の最適化"),
        (TechnicsCommands.GetOutsideToggle, "タッチでの切り替え順"),
        (TechnicsCommands.GetA2dpOption, "接続モード／LDAC"),
        (TechnicsCommands.GetMultiPoint, "マルチポイント"),
        (TechnicsCommands.GetWearingDetection, "装着検知"),
        (TechnicsCommands.GetAutoPowerOff, "オートパワーオフ"),
        (TechnicsCommands.GetKeyEnable, "タッチ操作"),
        (TechnicsCommands.GetLedFlash, "LED の点滅"),
        (TechnicsCommands.GetNoiseReduction, "通話のノイズ抑制"),
    ];

    /// <summary>
    /// 左右で同じでなければならない設定を、左右から読んで比べる（読むだけで、何も変えない）。
    /// どれかが読めなかったら（応答がないなど）、待ち続けないよう残りは比べずに「不明」とする。
    /// </summary>
    public async Task<IReadOnlyList<SettingComparison>> CompareSettingsAsync(IEnumerable<ushort>? ids = null, CancellationToken cancellationToken = default)
    {
        var targets = ids is null ? PairedSettings : PairedSettings.Where(s => ids.Contains(s.Id)).ToList();
        var results = new List<SettingComparison>();
        var readable = HasPartner;
        foreach (var (id, name) in targets)
        {
            if (!readable)
            {
                results.Add(new SettingComparison(id, name, null, null));
                continue;
            }
            try
            {
                var value = await ReadBothSettledAsync(async role => Convert.ToHexString(await ReadRawAsync(id, role, cancellationToken).ConfigureAwait(false)),
                    cancellationToken).ConfigureAwait(false);
                results.Add(new SettingComparison(id, name, value.Agent, value.PartnerPresent ? value.Partner : null));
                readable = value.PartnerPresent;
            }
            catch (Exception ex) when (ex is TimeoutException or RaceException)
            {
                results.Add(new SettingComparison(id, name, null, null));
                readable = false;
            }
        }
        return results;
    }

    private async Task<byte[]> ReadRawAsync(ushort id, EarbudRole role, CancellationToken cancellationToken) =>
        id == PeqIds.GetGroup
            ? [(byte)await GetSoundModeAsync(role, cancellationToken).ConfigureAwait(false)]
            : await GetAsync(id, role, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 左右を揃える設定の手順。
    /// 主側へ送る → 左右を読んで比べる → 不一致なら読み直し → 主側へ再送 →（許可されていれば）相方へ中継で送る。
    /// </summary>
    private async Task<SyncResult<T>> SetSyncedAsync<T>(ushort setId, byte[] payload, Func<EarbudRole, Task<T>> read, T expected,
        CancellationToken cancellationToken, Func<ushort, byte[], CancellationToken, Task>? send = null)
    {
        // 応答の形がふつうと違うコマンド（PEQ など）は、送り方を渡してもらう
        send ??= SetAsync;
        await send(setId, payload, cancellationToken).ConfigureAwait(false);
        var attempts = 1;
        var value = await ReadBothAsync(read).ConfigureAwait(false);
        if (IsSynced(value, expected))
            return new SyncResult<T>(value, attempts, false);

        await Task.Delay(SyncRetryDelay, cancellationToken).ConfigureAwait(false);
        value = await ReadBothAsync(read).ConfigureAwait(false);
        if (IsSynced(value, expected))
            return new SyncResult<T>(value, attempts, false);

        attempts++;
        await send(setId, payload, cancellationToken).ConfigureAwait(false);
        await Task.Delay(SyncRetryDelay, cancellationToken).ConfigureAwait(false);
        value = await ReadBothAsync(read).ConfigureAwait(false);
        if (IsSynced(value, expected) || !AllowPartnerRelaySet || _partnerDst is not { } dst)
            return new SyncResult<T>(value, attempts, false);

        attempts++;
        var relayed = await _client.SendToPartnerAsync(dst.Type, dst.Id, RacePacket.Request(setId, payload), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (relayed.Status != 0)
            throw new RaceException($"相方への 0x{setId:X4} の中継に失敗しました（status=0x{relayed.Status:X2}）。");
        await Task.Delay(SyncRetryDelay, cancellationToken).ConfigureAwait(false);
        return new SyncResult<T>(await ReadBothAsync(read).ConfigureAwait(false), attempts, true);
    }

    private static bool IsSynced<T>(SyncedValue<T> value, T expected) =>
        value.Matches && EqualityComparer<T>.Default.Equals(value.Agent, expected);

    private void OnIndication(RacePacket packet)
    {
        if (packet.Id == TechnicsCommands.GetOutsideControl && packet.Payload.Length >= 4)
            OutsideControlChanged?.Invoke(OutsideControl.FromPayload(packet.Payload.AsSpan(1)));
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
