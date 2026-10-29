using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace TechnicsControl.Core.Discovery;

public sealed record PairedDevice(string Name, ulong BluetoothAddress, bool IsConnected)
{
    public string AddressText => string.Join(':', BitConverter.GetBytes(BluetoothAddress).Take(6).Reverse().Select(b => b.ToString("X2")));
}

/// <summary>Windows にペアリング済みの Technics のイヤホンを探す。</summary>
public static class DeviceFinder
{
    public static async Task<IReadOnlyList<PairedDevice>> FindTechnicsAsync(CancellationToken cancellationToken = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
        var result = new List<PairedDevice>();
        foreach (var info in infos)
        {
            if (!info.Name.StartsWith("Technics", StringComparison.OrdinalIgnoreCase))
                continue;
            using var device = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellationToken);
            if (device is null)
                continue;
            result.Add(new PairedDevice(device.Name, device.BluetoothAddress,
                device.ConnectionStatus == BluetoothConnectionStatus.Connected));
        }
        // 接続中のものを先頭に
        return result.OrderByDescending(d => d.IsConnected).ToList();
    }
}
