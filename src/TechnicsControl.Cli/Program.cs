using System.Text;
using TechnicsControl.Core.Discovery;
using TechnicsControl.Core.Race;
using TechnicsControl.Core.Technics;
using TechnicsControl.Core.Transport;

// 実機での動作確認用のコマンドラインツール。
//   technics list
//   technics dump
//   technics outside <off|nc|ambient> [NCの強さ] [外音の強さ]
//   technics ambient <transparent|attention> [play|stop]
//   technics watch [秒]
//   オプション: --address B8208ECC7241  --log  --allow-relay-set

Console.OutputEncoding = Encoding.UTF8;
var options = args.Where(a => a.StartsWith("--")).ToList();
var positional = args.Where(a => !a.StartsWith("--")).ToList();
var addressOption = args.SkipWhile(a => a != "--address").Skip(1).FirstOrDefault();
if (addressOption is not null)
    positional.Remove(addressOption);
var command = positional.FirstOrDefault() ?? "dump";

try
{
    if (command == "list")
    {
        foreach (var d in await DeviceFinder.FindTechnicsAsync())
            Console.WriteLine($"{d.Name}  {d.AddressText}  {(d.IsConnected ? "接続中" : "未接続")}");
        return 0;
    }

    var address = addressOption is not null
        ? Convert.ToUInt64(addressOption.Replace(":", ""), 16)
        : (await DeviceFinder.FindTechnicsAsync()).FirstOrDefault()?.BluetoothAddress
          ?? throw new InvalidOperationException("ペアリング済みの Technics のイヤホンが見つかりません。");

    await using var device = await TechnicsDevice.ConnectAsync(address);
    device.AllowPartnerRelaySet = options.Contains("--allow-relay-set");
    if (options.Contains("--log"))
        device.Client.PacketLogged += (sent, p) => Console.WriteLine($"  {(sent ? "TX" : "RX")} {p}");
    Console.WriteLine($"接続しました（相方: {(device.HasPartner ? "あり" : "なし")}）");

    switch (command)
    {
        case "dump":
            await DumpAsync(device);
            break;
        case "outside":
            await SetOutsideAsync(device, positional.Skip(1).ToList());
            break;
        case "ambient":
            await SetAmbientAsync(device, positional.Skip(1).ToList());
            break;
        case "watch":
            await WatchAsync(device, positional.Skip(1).Select(int.Parse).DefaultIfEmpty(30).First());
            break;
        default:
            Console.Error.WriteLine($"不明なコマンド: {command}");
            return 2;
    }
    return 0;
}
catch (TransportException ex)
{
    Console.Error.WriteLine(ex.Message);
    return ex.ChannelBusy ? 3 : 1;
}
catch (Exception ex) when (ex is RaceException or TimeoutException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"エラー: {ex.Message}");
    return 1;
}

