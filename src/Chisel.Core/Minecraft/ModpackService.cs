using System.IO.Compression;
using System.Text.Json;
using Chisel.Core.Models;
using Chisel.Core.Net;
using Chisel.Core.Services;

namespace Chisel.Core.Minecraft;

public sealed class ModpackPreview
{
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string GameVersion { get; init; } = "";
    public string Loader { get; init; } = "vanilla";
    public string? LoaderVersion { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public string? Summary { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed class ModpackService
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly CurseForgeService _curseForge;
    private readonly object _manifestGate = new();

    public ModpackService(AppPaths paths, SettingsService settings, CurseForgeService curseForge)
    {
        _paths = paths;
        _settings = settings;
        _curseForge = curseForge;
    }

    public ModpackPreview Inspect(string path)
    {
        ValidatePath(path);
        using var archive = ZipFile.OpenRead(path);
        var mrpack = archive.GetEntry("modrinth.index.json");
        if (mrpack != null)
            return InspectMrpack(path, mrpack);
        var cf = archive.GetEntry("manifest.json");
        if (cf != null)
            return InspectCurseForge(path, cf);
        throw new InvalidOperationException("这个压缩包里没有 modrinth.index.json 或 manifest.json，不是可识别的整合包。");
    }

    public async Task ApplyAsync(
        string path,
        Instance instance,
        DownloadProgress progress,
        Action<string, string> report,
        CancellationToken ct)
    {
        ValidatePath(path);
        using var archive = ZipFile.OpenRead(path);
        var mrpackEntry = archive.GetEntry("modrinth.index.json");
        var cfEntry = archive.GetEntry("manifest.json");
        var gameDir = _paths.GameDir(instance.Id);
        Directory.CreateDirectory(gameDir);

        var items = new List<DownloadItem>();
        var manifestItems = new List<ContentManifestItem>();
        var warnings = new List<string>();
        var overridePrefixes = new List<string>();
        var curseFiles = new List<(int ProjectId, int FileId, string ProjectType)>();

        if (mrpackEntry != null)
        {
            var index = ReadJson<MrpackIndex>(mrpackEntry)
                        ?? throw new InvalidOperationException("modrinth.index.json 无法解析");
            overridePrefixes.Add("overrides/");
            overridePrefixes.Add("client-overrides/");
            foreach (var file in index.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(file.Env?.Client, "unsupported", StringComparison.OrdinalIgnoreCase)) continue;
                var relative = NormalizeRelative(file.Path);
                if (relative == null || !IsAllowedPackPath(relative))
                {
                    warnings.Add($"跳过了不安全的路径：{file.Path}");
                    continue;
                }
                var url = file.Downloads.FirstOrDefault(x =>
                    Uri.TryCreate(x, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps &&
                    (uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.Equals("cdn-raw.modrinth.com", StringComparison.OrdinalIgnoreCase)));
                if (string.IsNullOrWhiteSpace(url))
                {
                    warnings.Add($"「{Path.GetFileName(relative)}」没有可用的下载地址，已跳过。");
                    continue;
                }
                var destination = SafeUnder(gameDir, relative);
                var sha1 = file.Hashes?.Sha1 ?? "";
                items.Add(new DownloadItem
                {
                    Url = url,
                    Destination = destination,
                    Sha1 = sha1,
                    Size = file.FileSize,
                    Label = Path.GetFileName(relative)
                });
                RegisterManifestItem(manifestItems, instance, relative, sha1, index.Name, index.VersionId);
            }
        }
        else if (cfEntry != null)
        {
            var manifest = ReadJson<CurseForgeManifest>(cfEntry)
                           ?? throw new InvalidOperationException("manifest.json 无法解析");
            if (!string.IsNullOrWhiteSpace(manifest.Overrides))
                overridePrefixes.Add(manifest.Overrides.TrimEnd('/') + "/");
            overridePrefixes.Add("overrides/");
            foreach (var file in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                curseFiles.Add((file.ProjectId, file.FileId, "mod"));
            }
            foreach (var file in manifest.Files)
            {
                // Resolution happens below with bounded parallelism.
            }
        }
        else
        {
            throw new InvalidOperationException("这个压缩包里没有可识别的整合包清单。");
        }

        if (curseFiles.Count > 0)
        {
            report("正在核对整合包内容", "查询 CurseForge 文件");
            var gate = new SemaphoreSlim(4);
            var resolved = new List<CurseForgeFileInfo>();
            var warningsLock = new object();
            await Task.WhenAll(curseFiles.Select(async entry =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var file = await _curseForge.ResolveFileAsync(entry.ProjectId, entry.FileId,
                        instance.GameVersion, instance.Loader, entry.ProjectType, ct);
                    lock (resolved) resolved.Add(file);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lock (warningsLock) warnings.Add($"有一份 CurseForge 文件没有取到：{Web.Truncate(ex.Message, 140)}");
                }
                finally { gate.Release(); }
            }));

            _curseForge.ClearCache();
            foreach (var file in resolved)
            {
                var relative = Path.Combine("mods", file.Name);
                var destination = SafeUnder(gameDir, relative);
                items.Add(new DownloadItem
                {
                    Url = file.Url,
                    Destination = destination,
                    Size = file.Size,
                    Label = file.Name
                });
                manifestItems.Add(new ContentManifestItem
                {
                    ProjectId = "cf-" + file.ProjectId,
                    VersionId = file.FileId.ToString(),
                    ProjectType = "mod",
                    Title = file.Title,
                    FileName = file.Name,
                    IconUrl = file.IconUrl,
                    VersionNumber = file.VersionNumber,
                    Enabled = true,
                    InstalledAt = DateTimeOffset.Now
                });
            }
        }

        report("正在铺开整合包", "解压 overrides");
        ExtractOverrides(archive, gameDir, overridePrefixes, ct);

        if (items.Count > 0)
        {
            foreach (var item in items) progress.Plan(Math.Max(0, item.Size), 1);
            report("正在下载整合包内容", $"共 {items.Count} 个文件");
            var downloader = new Downloader(_settings.Snapshot().DownloadSource);
            await downloader.DownloadAllAsync(items, _settings.Snapshot().DownloadConcurrency, progress,
                label => report("正在下载整合包内容", label), ct);
        }

        if (manifestItems.Count > 0)
        {
            lock (ContentManifestLock.Gate)
            {
                var manifestPath = Path.Combine(gameDir, ".chisel-content.json");
                var manifest = AtomicJson.Read(manifestPath, () => new ContentManifest());
                foreach (var item in manifestItems)
                {
                    manifest.Items.RemoveAll(existing =>
                        string.Equals(existing.ProjectId, item.ProjectId, StringComparison.OrdinalIgnoreCase));
                    manifest.Items.Add(item);
                }
                AtomicJson.Write(manifestPath, manifest);
            }
        }

        report("整合包已经铺开", warnings.Count == 0 ? "文件与配置都已就位" : $"完成，有 {warnings.Count} 条提醒");
    }

