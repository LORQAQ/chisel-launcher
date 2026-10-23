using System.IO.Compression;
using System.Text.Json;

namespace Chisel.Core.Minecraft;

public sealed class LocalPackCandidate
{
    public string Id { get; init; } = "";
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Launcher { get; init; } = "";
    public string Name { get; init; } = "";
    public string GameVersion { get; init; } = "";
    public string Loader { get; init; } = "vanilla";
    public string? LoaderVersion { get; init; }
    public int ModCount { get; init; }
    public long SizeBytes { get; init; }
    public bool HasSaves { get; init; }
}

public sealed class LocalPackScanResult
{
    public List<LocalPackCandidate> Candidates { get; init; } = [];
    public List<string> Roots { get; init; } = [];
}

/// <summary>
/// 扫描本机其他启动器留下的实例与整合包压缩包。只读，不改动来源目录。
/// </summary>
public sealed class LocalPackScanner
{
    private static readonly string[] ContentDirs =
        ["mods", "config", "defaultconfigs", "scripts", "kubejs", "resourcepacks", "shaderpacks", "saves"];

    private const int MaxArchives = 60;
    private const int MaxInstancesPerRoot = 200;

    private readonly AppPaths _paths;

    public LocalPackScanner(AppPaths paths) => _paths = paths;

