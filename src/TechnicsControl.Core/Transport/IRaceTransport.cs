namespace TechnicsControl.Core.Transport;

/// <summary>RACE のバイト列を送受信する経路（実機では RFCOMM）。</summary>
public interface IRaceTransport : IAsyncDisposable
{
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>届いたバイトを読む。接続が切れたら 0 を返す。</summary>
    Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}
