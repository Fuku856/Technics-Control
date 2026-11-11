using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;
using TechnicsControl.Core.Technics;

namespace TechnicsControl.App.Views.Pages;

/// <summary>タッチ操作の有効／無効と、左右のキー割り当て（公式アプリの KeyCustomizeActivity に当たる）。</summary>
public sealed partial class KeyCustomizePage : Page
{
    private enum Scene
    {
        Music,
        IncomingCall,
        Talking,
    }

    // 公式アプリで変更できる枠だけを出す（着信時は 1 回タップと長押し、通話中は 1 回タップ以外）
    private static readonly (Scene Scene, KeyGesture Gesture)[] Slots =
    [
        (Scene.Music, KeyGesture.SingleTap), (Scene.Music, KeyGesture.DoubleTap), (Scene.Music, KeyGesture.TripleTap), (Scene.Music, KeyGesture.LongPress),
        (Scene.IncomingCall, KeyGesture.SingleTap), (Scene.IncomingCall, KeyGesture.LongPress),
        (Scene.Talking, KeyGesture.DoubleTap), (Scene.Talking, KeyGesture.TripleTap), (Scene.Talking, KeyGesture.LongPress),
    ];

    private readonly Dictionary<KeySide, Keymap> _maps = [];
    private readonly Dictionary<(KeySide, Scene, KeyGesture), ComboBox> _boxes = [];
    private bool _loading;

    private DeviceService Device => App.Device;

    public KeyCustomizePage()
    {
        InitializeComponent();
        BuildGrid();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadAsync();

    public static string FunctionName(KeyFunction function) => function switch
    {
        KeyFunction.PlayPause => "再生／一時停止",
        KeyFunction.PreviousTrack => "曲戻し",
        KeyFunction.NextTrack => "曲送り",
        KeyFunction.VolumeDown => "音量を下げる",
        KeyFunction.VolumeUp => "音量を上げる",
        KeyFunction.NoiseCanceling => "ノイズキャンセリング",
        KeyFunction.Ambient => "外音取り込み",
        KeyFunction.OutsideOff => "外音コントロール OFF",
        KeyFunction.OutsideSwitching => "外音コントロールの切り替え",
        KeyFunction.PowerOff => "電源 OFF",
        KeyFunction.VoiceAssistant => "音声アシスタント",
        KeyFunction.AnswerCall => "電話に出る",
        KeyFunction.RejectCall => "着信を拒否する",
        KeyFunction.EndCall => "通話を終える",
        KeyFunction.Mute => "マイクのミュート",
        KeyFunction.NoiseReduction => "通話相手からのノイズを抑える",
        _ => "機能なし",
    };

    private static string GestureName(KeyGesture gesture) => gesture switch
    {
        KeyGesture.SingleTap => "1 回タップ",
        KeyGesture.DoubleTap => "2 回タップ",
        KeyGesture.TripleTap => "3 回タップ",
        _ => "長押し",
    };

    private static KeyFunction[] Choices(Scene scene, KeyGesture gesture) => scene switch
    {
        Scene.Music => gesture == KeyGesture.LongPress ? Keymap.MusicLongPressChoices : Keymap.MusicChoices,
        Scene.IncomingCall => Keymap.IncomingCallChoices,
        _ => Keymap.TalkingChoices,
    };

    /// <summary>表の 1 行分。場面の見出しの行は Left / Right が null。</summary>
    private sealed record KeymapRow(TextBlock Label, ComboBox? Left = null, ComboBox? Right = null);

    private readonly List<KeymapRow> _rows = [];
    private bool? _compact;

    private void BuildGrid()
    {
        Scene? current = null;
        foreach (var (scene, gesture) in Slots)
        {
            if (scene != current)
            {
                current = scene;
                _rows.Add(new KeymapRow(new TextBlock
                {
                    Text = scene switch { Scene.Music => "音楽再生中", Scene.IncomingCall => "着信時", _ => "通話中" },
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    Margin = new Thickness(0, 12, 0, 0),
                }));
            }
            var label = new TextBlock { Text = GestureName(gesture), VerticalAlignment = VerticalAlignment.Center };
            _rows.Add(new KeymapRow(label, CreateBox(KeySide.Left, scene, gesture), CreateBox(KeySide.Right, scene, gesture)));
        }
        // 割り当ての名前は長いので、左右を横に並べきれない幅では縦に並べる
        ResponsiveHelper.Attach(Scroller, RootPanel, width => Layout(width < 640));
        Layout(false);
    }

    private void Layout(bool compact)
    {
        if (_compact == compact)
            return;
        _compact = compact;
        KeymapGrid.Children.Clear();
        KeymapGrid.RowDefinitions.Clear();
        KeymapGrid.ColumnDefinitions.Clear();
        KeymapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        KeymapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (!compact)
        {
            KeymapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(new TextBlock(), Header("左（L）"), Header("右（R）"));
        }

        foreach (var row in _rows)
        {
            if (row.Left is null || row.Right is null)
                AddRow(row.Label);
            else if (compact)
            {
                // 狭いとき: 「1 回タップ」の下に L と R を 1 行ずつ
                AddRow(row.Label);
                AddRow(SideLabel("L"), row.Left);
                AddRow(SideLabel("R"), row.Right);
            }
            else
                AddRow(row.Label, row.Left, row.Right);
        }
    }

    private static TextBlock SideLabel(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0),
    };

