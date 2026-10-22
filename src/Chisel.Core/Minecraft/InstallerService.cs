using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Chisel.Core.Models;
using Chisel.Core.Net;
using Chisel.Core.Services;

namespace Chisel.Core.Minecraft;

public sealed class InstallerService
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly VersionCatalogService _catalog;
    private readonly InstanceStore _instances;
    private readonly JavaManager _java;
    private readonly LoaderInstallerService _loaderInstaller;
    private readonly ModpackService _modpacks;

    public InstallerService(
        AppPaths paths,
        SettingsService settings,
        VersionCatalogService catalog,
        InstanceStore instances,
        JavaManager java,
        LoaderInstallerService loaderInstaller,
        ModpackService modpacks)
    {
        _paths = paths;
        _settings = settings;
        _catalog = catalog;
        _instances = instances;
        _java = java;
        _loaderInstaller = loaderInstaller;
        _modpacks = modpacks;
    }

    public async Task<Instance> InstallAsync(
        InstallRequest request,
        string jobId,
        Action<InstallProgress> onProgress,
        CancellationToken ct)
    {
        var settings = _settings.Snapshot();
        var progress = new DownloadProgress();
        void Report(string stage, string label, string? item = null, double speed = 0, bool done = false, string? resultInstanceId = null)
        {
            onProgress(new InstallProgress
            {
                JobId = jobId,
                Stage = stage,
                StageLabel = label,
                CompletedFiles = progress.CompletedFiles,
                TotalFiles = progress.TotalFiles,
                CompletedBytes = progress.CompletedBytes,
                TotalBytes = progress.TotalBytes,
                CurrentItem = item,
                BytesPerSecond = speed,
                Done = done,
                InstanceId = resultInstanceId
            });
        }

        Report("prepare", "正在核对版本信息");
        var gameVersion = request.GameVersion.Trim();
        var loader = string.Equals(request.Loader, "fabric", StringComparison.OrdinalIgnoreCase) ? "fabric"
            : string.Equals(request.Loader, "quilt", StringComparison.OrdinalIgnoreCase) ? "quilt"
            : string.Equals(request.Loader, "forge", StringComparison.OrdinalIgnoreCase) ? "forge"
            : string.Equals(request.Loader, "neoforge", StringComparison.OrdinalIgnoreCase) ? "neoforge"
            : "vanilla";
        var loaderVersion = request.LoaderVersion?.Trim();
        var externalLoader = loader is "forge" or "neoforge";

        var baseVersion = await _catalog.GetVersionAsync(gameVersion, ct);
        MinecraftVersion? loaderProfile = null;
        if (loader != "vanilla")
        {
            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                var inspection = await _catalog.GetLoaderInspectionAsync(gameVersion, loader, ct);
                loaderVersion = inspection.Versions.FirstOrDefault(x => x.Stable)?.Version
                                ?? inspection.Versions.FirstOrDefault()?.Version
                                ?? throw new InvalidOperationException($"{loader} 尚未支持这个游戏版本");
            }
            if (!externalLoader)
            {
                try
                {
                    loaderProfile = await _catalog.GetLoaderProfileAsync(gameVersion, loader, loaderVersion, ct);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"无法获取 {loader} 安装描述：{ex.Message}", ex);
                }
            }
        }

        if (loader != "vanilla" && !string.IsNullOrWhiteSpace(loaderVersion) && !AppPaths.IsSafeSegment(loaderVersion))
            throw new InvalidOperationException("加载器版本不合法");

        var instanceId = _instances.CreateId(gameVersion, loader);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? loader == "vanilla" ? gameVersion : $"{gameVersion} · {LoaderLabel(loader)}"
            : request.DisplayName.Trim();
        if (displayName.Length > 40) displayName = displayName[..40];

        var profileId = loaderProfile?.Id;
        if (!string.IsNullOrEmpty(profileId)) profileId = SafeSegment(profileId);
        var instanceDir = _paths.InstanceDir(instanceId);
        var gameDir = _paths.GameDir(instanceId);
        var nativesDir = Path.Combine(instanceDir, "natives");
        var created = false;

        try
        {
            Directory.CreateDirectory(instanceDir);
            created = true;
            Directory.CreateDirectory(gameDir);
            foreach (var sub in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "logs" })
                Directory.CreateDirectory(Path.Combine(gameDir, sub));
            Directory.CreateDirectory(nativesDir);

            var versionDir = _paths.VersionDir(gameVersion);
            Directory.CreateDirectory(versionDir);

            var libraries = LibraryUtil.Merge(baseVersion.Libraries, loaderProfile?.Libraries);
            var downloadSource = new Downloader(settings.DownloadSource);
            var libraryItems = new List<DownloadItem>();
            var assetItems = new List<DownloadItem>();
            var nativeExtractions = new List<(string Archive, List<string>? Exclude)>();
            var seenDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddItem(DownloadItem item, bool assets = false)
            {
                if (!seenDestinations.Add(item.Destination)) return;
                progress.Plan(Math.Max(0, item.Size), 1);
                if (assets) assetItems.Add(item);
                else libraryItems.Add(item);
            }

            var clientFile = baseVersion.Downloads != null && baseVersion.Downloads.TryGetValue("client", out var cf)
                ? cf : throw new InvalidOperationException("版本描述中缺少客户端文件");
            AddItem(new DownloadItem
            {
                Url = clientFile.Url,
                MirrorUrl = Mirror.ClientJar(gameVersion),
                Destination = Path.Combine(versionDir, "client.jar"),
                Sha1 = clientFile.Sha1,
                Size = clientFile.Size,
                Label = "Minecraft 客户端"
            });

            foreach (var library in libraries)
            {
                ct.ThrowIfCancellationRequested();
                if (!RuleEvaluator.IsAllowed(library.Rules)) continue;

                var artifact = LibraryUtil.ArtifactFor(library);
                if (artifact != null && !string.IsNullOrWhiteSpace(artifact.Url))
                {
                    var relative = string.IsNullOrWhiteSpace(artifact.Path) ? LibraryUtil.MavenPath(library.Name) : artifact.Path!;
                    AddItem(new DownloadItem
                    {
                        Url = artifact.Url,
                        MirrorUrl = Mirror.ForMojangAsset(artifact.Url),
                        Destination = SafeUnder(_paths.LibrariesDir, relative),
                        Sha1 = artifact.Sha1,
                        Size = artifact.Size,
                        Label = ShortName(library.Name)
                    });
                }

                var native = LibraryUtil.NativeArtifactFor(library);
                if (native != null && !string.IsNullOrWhiteSpace(native.Url))
                {
                    var relative = string.IsNullOrWhiteSpace(native.Path)
                        ? LibraryUtil.MavenPath(library.Name + ":natives")
                        : native.Path!;
                    var nativeDest = SafeUnder(_paths.LibrariesDir, relative);
                    AddItem(new DownloadItem
                    {
                        Url = native.Url,
                        MirrorUrl = Mirror.ForMojangAsset(native.Url),
                        Destination = nativeDest,
                        Sha1 = native.Sha1,
                        Size = native.Size,
                        Label = ShortName(library.Name) + " · 原生库"
                    });
                    nativeExtractions.Add((nativeDest, library.Extract?.Exclude));
                }
            }

            if (baseVersion.Logging?.Client?.File is { } logFile && !string.IsNullOrWhiteSpace(logFile.Url))
            {
                var logName = Path.GetFileName(string.IsNullOrEmpty(logFile.Path) ? logFile.Id ?? "client" : logFile.Path);
                if (!logName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) logName += ".xml";
                AddItem(new DownloadItem
                {
                    Url = logFile.Url,
                    MirrorUrl = Mirror.ForMojangAsset(logFile.Url),
                    Destination = Path.Combine(_paths.AssetsDir, "log_configs", logName),
                    Sha1 = logFile.Sha1,
                    Size = logFile.Size,
                    Label = "日志配置"
                });
            }

            AssetIndexFile? assetIndexFile = null;
            if (baseVersion.AssetIndex != null)
            {
                var indexBytes = await FetchAssetIndexAsync(baseVersion.AssetIndex, ct);
                var index = JsonSerializer.Deserialize<AssetIndexFile>(indexBytes, AtomicJson.Compact)
                            ?? throw new InvalidOperationException("资源索引损坏");
                assetIndexFile = index;
                foreach (var (key, obj) in index.Objects)
                {
                    ct.ThrowIfCancellationRequested();
                    if (obj.Hash.Length < 4) continue;
                    var prefix = obj.Hash[..2];
                    AddItem(new DownloadItem
                    {
                        Url = $"https://resources.download.minecraft.net/{prefix}/{obj.Hash}",
                        MirrorUrl = $"{Mirror.Host}/assets/{prefix}/{obj.Hash}",
                        Destination = Path.Combine(_paths.AssetsDir, "objects", prefix, obj.Hash),
                        Sha1 = obj.Hash,
                        Size = obj.Size,
                        VerifyExisting = false,
                        Label = key
                    }, assets: true);
                }
            }

            Report("libraries", "下载运行库", "准备运行环境");
            string? current = null;
            using var tickerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var ticker = Task.Run(async () =>
            {
                while (!tickerCts.IsCancellationRequested)
                {
                    try
                    {
                        var label = Volatile.Read(ref current);
                        if (!string.IsNullOrEmpty(label))
                            Report("libraries", "下载运行库", label, progress.BytesPerSecond);
                        await Task.Delay(180, tickerCts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }, CancellationToken.None);

            try
            {
                await downloadSource.DownloadAllAsync(libraryItems, settings.DownloadConcurrency, progress,
                    label => Volatile.Write(ref current, label), ct);
            }
            finally
            {
                tickerCts.Cancel();
                try { await ticker; } catch { }
            }

            Report("assets", "下载资源文件", "准备素材索引");
            using var assetTickerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var assetTicker = Task.Run(async () =>
            {
                while (!assetTickerCts.IsCancellationRequested)
                {
                    try
                    {
                        var label = Volatile.Read(ref current);
                        Report("assets", "下载资源文件", label, progress.BytesPerSecond);
                        await Task.Delay(180, assetTickerCts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }, CancellationToken.None);
            try
            {
                await downloadSource.DownloadAllAsync(assetItems, settings.DownloadConcurrency, progress,
                    label => Volatile.Write(ref current, label), ct);
            }
            finally
            {
                assetTickerCts.Cancel();
                try { await assetTicker; } catch { }
            }

            if (assetIndexFile != null && (assetIndexFile.Virtual || assetIndexFile.MapToResources))
            {
                Report("finish", "整理旧版资源", "正在建立旧版资源目录");
                var targetRoot = assetIndexFile.MapToResources
                    ? Path.Combine(gameDir, "resources")
                    : Path.Combine(_paths.AssetsDir, "virtual", SafeSegment(baseVersion.AssetIndex?.Id ?? "legacy"));
                MaterializeLegacyAssets(assetIndexFile, targetRoot, ct);
            }

            Report("finish", "整理实例", "解压原生库");
            foreach (var (archive, exclude) in nativeExtractions)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(archive))
                    ExtractNatives(archive, nativesDir, exclude);
            }

            if (externalLoader && !string.IsNullOrWhiteSpace(loaderVersion))
            {
                Report("loader", $"正在安装 {LoaderLabel(loader)}", loaderVersion);
                var result = loader == "forge"
                    ? await _loaderInstaller.InstallForgeAsync(gameVersion, loaderVersion!,
                        downloadSource, progress, label => Volatile.Write(ref current, label), ct,
                        useMirror: false)
                    : await _loaderInstaller.InstallNeoForgeAsync(gameVersion, loaderVersion!,
                        downloadSource, progress, label => Volatile.Write(ref current, label), ct,
                        useMirror: settings.DownloadSource != "official");
                loaderProfile = result.Profile;
                profileId = result.ProfileId;
                loaderVersion = result.LoaderVersion;

                Report("loader", $"正在补齐 {LoaderLabel(loader)} 运行库", "核对安装器留下的文件");
                var loaderItems = new List<DownloadItem>();
                var loaderNatives = new List<(string Archive, List<string>? Exclude)>();
                void AddLoaderItem(DownloadItem item)
                {
                    if (!seenDestinations.Add(item.Destination)) return;
                    progress.Plan(Math.Max(0, item.Size), 1);
                    loaderItems.Add(item);
                }

                foreach (var library in loaderProfile.Libraries)
                {
                    if (!RuleEvaluator.IsAllowed(library.Rules)) continue;
                    var artifact = LibraryUtil.ArtifactFor(library);
                    if (artifact != null && !string.IsNullOrWhiteSpace(artifact.Url))
                    {
                        var relative = string.IsNullOrWhiteSpace(artifact.Path) ? LibraryUtil.MavenPath(library.Name) : artifact.Path!;
                        AddLoaderItem(new DownloadItem
                        {
                            Url = artifact.Url,
                            MirrorUrl = Mirror.ForMojangAsset(artifact.Url),
                            Destination = SafeUnder(_paths.LibrariesDir, relative),
                            Sha1 = artifact.Sha1,
                            Size = artifact.Size,
                            Label = ShortName(library.Name)
                        });
                    }
                    var native = LibraryUtil.NativeArtifactFor(library);
                    if (native != null && !string.IsNullOrWhiteSpace(native.Url))
                    {
                        var relative = string.IsNullOrWhiteSpace(native.Path)
                            ? LibraryUtil.MavenPath(library.Name + ":natives")
                            : native.Path!;
                        var nativeDest = SafeUnder(_paths.LibrariesDir, relative);
                        AddLoaderItem(new DownloadItem
                        {
                            Url = native.Url,
                            MirrorUrl = Mirror.ForMojangAsset(native.Url),
                            Destination = nativeDest,
                            Sha1 = native.Sha1,
                            Size = native.Size,
                            Label = ShortName(library.Name) + " · 原生库"
                        });
                        loaderNatives.Add((nativeDest, library.Extract?.Exclude));
                    }
                }

                if (loaderItems.Count > 0)
                {
                    using var loaderTickerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var loaderTicker = Task.Run(async () =>
                    {
                        while (!loaderTickerCts.IsCancellationRequested)
                        {
                            try
                            {
                                var label = Volatile.Read(ref current);
                                if (!string.IsNullOrEmpty(label))
                                    Report("loader", $"正在补齐 {LoaderLabel(loader)} 运行库", label, progress.BytesPerSecond);
                                await Task.Delay(180, loaderTickerCts.Token);
                            }
                            catch (OperationCanceledException) { break; }
                        }
                    }, CancellationToken.None);
                    try
                    {
                        await downloadSource.DownloadAllAsync(loaderItems, settings.DownloadConcurrency, progress,
                            label => Volatile.Write(ref current, label), ct);
                    }
                    finally
                    {
                        loaderTickerCts.Cancel();
                        try { await loaderTicker; } catch { }
                    }
                }

                foreach (var (archive, exclude) in loaderNatives)
                {
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(archive)) ExtractNatives(archive, nativesDir, exclude);
                }
            }

            if (loaderProfile != null && !string.IsNullOrEmpty(profileId))
            {
                var loaderDir = _paths.LoaderDir(profileId);
                Directory.CreateDirectory(loaderDir);
                AtomicJson.Write(Path.Combine(loaderDir, "profile.json"), loaderProfile);
            }
            AtomicJson.Write(Path.Combine(versionDir, "version.json"), baseVersion);

            var instance = new Instance
            {
                Id = instanceId,
                DisplayName = displayName,
                GameVersion = gameVersion,
                Loader = loader,
                LoaderVersion = loaderVersion,
                ProfileId = profileId,
                CreatedAt = DateTimeOffset.Now,
                RequiredJava = baseVersion.JavaVersion?.MajorVersion ?? 8
            };

            if (!string.IsNullOrWhiteSpace(request.ModpackPath))
            {
                Report("modpack", "正在铺开整合包", "读取文件清单");
                await _modpacks.ApplyAsync(request.ModpackPath!, instance, progress,
                    (stage, label) => Report("modpack", stage, label, progress.BytesPerSecond), ct);
            }

            _instances.Save(instance);
            Report("done", "准备完成", null, 0, done: true, resultInstanceId: instanceId);
            return instance;
        }
        catch
        {
            try
            {
                if (created && Directory.Exists(instanceDir))
                    Directory.Delete(instanceDir, recursive: true);
            }
            catch { }
            throw;
        }
    }

    private async Task<byte[]> FetchAssetIndexAsync(AssetIndex assetIndex, CancellationToken ct)
    {
        var destination = Path.Combine(_paths.AssetsDir, "indexes", Path.GetFileName(assetIndex.Id) + ".json");
        if (File.Exists(destination))
        {
            var existing = await File.ReadAllBytesAsync(destination, ct);
            if (string.IsNullOrEmpty(assetIndex.Sha1) || HashMatches(existing, assetIndex.Sha1)) return existing;
        }

        var urls = new List<string>();
        var mirror = Mirror.ForMojangAsset(assetIndex.Url);
        var preferMirror = await SourceProbe.PreferMirrorAsync(_settings.Get().DownloadSource, ct);
        if (preferMirror && mirror != null) urls.Add(mirror);
        urls.Add(assetIndex.Url);
        if (mirror != null && !urls.Contains(mirror, StringComparer.OrdinalIgnoreCase)) urls.Add(mirror);

        Exception? last = null;
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));
                byte[] bytes;
                try
                {
                    bytes = await Web.Client.GetByteArrayAsync(url, cts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("获取资源索引超时");
                }
                if (!string.IsNullOrEmpty(assetIndex.Sha1) && !HashMatches(bytes, assetIndex.Sha1))
                    throw new IOException("资源索引校验失败");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, bytes, ct);
                return bytes;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { last = ex; }
        }
        throw new IOException($"无法获取资源索引：{last?.Message}", last);
    }

    private void MaterializeLegacyAssets(AssetIndexFile index, string targetRoot, CancellationToken ct)
    {
        foreach (var (key, obj) in index.Objects)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key) || obj.Hash.Length < 4) continue;
            var source = Path.Combine(_paths.AssetsDir, "objects", obj.Hash[..2], obj.Hash);
            if (!File.Exists(source)) continue;
            var destination = SafeUnder(targetRoot, key);
            var existing = new FileInfo(destination);
            if (existing.Exists && existing.Length == obj.Size) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static bool HashMatches(byte[] data, string sha1)
        => string.Equals(Convert.ToHexString(SHA1.HashData(data)), sha1, StringComparison.OrdinalIgnoreCase);

    private static string SafeUnder(string root, string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("文件路径越界");
        return full;
    }

    private static string ShortName(string maven)
    {
        var parts = maven.Split(':');
        return parts.Length >= 2 ? $"{parts[1]} {parts[2]}" : maven;
    }

    private static string LoaderLabel(string loader) => loader switch
    {
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "optifine" => "OptiFine",
        _ => "原版"
    };

    private static string SafeSegment(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray();
        return new string(chars).Trim('-', '.');
    }

    private static void ExtractNatives(string archivePath, string targetDir, List<string>? exclude)
    {
        Directory.CreateDirectory(targetDir);
        using var archive = ZipFile.OpenRead(archivePath);
        var root = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (exclude != null && exclude.Any(e => entry.FullName.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
                continue;
            var destination = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }
}
