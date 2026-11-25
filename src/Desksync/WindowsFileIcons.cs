using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Desksync;

sealed class WindowsFileIcons
{
    readonly Dictionary<string, (byte[] Bytes, DateTimeOffset At)> cache = new(StringComparer.OrdinalIgnoreCase);
    public byte[]? Read(string folder, string name)
    {
        var path = LocalFiles.Resolve(folder, name, false);
        if (cache.TryGetValue(path, out var saved) && DateTimeOffset.UtcNow - saved.At < TimeSpan.FromMinutes(1)) return saved.Bytes;
        var exists = File.Exists(path) || Directory.Exists(path);
        // Query the installed shell association, including WinRAR, Chrome, custom
        // application/file icons and folders. Never load an icon from a URL.
        var associationOnly = !exists || Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".url";
        if (SHGetFileInfo(path, 0x80, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100 | (associationOnly ? 0x10u : 0u)) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
        try
        {
            using var icon = Icon.FromHandle(info.Icon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
            var bytes = stream.ToArray();
            if (cache.Count >= 256) cache.Clear();
            cache[path] = (bytes, DateTimeOffset.UtcNow);
            return bytes;
        }
        finally { DestroyIcon(info.Icon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShellFileInfo
    {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}