    private ModpackPreview InspectMrpack(string path, ZipArchiveEntry entry)
    {
        var index = ReadJson<MrpackIndex>(entry) ?? throw new InvalidOperationException("modrinth.index.json 无法解析");
        var deps = index.Dependencies;
        var gameVersion = deps.TryGetValue("minecraft", out var mc) ? mc : "";
        var (loader, loaderVersion) = DetectLoader(deps);
        if (!AppPaths.IsSafeSegment(gameVersion))
            throw new InvalidOperationException("整合包里的游戏版本不合法。");
        if (loader != "vanilla" && !AppPaths.IsSafeSegment(loaderVersion))
            throw new InvalidOperationException("整合包里的加载器版本不合法。");
        var warnings = new List<string>();
        var unsupported = deps.Keys.FirstOrDefault(key =>
            key is not ("minecraft" or "fabric-loader" or "quilt-loader" or "forge" or "neoforge"));
        if (unsupported != null)
            warnings.Add($"整合包使用了暂不支持的加载器 {unsupported}，实例会按原版安装。");
        return new ModpackPreview
        {
            Path = path,
            Kind = "mrpack",
            Name = string.IsNullOrWhiteSpace(index.Name) ? Path.GetFileNameWithoutExtension(path) : index.Name,
            Version = index.VersionId,
            GameVersion = gameVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            FileCount = index.Files.Count,
            TotalBytes = index.Files.Sum(f => Math.Max(0, f.FileSize)),
            Summary = index.Summary,
            Warnings = warnings
        };
    }

