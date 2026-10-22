using System.Text.Json;

namespace Chisel.Core;

public sealed class AppPaths
{
    public string Root { get; }
    public string SharedDir => Path.Combine(Root, "shared");
    public string VersionsDir => Path.Combine(SharedDir, "versions");
    public string LibrariesDir => Path.Combine(SharedDir, "libraries");
    public string AssetsDir => Path.Combine(SharedDir, "assets");
    public string InstancesDir => Path.Combine(Root, "instances");
    public string JavaDir => Path.Combine(Root, "java");
    public string CacheDir => Path.Combine(Root, "cache");
    public string LogsDir => Path.Combine(Root, "logs");
    public string AccountsDir => Path.Combine(Root, "accounts");
    public string WebViewDir => Path.Combine(CacheDir, "webview2");
    public string TempDir => Path.Combine(Root, "temp");
    public string ConfigPath => Path.Combine(Root, "config.json");
    public string AccountsPath => Path.Combine(Root, "accounts.json");

    private AppPaths(string root) => Root = root;

    public static AppPaths Create()
    {
        var env = Environment.GetEnvironmentVariable("CHISEL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return Finish(Path.GetFullPath(env));

        var exeDir = AppContext.BaseDirectory;
        var portable = new[] { "--portable" }.Any(a => Environment.GetCommandLineArgs().Contains(a))
            || File.Exists(Path.Combine(exeDir, "portable.txt"));
        if (portable)
            return Finish(Path.Combine(exeDir, "data"));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Finish(Path.Combine(appData, "Chisel"));
    }

    private static AppPaths Finish(string root)
    {
        var paths = new AppPaths(root);
        foreach (var dir in new[]
                 {
                     paths.Root, paths.SharedDir, paths.VersionsDir, paths.LibrariesDir, paths.AssetsDir,
                     paths.InstancesDir, paths.JavaDir, paths.CacheDir, paths.LogsDir, paths.AccountsDir,
                     paths.WebViewDir, paths.TempDir,
                     Path.Combine(paths.AssetsDir, "indexes"), Path.Combine(paths.AssetsDir, "objects"),
                     Path.Combine(paths.AssetsDir, "log_configs")
                 })
            Directory.CreateDirectory(dir);
        return paths;
    }

    public string InstanceDir(string id) => Path.Combine(InstancesDir, id);
    public string InstanceFile(string id) => Path.Combine(InstanceDir(id), "instance.json");
    public string GameDir(string id) => Path.Combine(InstanceDir(id), "game");
    public string VersionDir(string gameVersion) => Path.Combine(VersionsDir, gameVersion);
    public string LoaderDir(string profileId) => Path.Combine(VersionsDir, profileId);

    public static bool IsSafeSegment(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length is > 0 and <= 80 && trimmed is not "." and not ".." &&
               trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public bool IsUnderRoot(string path, string? alsoAllow = null)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.TrimEnd('\\') == Root.TrimEnd('\\'))
                return true;
            if (alsoAllow != null)
            {
                var allow = Path.GetFullPath(alsoAllow) + Path.DirectorySeparatorChar;
                return full.StartsWith(allow, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // invalid paths are not openable
        }
        return false;
    }
}

internal static class AtomicJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static T Read<T>(string path, Func<T> fallback) where T : class
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(text, Options) ?? fallback();
        }
        catch
        {
            try
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".broken", overwrite: true);
            }
            catch { }
            return fallback();
        }
    }

    public static void Write<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
