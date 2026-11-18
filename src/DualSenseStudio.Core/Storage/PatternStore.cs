using System.Text.Json;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Core.Storage;

/// <summary>Saves user patterns as one JSON file each (*.dspattern.json).</summary>
public sealed class PatternStore
{
    public const string Extension = ".dspattern.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public PatternStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    public string PathFor(Pattern p) => System.IO.Path.Combine(Directory, p.Id + Extension);

    public List<Pattern> LoadAll()
    {
        var list = new List<Pattern>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
        {
            var p = TryLoad(file);
            if (p != null) list.Add(p);
        }
        return list.OrderByDescending(p => p.Modified).ToList();
    }

    public static Pattern? TryLoad(string file)
    {
        try
        {
            var p = JsonSerializer.Deserialize<Pattern>(File.ReadAllText(file), Json);
            if (p == null || p.Left.Length == 0) return null;
            p.Normalize();
            return p;
        }
        catch
        {
            return null;
        }
    }

    public void Save(Pattern p)
    {
        p.Modified = DateTime.Now;
        var tmp = PathFor(p) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(p, Json));
        File.Move(tmp, PathFor(p), overwrite: true);
    }

    public static void Export(Pattern p, string file) => File.WriteAllText(file, JsonSerializer.Serialize(p, Json));

    /// <summary>Imports a file as a new pattern (fresh id so it never overwrites an existing one).</summary>
    public Pattern? Import(string file)
    {
        var p = TryLoad(file);
        if (p == null) return null;
        p.Id = Guid.NewGuid().ToString("N");
        Save(p);
        return p;
    }
}
