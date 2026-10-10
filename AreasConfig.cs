using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RotemExcel;

public class AreaRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsSet => Width > 0 && Height > 0;

    public override string ToString() => $"X={X}, Y={Y}, W={Width}, H={Height}";
}

public class AreasConfig
{
    public Dictionary<string, AreaRect> Areas { get; set; } = new();

    private static string Path => System.IO.Path.Combine(
        AppContext.BaseDirectory, "areas.json");

    public static AreasConfig Load()
    {
        if (!File.Exists(Path))
            return new AreasConfig();

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<AreasConfig>(json, Options) ?? new AreasConfig();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, Options);
        File.WriteAllText(Path, json);
    }

    public AreaRect Get(string key) =>
        Areas.TryGetValue(key, out var r) ? r : new AreaRect();

    public void Set(string key, AreaRect rect) => Areas[key] = rect;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };
}
