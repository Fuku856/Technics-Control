using System.IO.Compression;
using AssetExtractor;
using SkiaSharp;

// 公式アプリの APK から、Windows アプリで使う画像とアイコンを取り出す。
//   dotnet run --project tools/AssetExtractor -- [APK のパス] [--preview 一覧画像のパス]
// 出力先: src/TechnicsControl.App/Assets/Official/
// 個人の非公開リポジトリで使う前提（画像は Panasonic、LDAC ロゴは Sony の著作物）。

var root = FindRepositoryRoot();
var apkPath = args.FirstOrDefault(a => !a.StartsWith("--") && a.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
    ?? Directory.GetFiles(root, "*Technics*.apk").FirstOrDefault()
    ?? throw new FileNotFoundException("Technics Audio Connect の APK をリポジトリ直下に置くか、パスを指定してください。");
var previewPath = args.SkipWhile(a => a != "--preview").Skip(1).FirstOrDefault();
var outputRoot = Path.Combine(root, "src", "TechnicsControl.App", "Assets", "Official");

using var apk = ZipFile.OpenRead(apkPath);
var table = ResourceTable.Parse(ReadEntry(apk, "resources.arsc"));
Console.WriteLine($"APK: {Path.GetFileName(apkPath)}（{table.Entries.Count} 件の画像リソース）");

var preview = new List<(string Label, SKBitmap Bitmap)>();

// イヤホンとケースの製品画像（色はそのまま）
foreach (var color in new[] { "blk", "slv", "nvy" })
foreach (var part in new[] { "l", "r", "c" })
    Save(Decode($"az80_{color}_{part}"), Path.Combine("Device", $"az80_{color}_{part}.png"));

// 単色のアイコン: ダーク用（白）とライト用（濃い灰色）に塗り替える
string[] monoIcons =
[
    "ic_noise_cancellation_regular_96px", "ic_ambiet_sound_regular_96px", "ic_normal_regular_96px",
    "ic_tab_bar_home_24px_sel", "ic_tab_bar_noise_24px_sel", "ic_tab_bar_sound_24px_sel", "ic_tab_bar_settings_24px_sel",
    "icon_codec_ldac", "icon_codec_aac", "icon_codec_sbc", "icon_codec_lc3", "icon_codec_usb_audio", "icon_codec_analog", "icon_codec_no_connection",
    "ic_single_tap", "ic_double_tap", "ic_triple_tap", "ic_long_press",
    "ic_bass_regular_42px", "ic_superbass_regular_42px", "ic_dynamic_42px", "ic_vocals_regular_42px", "ic_treble_42px",
    "ic_direct_regular_42px", "ic_custom_regular_42px", "ic_normal_regular_42px",
];
foreach (var name in monoIcons)
{
    using var original = Decode(name);
    // LDAC ロゴは白い塗りの上に黒い文字と枠がある画像なので、暗い部分だけを残してから塗り替える
    var inkOnly = name == "icon_codec_ldac";
    Save(Tint(original, SKColors.White, inkOnly), Path.Combine("Icons", "Dark", $"{name}.png"));
    Save(Tint(original, new SKColor(0x1F, 0x1F, 0x1F), inkOnly), Path.Combine("Icons", "Light", $"{name}.png"));
}

// アプリのアイコン（exe とウィンドウ）
using (var launcher = Decode("ic_launcher_foreground"))
    WriteIco(launcher, Path.Combine(outputRoot, "AppIcon.ico"), [16, 20, 24, 32, 48, 64, 128, 256], padding: 0);

// トレイアイコン: モードのアイコンを、暗いタスクバー用（白）と明るいタスクバー用（黒）にする
foreach (var (mode, name) in new[] { ("nc", "ic_noise_cancellation_regular_96px"), ("ambient", "ic_ambiet_sound_regular_96px"), ("off", "ic_normal_regular_96px") })
{
    using var original = Decode(name);
    using var white = Tint(original, SKColors.White);
    using var black = Tint(original, SKColors.Black);
    WriteIco(white, Path.Combine(outputRoot, "Tray", $"{mode}_dark.ico"), [16, 20, 24, 32, 40, 48], padding: 0.06f);
    WriteIco(black, Path.Combine(outputRoot, "Tray", $"{mode}_light.ico"), [16, 20, 24, 32, 40, 48], padding: 0.06f);
}

if (previewPath is not null)
    WritePreview(previewPath);
Console.WriteLine($"出力先: {outputRoot}");
return;

SKBitmap Decode(string name)
{
    var entry = table.Best(name) ?? throw new InvalidOperationException($"APK に {name} がありません。");
    var bitmap = SKBitmap.Decode(ReadEntry(apk, entry.Path))
        ?? throw new InvalidOperationException($"{name}（{entry.Path}）を読めませんでした。");
    preview.Add((name, bitmap.Copy()));
    return bitmap;
}

void Save(SKBitmap bitmap, string relativePath)
{
    var path = Path.Combine(outputRoot, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var image = SKImage.FromBitmap(bitmap);
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, png.ToArray());
    if (relativePath.StartsWith("Icons") || relativePath.StartsWith("Device"))
        preview.Add((relativePath, bitmap.Copy()));
    bitmap.Dispose();
}

// 透明度だけを残して色を置き換える。inkOnly なら明るい部分を透明にする（白い塗りを抜く）。
// 公式の「通常」アイコンは半透明で描かれているので、最も濃い部分が不透明になるよう引き伸ばす
static SKBitmap Tint(SKBitmap source, SKColor color, bool inkOnly = false)
{
    var alphas = new double[source.Width * source.Height];
    for (var y = 0; y < source.Height; y++)
    for (var x = 0; x < source.Width; x++)
    {
        var pixel = source.GetPixel(x, y);
        double alpha = pixel.Alpha;
        if (inkOnly)
            alpha *= 1 - (0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue) / 255.0;
        alphas[y * source.Width + x] = alpha;
    }
    var scale = 255.0 / Math.Max(1, alphas.Max());

    var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    for (var y = 0; y < source.Height; y++)
    for (var x = 0; x < source.Width; x++)
        result.SetPixel(x, y, color.WithAlpha((byte)Math.Min(255, Math.Round(alphas[y * source.Width + x] * scale))));
    return result;
}

// PNG を格納した .ico（Windows Vista 以降で使える形式）を書く
static void WriteIco(SKBitmap source, string path, int[] sizes, float padding)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var images = sizes.Select(size => RenderSquare(source, size, padding)).ToList();
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    for (var i = 0; i < images.Count; i++)
    {
        var size = sizes[i];
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(images[i].Length);
        writer.Write(offset);
        offset += images[i].Length;
    }
    foreach (var image in images)
        writer.Write(image);
}

