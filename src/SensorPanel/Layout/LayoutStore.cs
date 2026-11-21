using System.Text.RegularExpressions;

namespace SensorPanel.Layout;

public static class LayoutStore
{
    public static LayoutLibrary Default { get; } = new(AppPaths.Layouts);

    /// <summary>
    /// Creates the layout folder. Returns true when it has no layouts at all, i.e. a first run that
    /// should create one from a template (see <see cref="App.FirstRun"/>).
    /// </summary>
    public static bool EnsureDefaults() => EnsureDefaults(AppPaths.Layouts);

    public static bool EnsureDefaults(string directory)
    {
        Directory.CreateDirectory(directory);
        return new LayoutLibrary(directory).List().Count == 0;
    }

    public static string ResolvePath(string nameOrPath) => Default.ResolvePath(nameOrPath);
    public static PanelLayout Load(string nameOrPath) => Default.Load(nameOrPath);
    public static void Save(string nameOrPath, PanelLayout layout) => Default.Save(nameOrPath, layout);
}

/// <summary>
/// The layout files in one folder. Names coming from the editor are plain file names
/// (validated by <see cref="IsValidName"/>), never paths.
/// </summary>
public sealed partial class LayoutLibrary(string directory)
{
    private const int BackupsToKeep = 20;

    public string Directory { get; } = directory;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _.-]{0,59}\.json$")]
    private static partial Regex NameRegex();

    public static bool IsValidName(string? name) =>
        name is not null && NameRegex().IsMatch(name) && !name.Contains("..");

    /// <summary>Turns user input like "GPU page" into "GPU page.json"; null if it can't be a valid name.</summary>
    public static string? NormalizeName(string? input)
    {
        var name = input?.Trim() ?? "";
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name += ".json";
        else name = name[..^5] + ".json";
        return IsValidName(name) ? name : null;
    }

    public string ResolvePath(string nameOrPath) =>
        Path.IsPathRooted(nameOrPath) ? nameOrPath : Path.Combine(Directory, nameOrPath);

    public bool Exists(string name) => IsValidName(name) && File.Exists(Path.Combine(Directory, name));

    public IReadOnlyList<string> List() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json")
                .Select(Path.GetFileName).OfType<string>()
                .Where(IsValidName)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    public PanelLayout Load(string nameOrPath) =>
        LayoutSerializer.Parse(File.ReadAllText(ResolvePath(nameOrPath)));

    /// <summary>Saves atomically, first copying the previous version into a "backups" subfolder.</summary>
    public void Save(string nameOrPath, PanelLayout layout)
    {
        var path = ResolvePath(nameOrPath);
        var json = LayoutSerializer.Serialize(layout);
        Backup(path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, json);
    }

    /// <summary>Creates a new layout file; fails if the name is taken.</summary>
    public void Create(string name, PanelLayout layout)
    {
        Require(name);
        if (Exists(name)) throw new IOException($"A layout named “{name}” already exists.");
        Save(name, layout);
    }

    /// <summary>Deletes a layout (a copy stays in backups).</summary>
    public void Delete(string name)
    {
        Require(name);
        var path = Path.Combine(Directory, name);
        Backup(path);
        File.Delete(path);
    }

    public void Rename(string from, string to)
    {
        Require(from);
        Require(to);
        if (string.Equals(from, to, StringComparison.Ordinal)) return;
        var src = Path.Combine(Directory, from);
        if (!File.Exists(src)) throw new FileNotFoundException($"Layout “{from}” not found.");
        // Case-only renames are allowed on the same file.
        if (Exists(to) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"A layout named “{to}” already exists.");
        File.Move(src, Path.Combine(Directory, to), overwrite: string.Equals(from, to, StringComparison.OrdinalIgnoreCase));
    }

    private static void Require(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException($"Invalid layout name “{name}”.");
    }

    private static void Backup(string path)
    {
        if (!File.Exists(path)) return;
        var dir = Path.Combine(Path.GetDirectoryName(path)!, "backups");
        System.IO.Directory.CreateDirectory(dir);
        var stem = Path.GetFileNameWithoutExtension(path);
        File.Copy(path, Path.Combine(dir, $"{stem}.{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: true);

        foreach (var old in new DirectoryInfo(dir).GetFiles($"{stem}.*.json")
                     .OrderByDescending(f => f.Name).Skip(BackupsToKeep))
            old.Delete();
    }
}
