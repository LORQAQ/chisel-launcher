using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Chisel.Core.Minecraft;
using Chisel.Core.Models;
using Chisel.Core.Net;

namespace Chisel.Core.Services;

public sealed class VersionCatalogService
{
    private const string OfficialManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private readonly SemaphoreSlim _versionGate = new(1, 1);
    private readonly Dictionary<string, MinecraftVersion> _versionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoaderInspection> _loaderCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _forgePromoGate = new(1, 1);
    private Dictionary<string, string>? _forgePromos;
    private DateTimeOffset _forgePromosAt;
    private VersionManifest? _manifest;
    private DateTimeOffset _manifestAt;

    public VersionCatalogService(AppPaths paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public async Task<VersionManifest> GetManifestAsync(bool force, CancellationToken ct)
    {
        if (!force && _manifest is not null && DateTimeOffset.UtcNow - _manifestAt < TimeSpan.FromMinutes(10))
            return _manifest;

        await _fetchGate.WaitAsync(ct);
        try
        {
            if (!force && _manifest is not null && DateTimeOffset.UtcNow - _manifestAt < TimeSpan.FromMinutes(10))
                return _manifest;

            var cachePath = Path.Combine(_paths.CacheDir, "version_manifest_v2.json");
            var preferMirror = await SourceProbe.PreferMirrorAsync(_settings.Get().DownloadSource, ct);
            var urls = preferMirror
                ? new[] { Mirror.Manifest(), OfficialManifest }
                : new[] { OfficialManifest, Mirror.Manifest() };

            foreach (var url in urls)
            {
                try
                {
                    var text = await GetStringAsync(url, TimeSpan.FromSeconds(12), ct);
                    var manifest = JsonSerializer.Deserialize<VersionManifest>(text, AtomicJson.Options);
                    if (manifest?.Versions.Count > 0)
                    {
                        _manifest = manifest;
                        _manifestAt = DateTimeOffset.UtcNow;
                        await File.WriteAllTextAsync(cachePath, text, ct);
                        return manifest;
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch { }
            }

            if (File.Exists(cachePath))
            {
                var cached = AtomicJson.Read<VersionManifest>(cachePath, () => new VersionManifest());
                if (cached.Versions.Count > 0)
                {
                    _manifest = cached;
                    _manifestAt = DateTimeOffset.UtcNow.AddMinutes(-5);
                    return cached;
                }
            }

            throw new IOException("无法获取 Minecraft 版本列表，请检查网络后重试。");
        }
        finally { _fetchGate.Release(); }
    }

    public async Task<MinecraftVersion> GetVersionAsync(string id, CancellationToken ct)
    {
        id = (id ?? "").Trim();
        if (!AppPaths.IsSafeSegment(id)) throw new InvalidOperationException("版本名称不合法");
        if (_versionCache.TryGetValue(id, out var cachedVersion)) return cachedVersion;
        await _versionGate.WaitAsync(ct);
        try
        {
            if (_versionCache.TryGetValue(id, out cachedVersion)) return cachedVersion;

            var installedPath = Path.Combine(_paths.VersionDir(id), "version.json");
            if (File.Exists(installedPath))
            {
                var installed = AtomicJson.Read<MinecraftVersion>(installedPath, () => new MinecraftVersion());
                if (!string.IsNullOrEmpty(installed.Id))
                {
                    _versionCache[id] = installed;
                    return installed;
                }
            }

            var cachePath = Path.Combine(_paths.CacheDir, "versions", id + ".json");
            if (File.Exists(cachePath))
            {
                var cachedFile = AtomicJson.Read<MinecraftVersion>(cachePath, () => new MinecraftVersion());
                if (!string.IsNullOrEmpty(cachedFile.Id))
                {
                    _versionCache[id] = cachedFile;
                    return cachedFile;
                }
            }

            var manifest = await GetManifestAsync(false, ct);
            var entry = manifest.Versions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"找不到版本 {id}");

            var urls = new List<string>();
            var preferMirror = await SourceProbe.PreferMirrorAsync(_settings.Get().DownloadSource, ct);
            if (preferMirror) urls.Add(Mirror.VersionJson(id));
            urls.Add(entry.Url);
            if (!preferMirror) urls.Add(Mirror.VersionJson(id));

            Exception? last = null;
            foreach (var url in urls)
            {
                try
                {
                    var bytes = await GetBytesAsync(url, TimeSpan.FromSeconds(20), ct);
                    if (!string.IsNullOrEmpty(entry.Sha1))
                    {
                        var hash = Convert.ToHexString(SHA1.HashData(bytes));
                        if (!string.Equals(hash, entry.Sha1, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("版本描述文件校验失败");
                    }
                    var version = JsonSerializer.Deserialize<MinecraftVersion>(bytes, AtomicJson.Options)!;
                    if (string.IsNullOrEmpty(version.Id)) throw new IOException("版本描述为空");
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllBytesAsync(cachePath, bytes, ct);
                    _versionCache[id] = version;
                    return version;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex) { last = ex; }
            }

            throw new IOException($"无法获取版本 {id} 的描述：{last?.Message}", last);
        }
        finally { _versionGate.Release(); }
    }

    public async Task<VersionInspection> InspectAsync(string gameVersion, CancellationToken ct)
    {
        var version = await GetVersionAsync(gameVersion, ct);
        var libraryBytes = 0L;
        foreach (var lib in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;
            var artifact = LibraryUtil.ArtifactFor(lib);
            if (artifact != null) libraryBytes += Math.Max(0, artifact.Size);
            var native = LibraryUtil.NativeArtifactFor(lib);
            if (native != null) libraryBytes += Math.Max(0, native.Size);
        }

        var loaders = new List<LoaderInspection>();
        var fabricTask = GetLoaderInspectionSafeAsync(gameVersion, "fabric", ct);
        var quiltTask = GetLoaderInspectionSafeAsync(gameVersion, "quilt", ct);
        var forgeTask = GetLoaderInspectionSafeAsync(gameVersion, "forge", ct);
        var neoTask = GetLoaderInspectionSafeAsync(gameVersion, "neoforge", ct);
        await Task.WhenAll(fabricTask, quiltTask, forgeTask, neoTask);
        if (fabricTask.Result != null) loaders.Add(fabricTask.Result);
        if (quiltTask.Result != null) loaders.Add(quiltTask.Result);
        if (forgeTask.Result != null) loaders.Add(forgeTask.Result);
        if (neoTask.Result != null) loaders.Add(neoTask.Result);

        var client = version.Downloads != null && version.Downloads.TryGetValue("client", out var c) ? c.Size : 0;
        return new VersionInspection
        {
            GameVersion = version.Id,
            Type = version.Type,
            ReleaseTime = version.ReleaseTime,
            JavaMajor = version.JavaVersion?.MajorVersion ?? 8,
            AssetBytes = version.AssetIndex?.TotalSize ?? 0,
            ClientBytes = client,
            LibraryBytes = libraryBytes,
            TotalBytes = (version.AssetIndex?.TotalSize ?? 0) + client + libraryBytes,
            Loaders = loaders
        };
    }

    private async Task<LoaderInspection?> GetLoaderInspectionSafeAsync(string gameVersion, string loader, CancellationToken ct)
    {
        try { return await GetLoaderInspectionAsync(gameVersion, loader, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch { return null; }
    }

    public async Task<LoaderInspection> GetLoaderInspectionAsync(string gameVersion, string loader, CancellationToken ct)
    {
        var cacheKey = $"{loader}:{gameVersion}";
        if (_loaderCache.TryGetValue(cacheKey, out var cached)) return cached;

        var normalized = loader.ToLowerInvariant();
        LoaderInspection result;
        if (normalized == "forge")
        {
            result = await GetForgeInspectionAsync(gameVersion, ct);
        }
        else if (normalized == "neoforge")
        {
            result = await GetNeoForgeInspectionAsync(gameVersion, ct);
        }
        else
        {
            var isFabric = normalized == "fabric";
            var label = isFabric ? "Fabric" : "Quilt";
            var url = isFabric
                ? $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}"
                : $"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(gameVersion)}";

            List<LoaderVersionOption> options;
            if (isFabric)
            {
                var data = await Web.GetJsonAsync<List<FabricLoaderEntry>>(url, TimeSpan.FromSeconds(15), ct);
                options = data.Select(x => new LoaderVersionOption
                {
                    Version = x.Loader?.Version ?? "",
                    Stable = x.Loader?.Stable ?? false,
                    Build = x.Loader?.Build ?? 0,
                    GameVersion = gameVersion
                }).Where(x => x.Version.Length > 0).ToList();
            }
            else
            {
                var data = await Web.GetJsonAsync<List<QuiltLoaderEntry>>(url, TimeSpan.FromSeconds(15), ct);
                options = data.Select(x => new LoaderVersionOption
                {
                    Version = x.Loader?.Version ?? "",
                    Stable = x.Loader?.Stable ?? false,
                    Build = x.Loader?.Build ?? 0,
                    GameVersion = gameVersion
                }).Where(x => x.Version.Length > 0).ToList();
            }

            var stable = options.Where(x => x.Stable).ToList();
            List<LoaderVersionOption> selected;
            string? note = null;
            if (stable.Count > 0) selected = stable.Take(25).ToList();
            else
            {
                selected = options.Take(10).ToList();
                note = "该版本仅有测试版加载器";
            }

            if (options.Count == 0)
                return new LoaderInspection { Type = normalized, Label = label, Available = false, Note = "尚未支持这个游戏版本" };

            result = new LoaderInspection
            {
                Type = normalized,
                Label = label,
                Available = selected.Count > 0,
                Note = note,
                Versions = selected
            };
        }

        _loaderCache[cacheKey] = result;
        return result;
    }

    private async Task<LoaderInspection> GetForgeInspectionAsync(string gameVersion, CancellationToken ct)
    {
        var url = $"{Mirror.Host}/forge/minecraft/{Uri.EscapeDataString(gameVersion)}";
        List<ForgeBuild> builds;
        try
        {
            builds = await Web.GetJsonAsync<List<ForgeBuild>>(url, TimeSpan.FromSeconds(15), ct);
        }
        catch
        {
            return new LoaderInspection { Type = "forge", Label = "Forge", Available = false, Note = "没有找到这个版本的 Forge" };
        }
        if (builds.Count == 0)
            return new LoaderInspection { Type = "forge", Label = "Forge", Available = false, Note = "没有找到这个版本的 Forge" };

        var recommended = await GetForgeRecommendedAsync(gameVersion, ct);
        var options = builds
            .Where(b => !string.IsNullOrWhiteSpace(b.Version))
            .OrderByDescending(b => ParseVersion(b.Version))
            .Take(40)
            .Select(b => new LoaderVersionOption
            {
                Version = b.Version,
                Stable = string.Equals(b.Version, recommended, StringComparison.OrdinalIgnoreCase),
                Build = b.Build,
                GameVersion = gameVersion
            })
            .ToList();

        if (!options.Any(x => x.Stable) && options.Count > 0)
        {
            options[0] = new LoaderVersionOption
            {
                Version = options[0].Version,
                Stable = true,
                Build = options[0].Build,
                GameVersion = options[0].GameVersion
            };
        }
        return new LoaderInspection
        {
            Type = "forge",
            Label = "Forge",
            Available = true,
            Note = recommended == null ? "官方推荐版未知，默认取最新" : $"推荐 {recommended}",
            Versions = options
        };
    }

    private async Task<LoaderInspection> GetNeoForgeInspectionAsync(string gameVersion, CancellationToken ct)
    {
        var url = $"{Mirror.Host}/neoforge/list/{Uri.EscapeDataString(gameVersion)}";
        List<NeoForgeBuild> builds;
        try
        {
            builds = await Web.GetJsonAsync<List<NeoForgeBuild>>(url, TimeSpan.FromSeconds(15), ct);
        }
        catch
        {
            return new LoaderInspection { Type = "neoforge", Label = "NeoForge", Available = false, Note = "没有找到这个版本的 NeoForge" };
        }
        var options = builds
            .Where(b => !string.IsNullOrWhiteSpace(b.Version))
            .OrderByDescending(b => ParseVersion(b.Version))
            .Take(30)
            .Select((b, index) => new LoaderVersionOption
            {
                Version = b.Version,
                Stable = index == 0,
                Build = 0,
                GameVersion = gameVersion
            })
            .ToList();
        if (options.Count == 0)
            return new LoaderInspection { Type = "neoforge", Label = "NeoForge", Available = false, Note = "没有找到这个版本的 NeoForge" };
        return new LoaderInspection
        {
            Type = "neoforge",
            Label = "NeoForge",
            Available = true,
            Versions = options
        };
    }

    private async Task<string?> GetForgeRecommendedAsync(string gameVersion, CancellationToken ct)
    {
        try
        {
            await _forgePromoGate.WaitAsync(ct);
            try
            {
                if (_forgePromos == null || DateTimeOffset.UtcNow - _forgePromosAt > TimeSpan.FromMinutes(30))
                {
                    var text = await GetStringAsync(
                        "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json",
                        TimeSpan.FromSeconds(15), ct);
                    _forgePromos = JsonSerializer.Deserialize<ForgePromotions>(text, AtomicJson.Options)?.Promos ?? [];
                    _forgePromosAt = DateTimeOffset.UtcNow;
                }
            }
            finally { _forgePromoGate.Release(); }

            if (_forgePromos.TryGetValue(gameVersion + "-recommended", out var recommended)) return recommended;
            if (_forgePromos.TryGetValue(gameVersion + "-latest", out var latest)) return latest;
        }
        catch { }
        return null;
    }

    private static double ParseVersion(string version)
    {
        var parts = version.Split('.', '-', '+');
        var value = 0.0;
        var scale = 1.0;
        foreach (var part in parts.Take(4))
        {
            if (!int.TryParse(part, out var number)) number = 0;
            value += number * scale;
            scale /= 1000.0;
        }
        return value;
    }

    public async Task<MinecraftVersion> GetLoaderProfileAsync(string gameVersion, string loader, string loaderVersion, CancellationToken ct)
    {
        if (string.Equals(loader, "forge", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(loader, "neoforge", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("这个加载器需要运行官方安装器，不能直接获取描述。");
        var url = string.Equals(loader, "fabric", StringComparison.OrdinalIgnoreCase)
            ? $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json"
            : $"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
        var profile = await Web.GetJsonAsync<MinecraftVersion>(url, TimeSpan.FromSeconds(20), ct);
        if (string.IsNullOrEmpty(profile.Id)) throw new IOException("加载器返回的版本描述为空");
        return profile;
    }

    private static async Task<string> GetStringAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await Web.Client.GetStringAsync(url, cts.Token);
    }

    private static async Task<byte[]> GetBytesAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await Web.Client.GetByteArrayAsync(url, cts.Token);
    }
}

internal sealed class FabricLoaderEntry
{
    public FabricLoaderInfo? Loader { get; set; }
}

internal sealed class QuiltLoaderEntry
{
    public FabricLoaderInfo? Loader { get; set; }
}

internal sealed class FabricLoaderInfo
{
    public string? Version { get; set; }
    public bool? Stable { get; set; }
    public int? Build { get; set; }
}

internal sealed class ForgeBuild
{
    public string Version { get; set; } = "";
    public int Build { get; set; }
    public string McVersion { get; set; } = "";
}

internal sealed class NeoForgeBuild
{
    public string Version { get; set; } = "";
    public string McVersion { get; set; } = "";
    public string InstallerPath { get; set; } = "";
}

internal sealed class ForgePromotions
{
    public Dictionary<string, string> Promos { get; set; } = [];
}
