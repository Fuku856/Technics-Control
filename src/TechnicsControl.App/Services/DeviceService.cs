using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using TechnicsControl.Core.Discovery;
using TechnicsControl.Core.Race;
using TechnicsControl.Core.Technics;
using TechnicsControl.Core.Transport;

namespace TechnicsControl.App.Services;

public enum ConnectionState
{
    Searching,
    Connected,
    NotFound,
    Busy,
    Error,
    Paused,
}

/// <summary>画面で選ぶ 4 つのモード。外音取り込みは 2 種類に分けて見せる。</summary>
public enum UiMode
{
    NoiseCanceling,
    Transparent,
    Attention,
    Off,
}

/// <summary>
/// イヤホンとの接続と、画面に出す状態をまとめて持つ。UI スレッドで使う。
/// </summary>
public sealed partial class DeviceService : ObservableObject
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LevelPreviewInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>強さを変えてからこの間は、イヤホンから返ってくる同じモードの値（途中で送った値）で表示を戻さない。</summary>
    private static readonly TimeSpan LevelEchoWindow = TimeSpan.FromMilliseconds(1500);

    private readonly DispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0);
    // モードの切り替えと強さの確定は、確認と再送が入れ違わないよう 1 つずつ行う
    private readonly SemaphoreSlim _outsideGate = new(1, 1);
    private TechnicsDevice? _device;
    private DateTime _lastLevelPreview = DateTime.MinValue;
    private int? _pendingLevel;
    private bool _previewScheduled;
    private UiMode? _requestedMode;
    private bool _applyingMode;
    private int _applyingCount;

    // 画面から外音コントロールを変えるたびに増やす。読み直しの途中で変わったら、読んだ値は古いので使わない
    private int _outsideVersion;
    private DateTime _levelChangedAt = DateTime.MinValue;

    public DeviceService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [ObservableProperty]
    public partial ConnectionState State { get; private set; } = ConnectionState.Searching;

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "イヤホンを探しています…";

    [ObservableProperty]
    public partial string DeviceName { get; private set; } = "Technics EAH-AZ80";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode), nameof(Level), nameof(ModeText))]
    public partial OutsideControl? Outside { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode), nameof(Level), nameof(ModeText))]
    public partial AmbientSetting? Ambient { get; private set; }

    [ObservableProperty]
    public partial bool PartnerPresent { get; private set; }

    /// <summary>NC／外音取り込みの設定が左右で一致しているか。null は未確認。</summary>
    [ObservableProperty]
    public partial bool? LeftRightMatch { get; private set; }

    [ObservableProperty]
    public partial int? LeftBattery { get; private set; }

    [ObservableProperty]
    public partial int? RightBattery { get; private set; }

    [ObservableProperty]
    public partial int? CaseBattery { get; private set; }

    [ObservableProperty]
    public partial CodecInfo? Codec { get; private set; }

    /// <summary>主側が右のイヤホンか。左右の電池や相方の表示に使う。</summary>
    [ObservableProperty]
    public partial bool AgentIsRight { get; private set; }

    [ObservableProperty]
    public partial bool IsApplying { get; private set; }

    public bool IsConnected => State == ConnectionState.Connected;

    public UiMode? Mode => Outside?.Mode switch
    {
        OutsideMode.NoiseCanceling => UiMode.NoiseCanceling,
        OutsideMode.Ambient => Ambient?.Type == AmbientType.Attention ? UiMode.Attention : UiMode.Transparent,
        OutsideMode.Off => UiMode.Off,
        _ => null,
    };

    /// <summary>今のモードで調整できる強さ。OFF とアテンションには強さがないので null。</summary>
    public int? Level => Mode == UiMode.Attention ? null : Outside?.CurrentLevel;

    public string ModeText => Mode is { } mode ? ModeName(mode) : "—";

    /// <summary>ウィンドウを閉じているあいだ接続を切る設定（SettingsStore と同期）。</summary>
    public bool Paused { get; private set; }

    public event Action? DeviceStateChanged;

    public static string ModeName(UiMode mode) => mode switch
    {
        UiMode.NoiseCanceling => "ノイズキャンセリング",
        UiMode.Transparent => "外音取り込み（トランスペアレント）",
        UiMode.Attention => "外音取り込み（アテンション）",
        _ => "OFF",
    };

    public static string ShortModeName(UiMode mode) => mode switch
    {
        UiMode.NoiseCanceling => "NC",
        UiMode.Transparent => "トランスペアレント",
        UiMode.Attention => "アテンション",
        _ => "OFF",
    };

    public void Start() => _ = LoopAsync(_stop.Token);

    public void Stop() => _stop.Cancel();

    /// <summary>すぐに接続を試す（再試行ボタン）。</summary>
    public void Reconnect()
    {
        Paused = false;
        _wake.Release();
    }

    /// <summary>接続を切って、Reconnect まで再接続しない。</summary>
    public async Task PauseAsync()
    {
        Paused = true;
        await DisconnectAsync("接続を切っています（スマホのアプリと併用中）");
        State = ConnectionState.Paused;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_device is null && !Paused)
                await TryConnectAsync(cancellationToken);
            else if (_device is not null)
                await RefreshAsync();

            var interval = _device is null ? RetryInterval : RefreshInterval;
            try
            {
                await _wake.WaitAsync(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        await DisconnectAsync("終了しました");
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        State = ConnectionState.Searching;
        StatusMessage = "イヤホンを探しています…";
        try
        {
            var devices = await DeviceFinder.FindTechnicsAsync(cancellationToken);
            var target = devices.FirstOrDefault(d => d.IsConnected);
            if (target is null)
            {
                State = ConnectionState.NotFound;
                StatusMessage = devices.Count == 0
                    ? "ペアリング済みの Technics のイヤホンがありません。Windows の Bluetooth 設定でペアリングしてください。"
                    : "イヤホンが PC に接続されていません。ケースから取り出して接続してください。";
                return;
            }
            DeviceName = target.Name;
            var device = await TechnicsDevice.ConnectAsync(target.BluetoothAddress, cancellationToken);
            // 主側への設定が相方に届かないときの最終手段として、相方への中継を許可する（実機で動作確認済み）
            device.AllowPartnerRelaySet = true;
            device.OutsideControlChanged += value => _dispatcher.TryEnqueue(() => OnOutsideChangedByDevice(value));
            device.Disconnected += error => _dispatcher.TryEnqueue(() => _ = DisconnectAsync("イヤホンとの接続が切れました。再接続します…"));
            _device = device;
            State = ConnectionState.Connected;
            StatusMessage = "接続中";
            AgentIsRight = await device.IsAgentRightAsync();
            await RefreshAsync();
        }
        catch (TransportException ex)
        {
            State = ex.ChannelBusy ? ConnectionState.Busy : ConnectionState.Error;
            StatusMessage = ex.Message;
        }
        catch (Exception ex) when (ex is RaceException or TimeoutException or OperationCanceledException or IOException)
        {
            await DisconnectAsync($"接続できませんでした: {ex.Message}");
            State = ConnectionState.Error;
        }
        finally
        {
            OnPropertyChanged(nameof(IsConnected));
        }
    }

    /// <summary>状態をまとめて読み直す（定期的に呼ばれる）。</summary>
    public async Task RefreshAsync()
    {
        await RunAsync(async device =>
        {
            var version = _outsideVersion;
            var outside = await device.ReadBothAsync(role => device.GetOutsideControlAsync(role));
            var ambient = await device.ReadBothAsync(role => device.GetAmbientSettingAsync(role));
            // 読んでいるあいだに画面から変えていたら、読んだ値は古い（表示が一瞬戻ってしまう）。次の読み直しに任せる
            if (version == _outsideVersion && !IsApplying && !IsAdjustingLevel)
            {
                Outside = outside.Agent;
                Ambient = ambient.Agent;
                PartnerPresent = outside.PartnerPresent;
                LeftRightMatch = outside.PartnerPresent ? outside.Matches && ambient.Matches : null;
            }

            var agent = await device.GetBatteryAsync(EarbudRole.Agent);
            int? partner = device.HasPartner ? (await device.GetBatteryAsync(EarbudRole.Partner)).Percent : null;
            int? own = agent.Percent;
            (LeftBattery, RightBattery) = AgentIsRight ? (partner, own) : (own, partner);
            CaseBattery = await device.GetCradleBatteryAsync();
            Codec = await device.GetCodecInfoAsync();
            return true;
        });
        DeviceStateChanged?.Invoke();
    }

    /// <summary>
    /// モードを切り替える。左右が一致するまで確認する。
    /// 切り替え中に別のモードが選ばれたら、今の切り替えが終わってから最後に選ばれたモードだけを適用する。
    /// </summary>
    public async Task SetModeAsync(UiMode mode)
    {
        _requestedMode = mode;
        if (_applyingMode)
            return;
        _applyingMode = true;
        BeginApplying();
        try
        {
            while (_requestedMode is { } next)
            {
                // 強さの確定中なら、終わってから切り替える
                await _outsideGate.WaitAsync();
                try
                {
                    _requestedMode = null;
                    await ApplyModeAsync(next);
                }
                finally
                {
                    _outsideGate.Release();
                }
            }
        }
        finally
        {
            _applyingMode = false;
            EndApplying();
        }
    }

    /// <summary>まだ適用していない、最後に選ばれたモード（なければ null）。</summary>
    public UiMode? RequestedMode => _requestedMode;

    private async Task ApplyModeAsync(UiMode mode)
    {
        if (Outside is not { } current || mode == Mode)
            return;
        _outsideVersion++;
        await RunAsync(async device =>
            {
                var matched = true;
                if (mode is UiMode.Transparent or UiMode.Attention)
                {
                    // 外音取り込みの種類を先に決めてから、外音取り込みに切り替える
                    var type = mode == UiMode.Attention ? AmbientType.Attention : AmbientType.Transparent;
                    if (Ambient?.Type != type)
                    {
                        var ambient = await device.SetAmbientSettingAsync(new AmbientSetting(type, Ambient?.StopMusic ?? false));
                        Ambient = ambient.Value.Agent;
                        matched &= ambient.Value.Matches;
                    }
                }
                var outsideMode = mode switch
                {
                    UiMode.NoiseCanceling => OutsideMode.NoiseCanceling,
                    UiMode.Off => OutsideMode.Off,
                    _ => OutsideMode.Ambient,
                };
                var result = await device.SetOutsideControlAsync(current with { Mode = outsideMode });
                Outside = result.Value.Agent;
                PartnerPresent = result.Value.PartnerPresent;
                matched &= result.Value.Matches;
                LeftRightMatch = PartnerPresent ? matched : null;
                return true;
            });
    }

    /// <summary>
    /// ダイヤルを動かしている途中の強さ。0.5 秒に 1 回まで、確認なしで送る（公式アプリと同じ間隔）。
    /// </summary>
    public void PreviewLevel(int level)
    {
        _pendingLevel = level;
        if (_previewScheduled)
            return;
        var wait = LevelPreviewInterval - (DateTime.UtcNow - _lastLevelPreview);
        _previewScheduled = true;
        _ = SendPreviewAsync(wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
    }

    private async Task SendPreviewAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        _previewScheduled = false;
        // モードの切り替え中は送らない（前のモードで送ると切り替えが戻る）
        if (_applyingMode || _pendingLevel is not { } level || WithLevel(level) is not { } value)
            return;
        _pendingLevel = null;
        _lastLevelPreview = DateTime.UtcNow;
        // 送った値を今の値として扱う（あとから届く古い値や、モードの切り替えで強さが戻らないように）
        Outside = value;
        MarkLevelChanged();
        await RunAsync(async device =>
        {
            await device.SendOutsideControlAsync(value);
            return true;
        });
    }

    /// <summary>ダイヤルを離したときの最終値。送ったあと左右が一致しているか確認する。</summary>
    public async Task CommitLevelAsync(int level)
    {
        _pendingLevel = null;
        if (WithLevel(level) is not { } value)
            return;
        if (!_applyingMode)
            Outside = value;
        MarkLevelChanged();
        BeginApplying();
        await _outsideGate.WaitAsync();
        try
        {
            // 待っているあいだにモードが切り替わったら、前のモードの強さは送らない
            if (Outside?.Mode != value.Mode)
                return;
            await RunAsync(async device =>
            {
                var result = await device.SetOutsideControlAsync(value);
                Outside = result.Value.Agent;
                PartnerPresent = result.Value.PartnerPresent;
                LeftRightMatch = PartnerPresent ? result.Value.Matches : null;
                return true;
            });
        }
        finally
        {
            _outsideGate.Release();
            MarkLevelChanged();
            EndApplying();
        }
    }

    private bool IsAdjustingLevel => DateTime.UtcNow - _levelChangedAt < LevelEchoWindow;

    private void MarkLevelChanged()
    {
        _outsideVersion++;
        _levelChangedAt = DateTime.UtcNow;
    }

    private void BeginApplying()
    {
        _applyingCount++;
        IsApplying = true;
    }

    private void EndApplying()
    {
        if (--_applyingCount == 0)
            IsApplying = false;
        DeviceStateChanged?.Invoke();
    }

    private OutsideControl? WithLevel(int level)
    {
        // アテンションは強さを調整できない
        if (Mode == UiMode.Attention)
            return null;
        var clamped = (byte)Math.Clamp(level, OutsideControl.MinLevel, OutsideControl.MaxLevel);
        return Outside?.Mode switch
        {
            OutsideMode.NoiseCanceling => Outside with { NoiseCancelingLevel = clamped },
            OutsideMode.Ambient => Outside with { AmbientLevel = clamped },
            _ => null,
        };
    }

    /// <summary>
    /// 設定ページなどからイヤホンを操作する。接続が切れていれば null を返し、エラーは StatusMessage に出す。
    /// </summary>
    public async Task<T?> RunAsync<T>(Func<TechnicsDevice, Task<T>> action)
    {
        if (_device is not { } device)
            return default;
        try
        {
            return await action(device);
        }
        catch (Exception ex) when (ex is RaceException or TimeoutException or IOException or InvalidOperationException or ArgumentException)
        {
            if (device.Client.IsClosed)
                await DisconnectAsync("イヤホンとの接続が切れました。再接続します…");
            else
                StatusMessage = $"エラー: {ex.Message}";
            return default;
        }
    }

    private void OnOutsideChangedByDevice(OutsideControl value)
    {
        // 強さを変えている途中に届く同じモードの通知は、途中で送った値が返ってきたもの。使うと表示が戻る
        if (IsAdjustingLevel && value.Mode == Outside?.Mode)
            return;
        _outsideVersion++;
        Outside = value;
        // イヤホンのタッチ操作でも左右がずれていないか確かめる
        _wake.Release();
        DeviceStateChanged?.Invoke();
    }

    private async Task DisconnectAsync(string message)
    {
        var device = _device;
        _device = null;
        if (device is not null)
            await device.DisposeAsync();
        if (State == ConnectionState.Connected)
            State = ConnectionState.Searching;
        StatusMessage = message;
        LeftRightMatch = null;
        OnPropertyChanged(nameof(IsConnected));
        DeviceStateChanged?.Invoke();
    }
}
