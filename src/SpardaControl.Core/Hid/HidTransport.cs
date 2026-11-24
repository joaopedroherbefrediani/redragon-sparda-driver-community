using HidSharp;
using SpardaControl.Core.Protocol;

namespace SpardaControl.Core.Hid;

public sealed class HidTransport : IDisposable
{
    public const int VendorId = 0x3554;

    readonly HidDevice _device;
    readonly HidStream _stream;
    readonly Thread _reader;
    readonly SemaphoreSlim _lock = new(1, 1);
    readonly object _pendingSync = new();
    volatile bool _disposed;
    Pending? _pending;

    public int ProductId => _device.ProductID;
    public string ProductName { get; }

    public event Action<byte[]>? Unsolicited;
    public event Action? Disconnected;

    sealed class Pending
    {
        public required byte[] Request;
        public required int MatchLength;
        public readonly TaskCompletionSource<byte[]> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    HidTransport(HidDevice device, HidStream stream)
    {
        _device = device;
        _stream = stream;
        _stream.ReadTimeout = Timeout.Infinite;
        ProductName = SafeName(device);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "HID reader" };
        _reader.Start();
    }

    static string SafeName(HidDevice d)
    {
        try { return d.GetProductName(); } catch { return "Mouse"; }
    }

    public static IEnumerable<HidDevice> FindCandidates(IReadOnlyCollection<int> productIds)
    {
        foreach (var d in DeviceList.Local.GetHidDevices(VendorId))
        {
            if (productIds.Count > 0 && !productIds.Contains(d.ProductID)) continue;
            bool ok;
            try
            {
                ok = d.GetMaxOutputReportLength() == Packet.Size + 1 && d.GetMaxInputReportLength() == Packet.Size + 1;
                if (ok)
                {
                    var rd = d.GetReportDescriptor();
                    ok = rd.OutputReports.Any(r => r.ReportID == Packet.ReportId)
                         && rd.InputReports.Any(r => r.ReportID == Packet.ReportId);
                }
            }
            catch { ok = false; }
            if (ok) yield return d;
        }
    }

    public static HidTransport? TryOpen(IReadOnlyCollection<int> productIds)
    {
        foreach (var d in FindCandidates(productIds))
        {
            try
            {
                var cfg = new OpenConfiguration();
                cfg.SetOption(OpenOption.Exclusive, false);
                cfg.SetOption(OpenOption.Interruptible, true);
                if (d.TryOpen(cfg, out var s)) return new HidTransport(d, s);
            }
            catch { }
        }
        return null;
    }

    void ReadLoop()
    {
        var buf = new byte[Packet.Size + 1];
        while (!_disposed)
        {
            int n;
            try
            {
                n = _stream.Read(buf, 0, buf.Length);
            }
            catch (TimeoutException) { continue; }
            catch
            {
                if (!_disposed) Disconnected?.Invoke();
                return;
            }
            if (n < Packet.Size + 1 || buf[0] != Packet.ReportId) continue;
            var payload = buf.AsSpan(1, Packet.Size).ToArray();
            Dispatch(payload);
        }
    }

    void Dispatch(byte[] payload)
    {
        Pending? p;
        lock (_pendingSync) p = _pending;
        if (p != null && Matches(p, payload))
        {
            lock (_pendingSync) if (_pending == p) _pending = null;
            p.Tcs.TrySetResult(payload);
            return;
        }
        try { Unsolicited?.Invoke(payload); } catch { }
    }

    static bool Matches(Pending p, byte[] payload)
    {
        if (payload[0] != p.Request[0]) return false;
        if (payload[1] == 1) return true;
        for (int i = 1; i < p.MatchLength; i++)
            if (payload[i] != p.Request[i]) return false;
        return true;
    }

    public async Task<byte[]?> ExchangeAsync(byte[] packet, int timeoutMs = 250, int retries = 4, CancellationToken ct = default)
    {
        if (_disposed) return null;
        int matchLength = packet[0] == (byte)Command.ReadFlashData ? 5 : 3;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var report = new byte[Packet.Size + 1];
            report[0] = Packet.ReportId;
            packet.CopyTo(report, 1);
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                var pending = new Pending { Request = packet, MatchLength = matchLength };
                lock (_pendingSync) _pending = pending;
                try
                {
                    _stream.Write(report, 0, report.Length);
                }
                catch
                {
                    lock (_pendingSync) _pending = null;
                    Disconnected?.Invoke();
                    return null;
                }
                var done = await Task.WhenAny(pending.Tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                if (done == pending.Tcs.Task) return pending.Tcs.Task.Result;
                lock (_pendingSync) if (_pending == pending) _pending = null;
            }
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SendAsync(byte[] packet)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var report = new byte[Packet.Size + 1];
            report[0] = Packet.ReportId;
            packet.CopyTo(report, 1);
            _stream.Write(report, 0, report.Length);
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _stream.Dispose(); } catch { }
    }
}