    // 左右の見出しは、下に並ぶ選択ボックスの中央に置く
    private static TextBlock Header(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private ComboBox CreateBox(KeySide side, Scene scene, KeyGesture gesture)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var function in Choices(scene, gesture))
            box.Items.Add(new ComboBoxItem { Content = FunctionName(function), Tag = function });
        box.SelectionChanged += async (_, _) => await OnSlotChangedAsync(side, scene, gesture, box);
        _boxes[(side, scene, gesture)] = box;
        return box;
    }

    private void AddRow(params FrameworkElement[] cells)
    {
        var row = KeymapGrid.RowDefinitions.Count;
        KeymapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < cells.Length; column++)
        {
            Grid.SetRow(cells[column], row);
            Grid.SetColumn(cells[column], column);
            // 1 つだけの行（見出し）は表の幅いっぱいに
            Grid.SetColumnSpan(cells[column], cells.Length == 1 ? KeymapGrid.ColumnDefinitions.Count : 1);
            KeymapGrid.Children.Add(cells[column]);
        }
    }

    private async Task LoadAsync()
    {
        KeymapHost.IsEnabled = TouchSwitch.IsEnabled = Device.IsConnected;
        var loaded = await Device.RunAsync(async d =>
        {
            var touch = await d.GetTouchEnabledAsync();
            var left = await d.GetKeymapAsync(KeySide.Left);
            var right = await d.GetKeymapAsync(KeySide.Right);
            return (touch, left, right);
        });
        if (loaded.left is null || loaded.right is null)
            return;
        var (touch, left, right) = loaded;

        _loading = true;
        try
        {
            TouchSwitch.IsOn = touch;
            Show(left);
            Show(right);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Show(Keymap map)
    {
        _maps[map.Side] = map;
        foreach (var (scene, gesture) in Slots)
        {
            var function = Get(map, scene)[(int)gesture];
            var box = _boxes[(map.Side, scene, gesture)];
            var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (KeyFunction)i.Tag == function);
            if (item is null)
            {
                // AZ80 の選択肢にない値（他機種向けの機能など）は、そのまま表示だけする
                item = new ComboBoxItem { Content = $"{FunctionName(function)}（0x{(byte)function:X2}）", Tag = function };
                box.Items.Add(item);
            }
            box.SelectedItem = item;
        }
    }

    private static KeyFunction[] Get(Keymap map, Scene scene) => scene switch
    {
        Scene.Music => map.Music,
        Scene.IncomingCall => map.IncomingCall,
        _ => map.Talking,
    };

    private async Task OnSlotChangedAsync(KeySide side, Scene scene, KeyGesture gesture, ComboBox box)
    {
        if (_loading || box.SelectedItem is not ComboBoxItem { Tag: KeyFunction function } || !_maps.TryGetValue(side, out var map))
            return;
        if (Get(map, scene)[(int)gesture] == function)
            return;

        var music = (KeyFunction[])map.Music.Clone();
        var incoming = (KeyFunction[])map.IncomingCall.Clone();
        var talking = (KeyFunction[])map.Talking.Clone();
        (scene switch { Scene.Music => music, Scene.IncomingCall => incoming, _ => talking })[(int)gesture] = function;
        var request = map with { Music = music, IncomingCall = incoming, Talking = talking };

        ErrorBar.IsOpen = false;
        var applied = await Device.RunAsync(d => d.SetKeymapAsync(request));
        _loading = true;
        try
        {
            if (applied is null)
            {
                ErrorBar.Title = "キーの割り当てを変更できませんでした";
                ErrorBar.Message = Device.StatusMessage;
                ErrorBar.IsOpen = true;
                Show(map);
                return;
            }
            Show(applied);
            if (!applied.Equals(request))
            {
                ErrorBar.Title = "イヤホンが別の割り当てにしました";
                ErrorBar.Message = "この組み合わせはイヤホン側で受け付けられませんでした。今の割り当てを表示しています。";
                ErrorBar.IsOpen = true;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnTouchToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var on = TouchSwitch.IsOn;
        var ok = await Device.RunAsync(async d =>
        {
            await d.SetTouchEnabledAsync(on);
            return true;
        });
        if (!ok)
        {
            ErrorBar.Title = "タッチ操作を変更できませんでした";
            ErrorBar.Message = Device.StatusMessage;
            ErrorBar.IsOpen = true;
            await LoadAsync();
        }
    }
}
