using SpardaControl.Core.Protocol;

namespace SpardaControl.Core.Models;

public sealed class DpiStage
{
    public int X { get; set; }
    public int Y { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
}

public sealed class MouseSettings
{
    public const int MaxKeys = 16;

    public int ReportRate { get; set; } = 1000;
    public int MaxDpiStage { get; set; } = 4;
    public int CurrentDpi { get; set; }
    public int Lod { get; set; } = 1;
    public DpiStage[] Dpis { get; } = Enumerable.Range(0, 8).Select(_ => new DpiStage()).ToArray();

    public int DpiEffectMode { get; set; }
    public int DpiEffectBrightness { get; set; } = 5;
    public int DpiEffectSpeed { get; set; } = 3;
    public bool DpiEffectOn { get; set; } = true;

    public int LightMode { get; set; }
    public byte LightR { get; set; } = 255;
    public byte LightG { get; set; }
    public byte LightB { get; set; }
    public int LightSpeed { get; set; } = 3;
    public int LightBrightness { get; set; } = 3;
    public bool LightOn { get; set; } = true;
    public bool MovingOffLight { get; set; }

    public int DebounceTime { get; set; } = 8;
    public bool MotionSync { get; set; }
    public int SleepTime { get; set; } = 6;
    public bool Angle { get; set; }
    public bool Ripple { get; set; }
    public bool PerformanceState { get; set; }
    public int Performance { get; set; } = 6;
    public int SensorMode { get; set; }
    public int SensorFps20K { get; set; }

    public KeyAssignment[] Keys { get; } = Enumerable.Range(0, MaxKeys).Select(_ => new KeyAssignment(KeyFunctionType.Disable, 0)).ToArray();

    public static int FlashToReportRate(byte v) => v >= 16 ? v / 16 * 2000 : v == 0 ? 1000 : 1000 / v;
    public static byte ReportRateToFlash(int rate) => (byte)(rate <= 1000 ? 1000 / rate : rate / 2000 * 16);

    public static int BrightnessFromFlash(byte v) => v switch
    {
        16 => 1, 128 => 5, 230 => 9, 255 => 10,
        _ when v % 30 == 0 => v / 30 + 1,
        _ => 5,
    };

    public static byte BrightnessToFlash(int level) => level switch
    {
        1 => 16, 5 => 128, 9 => 230, 10 => 255,
        2 or 3 or 4 or 6 or 7 or 8 => (byte)(30 * (level - 1)),
        _ => 128,
    };

    public void Parse(ReadOnlySpan<byte> f, SensorInfo sensor, int maxReportRate)
    {
        ReportRate = Math.Min(FlashToReportRate(f[Addr.ReportRate]), maxReportRate);
        MaxDpiStage = f[Addr.MaxDpiStage];
        CurrentDpi = f[Addr.CurrentDpi];
        Lod = f[Addr.Lod];
        for (int i = 0; i < 8; i++)
        {
            var rec = f.Slice(Addr.DpiValue + 4 * i, 4);
            var (x, y) = DpiCodec.DecodeRecord(sensor, rec);
            Dpis[i].X = x;
            Dpis[i].Y = y;
            int c = Addr.DpiColor + 4 * i;
            Dpis[i].R = f[c]; Dpis[i].G = f[c + 1]; Dpis[i].B = f[c + 2];
        }
        DpiEffectMode = f[Addr.DpiEffectMode];
        DpiEffectBrightness = BrightnessFromFlash(f[Addr.DpiEffectBrightness]);
        DpiEffectSpeed = f[Addr.DpiEffectSpeed];
        DpiEffectOn = f[Addr.DpiEffectState] == 1;

        LightMode = f[Addr.Light];
        LightR = f[Addr.Light + 1]; LightG = f[Addr.Light + 2]; LightB = f[Addr.Light + 3];
        LightSpeed = Math.Min((int)f[Addr.Light + 4], 9);
        LightBrightness = Math.Min((int)f[Addr.Light + 5], 9);
        LightOn = f[Addr.LightState] == 1;
        MovingOffLight = f[Addr.MovingOffLight] == 1;

        DebounceTime = f[Addr.DebounceTime];
        MotionSync = f[Addr.MotionSync] == 1;
        SleepTime = f[Addr.SleepTime];
        Angle = f[Addr.Angle] == 1;
        Ripple = f[Addr.Ripple] == 1;
        PerformanceState = f[Addr.PerformanceState] == 1;
        Performance = f[Addr.Performance];
        SensorMode = f[Addr.SensorMode];
        SensorFps20K = Packet.IsValidPair(f[Addr.SensorFps20K], f[Addr.SensorFps20K + 1]) ? f[Addr.SensorFps20K] : 0;

        for (int i = 0; i < MaxKeys; i++)
            Keys[i] = KeyAssignment.FromRecord(f.Slice(Addr.KeyFunction + 4 * i, 4));
    }
}
