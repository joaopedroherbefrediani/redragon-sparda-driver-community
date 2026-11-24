namespace SpardaControl.Core.Protocol;

public enum Command : byte
{
    EncryptionData = 1,
    PCDriverStatus = 2,
    DeviceOnLine = 3,
    BatteryLevel = 4,
    DongleEnterPair = 5,
    GetPairState = 6,
    WriteFlashData = 7,
    ReadFlashData = 8,
    ClearSetting = 9,
    StatusChanged = 10,
    GetCurrentConfig = 14,
    SetCurrentConfig = 15,
    ReadVersionID = 18,
    SetLongRangeMode = 22,
    GetLongRangeMode = 23,
    GetDongleVersion = 29,
}

public static class Addr
{
    public const int ReportRate = 0;
    public const int MaxDpiStage = 2;
    public const int CurrentDpi = 4;
    public const int KeyOperation = 8;
    public const int Lod = 10;
    public const int DpiValue = 12;
    public const int DpiColor = 44;
    public const int DpiEffectMode = 76;
    public const int DpiEffectBrightness = 78;
    public const int DpiEffectSpeed = 80;
    public const int DpiEffectState = 82;
    public const int KeyFunction = 96;
    public const int Light = 160;
    public const int LightState = 167;
    public const int DebounceTime = 169;
    public const int MotionSync = 171;
    public const int SleepTime = 173;
    public const int Angle = 175;
    public const int Ripple = 177;
    public const int MovingOffLight = 179;
    public const int PerformanceState = 181;
    public const int Performance = 183;
    public const int SensorMode = 185;
    public const int SensorFps20K = 225;
    public const int ShortcutKey = 256;
    public const int Macro = 768;

    public const int ShortcutSize = 32;
    public const int MacroSize = 384;
    public const int MainBlockSize = 256;
}

public enum KeyFunctionType : byte
{
    Disable = 0,
    MouseKey = 1,
    DpiSwitch = 2,
    LeftRightRoll = 3,
    FireKey = 4,
    ShortcutKey = 5,
    Macro = 6,
    ReportRateSwitch = 7,
    LightSwitch = 8,
    ProfileSwitch = 9,
    DpiLock = 10,
    UpDownRoll = 11,
}

[Flags]
public enum StatusFlags1 : byte
{
    CurrentDpi = 1,
    ReportRate = 2,
    Profile = 4,
    DpiEffect = 8,
    Light = 32,
    Battery = 64,
}

[Flags]
public enum StatusFlags2 : byte
{
    Lod = 1,
    Debounce = 2,
    MotionSync = 4,
    Flywheel = 8,
}