    private ModpackPreview InspectCurseForge(string path, ZipArchiveEntry entry)
    {
        var manifest = ReadJson<CurseForgeManifest>(entry) ?? throw new InvalidOperationException("manifest.json 无法解析");
        var gameVersion = manifest.Minecraft?.Version ?? "";
        if (!AppPaths.IsSafeSegment(gameVersion))
            throw new InvalidOperationException("整合包里的游戏版本不合法。");
        var loader = "vanilla";
        string? loaderVersion = null;
        var raw = manifest.Minecraft?.ModLoaders?.FirstOrDefault(x => x.Primary)?.Id
                  ?? manifest.Minecraft?.ModLoaders?.FirstOrDefault()?.Id;
        if (!string.IsNullOrWhiteSpace(raw))
            (loader, loaderVersion) = ParseCurseLoader(raw, gameVersion);
        if (loader != "vanilla" && !AppPaths.IsSafeSegment(loaderVersion))
            throw new InvalidOperationException("整合包里的加载器版本不合法。");
        return new ModpackPreview
        {
            Path = path,
            Kind = "curseforge",
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(path) : manifest.Name,
            Version = manifest.Version,
            GameVersion = gameVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            FileCount = manifest.Files.Count,
            TotalBytes = 0,
            Warnings = ["CurseForge 文件体积需要逐个查询，导入时会显示实际进度。"]
        };
    }

    private static readonly string[] LocalCopyDirs =
        ["mods", "config", "defaultconfigs", "scripts", "kubejs", "resourcepacks", "shaderpacks", "saves"];

    private static readonly string[] LocalCopyFiles = ["options.txt", "servers.dat"];

    /// <summary>
    /// 把别的启动器的一个实例搬进新建的实例目录：只带内容与存档，不带日志、缓存和崩溃报告。
    /// </summary>
    public async Task CopyLocalAsync(
        string sourceGameDir,
        Instance instance,
        DownloadProgress progress,
        Action<string, string> report,
        CancellationToken ct)
    {
        if (!Directory.Exists(sourceGameDir)) throw new DirectoryNotFoundException("来源实例目录不存在");

        var gameDir = _paths.GameDir(instance.Id);
        Directory.CreateDirectory(gameDir);
        report("正在复制本地实例", "统计文件");

        var files = new List<(string From, long Size)>();
        foreach (var name in LocalCopyDirs)
        {
            var dir = Path.Combine(sourceGameDir, name);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                try { files.Add((file, new FileInfo(file).Length)); }
                catch { }
            }
        }

        foreach (var name in LocalCopyFiles)
        {
            var file = Path.Combine(sourceGameDir, name);
            if (File.Exists(file)) files.Add((file, new FileInfo(file).Length));
        }

        progress.Plan(files.Sum(f => f.Size), files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (from, size) = files[i];
            var relative = Path.GetRelativePath(sourceGameDir, from);
            var destination = SafeUnder(gameDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
            await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
                await input.CopyToAsync(output, 131072, ct);

            progress.Complete(new DownloadItem { Url = "", Destination = destination, Size = size });
            if (i % 20 == 0) report("正在复制本地实例", Path.GetFileName(from));
        }

        report("本地实例已就位", $"{files.Count} 个文件");
    }

    private static (string Loader, string? Version) DetectLoader(Dictionary<string, string> deps)
    {
        if (deps.TryGetValue("fabric-loader", out var fabric)) return ("fabric", fabric);
        if (deps.TryGetValue("quilt-loader", out var quilt)) return ("quilt", quilt);
        if (deps.TryGetValue("neoforge", out var neo)) return ("neoforge", neo);
        if (deps.TryGetValue("forge", out var forge)) return ("forge", forge);
        return ("vanilla", null);
    }

    private static (string Loader, string? Version) ParseCurseLoader(string raw, string gameVersion)
    {
        var parts = raw.Split('-', 2);
        if (parts.Length < 2) return ("vanilla", null);
        var loader = parts[0].ToLowerInvariant() switch
        {
            "forge" => "forge",
            "neoforge" => "neoforge",
            "fabric" => "fabric",
            "quilt" => "quilt",
            _ => "vanilla"
        };
        var version = parts[1];
        if (!string.Equals(loader, "vanilla", StringComparison.Ordinal) &&
            version.StartsWith(gameVersion + "-", StringComparison.OrdinalIgnoreCase))
            version = version[(gameVersion.Length + 1)..];
        return (loader, version);
    }

