using SpardaControl.Core.Hid;
using SpardaControl.Core.Models;
using SpardaControl.Core.Protocol;

namespace SpardaControl.Core;

public sealed class DeviceOfflineException() : Exception("O mouse está desligado ou fora de alcance.");
public sealed class DeviceIoException(string msg) : Exception(msg);

public sealed record BatteryInfo(int Level, bool Charging, int VoltageMv);

public sealed class MouseDevice : IDisposable
{
    readonly HidTransport _t;
    readonly byte[] _flash = new byte[Addr.Macro + Addr.MacroSize * MouseSettings.MaxKeys];
    readonly System.Threading.Timer _batteryTimer;
    int _refreshQueued;

    public int Cid { get; private set; }
    public int Mid { get; private set; }
    public int ConnectionType { get; private set; }
    public bool IsWired { get; private set; }
    public int MaxReportRate { get; private set; } = 1000;
    public ModelInfo Model { get; private set; } = null!;
    public string ReceiverName => _t.ProductName;
    public int ProductId => _t.ProductId;

    public MouseSettings Settings { get; } = new();
    public Dictionary<int, Macro> KeyMacros { get; } = [];
    public int Profile { get; private set; }
    public bool SupportsProfiles { get; private set; }
    public bool? LongDistance { get; private set; }
    public string MouseFirmware { get; private set; } = "--";
    public string DongleFirmware { get; private set; } = "--";
    public BatteryInfo? Battery { get; private set; }
    public bool Online { get; private set; }

    public event Action? SettingsChanged;
    public event Action? BatteryChanged;
    public event Action<bool>? OnlineChanged;
    public event Action? Disconnected;

