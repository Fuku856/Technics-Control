using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TechnicsControl.App.Services;
using TechnicsControl.Core.Technics;

namespace TechnicsControl.App.Views.Pages;

/// <summary>製品情報（表示のみ）。公式アプリの「ヘルプ &gt; 製品情報」に当たる。</summary>
public sealed partial class ProductInfoPage : Page
{
    private DeviceService Device => App.Device;

    public ProductInfoPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadAsync();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        LoadingBar.Visibility = Visibility.Visible;
        ModelText.Text = Device.DeviceName;
        try
        {
            var info = await Device.RunAsync(async d =>
            {
                // 左右は主側・相方で読み、主側が右かどうかで並べ替える
                var right = Device.AgentIsRight;
                var roles = d.HasPartner ? new[] { EarbudRole.Agent, EarbudRole.Partner } : [EarbudRole.Agent];
                var rows = new List<(string Side, string Firmware, UsageTime Usage, ChargeError Charge)>();
                foreach (var role in roles)
                {
                    var side = (role == EarbudRole.Agent) == right ? "R" : "L";
                    rows.Add((side, await d.GetFirmwareVersionAsync(role), await d.GetUsageTimeAsync(role), await d.GetChargeErrorAsync(role)));
                }
                return rows.OrderBy(r => r.Side).ToList();
            });
            if (info is null)
            {
                FirmwareText.Text = UsageText.Text = ChargeErrorText.Text = "読み込めませんでした";
                return;
            }
            FirmwareText.Text = string.Join("\n", info.Select(r => $"{r.Side}  {r.Firmware}"));
            UsageText.Text = string.Join("\n", info.Select(r => $"{r.Side}  {r.Usage.TotalMinutes / 60:N0} 時間 {r.Usage.TotalMinutes % 60} 分"));
            ChargeErrorText.Text = string.Join("\n", info.Select(r => $"{r.Side}  {r.Charge.Consecutive} 回 ／ {r.Charge.Total} 回"));
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }
}