static async Task DumpAsync(TechnicsDevice device)
{
    var agentIsRight = await device.IsAgentRightAsync();
    string Side(EarbudRole role) => (role == EarbudRole.Agent) == agentIsRight ? "右" : "左";
    Console.WriteLine($"主側: {Side(EarbudRole.Agent)}");

    await Both("外音コントロール", role => device.GetOutsideControlAsync(role));
    await Both("アンビエントモード", role => device.GetAmbientSettingAsync(role));
    await Both("NC の最適化", role => device.GetNoiseCancelingAdjustAsync(role));
    await Both("外音の切り替え順", role => device.GetOutsideToggleAsync(role));

    foreach (var role in Enum.GetValues<EarbudRole>().Where(r => r == EarbudRole.Agent || device.HasPartner))
    {
        var battery = await device.GetBatteryAsync(role);
        var firmware = await device.GetFirmwareVersionAsync(role);
        var usage = await device.GetUsageTimeAsync(role);
        var charge = await device.GetChargeErrorAsync(role);
        Console.WriteLine($"[{Side(role)}] 電池 {battery.Percent}%  FW {firmware}  使用 {usage.TotalMinutes / 60} 時間（主側 {usage.AgentMinutes / 60} 時間）  充電エラー 連続 {charge.Consecutive} / 累計 {charge.Total}");
    }
    var cradle = await device.GetCradleBatteryAsync();
    Console.WriteLine($"ケースの電池: {(cradle is { } percent ? $"{percent}%" : "不明")}");
    var codec = await device.GetCodecInfoAsync();
    Console.WriteLine($"コーデック: {codec.Type} {codec.SamplingFrequency} Hz");
    Console.WriteLine($"接続中の機器: {string.Join(", ", await device.GetConnectedDevicesAsync())}");

    Console.WriteLine($"マルチポイント: {await device.GetMultiPointAsync()}  接続モード: {await device.GetA2dpOptionAsync()}");
    Console.WriteLine($"装着検知: {await device.GetWearingDetectionAsync()}");
    Console.WriteLine($"オートパワーオフ: {await device.GetAutoPowerOffAsync()}  タッチ操作: {await device.GetTouchEnabledAsync()}  LED: {await device.GetLedFlashAsync()}  通話ノイズ抑制: {await device.GetNoiseReductionAsync()}");
    foreach (var side in Enum.GetValues<KeySide>())
    {
        var map = await device.GetKeymapAsync(side);
        Console.WriteLine($"キー割り当て[{side}] 音楽={string.Join(",", map.Music)}  着信={string.Join(",", map.IncomingCall)}  通話={string.Join(",", map.Talking)}");
    }

    foreach (var (name, id) in new (string, ushort)[]
             {
                 ("接続モード/LDAC", TechnicsCommands.GetA2dpOption), ("マルチポイント", TechnicsCommands.GetMultiPoint),
                 ("装着検知", TechnicsCommands.GetWearingDetection), ("オートパワーオフ", TechnicsCommands.GetAutoPowerOff),
                 ("タッチ操作", TechnicsCommands.GetKeyEnable), ("LED", TechnicsCommands.GetLedFlash),
                 ("通話ノイズ抑制", TechnicsCommands.GetNoiseReduction),
             })
    {
        await Both(name, async role => Convert.ToHexString(await device.GetAsync(id, role)));
    }

    async Task Both<T>(string name, Func<EarbudRole, Task<T>> read)
    {
        var value = await device.ReadBothAsync(read);
        var mark = value.PartnerPresent ? (value.Matches ? "一致" : "★不一致") : "片耳";
        Console.WriteLine($"{name}: 主側={value.Agent}  相方={value.Partner}  [{mark}]");
    }
}

static async Task SetOutsideAsync(TechnicsDevice device, List<string> args)
{
    var current = await device.GetOutsideControlAsync();
    var mode = args.ElementAtOrDefault(0) switch
    {
        "off" => OutsideMode.Off,
        "nc" => OutsideMode.NoiseCanceling,
        "ambient" => OutsideMode.Ambient,
        var other => throw new ArgumentException($"モードは off / nc / ambient のどれかです（{other}）。"),
    };
    var nc = args.Count > 1 ? byte.Parse(args[1]) : current.NoiseCancelingLevel;
    var ambient = args.Count > 2 ? byte.Parse(args[2]) : current.AmbientLevel;
    Report(await device.SetOutsideControlAsync(new OutsideControl(mode, nc, ambient)));
}

static async Task SetAmbientAsync(TechnicsDevice device, List<string> args)
{
    var current = await device.GetAmbientSettingAsync();
    var type = args.ElementAtOrDefault(0) switch
    {
        "transparent" => AmbientType.Transparent,
        "attention" => AmbientType.Attention,
        var other => throw new ArgumentException($"種類は transparent / attention のどちらかです（{other}）。"),
    };
    var stopMusic = args.ElementAtOrDefault(1) switch
    {
        null => current.StopMusic,
        "stop" => true,
        "play" => false,
        var other => throw new ArgumentException($"音楽は play / stop のどちらかです（{other}）。"),
    };
    Report(await device.SetAmbientSettingAsync(new AmbientSetting(type, stopMusic)));
}

static void Report<T>(SyncResult<T> result)
{
    var v = result.Value;
    var mark = v.PartnerPresent ? (v.Matches ? "左右一致" : "★左右不一致") : "片耳のみ";
    Console.WriteLine($"主側={v.Agent}  相方={v.Partner}  [{mark}]  送信回数={result.Attempts}{(result.UsedPartnerRelay ? "（相方へ中継あり）" : "")}");
}

static async Task WatchAsync(TechnicsDevice device, int seconds)
{
    device.Client.IndicationReceived += p => Console.WriteLine($"{DateTime.Now:HH:mm:ss} 通知 {p}");
    device.OutsideControlChanged += v => Console.WriteLine($"{DateTime.Now:HH:mm:ss} 外音コントロールが変わりました: {v}");
    Console.WriteLine($"{seconds} 秒間、イヤホンからの通知を表示します。イヤホンをタッチしてモードを切り替えてみてください。");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
}
