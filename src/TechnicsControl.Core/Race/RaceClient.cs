using TechnicsControl.Core.Transport;

namespace TechnicsControl.Core.Race;

/// <summary>応答の受け取り方。</summary>
public enum RaceReply
{
    /// <summary>応答（5B）だけで値が返る。</summary>
    Response,

    /// <summary>応答（5B）で受付、そのあと通知（5D）で値が返る。</summary>
    Indication,
}

public sealed class RaceException(string message) : Exception(message);

/// <summary>
/// RACE の要求と応答を 1 つずつ順番にやり取りする。
/// 待っていない通知（イヤホン側のタッチ操作による変化など）は <see cref="IndicationReceived"/> で知らせる。
/// </summary>
public sealed class RaceClient : IAsyncDisposable
{
    private readonly IRaceTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _waitersLock = new();
    private readonly List<Waiter> _waiters = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoop;

    public RaceClient(IRaceTransport transport)
    {
        _transport = transport;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    public bool IsClosed { get; private set; }

    public event Action<RacePacket>? IndicationReceived;

    /// <summary>接続が切れたとき。引数は原因（正常終了なら null）。</summary>
    public event Action<Exception?>? Closed;

    /// <summary>送受信したパケットの記録（デバッグ用）。true が送信。</summary>
    public event Action<bool, RacePacket>? PacketLogged;

    public Task<RacePacket> SendAsync(RacePacket request, RaceReply reply = RaceReply.Response, CancellationToken cancellationToken = default) =>
        ExchangeAsync(request, request.Id, reply, inner: null, checkStatus: true, cancellationToken);

    /// <summary>応答の先頭がステータスではないコマンド（0x0D00 など）用。</summary>
    public Task<RacePacket> SendUncheckedAsync(RacePacket request, CancellationToken cancellationToken = default) =>
        ExchangeAsync(request, request.Id, RaceReply.Response, inner: null, checkStatus: false, cancellationToken);

    /// <summary>
    /// 相方のイヤホンへ中継して送る（0x0D01）。返すのは相方側の応答パケット。
    /// </summary>
    public Task<RacePacket> SendToPartnerAsync(byte dstType, byte dstId, RacePacket inner, RaceReply reply = RaceReply.Response, CancellationToken cancellationToken = default)
    {
        var raw = inner.ToBytes();
        var payload = new byte[2 + raw.Length];
        payload[0] = dstType;
        payload[1] = dstId;
        raw.CopyTo(payload, 2);
        return ExchangeAsync(RacePacket.Request(RelayIds.PassToDst, payload), RelayIds.PassToDst, reply, inner, checkStatus: true, cancellationToken);
    }

    private async Task<RacePacket> ExchangeAsync(RacePacket request, ushort id, RaceReply reply, RacePacket? inner, bool checkStatus, CancellationToken cancellationToken)
    {
        RaceWhitelist.Ensure(request);
        if (IsClosed)
            throw new RaceException("イヤホンとの接続が切れています。");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var waiters = new List<Waiter>();
        try
        {
            // 応答と通知は同じ受信単位で続けて届くことがあるため、送信前にすべて登録しておく
            var accepted = AddWaiter(waiters, p => p.Type == RaceType.Response && p.Id == id);
            Waiter? result = null;
            Waiter? innerAccepted = null;
            if (inner is null)
            {
                if (reply == RaceReply.Indication)
                    result = AddWaiter(waiters, p => p.Type == RaceType.Indication && p.Id == id);
            }
            else
            {
                innerAccepted = AddWaiter(waiters, p => UnwrapRelay(p) is { Type: RaceType.Response } q && q.Id == inner.Id);
                if (reply == RaceReply.Indication)
                    result = AddWaiter(waiters, p => UnwrapRelay(p) is { Type: RaceType.Indication } q && q.Id == inner.Id);
            }

            PacketLogged?.Invoke(true, request);
            await _transport.SendAsync(request.ToBytes(), cancellationToken).ConfigureAwait(false);

            var first = await WaitAsync(accepted, cancellationToken).ConfigureAwait(false);
            if (checkStatus && first.Status != 0)
                throw new RaceException($"0x{id:X4} がエラーを返しました（status=0x{first.Status:X2}）。");
            if (innerAccepted is null)
                return result is null ? first : await WaitAsync(result, cancellationToken).ConfigureAwait(false);

            var innerFirst = UnwrapRelay(await WaitAsync(innerAccepted, cancellationToken).ConfigureAwait(false))!;
            if (result is null || innerFirst.Status != 0)
                return innerFirst;
            return UnwrapRelay(await WaitAsync(result, cancellationToken).ConfigureAwait(false))!;
        }
        finally
        {
            lock (_waitersLock)
                _waiters.RemoveAll(waiters.Contains);
            _gate.Release();
        }
    }

    /// <summary>中継の通知（0x0D01 の 5D）から、相方側のパケットを取り出す。</summary>
    public static RacePacket? UnwrapRelay(RacePacket packet)
    {
        if (packet.Type != RaceType.Indication || packet.Id != RelayIds.PassToDst || packet.Payload.Length < 2 + RacePacket.HeaderSize)
            return null;
        try
        {
            return RacePacket.Parse(packet.Payload.AsSpan(2));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private Waiter AddWaiter(List<Waiter> owned, Func<RacePacket, bool> match)
    {
        var waiter = new Waiter(match);
        owned.Add(waiter);
        lock (_waitersLock)
        {
            // 受信が終わったあとに登録した待ちは誰も完了させないので、タイムアウトを待たずにすぐ失敗させる
            if (IsClosed)
                waiter.Completion.TrySetException(new RaceException("イヤホンとの接続が切れています。"));
            else
                _waiters.Add(waiter);
        }
        return waiter;
    }

    private async Task<RacePacket> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            return await waiter.Completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("イヤホンから応答がありません。");
        }
    }

    private async Task ReadLoopAsync()
    {
        var framer = new RaceFramer();
        var buffer = new byte[2048];
        Exception? error = null;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _transport.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(false);
                if (read == 0)
                    break;
                foreach (var packet in framer.Push(buffer.AsSpan(0, read)))
                    Dispatch(packet);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = ex;
        }

        lock (_waitersLock)
        {
            IsClosed = true;
            foreach (var waiter in _waiters)
                waiter.Completion.TrySetException(new RaceException("イヤホンとの接続が切れました。"));
            _waiters.Clear();
        }
        Closed?.Invoke(error);
    }

    private void Dispatch(RacePacket packet)
    {
        PacketLogged?.Invoke(false, packet);
        Waiter? matched;
        lock (_waitersLock)
        {
            matched = _waiters.FirstOrDefault(w => !w.Completion.Task.IsCompleted && w.Match(packet));
            if (matched is not null)
                _waiters.Remove(matched);
        }
        if (matched is not null)
            matched.Completion.TrySetResult(packet);
        else if (packet.Type == RaceType.Indication)
            IndicationReceived?.Invoke(packet);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _transport.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        _cts.Dispose();
    }

    private sealed class Waiter(Func<RacePacket, bool> match)
    {
        public Func<RacePacket, bool> Match { get; } = match;
        public TaskCompletionSource<RacePacket> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
