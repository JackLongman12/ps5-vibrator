using System.Text.Json;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Core.Storage;

/// <summary>Saves user patterns as one JSON file each (*.dspattern.json).</summary>
public sealed class PatternStore
{
    public const string Extension = ".dspattern.json";

    /// <summary>Files larger than this are refused (a 10-minute 4-channel pattern is well under 4 MB).</summary>
    public const long MaxFileBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        MaxDepth = 8,
    };

    public PatternStore(string directory)
    {
        Directory = Path.GetFullPath(directory);
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    /// <summary>
    /// File path for a pattern. The id must be a plain 32-char hex id, so a crafted id
    /// (e.g. "..\..\x") can never point outside the patterns folder.
    /// </summary>
    public string PathFor(Pattern p)
    {
        if (!Pattern.IsValidId(p.Id))
            throw new InvalidOperationException("Pattern id is not a valid identifier.");
        var path = Path.GetFullPath(Path.Combine(Directory, p.Id + Extension));
        if (!path.StartsWith(Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pattern path escapes the library folder.");
        return path;
    }

    public List<Pattern> LoadAll()
    {
        var list = new List<Pattern>();
        var seen = new HashSet<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
        {
            var p = TryLoad(file);
            if (p == null) continue;
            // A file's id must match its file name; otherwise (copied/edited files) give it a fresh one.
            var expected = Path.GetFileName(file)[..^Extension.Length];
            if (p.Id != expected || !seen.Add(p.Id))
            {
                p.Id = Guid.NewGuid().ToString("N");
                try
                {
                    Save(p);
                    File.Delete(file);
                }
                catch { continue; }
                seen.Add(p.Id);
            }
            list.Add(p);
        }
        return list.OrderByDescending(p => p.Modified).ToList();
    }

    /// <summary>Loads and sanitizes a pattern file. Returns null for anything unreadable, oversized or invalid.</summary>
    public static Pattern? TryLoad(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            var p = JsonSerializer.Deserialize<Pattern>(File.ReadAllText(file), Json);
            if (p == null || !p.Sanitize()) return null;
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
        var path = PathFor(p);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(p, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public static void Export(Pattern p, string file) => File.WriteAllText(file, JsonSerializer.Serialize(p, Json));

    /// <summary>Imports a file as a new pattern (fresh id so it never overwrites an existing one).</summary>
    public Pattern? Import(string file)
    {
        var p = TryLoad(file);
        if (p == null) return null;
        p.Id = Guid.NewGuid().ToString("N");
        p.Category = "Imported";
        Save(p);
        return p;
    }
}
