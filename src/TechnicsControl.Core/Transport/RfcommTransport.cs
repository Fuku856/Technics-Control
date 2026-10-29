using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace TechnicsControl.Core.Transport;

public sealed class TransportException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>別の機器（スマホの公式アプリなど）が RACE チャネルを使っている可能性が高い。</summary>
    public bool ChannelBusy { get; init; }
}

/// <summary>Airoha の RACE 用 RFCOMM（SPP）サービスに接続する。</summary>
public sealed class RfcommTransport : IRaceTransport
{
    public static readonly Guid AirohaSppUuid = new("00000000-0000-0000-0099-aabbccddeeff");

    // WSAEHOSTDOWN（10064）。RACE チャネルが他の機器に使われていると返ってくる
    private const int HResultHostDown = unchecked((int)0x80072750);

    private readonly StreamSocket _socket;
    private readonly Stream _input;
    private readonly Stream _output;

    private RfcommTransport(StreamSocket socket)
    {
        _socket = socket;
        _input = socket.InputStream.AsStreamForRead(0);
        _output = socket.OutputStream.AsStreamForWrite(0);
    }

    public static async Task<RfcommTransport> ConnectAsync(ulong bluetoothAddress, CancellationToken cancellationToken = default)
    {
        var device = await BluetoothDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(cancellationToken)
            ?? throw new TransportException("イヤホンが見つかりません。Windows の Bluetooth 設定でペアリングされているか確認してください。");

        var services = await device.GetRfcommServicesForIdAsync(
            RfcommServiceId.FromUuid(AirohaSppUuid), BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        if (services.Services.Count == 0)
        {
            // 電源が切れているなどで取得できないときは、キャッシュから探し直す
            services = await device.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromUuid(AirohaSppUuid), BluetoothCacheMode.Cached).AsTask(cancellationToken);
        }
        if (services.Services.Count == 0)
            throw new TransportException("イヤホンの設定用サービスが見つかりません。イヤホンをケースから出して接続してください。");

        var service = services.Services[0];
        var socket = new StreamSocket();
        try
        {
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(cancellationToken);
        }
        catch (Exception ex) when (ex.HResult == HResultHostDown)
        {
            socket.Dispose();
            throw new TransportException("イヤホンの設定チャネルを他の機器が使用中です。スマホの Technics Audio Connect を閉じてから再試行してください。", ex)
            {
                ChannelBusy = true,
            };
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            socket.Dispose();
            throw new TransportException($"イヤホンに接続できませんでした（0x{ex.HResult:X8}）。", ex);
        }
        return new RfcommTransport(socket);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        await _output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await _input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or IOException or ObjectDisposedException)
        {
            return 0;
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
