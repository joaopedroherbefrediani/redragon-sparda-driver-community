using System.IO;
using System.Text.Json;
using SpardaControl.Core.Models;

namespace SpardaControl.Services;

public static class MacroLibrary
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpardaControl");
    static string FilePath => Path.Combine(Folder, "macros.json");

    public static List<Macro> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<Macro>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<Macro> macros)
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(macros.ToList(), Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
