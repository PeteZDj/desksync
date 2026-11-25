namespace Desksync;

public sealed record LocalFileState(bool Available, bool IsDirectory);

// Renderer input and peer filenames are relative to the selected sync folder.
public static class LocalFiles
{
    public static string Resolve(string folder, string name, bool mustExist = true)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 4096 || name[0] is '/' or '\\' || name.Any(char.IsControl))
            throw new IOException("Choose a file inside your Desksync folder.");
        var parts = name.Split(['/', '\\']);
        if (parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.Contains(':') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            (OperatingSystem.IsWindows() && (p.EndsWith('.') || p.EndsWith(' ') || IsDeviceName(p)))))
            throw new IOException("Choose a file inside your Desksync folder.");
        var root = Path.GetFullPath(folder);
        var target = Path.GetFullPath(Path.Combine([root, .. parts]));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new IOException("The file is outside your Desksync folder.");
        // Do not follow junctions or symbolic links to files outside the folder.
        var current = root;
        CheckLink(current);
        foreach (var part in parts) { current = Path.Combine(current, part); CheckLink(current); }
        if (mustExist && !File.Exists(target) && !Directory.Exists(target))
            throw new IOException("This item is not available on this computer. It may have been deleted or may still be downloading.");
        return target;
    }

    static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]));
    }

    static void CheckLink(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Open linked items from your file manager instead."); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    public static LocalFileState Describe(string folder, string name)
    {
        try { var path = Resolve(folder, name, false); var directory = Directory.Exists(path); return new(File.Exists(path) || directory, directory); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return new(false, false); }
    }

    public static string? ActivityPath(SyncActivity item) => item.Action is "Changed locally" or "Deleted locally" or "Downloaded" or "Deletion received" || item.Action.StartsWith("Uploaded to ", StringComparison.Ordinal) ? item.Name : null;
}