// 縦横比を保って正方形の中央に描き、PNG にする
static byte[] RenderSquare(SKBitmap source, int size, float padding)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    var inner = size * (1 - padding * 2);
    var scale = Math.Min(inner / source.Width, inner / source.Height);
    var w = source.Width * scale;
    var h = source.Height * scale;
    using var image = SKImage.FromBitmap(source);
    surface.Canvas.DrawImage(image, SKRect.Create((size - w) / 2, (size - h) / 2, w, h),
        new SKSamplingOptions(SKCubicResampler.Mitchell));
    using var snapshot = surface.Snapshot();
    using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
    return png.ToArray();
}

// 取り出した画像を、暗い背景と明るい背景に並べた一覧画像（目視確認用）
void WritePreview(string path)
{
    const int cell = 96;
    const int columns = 12;
    var rows = (preview.Count + columns - 1) / columns;
    using var surface = SKSurface.Create(new SKImageInfo(columns * cell * 2, rows * cell));
    var canvas = surface.Canvas;
    using var dark = new SKPaint { Color = new SKColor(0x20, 0x20, 0x20) };
    using var light = new SKPaint { Color = new SKColor(0xF3, 0xF3, 0xF3) };
    canvas.DrawRect(0, 0, columns * cell, rows * cell, dark);
    canvas.DrawRect(columns * cell, 0, columns * cell, rows * cell, light);
    for (var i = 0; i < preview.Count; i++)
    {
        var bitmap = preview[i].Bitmap;
        var scale = Math.Min((cell - 8f) / bitmap.Width, (cell - 8f) / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        using var image = SKImage.FromBitmap(bitmap);
        foreach (var half in new[] { 0, columns * cell })
        {
            var x = half + i % columns * cell + (cell - w) / 2;
            var y = i / columns * cell + (cell - h) / 2;
            canvas.DrawImage(image, SKRect.Create(x, y, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
    }
    using var snapshot = surface.Snapshot();
    using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, png.ToArray());
    Console.WriteLine($"一覧画像: {path}");
}

static byte[] ReadEntry(ZipArchive archive, string name)
{
    var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"APK に {name} がありません。");
    using var stream = entry.Open();
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return memory.ToArray();
}

static string FindRepositoryRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TechnicsControl.slnx")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
