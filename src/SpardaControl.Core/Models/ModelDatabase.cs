using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpardaControl.Core.Models;

public sealed class KeyInfo
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("param")] public int Param { get; set; }
}

public sealed class ModelInfo
{
    [JsonPropertyName("cid")] public int Cid { get; set; }
    [JsonPropertyName("mid")] public int Mid { get; set; }
    [JsonPropertyName("sensor")] public string Sensor { get; set; } = "";
    [JsonPropertyName("maxDpi")] public int MaxDpi { get; set; }
    [JsonPropertyName("maxDebounce")] public int MaxDebounce { get; set; }
    [JsonPropertyName("tipsDebounce")] public int TipsDebounce { get; set; }
    [JsonPropertyName("dpiStages")] public int DpiStages { get; set; }
    [JsonPropertyName("hasDpiEffect")] public bool HasDpiEffect { get; set; }
    [JsonPropertyName("hasLightEffect")] public bool HasLightEffect { get; set; }
    [JsonPropertyName("hasLongDistance")] public bool HasLongDistance { get; set; }
    [JsonPropertyName("hasPerformance")] public bool HasPerformance { get; set; }
    [JsonPropertyName("keys")] public List<KeyInfo> Keys { get; set; } = [];

    [JsonIgnore] public SensorInfo SensorInfo { get; set; } = null!;
    [JsonIgnore] public bool HasSensorMode { get; set; }
    [JsonIgnore] public bool HasLod { get; set; }
    [JsonIgnore] public bool HasRipple { get; set; }
    [JsonIgnore] public bool HasAngle { get; set; }
    [JsonIgnore] public bool HasMotionSync { get; set; }
    [JsonIgnore] public bool HasSensorSettings { get; set; }
}

public sealed class SensorRange
{
    [JsonPropertyName("min")] public int Min { get; set; }
    [JsonPropertyName("max")] public int Max { get; set; }
    [JsonPropertyName("step")] public int Step { get; set; }
    [JsonPropertyName("dpiEx")] public int DpiEx { get; set; }
}

public sealed class SensorInfo
{
    [JsonIgnore] public string Name { get; set; } = "";
    [JsonPropertyName("range")] public List<SensorRange> Range { get; set; } = [];
    [JsonPropertyName("values")] public List<int>? Values { get; set; }
    [JsonPropertyName("unsupported")] public bool Unsupported { get; set; }
}

sealed class DbFile
{
    [JsonPropertyName("pids")] public Dictionary<string, List<int>> Pids { get; set; } = [];
    [JsonPropertyName("models")] public List<ModelInfo> Models { get; set; } = [];
    [JsonPropertyName("sensors")] public Dictionary<string, SensorInfo> Sensors { get; set; } = [];
    [JsonPropertyName("features")] public Dictionary<string, List<string>> Features { get; set; } = [];
}

public static class ModelDatabase
{
    static readonly Lazy<DbFile> Db = new(Load);

    public static IReadOnlyList<int> AllProductIds =>
        Db.Value.Pids.Values.SelectMany(x => x).Distinct().ToList();

    public static bool IsWiredPid(int pid) =>
        Db.Value.Pids.TryGetValue("wired", out var l) && l.Contains(pid);

    public static ModelInfo? Find(int cid, int mid) =>
        Db.Value.Models.FirstOrDefault(m => m.Cid == cid && m.Mid == mid);

    static DbFile Load()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpardaControl.Core.Data.models.json")
                      ?? throw new InvalidOperationException("models.json missing");
        var db = JsonSerializer.Deserialize<DbFile>(s) ?? new DbFile();
        foreach (var (name, info) in db.Sensors) info.Name = name;
        bool Has(string feature, string sensor) => db.Features.TryGetValue(feature, out var l) && l.Contains(sensor);
        foreach (var m in db.Models)
        {
            m.SensorInfo = db.Sensors.TryGetValue(m.Sensor, out var si) ? si : new SensorInfo { Name = m.Sensor };
            m.HasSensorSettings = Has("setting", m.Sensor);
            m.HasSensorMode = Has("modeSelect", m.Sensor);
            m.HasLod = Has("lod", m.Sensor);
            m.HasRipple = Has("ripple", m.Sensor);
            m.HasAngle = Has("angle", m.Sensor);
            m.HasMotionSync = Has("motionSync", m.Sensor);
        }
        return db;
    }
}