    private static T? ReadJson<T>(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<T>(stream, AtomicJson.Options);
        }
        catch
        {
            return default;
        }
    }

    private static void ExtractOverrides(ZipArchive archive, string gameDir, List<string> prefixes, CancellationToken ct)
    {
        var root = Path.GetFullPath(gameDir) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var normalized = entry.FullName.Replace('\\', '/');
            var prefix = prefixes.FirstOrDefault(p => normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (prefix == null) continue;
            var relative = NormalizeRelative(normalized[prefix.Length..]);
            if (relative == null) continue;
            var destination = Path.GetFullPath(Path.Combine(gameDir, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try { entry.ExtractToFile(destination, overwrite: true); } catch { }
        }
    }

    private static void RegisterManifestItem(
        List<ContentManifestItem> items,
        Instance instance,
        string relative,
        string sha1,
        string packName,
        string packVersion)
    {
        var normalized = relative.Replace('\\', '/');
        string type;
        if (normalized.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)) type = "mod";
        else if (normalized.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase)) type = "resourcepack";
        else if (normalized.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase)) type = "shader";
        else return;
        var name = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(name)) return;
        items.Add(new ContentManifestItem
        {
            ProjectId = "pack-" + (sha1.Length >= 12 ? sha1[..12] : Guid.NewGuid().ToString("N")[..12]),
            VersionId = sha1,
            ProjectType = type,
            Title = name,
            FileName = name,
            VersionNumber = string.IsNullOrWhiteSpace(packVersion) ? packName : packVersion,
            Enabled = true,
            InstalledAt = DateTimeOffset.Now
        });
    }

    private static string? NormalizeRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("../", StringComparison.Ordinal) || normalized.Contains("/..", StringComparison.Ordinal))
            return null;
        if (normalized.StartsWith(".chisel", StringComparison.OrdinalIgnoreCase)) return null;
        var full = normalized.Replace('/', Path.DirectorySeparatorChar);
        return full;
    }

    private static bool IsAllowedPackPath(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (normalized.Length == 0 || Path.IsPathRooted(normalized)) return false;
        return true;
    }

    private static string SafeUnder(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("整合包文件路径越界");
        return full;
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new ArgumentException("整合包文件不存在");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".mrpack" and not ".zip")
            throw new ArgumentException("只支持 .mrpack 或 CurseForge 导出的 .zip 整合包");
        if (new FileInfo(path).Length > 4L * 1024 * 1024 * 1024)
            throw new ArgumentException("整合包文件过大");
    }

    private sealed class MrpackIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("versionId")] public string VersionId { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("summary")] public string? Summary { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("files")] public List<MrpackFile> Files { get; set; } = [];
        [System.Text.Json.Serialization.JsonPropertyName("dependencies")] public Dictionary<string, string> Dependencies { get; set; } = [];
    }

    private sealed class MrpackFile
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")] public string Path { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("hashes")] public MrpackHashes? Hashes { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("env")] public MrpackEnv? Env { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("downloads")] public List<string> Downloads { get; set; } = [];
        [System.Text.Json.Serialization.JsonPropertyName("fileSize")] public long FileSize { get; set; }
    }

    private sealed class MrpackHashes
    {
        [System.Text.Json.Serialization.JsonPropertyName("sha1")] public string Sha1 { get; set; } = "";
    }

    private sealed class MrpackEnv
    {
        [System.Text.Json.Serialization.JsonPropertyName("client")] public string? Client { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("server")] public string? Server { get; set; }
    }

    private sealed class CurseForgeManifest
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("version")] public string Version { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("overrides")] public string? Overrides { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("minecraft")] public CurseMinecraft? Minecraft { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("files")] public List<CurseManifestFile> Files { get; set; } = [];
    }

    private sealed class CurseMinecraft
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")] public string Version { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("modLoaders")] public List<CurseLoader>? ModLoaders { get; set; }
    }

    private sealed class CurseLoader
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("primary")] public bool Primary { get; set; }
    }

    private sealed class CurseManifestFile
    {
        [System.Text.Json.Serialization.JsonPropertyName("projectID")] public int ProjectId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("fileID")] public int FileId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("required")] public bool Required { get; set; }
    }
}