    public LocalPackScanResult Scan(IEnumerable<string>? extraRoots, CancellationToken ct)
    {
        var result = new LocalPackScanResult();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, kind, launcher) in InstanceRoots(extraRoots))
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            if (!result.Roots.Contains(root, StringComparer.OrdinalIgnoreCase)) result.Roots.Add(root);

            var count = 0;
            foreach (var dir in SafeDirs(root))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > MaxInstancesPerRoot) break;
                if (IsUnderOwnInstances(dir)) continue;

                var candidate = InspectInstance(dir, kind, launcher);
                if (candidate == null) continue;
                if (!seen.Add(candidate.Path)) continue;
                result.Candidates.Add(candidate);
            }
        }

        foreach (var file in ArchiveFiles(extraRoots, ct))
        {
            ct.ThrowIfCancellationRequested();
            var candidate = InspectArchive(file);
            if (candidate == null) continue;
            if (!seen.Add(candidate.Path)) continue;
            result.Candidates.Add(candidate);
        }

        result.Candidates.Sort((a, b) =>
        {
            var byMods = b.ModCount.CompareTo(a.ModCount);
            return byMods != 0 ? byMods : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private IEnumerable<(string Root, string Kind, string Launcher)> InstanceRoots(IEnumerable<string>? extraRoots)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 这些启动器都把每个实例放成 <root>/<instance>/
        yield return (Path.Combine(appData, "PrismLauncher", "instances"), "prism", "Prism Launcher");
        yield return (Path.Combine(appData, "MultiMC", "instances"), "prism", "MultiMC");
        yield return (Path.Combine(appData, "PolyMC", "instances"), "prism", "PolyMC");
        yield return (Path.Combine(appData, "com.modrinth.theseus", "profiles"), "modrinth", "Modrinth App");
        yield return (Path.Combine(appData, "xmcl", "instances"), "xmcl", "X Minecraft Launcher");

        // HMCL / PCL2 / 官方启动器：版本隔离后每个版本就是一个实例目录
        yield return (Path.Combine(appData, "HMCL", ".minecraft", "versions"), "minecraft", "HMCL");
        yield return (Path.Combine(home, "HMCL", ".minecraft", "versions"), "minecraft", "HMCL");
        yield return (Path.Combine(appData, ".minecraft", "versions"), "minecraft", "官方启动器 / PCL");
        yield return (Path.Combine(home, ".minecraft", "versions"), "minecraft", "官方启动器 / PCL");

        if (extraRoots != null)
            foreach (var root in extraRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
                yield return (root, "auto", "手动添加");
    }

    private LocalPackCandidate? InspectInstance(string dir, string kind, string launcher)
    {
        var name = Path.GetFileName(dir);
        var gameDir = Directory.Exists(Path.Combine(dir, ".minecraft")) ? Path.Combine(dir, ".minecraft") : dir;

        if (kind == "auto")
        {
            if (File.Exists(Path.Combine(dir, "mmc-pack.json")) || File.Exists(Path.Combine(dir, "instance.cfg")))
                (kind, launcher) = ("prism", "Prism 兼容实例");
            else if (File.Exists(Path.Combine(dir, "profile.json")))
                (kind, launcher) = ("modrinth", "Modrinth App");
            else if (File.Exists(Path.Combine(dir, "instance.json")))
                (kind, launcher) = ("xmcl", "X Minecraft Launcher");
            else
                kind = "minecraft";
        }

        string gameVersion = "";
        var loader = "vanilla";
        string? loaderVersion = null;
        var displayName = name;

        switch (kind)
        {
            case "prism":
            {
                var pack = Path.Combine(dir, "mmc-pack.json");
                if (!File.Exists(pack) && !Directory.Exists(Path.Combine(gameDir, "mods"))) return null;
                displayName = ReadIniValue(Path.Combine(dir, "instance.cfg"), "name") ?? name;
                if (File.Exists(pack))
                    (gameVersion, loader, loaderVersion) = ReadMmcPack(pack);
                break;
            }
            case "modrinth":
            {
                var profile = Path.Combine(dir, "profile.json");
                if (!File.Exists(profile)) return null;
                (displayName, gameVersion, loader, loaderVersion) = ReadModrinthProfile(profile, name);
                break;
            }
            case "xmcl":
            {
                var instanceJson = Path.Combine(dir, "instance.json");
                if (!File.Exists(instanceJson)) return null;
                (displayName, gameVersion, loader, loaderVersion) = ReadXmclInstance(instanceJson, name);
                break;
            }
            default:
            {
                var versionJson = Path.Combine(dir, name + ".json");
                if (!File.Exists(versionJson))
                {
                    var any = SafeFiles(dir, "*.json").FirstOrDefault();
                    if (any == null) return null;
                    versionJson = any;
                }

                if (Directory.Exists(Path.Combine(dir, "mods")) || Directory.Exists(Path.Combine(gameDir, "mods")))
                    (gameVersion, loader, loaderVersion) = ReadVersionJson(versionJson);
                else if (SafeFiles(dir, "*.jar").Any(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                                          && Path.GetFileName(f).StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                    (gameVersion, loader, loaderVersion) = ReadVersionJson(versionJson);
                else
                    return null;
                break;
            }
        }

        var modsDir = Path.Combine(gameDir, "mods");
        var modCount = Directory.Exists(modsDir)
            ? Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly).Length
            : 0;
        if (modCount == 0 && kind != "prism" && kind != "modrinth" && kind != "xmcl") return null;
        if (modCount == 0 && string.IsNullOrEmpty(gameVersion)) return null;

        var size = MeasureContent(gameDir);
        return new LocalPackCandidate
        {
            Id = StableId(dir),
            Path = gameDir,
            Kind = kind,
            Launcher = launcher,
            Name = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
            GameVersion = gameVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            ModCount = modCount,
            SizeBytes = size,
            HasSaves = Directory.Exists(Path.Combine(gameDir, "saves")) &&
                       Directory.EnumerateDirectories(Path.Combine(gameDir, "saves")).Any()
        };
    }

    private LocalPackCandidate? InspectArchive(string file)
    {
        try
        {
            using var archive = ZipFile.OpenRead(file);
            var hasModrinth = archive.GetEntry("modrinth.index.json") != null;
            var hasCurse = archive.GetEntry("manifest.json") != null;
            if (!hasModrinth && !hasCurse) return null;

            var info = new FileInfo(file);
            return new LocalPackCandidate
            {
                Id = StableId(file),
                Path = file,
                Kind = "archive",
                Launcher = hasModrinth ? "Modrinth 整合包" : "CurseForge 整合包",
                Name = Path.GetFileNameWithoutExtension(file),
                GameVersion = "",
                Loader = "vanilla",
                ModCount = 0,
                SizeBytes = info.Length
            };
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> ArchiveFiles(IEnumerable<string>? extraRoots, CancellationToken ct)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>
        {
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
            Path.Combine(home, "Documents")
        };
        if (extraRoots != null) roots.AddRange(extraRoots.Where(r => !string.IsNullOrWhiteSpace(r)));

        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in SafeArchives(root))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > MaxArchives) yield break;
                if (seen.Add(file)) yield return file;
            }
        }
    }

    private bool IsUnderOwnInstances(string dir) =>
        dir.StartsWith(_paths.InstancesDir, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SafeDirs(string root)
    {
        string[] items;
        try { items = Directory.GetDirectories(root); }
        catch { return []; }
        return items;
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeArchives(string root)
    {
        try
        {
            return Directory.GetFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return [];
        }
    }

    private static long MeasureContent(string gameDir)
    {
        long total = 0;
        foreach (var name in ContentDirs)
        {
            var dir = Path.Combine(gameDir, name);
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { }
                }
            }
            catch { }
        }

        return total;
    }

    private static (string GameVersion, string Loader, string? LoaderVersion) ReadMmcPack(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("components", out var components)) return ("", "vanilla", null);
            string gameVersion = "";
            var loader = "vanilla";
            string? loaderVersion = null;
            foreach (var component in components.EnumerateArray())
            {
                var uid = component.TryGetProperty("uid", out var u) ? u.GetString() ?? "" : "";
                var version = component.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                if (uid.Equals("net.minecraft", StringComparison.OrdinalIgnoreCase)) gameVersion = version;
                else if (uid.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase)) (loader, loaderVersion) = ("fabric", version);
                else if (uid.Contains("quilt-loader", StringComparison.OrdinalIgnoreCase)) (loader, loaderVersion) = ("quilt", version);
                else if (uid.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) (loader, loaderVersion) = ("neoforge", version);
                else if (uid.Contains("forge", StringComparison.OrdinalIgnoreCase)) (loader, loaderVersion) = ("forge", version);
            }

            return (gameVersion, loader, loaderVersion);
        }
        catch
        {
            return ("", "vanilla", null);
        }
    }

    private static (string Name, string GameVersion, string Loader, string? LoaderVersion) ReadModrinthProfile(
        string path, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? fallback : fallback;
            var game = root.TryGetProperty("game_version", out var g) ? g.GetString() ?? "" : "";
            var loader = root.TryGetProperty("loader", out var l) ? l.GetString() ?? "vanilla" : "vanilla";
            string? loaderVersion = null;
            if (root.TryGetProperty("loader_version", out var lv))
                loaderVersion = lv.ValueKind == JsonValueKind.String ? lv.GetString() : null;
            return (name, game, loader, loaderVersion);
        }
        catch
        {
            return (fallback, "", "vanilla", null);
        }
    }

    private static (string Name, string GameVersion, string Loader, string? LoaderVersion) ReadXmclInstance(
        string path, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? fallback : fallback;
            string game = "", loader = "vanilla";
            string? loaderVersion = null;
            if (root.TryGetProperty("runtime", out var runtime))
            {
                if (runtime.TryGetProperty("minecraft", out var mc)) game = mc.GetString() ?? "";
                if (runtime.TryGetProperty("neoforge", out var neo)) (loader, loaderVersion) = ("neoforge", neo.GetString());
                else if (runtime.TryGetProperty("forge", out var forge)) (loader, loaderVersion) = ("forge", forge.GetString());
                else if (runtime.TryGetProperty("fabricLoader", out var fabric)) (loader, loaderVersion) = ("fabric", fabric.GetString());
                else if (runtime.TryGetProperty("quiltLoader", out var quilt)) (loader, loaderVersion) = ("quilt", quilt.GetString());
            }

            return (name, game, loader, loaderVersion);
        }
        catch
        {
            return (fallback, "", "vanilla", null);
        }
    }

    private static (string GameVersion, string Loader, string? LoaderVersion) ReadVersionJson(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            var gameVersion = root.TryGetProperty("inheritsFrom", out var inh) ? inh.GetString() ?? "" : id;
            var loader = "vanilla";
            string? loaderVersion = null;

            if (root.TryGetProperty("libraries", out var libraries))
            {
                foreach (var library in libraries.EnumerateArray())
                {
                    if (!library.TryGetProperty("name", out var nameNode)) continue;
                    var coords = nameNode.GetString() ?? "";
                    if (coords.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase))
                        (loader, loaderVersion) = ("fabric", coords["net.fabricmc:fabric-loader:".Length..].Split(':')[0]);
                    else if (coords.StartsWith("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase))
                        (loader, loaderVersion) = ("quilt", coords["org.quiltmc:quilt-loader:".Length..].Split(':')[0]);
                    else if (coords.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase))
                        (loader, loaderVersion) = ("neoforge", coords["net.neoforged:neoforge:".Length..].Split(':')[0]);
                    else if (coords.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase))
                        (loader, loaderVersion) = ("forge", coords["net.minecraftforge:forge:".Length..].Split(':')[0]);
                }
            }

            if (id.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
                (loader, gameVersion) = ("fabric", gameVersion);
            return (gameVersion, loader, loaderVersion);
        }
        catch
        {
            return ("", "vanilla", null);
        }
    }

    private static string? ReadIniValue(string path, string key)
    {
        try
        {
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) continue;
                var value = trimmed[(key.Length + 1)..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch { }

        return null;
    }

    private static string StableId(string path)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