    MouseDevice(HidTransport t)
    {
        _t = t;
        _t.Unsolicited += OnUnsolicited;
        _t.Disconnected += () => Disconnected?.Invoke();
        _batteryTimer = new System.Threading.Timer(_ => _ = PollBatteryAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static async Task<MouseDevice?> ConnectAsync(CancellationToken ct = default)
    {
        var t = HidTransport.TryOpen(ModelDatabase.AllProductIds.ToList());
        if (t == null) return null;
        var dev = new MouseDevice(t);
        try
        {
            if (!await dev.IdentifyAsync(ct)) { dev.Dispose(); return null; }
            return dev;
        }
        catch
        {
            dev.Dispose();
            throw;
        }
    }

    async Task<bool> IdentifyAsync(CancellationToken ct)
    {
        var rnd = new byte[8];
        Random.Shared.NextBytes(rnd.AsSpan(0, 4));
        var r = await _t.ExchangeAsync(Packet.Build(Command.EncryptionData, 0, rnd), ct: ct);
        if (r == null || r[1] != 0) return false;
        Cid = r[9];
        Mid = r[10];
        ConnectionType = r[11];
        (IsWired, MaxReportRate) = ConnectionType switch
        {
            2 => (true, 1000),
            3 => (true, 8000),
            1 => (false, 4000),
            4 => (false, 2000),
            5 => (false, 8000),
            _ => (false, 1000),
        };
        Model = ModelDatabase.Find(Cid, Mid) ?? throw new NotSupportedException($"Modelo não suportado (cid={Cid}, mid={Mid}).");
        return true;
    }

    async Task<byte[]> Exchange(Command cmd, byte[]? data = null, int addr = 0, int? len = null, int timeout = 250)
    {
        var p = Packet.Build(cmd, addr, data, len);
        return await _t.ExchangeAsync(p, timeout).ConfigureAwait(false)
               ?? throw new DeviceIoException($"Sem resposta do dispositivo ({cmd}).");
    }

    async Task ReadFlashAsync(int start, int end)
    {
        for (int a = start; a < end; a += Packet.MaxData)
        {
            int len = Math.Min(Packet.MaxData, end - a);
            var r = await Exchange(Command.ReadFlashData, null, a, len).ConfigureAwait(false);
            if (r[1] != 0) throw new DeviceIoException($"Erro ao ler memória em {a}.");
            int n = Math.Min(len, r[4] & 0x0F);
            Array.Copy(r, 5, _flash, a, n);
        }
    }

    async Task WriteFlashAsync(int addr, ReadOnlyMemory<byte> data)
    {
        for (int off = 0; off < data.Length; off += Packet.MaxData)
        {
            int len = Math.Min(Packet.MaxData, data.Length - off);
            var r = await Exchange(Command.WriteFlashData, data.Slice(off, len).ToArray(), addr + off, len).ConfigureAwait(false);
            if (r[1] != 0) throw new DeviceIoException($"Erro ao gravar memória em {addr + off}.");
        }
        data.Span.CopyTo(_flash.AsSpan(addr));
    }

    Task WriteByteSettingAsync(int addr, int value)
    {
        var b = (byte)value;
        return WriteFlashAsync(addr, new[] { b, (byte)(Packet.Magic - b) });
    }

    public async Task<bool> CheckOnlineAsync()
    {
        var r = await Exchange(Command.DeviceOnLine).ConfigureAwait(false);
        bool online = r[5] == 1;
        if (online != Online)
        {
            Online = online;
            OnlineChanged?.Invoke(online);
        }
        return online;
    }

    async Task EnsureOnline()
    {
        if (!await CheckOnlineAsync().ConfigureAwait(false)) throw new DeviceOfflineException();
    }

    public async Task LoadAllAsync()
    {
        await EnsureOnline().ConfigureAwait(false);
        try { await Exchange(Command.PCDriverStatus, new byte[] { 1 }).ConfigureAwait(false); } catch { }
        await ReadSettingsAsync().ConfigureAwait(false);
        await ReadProfileAsync().ConfigureAwait(false);
        try
        {
            var v = await Exchange(Command.ReadVersionID).ConfigureAwait(false);
            MouseFirmware = $"v{v[5]}.{v[6]:x2}";
        }
        catch { }
        if (!IsWired)
        {
            try
            {
                var v = await Exchange(Command.GetDongleVersion).ConfigureAwait(false);
                DongleFirmware = v[1] == 0 ? $"v{v[5]}.{v[6]:x2}" : "v1.0";
            }
            catch { }
            if (Model.HasLongDistance)
            {
                try
                {
                    var l = await Exchange(Command.GetLongRangeMode).ConfigureAwait(false);
                    LongDistance = l[1] == 0 ? l[5] == 1 : null;
                }
                catch { LongDistance = null; }
            }
        }
        await PollBatteryAsync().ConfigureAwait(false);
        _batteryTimer.Change(5000, 5000);
    }

    public async Task ReadSettingsAsync()
    {
        Array.Fill(_flash, (byte)0xFF);
        await ReadFlashAsync(0, Addr.MainBlockSize).ConfigureAwait(false);
        Settings.Parse(_flash, Model.SensorInfo, MaxReportRate);
        KeyMacros.Clear();
        for (int i = 0; i < MouseSettings.MaxKeys; i++)
        {
            var k = Settings.Keys[i];
            if (k.Type == KeyFunctionType.ShortcutKey)
            {
                int a = Addr.ShortcutKey + Addr.ShortcutSize * i;
                await ReadFlashAsync(a, a + Addr.ShortcutSize).ConfigureAwait(false);
                Settings.Keys[i] = k with { Combo = ParseCombo(_flash.AsSpan(a, Addr.ShortcutSize)) };
            }
            else if (k.Type == KeyFunctionType.Macro)
            {
                int a = Addr.Macro + Addr.MacroSize * i;
                await ReadFlashAsync(a, a + 32).ConfigureAwait(false);
                int count = Math.Min((int)_flash[a + 31], Macro.MaxEvents);
                if (_flash[a + 31] <= Macro.MaxEvents && count > 0)
                    await ReadFlashAsync(a + 32, a + 33 + 5 * count).ConfigureAwait(false);
                var m = Macro.FromFlash(_flash.AsSpan(a, Addr.MacroSize));
                if (m != null)
                {
                    m.Cycle = k.Param & 0xFF;
                    KeyMacros[i] = m;
                }
                Settings.Keys[i] = k with { MacroName = m?.Name };
            }
        }
    }

    static List<ComboKey> ParseCombo(ReadOnlySpan<byte> s)
    {
        var list = new List<ComboKey>();
        int n = s[0] / 2;
        if (n > 5) return list;
        for (int i = 0; i < n; i++)
        {
            int o = 1 + 3 * i;
            list.Add(new ComboKey(s[o] & 0x0F, s[o + 1] | (s[o + 2] << 8)));
        }
        return list;
    }

    async Task ReadProfileAsync()
    {
        try
        {
            var r = await Exchange(Command.GetCurrentConfig).ConfigureAwait(false);
            SupportsProfiles = r[1] == 0;
            Profile = SupportsProfiles ? r[5] : 0;
        }
        catch { SupportsProfiles = false; }
    }

    async Task PollBatteryAsync()
    {
        try
        {
            if (!await CheckOnlineAsync().ConfigureAwait(false)) return;
            var r = await Exchange(Command.BatteryLevel).ConfigureAwait(false);
            ApplyBattery(r);
        }
        catch { }
    }

    static readonly int[] VoltageTable = [3050, 3420, 3480, 3540, 3600, 3660, 3720, 3760, 3800, 3840, 3880, 3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110];

    static int VoltageToPercent(int mv, bool charging)
    {
        if (mv > VoltageTable[^1]) return charging ? 99 : 100;
        for (int i = 0; i < VoltageTable.Length; i++)
        {
            if (mv < VoltageTable[i])
            {
                if (i == 0) return 0;
                double step = (VoltageTable[i] - VoltageTable[i - 1]) / 5.0;
                return (int)Math.Round((mv - VoltageTable[i - 1]) / step + 5 * (i - 1));
            }
        }
        return 100;
    }

    void ApplyBattery(byte[] r)
    {
        if (r[1] != 0) return;
        int level = r[5];
        bool charging = r[6] == 1;
        int mv = (r[7] << 8) | r[8];
        if (mv > 0) level = VoltageToPercent(mv, charging);
        Battery = new BatteryInfo(Math.Clamp(level, 0, 100), charging, mv);
        BatteryChanged?.Invoke();
    }

    void OnUnsolicited(byte[] p)
    {
        if (p[1] != 0) return;
        switch ((Command)p[0])
        {
            case Command.StatusChanged:
                var f1 = (StatusFlags1)p[5];
                var f2 = (StatusFlags2)p[6];
                if (f1.HasFlag(StatusFlags1.Battery)) _ = PollBatteryAsync();
                if ((f1 & ~StatusFlags1.Battery) != 0 || f2 != 0) QueueRefresh(f1.HasFlag(StatusFlags1.Profile));
                break;
            case Command.BatteryLevel:
                ApplyBattery(p);
                break;
            case Command.DeviceOnLine:
                bool online = p[5] == 1;
                if (online != Online) { Online = online; OnlineChanged?.Invoke(online); }
                break;
        }
    }

    void QueueRefresh(bool profile)
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(150).ConfigureAwait(false);
            Interlocked.Exchange(ref _refreshQueued, 0);
            try
            {
                if (profile) await ReadProfileAsync().ConfigureAwait(false);
                await ReadSettingsAsync().ConfigureAwait(false);
                SettingsChanged?.Invoke();
            }
            catch { }
        });
    }

    public async Task SetReportRateAsync(int rate)
    {
        await EnsureOnline();
        await WriteByteSettingAsync(Addr.ReportRate, MouseSettings.ReportRateToFlash(rate));
        Settings.ReportRate = rate;
    }

    public async Task SetDpiStageCountAsync(int count)
    {
        await EnsureOnline();
        await WriteByteSettingAsync(Addr.MaxDpiStage, count);
        Settings.MaxDpiStage = count;
        if (Settings.CurrentDpi >= count) await SetCurrentDpiAsync(count - 1);
    }

    public async Task SetCurrentDpiAsync(int index)
    {
        await EnsureOnline();
        await WriteByteSettingAsync(Addr.CurrentDpi, index);
        Settings.CurrentDpi = index;
    }

    public async Task SetDpiValueAsync(int index, int dpi)
    {
        await EnsureOnline();
        dpi = DpiCodec.Snap(Model.SensorInfo, dpi, Model.MaxDpi);
        await WriteFlashAsync(Addr.DpiValue + 4 * index, DpiCodec.EncodeRecord(Model.SensorInfo, dpi, dpi));
        Settings.Dpis[index].X = Settings.Dpis[index].Y = dpi;
    }

    public async Task SetDpiColorAsync(int index, byte r, byte g, byte b)
    {
        await EnsureOnline();
        var rec = new byte[] { r, g, b, 0 };
        rec[3] = Packet.RecordChecksum(rec.AsSpan(0, 3));
        await WriteFlashAsync(Addr.DpiColor + 4 * index, rec);
        Settings.Dpis[index].R = r; Settings.Dpis[index].G = g; Settings.Dpis[index].B = b;
    }

    public async Task SetDpiEffectModeAsync(int mode)
    {
        await EnsureOnline();
        if (mode == 0)
        {
            await WriteByteSettingAsync(Addr.DpiEffectState, 0);
            Settings.DpiEffectOn = false;
            return;
        }
        await WriteByteSettingAsync(Addr.DpiEffectMode, mode);
        Settings.DpiEffectMode = mode;
        if (!Settings.DpiEffectOn)
        {
            await WriteByteSettingAsync(Addr.DpiEffectState, 1);
            Settings.DpiEffectOn = true;
        }
    }

    public async Task SetDpiEffectBrightnessAsync(int level)
    {
        await EnsureOnline();
        await WriteByteSettingAsync(Addr.DpiEffectBrightness, MouseSettings.BrightnessToFlash(level));
        Settings.DpiEffectBrightness = level;
    }

    public async Task SetDpiEffectSpeedAsync(int speed)
    {
        await EnsureOnline();
        await WriteByteSettingAsync(Addr.DpiEffectSpeed, speed);
        Settings.DpiEffectSpeed = speed;
    }

    async Task WriteLightAsync()
    {
        var s = Settings;
        var rec = new byte[] { (byte)s.LightMode, s.LightR, s.LightG, s.LightB, (byte)s.LightSpeed, (byte)s.LightBrightness, 0 };
        rec[6] = Packet.RecordChecksum(rec.AsSpan(0, 6));
        await WriteFlashAsync(Addr.Light, rec);
    }

    public async Task SetLightModeAsync(int mode)
    {
        await EnsureOnline();
        if (mode == 0)
        {
            if (Settings.LightOn) { await WriteByteSettingAsync(Addr.LightState, 0); Settings.LightOn = false; }
            return;
        }
        if (!Settings.LightOn) { await WriteByteSettingAsync(Addr.LightState, 1); Settings.LightOn = true; }
        Settings.LightMode = mode;
        await WriteLightAsync();
    }

    public async Task SetLightColorAsync(byte r, byte g, byte b)
    {
        await EnsureOnline();
        Settings.LightR = r; Settings.LightG = g; Settings.LightB = b;
        await WriteLightAsync();
    }

    public async Task SetLightBrightnessAsync(int v)
    {
        await EnsureOnline();
        Settings.LightBrightness = v;
        await WriteLightAsync();
    }

    public async Task SetLightSpeedAsync(int v)
    {
        await EnsureOnline();
        Settings.LightSpeed = v;
        await WriteLightAsync();
    }

    public async Task SetMovingOffLightAsync(bool on)
    {
        await EnsureOnline();
        await WriteByteSettingAsync(Addr.MovingOffLight, on ? 1 : 0);
        Settings.MovingOffLight = on;
    }

    public async Task SetLodAsync(int v) { await EnsureOnline(); await WriteByteSettingAsync(Addr.Lod, v); Settings.Lod = v; }
    public async Task SetDebounceAsync(int v) { await EnsureOnline(); await WriteByteSettingAsync(Addr.DebounceTime, v); Settings.DebounceTime = v; }
    public async Task SetMotionSyncAsync(bool on) { await EnsureOnline(); await WriteByteSettingAsync(Addr.MotionSync, on ? 1 : 0); Settings.MotionSync = on; }
    public async Task SetSleepTimeAsync(int v) { await EnsureOnline(); await WriteByteSettingAsync(Addr.SleepTime, v); Settings.SleepTime = v; }
    public async Task SetAngleSnapAsync(bool on) { await EnsureOnline(); await WriteByteSettingAsync(Addr.Angle, on ? 1 : 0); Settings.Angle = on; }
    public async Task SetRippleAsync(bool on) { await EnsureOnline(); await WriteByteSettingAsync(Addr.Ripple, on ? 1 : 0); Settings.Ripple = on; }
    public async Task SetPerformanceStateAsync(bool on) { await EnsureOnline(); await WriteByteSettingAsync(Addr.PerformanceState, on ? 1 : 0); Settings.PerformanceState = on; }
    public async Task SetPerformanceTimeAsync(int v) { await EnsureOnline(); await WriteByteSettingAsync(Addr.Performance, v); Settings.Performance = v; }
    public async Task SetSensorModeAsync(int v) { await EnsureOnline(); await WriteByteSettingAsync(Addr.SensorMode, v); Settings.SensorMode = v; }

    public (int Display, bool Locked) SensorModeDisplay()
    {
        if (!IsWired && Settings.SensorFps20K == 0)
        {
            if (Settings.ReportRate < 2000) return (Settings.SensorMode, false);
            if (Settings.ReportRate != 8000 && Model.Sensor == "3955") return (1, true);
        }
        return (256, true);
    }

    public async Task SetKeyAsync(int index, KeyAssignment a)
    {
        await EnsureOnline();
        await WriteFlashAsync(Addr.KeyFunction + 4 * index, a.ToRecord());
        Settings.Keys[index] = a;
        if (a.Type != KeyFunctionType.Macro) KeyMacros.Remove(index);
    }

    public async Task SetMediaKeyAsync(int index, int usage)
    {
        await EnsureOnline();
        var rec = new byte[] { 2, 0x82, (byte)usage, (byte)(usage >> 8), 0x42, (byte)usage, (byte)(usage >> 8), 0 };
        rec[7] = Packet.RecordChecksum(rec.AsSpan(0, 7));
        await WriteFlashAsync(Addr.ShortcutKey + Addr.ShortcutSize * index, rec);
        await SetKeyAsync(index, new KeyAssignment(KeyFunctionType.ShortcutKey, 0) { Combo = [new ComboKey(HidEventType.Consumer, usage)] });
    }

    public async Task SetComboAsync(int index, IReadOnlyList<ComboKey> keys)
    {
        if (keys.Count is 0 or > 5) throw new ArgumentException("Use de 1 a 5 teclas.");
        await EnsureOnline();
        var rec = new List<byte> { (byte)(2 * keys.Count) };
        foreach (var k in keys) rec.AddRange([(byte)(0x80 | k.Type), (byte)k.Value, (byte)(k.Value >> 8)]);
        for (int i = keys.Count - 1; i >= 0; i--)
        {
            var k = keys[i];
            rec.AddRange([(byte)(0x40 | k.Type), (byte)k.Value, (byte)(k.Value >> 8)]);
        }
        rec.Add(Packet.RecordChecksum(rec.ToArray()));
        await SetKeyAsync(index, new KeyAssignment(KeyFunctionType.ShortcutKey, 0));
        await WriteFlashAsync(Addr.ShortcutKey + Addr.ShortcutSize * index, rec.ToArray());
        Settings.Keys[index] = Settings.Keys[index] with { Combo = keys.ToList() };
    }

    public Task SetFireKeyAsync(int index, int times, int intervalMs) =>
        SetKeyAsync(index, new KeyAssignment(KeyFunctionType.FireKey, (Math.Clamp(intervalMs, 10, 255) << 8) | Math.Clamp(times, 0, 3)));

    public async Task SetMacroKeyAsync(int index, Macro macro)
    {
        await EnsureOnline();
        int cycle = macro.Cycle is >= 1 and <= MacroCycle.MaxTimes or MacroCycle.UntilThisPressed or MacroCycle.UntilThisReleased or MacroCycle.UntilAnyPressed
            ? macro.Cycle : 1;
        await WriteFlashAsync(Addr.Macro + Addr.MacroSize * index, macro.ToFlash());
        await SetKeyAsync(index, new KeyAssignment(KeyFunctionType.Macro, (index << 8) | cycle) { MacroName = macro.Name });
        KeyMacros[index] = macro.Clone();
    }

    public async Task SetProfileAsync(int profile)
    {
        await EnsureOnline();
        var r = await Exchange(Command.SetCurrentConfig, new[] { (byte)profile });
        if (r[1] != 0) throw new DeviceIoException("Troca de perfil não suportada.");
        Profile = profile;
        await ReadSettingsAsync();
    }

    public async Task SetLongDistanceAsync(bool on)
    {
        await EnsureOnline();
        var data = new byte[10];
        data[0] = (byte)(on ? 1 : 0);
        await Exchange(Command.SetLongRangeMode, data);
        LongDistance = on;
    }

    public async Task RestoreAsync()
    {
        await EnsureOnline();
        var r = await _t.ExchangeAsync(Packet.Build(Command.ClearSetting), timeoutMs: 6000, retries: 0)
                ?? throw new DeviceIoException("O dispositivo não confirmou a restauração.");
        await Task.Delay(200);
        await ReadSettingsAsync();
        await ReadProfileAsync();
        if (!IsWired && Model.HasLongDistance && LongDistance != null) await SetLongDistanceAsync(false);
    }

    public async Task<bool> PairAsync(IProgress<int>? secondsLeft = null, CancellationToken ct = default)
    {
        var p = Packet.Build(Command.DongleEnterPair, 0, new byte[] { 0, 0, (byte)Cid }, length: 2);
        if (await _t.ExchangeAsync(p, ct: ct) == null) throw new DeviceIoException("O receptor não entrou em modo de pareamento.");
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(1000, ct);
            var r = await _t.ExchangeAsync(Packet.Build(Command.GetPairState), ct: ct);
            if (r == null) continue;
            secondsLeft?.Report(r[6]);
            if (r[5] == 3) return true;
            if (r[5] == 2) return false;
        }
        return false;
    }

    public byte[] SnapshotFlash()
    {
        var copy = new byte[_flash.Length];
        _flash.CopyTo(copy, 0);
        return copy;
    }

    public async Task RestoreSnapshotAsync(byte[] snap)
    {
        if (snap.Length != _flash.Length) throw new ArgumentException("Arquivo de configuração inválido.");
        await EnsureOnline();
        int last = Addr.MainBlockSize - 1;
        while (last > 0 && snap[last] == 0xFF) last--;
        await WriteDiffAsync(snap, 0, last + 1);
        var tmp = new MouseSettings();
        tmp.Parse(snap, Model.SensorInfo, MaxReportRate);
        for (int i = 0; i < MouseSettings.MaxKeys; i++)
        {
            if (tmp.Keys[i].Type == KeyFunctionType.ShortcutKey)
                await WriteDiffAsync(snap, Addr.ShortcutKey + Addr.ShortcutSize * i, Addr.ShortcutSize, force: true);
            else if (tmp.Keys[i].Type == KeyFunctionType.Macro)
            {
                int a = Addr.Macro + Addr.MacroSize * i;
                int count = Math.Min((int)snap[a + 31], Macro.MaxEvents);
                await WriteDiffAsync(snap, a, 33 + 5 * count, force: true);
            }
        }
        await ReadSettingsAsync();
    }

    async Task WriteDiffAsync(byte[] snap, int start, int length, bool force = false)
    {
        for (int off = 0; off < length; off += Packet.MaxData)
        {
            int len = Math.Min(Packet.MaxData, length - off);
            int a = start + off;
            if (!force && snap.AsSpan(a, len).SequenceEqual(_flash.AsSpan(a, len))) continue;
            await WriteFlashAsync(a, snap.AsMemory(a, len));
        }
    }

    public async Task CloseAsync()
    {
        _batteryTimer.Change(Timeout.Infinite, Timeout.Infinite);
        try { await _t.ExchangeAsync(Packet.Build(Command.PCDriverStatus, 0, new byte[] { 0 }), 150, 0); } catch { }
    }

    public void Dispose()
    {
        _batteryTimer.Dispose();
        _t.Dispose();
    }
}
