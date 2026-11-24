using HidSharp;
using SpardaControl.Core.Hid;
using SpardaControl.Core.Protocol;

if (args.Length > 0 && args[0] == "--write-test") { await WriteTest.RunAsync(); return; }

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("== HID collections (VID 0x3554) ==");
foreach (var d in DeviceList.Local.GetHidDevices(HidTransport.VendorId))
{
    string info;
    try { info = $"in={d.GetMaxInputReportLength()} out={d.GetMaxOutputReportLength()} feat={d.GetMaxFeatureReportLength()}"; }
    catch (Exception e) { info = e.Message; }
    Console.WriteLine($"PID 0x{d.ProductID:X4}  {info}  {d.DevicePath}");
}

using var t = HidTransport.TryOpen(Array.Empty<int>());
if (t == null) { Console.WriteLine("Config interface not found."); return; }
Console.WriteLine($"\nOpened: {t.ProductName} PID 0x{t.ProductId:X4}");
t.Unsolicited += p => Console.WriteLine("  [unsolicited] " + Convert.ToHexString(p));

async Task<byte[]?> X(Command c, byte[]? data = null, int addr = 0)
{
    var r = await t.ExchangeAsync(Packet.Build(c, addr, data ?? []));
    Console.WriteLine($"{c,-18} -> {(r == null ? "TIMEOUT" : Convert.ToHexString(r))}");
    return r;
}

var rnd = new byte[8];
Random.Shared.NextBytes(rnd.AsSpan(0, 4));
var dev = await X(Command.EncryptionData, rnd);
if (dev != null) Console.WriteLine($"   cid={dev[9]} mid={dev[10]} type={dev[11]}");
var online = await X(Command.DeviceOnLine);
if (online != null) Console.WriteLine($"   online={online[5]}");
var bat = await X(Command.BatteryLevel);
if (bat != null) Console.WriteLine($"   battery={bat[5]}% charging={bat[6]} voltage={(bat[7] << 8) + bat[8]}mV");
var prof = await X(Command.GetCurrentConfig);
if (prof != null) Console.WriteLine($"   profile={prof[5]}");
var ver = await X(Command.ReadVersionID);
if (ver != null) Console.WriteLine($"   mouse fw=v{ver[5]}.{ver[6]:x2}");
var dver = await X(Command.GetDongleVersion);
if (dver != null) Console.WriteLine($"   dongle fw=v{dver[5]}.{dver[6]:x2}");
await X(Command.GetLongRangeMode);

Console.WriteLine("\n== Flash 0x000-0x0FF ==");
var flash = new byte[256];
for (int a = 0; a < 256; a += 10)
{
    int len = Math.Min(10, 256 - a);
    var r = await t.ExchangeAsync(Packet.Build(Command.ReadFlashData, a, default, len));
    if (r == null) { Console.WriteLine($"read @{a} timeout"); continue; }
    Array.Copy(r, 5, flash, a, Math.Min(len, r[4] & 15));
}
for (int a = 0; a < 256; a += 16)
    Console.WriteLine($"{a:X3}: {Convert.ToHexString(flash, a, 16)}");

t.Dispose();
Console.WriteLine("\n== MouseDevice decode ==");
using var mouse = await SpardaControl.Core.MouseDevice.ConnectAsync();
if (mouse == null) { Console.WriteLine("connect failed"); return; }
await mouse.LoadAllAsync();
var s = mouse.Settings;
var m = mouse.Model;
Console.WriteLine($"Model mid={m.Mid} sensor={m.Sensor} maxDpi={m.MaxDpi} stages={m.DpiStages} light={m.HasLightEffect} wired={mouse.IsWired} maxRate={mouse.MaxReportRate}");
Console.WriteLine($"Profile={mouse.Profile} (supported={mouse.SupportsProfiles}) LongDistance={mouse.LongDistance} FW mouse={mouse.MouseFirmware} dongle={mouse.DongleFirmware}");
Console.WriteLine($"Battery={mouse.Battery}");
Console.WriteLine($"Rate={s.ReportRate} Stages={s.MaxDpiStage} Current={s.CurrentDpi} LOD={s.Lod} Debounce={s.DebounceTime} Sleep={s.SleepTime}");
for (int i = 0; i < s.MaxDpiStage; i++) Console.WriteLine($"  DPI{i + 1}: {s.Dpis[i].X}x{s.Dpis[i].Y} #{s.Dpis[i].R:X2}{s.Dpis[i].G:X2}{s.Dpis[i].B:X2}");
Console.WriteLine($"DPI effect mode={s.DpiEffectMode} on={s.DpiEffectOn} bright={s.DpiEffectBrightness} speed={s.DpiEffectSpeed}");
Console.WriteLine($"MotionSync={s.MotionSync} Angle={s.Angle} Ripple={s.Ripple} Perf={s.PerformanceState}/{s.Performance} SensorMode={s.SensorMode} display={mouse.SensorModeDisplay()}");
foreach (var k in m.Keys) Console.WriteLine($"  key idx {k.Index}: {s.Keys[k.Index].Describe()}");
var snap = mouse.SnapshotFlash();
for (int i = 0; i < 8; i++)
{
    var rec = SpardaControl.Core.Protocol.DpiCodec.EncodeRecord(m.SensorInfo, s.Dpis[i].X, s.Dpis[i].Y);
    var raw = snap.AsSpan(12 + 4 * i, 4).ToArray();
    Console.WriteLine($"  codec stage {i}: {(rec.AsSpan().SequenceEqual(raw) ? "OK" : "MISMATCH " + Convert.ToHexString(rec) + " vs " + Convert.ToHexString(raw))}");
}
foreach (var dpi in new[] { 10, 400, 10000, 10100, 26000, 30000, 30200, 45000 })
{
    var r = SpardaControl.Core.Protocol.DpiCodec.EncodeRecord(m.SensorInfo, dpi, dpi);
    var back = SpardaControl.Core.Protocol.DpiCodec.DecodeRecord(m.SensorInfo, r);
    Console.WriteLine($"  {dpi} -> {Convert.ToHexString(r)} -> {back.X}");
}
await mouse.CloseAsync();
