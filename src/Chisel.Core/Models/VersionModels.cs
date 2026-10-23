using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chisel.Core.Models;

public sealed class VersionManifest
{
    [JsonPropertyName("latest")] public LatestVersions Latest { get; set; } = new();
    [JsonPropertyName("versions")] public List<RemoteVersion> Versions { get; set; } = [];
}

public sealed class LatestVersions
{
    [JsonPropertyName("release")] public string Release { get; set; } = "";
    [JsonPropertyName("snapshot")] public string Snapshot { get; set; } = "";
}

public sealed class RemoteVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("time")] public DateTimeOffset Time { get; set; }
    [JsonPropertyName("releaseTime")] public DateTimeOffset ReleaseTime { get; set; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
}

public sealed class MinecraftVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "release";
    [JsonPropertyName("mainClass")] public string MainClass { get; set; } = "";
    [JsonPropertyName("releaseTime")] public DateTimeOffset ReleaseTime { get; set; }
    [JsonPropertyName("time")] public DateTimeOffset? Time { get; set; }
    [JsonPropertyName("assetIndex")] public AssetIndex? AssetIndex { get; set; }
    [JsonPropertyName("assets")] public string? Assets { get; set; }
    [JsonPropertyName("javaVersion")] public JavaVersionInfo? JavaVersion { get; set; }
    [JsonPropertyName("downloads")] public Dictionary<string, DownloadFile>? Downloads { get; set; }
    [JsonPropertyName("libraries")] public List<Library> Libraries { get; set; } = [];
    [JsonPropertyName("arguments")] public VersionArguments? Arguments { get; set; }
    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; set; }
    [JsonPropertyName("logging")] public LoggingInfo? Logging { get; set; }
    [JsonPropertyName("minimumLauncherVersion")] public int MinimumLauncherVersion { get; set; }
}

public sealed class AssetIndex
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("sha1")] public string Sha1 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("totalSize")] public long TotalSize { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed class AssetIndexFile
{
    [JsonPropertyName("objects")] public Dictionary<string, AssetObject> Objects { get; set; } = [];
    [JsonPropertyName("virtual")] public bool Virtual { get; set; }
    [JsonPropertyName("map_to_resources")] public bool MapToResources { get; set; }
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

public sealed class JavaVersionInfo
{
    [JsonPropertyName("component")] public string? Component { get; set; }
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; }
}

public sealed class DownloadFile
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha1")] public string Sha1 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

public sealed class Library
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; set; }
    [JsonPropertyName("rules")] public List<Rule>? Rules { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("extract")] public ExtractRule? Extract { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")] public DownloadFile? Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadFile>? Classifiers { get; set; }
}

public sealed class ExtractRule
{
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }
}

public sealed class Rule
{
    [JsonPropertyName("action")] public string Action { get; set; } = "allow";
    [JsonPropertyName("os")] public OsRule? Os { get; set; }
    [JsonPropertyName("features")] public Dictionary<string, bool>? Features { get; set; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("arch")] public string? Arch { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}

public sealed class VersionArguments
{
    [JsonPropertyName("game")] public List<JsonElement>? Game { get; set; }
    [JsonPropertyName("jvm")] public List<JsonElement>? Jvm { get; set; }
}

public sealed class LoggingInfo
{
    [JsonPropertyName("client")] public LoggingClient? Client { get; set; }
}

public sealed class LoggingClient
{
    [JsonPropertyName("argument")] public string Argument { get; set; } = "";
    [JsonPropertyName("file")] public DownloadFile? File { get; set; }
}

public sealed class LoaderVersionOption
{
    public string Version { get; init; } = "";
    public bool Stable { get; init; }
    public int Build { get; init; }
    public string? GameVersion { get; init; }
}

public sealed class LoaderInspection
{
    public string Type { get; init; } = "";
    public string Label { get; init; } = "";
    public bool Available { get; init; }
    public string? Note { get; init; }
    public List<LoaderVersionOption> Versions { get; init; } = [];
}

public sealed class VersionInspection
{
    public string GameVersion { get; init; } = "";
    public string Type { get; init; } = "";
    public DateTimeOffset ReleaseTime { get; init; }
    public int JavaMajor { get; init; }
    public long AssetBytes { get; init; }
    public long ClientBytes { get; init; }
    public long LibraryBytes { get; init; }
    public long TotalBytes { get; init; }
    public List<LoaderInspection> Loaders { get; init; } = [];
}

public sealed class InstallRequest
{
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "vanilla";
    public string? LoaderVersion { get; set; }
    public string? DisplayName { get; set; }
    public string? ModpackPath { get; set; }

    /// <summary>来自扫描结果的本地实例游戏目录，安装完成后把内容与存档复制进去。</summary>
    public string? LocalInstancePath { get; set; }
}

public sealed class InstallProgress
{
    public string JobId { get; init; } = "";
    public string Stage { get; init; } = "plan";
    public string StageLabel { get; init; } = "";
    public int CompletedFiles { get; init; }
    public int TotalFiles { get; init; }
    public long CompletedBytes { get; init; }
    public long TotalBytes { get; init; }
    public string? CurrentItem { get; init; }
    public double BytesPerSecond { get; init; }
    public string? InstanceId { get; init; }
    public bool Done { get; init; }
    public bool Failed { get; init; }
}

public sealed class LaunchOptions
{
    public int MemoryMb { get; init; }
}

public sealed class CrashDiagnosis
{
    public string Severity { get; init; } = "error";
    public string Title { get; init; } = "这次没能进去";
    public string Summary { get; init; } = "";
    public string? Evidence { get; init; }
    public List<string> Suggestions { get; init; } = [];
    public List<string> SuspectedMods { get; init; } = [];
    public string? LatestLogPath { get; init; }
    public string? CrashReportPath { get; init; }
    public int ExitCode { get; init; }
}

public sealed class NewsItem
{
    public string Title { get; init; } = "";
    public string? Tag { get; init; }
    public string? Category { get; init; }
    public string? Date { get; init; }
    public string? Text { get; init; }
    public string? ImageUrl { get; init; }
    public string? Link { get; init; }
}

public sealed class DailyBlock
{
    public string Id { get; init; } = "";
    public string NameZh { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string Note { get; init; } = "";
    public string? DataUrl { get; init; }
    public string? GameVersion { get; init; }
    public string Accent { get; init; } = "#7d8a72";
}
