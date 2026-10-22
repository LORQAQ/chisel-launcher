using System.Text.Json;
using Chisel.Core.Models;
using Chisel.Core.Services;

namespace Chisel.Core.Minecraft;

public sealed class ResolvedVersion
{
    public required MinecraftVersion Base { get; init; }
    public MinecraftVersion? Loader { get; init; }
    public required List<Library> Libraries { get; init; }
    public required string VersionName { get; init; }
    public required string MainClass { get; init; }
    public required string ClientJarPath { get; init; }
    public required int RequiredJava { get; init; }
    public string? LogConfigPath { get; init; }
    public string? AssetIndexPath { get; init; }
}

public sealed class VersionResolver
{
    private readonly AppPaths _paths;
    private readonly VersionCatalogService _catalog;

    public VersionResolver(AppPaths paths, VersionCatalogService catalog)
    {
        _paths = paths;
        _catalog = catalog;
    }

    public async Task<ResolvedVersion> ResolveAsync(Instance instance, CancellationToken ct)
    {
        if (!AppPaths.IsSafeSegment(instance.GameVersion))
            throw new InvalidOperationException("实例的版本信息损坏，请重新安装这个实例。");
        var versionPath = Path.Combine(_paths.VersionDir(instance.GameVersion), "version.json");
        if (!File.Exists(versionPath))
            throw new InvalidOperationException("版本文件缺失，可能需要在版本库中重新安装。");
        var baseVersion = AtomicJson.Read<MinecraftVersion>(versionPath, () => new MinecraftVersion());
        if (string.IsNullOrEmpty(baseVersion.Id))
            baseVersion = await _catalog.GetVersionAsync(instance.GameVersion, ct);

        MinecraftVersion? loader = null;
        string versionName = baseVersion.Id;
        string mainClass = baseVersion.MainClass;
        string clientJarPath = Path.Combine(_paths.VersionDir(instance.GameVersion), "client.jar");
        List<Library> libraries;
        List<JsonElement> gameArgs = baseVersion.Arguments?.Game?.ToList() ?? [];
        List<JsonElement> jvmArgs = baseVersion.Arguments?.Jvm?.ToList() ?? [];

        if (!string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            var profileId = instance.ProfileId;
            if (string.IsNullOrEmpty(profileId))
                throw new InvalidOperationException("加载器信息缺失，请重新安装该实例。");
            if (!AppPaths.IsSafeSegment(profileId))
                throw new InvalidOperationException("加载器信息损坏，请重新安装该实例。");
            var profilePath = Path.Combine(_paths.LoaderDir(profileId), "profile.json");
            if (!File.Exists(profilePath))
                throw new InvalidOperationException("加载器文件缺失，请重新安装该实例。");
            loader = AtomicJson.Read<MinecraftVersion>(profilePath, () => new MinecraftVersion());
            if (string.IsNullOrEmpty(loader.MainClass))
                throw new InvalidOperationException("加载器描述损坏，请重新安装该实例。");
            versionName = string.IsNullOrEmpty(loader.Id) ? profileId : loader.Id;
            mainClass = loader.MainClass;
            var loaderJar = Path.Combine(_paths.LoaderDir(profileId), profileId + ".jar");
            if (File.Exists(loaderJar)) clientJarPath = loaderJar;
            libraries = LibraryUtil.Merge(baseVersion.Libraries, loader.Libraries);
            if (loader.Arguments?.Game != null) gameArgs.AddRange(loader.Arguments.Game);
            if (loader.Arguments?.Jvm != null) jvmArgs.AddRange(loader.Arguments.Jvm);
        }
        else
        {
            libraries = baseVersion.Libraries;
        }

        var requiredJava = baseVersion.JavaVersion?.MajorVersion ?? 8;
        string? logConfigPath = null;
        if (baseVersion.Logging?.Client?.File is { } logFile)
        {
            var name = string.IsNullOrEmpty(logFile.Path) ? logFile.Id ?? "client" : logFile.Path;
            name = Path.GetFileName(name);
            if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name += ".xml";
            logConfigPath = Path.Combine(_paths.AssetsDir, "log_configs", name);
        }

        string? assetIndexPath = null;
        if (baseVersion.AssetIndex != null)
            assetIndexPath = Path.Combine(_paths.AssetsDir, "indexes", Path.GetFileName(baseVersion.AssetIndex.Id) + ".json");

        return new ResolvedVersion
        {
            Base = baseVersion,
            Loader = loader,
            Libraries = libraries,
            VersionName = versionName,
            MainClass = mainClass,
            ClientJarPath = clientJarPath,
            RequiredJava = requiredJava,
            LogConfigPath = logConfigPath,
            AssetIndexPath = assetIndexPath
        };
    }

    public static (List<JsonElement> Game, List<JsonElement> Jvm) GetArguments(ResolvedVersion version)
    {
        var game = version.Base.Arguments?.Game?.ToList() ?? [];
        var jvm = version.Base.Arguments?.Jvm?.ToList() ?? [];
        if (version.Loader?.Arguments?.Game != null) game.AddRange(version.Loader.Arguments.Game);
        if (version.Loader?.Arguments?.Jvm != null) jvm.AddRange(version.Loader.Arguments.Jvm);
        return (game, jvm);
    }
}
