namespace Chisel.Core.Models;

public sealed class ModrinthSearchResult
{
    public List<ModrinthHit> Hits { get; init; } = [];
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int TotalHits { get; init; }
}

public sealed class ModrinthHit
{
    public string ProjectId { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public long Downloads { get; init; }
    public long Follows { get; init; }
    public List<string> Categories { get; init; } = [];
    public string ProjectType { get; init; } = "";
    public List<string> Versions { get; init; } = [];
    public string? LatestVersion { get; init; }
    public long? Color { get; init; }
    public bool Installed { get; init; }
    public bool InstalledCompatible { get; init; }
}

public sealed class ModrinthProjectInfo
{
    public string ProjectId { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ProjectType { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public long Downloads { get; init; }
    public long Color { get; init; }
}

public sealed class ModrinthVersionInfo
{
    public string Id { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string Name { get; init; } = "";
    public string VersionNumber { get; init; } = "";
    public string VersionType { get; init; } = "";
    public DateTimeOffset DatePublished { get; init; }
    public List<string> GameVersions { get; init; } = [];
    public List<string> Loaders { get; init; } = [];
    public List<string> Changelog { get; init; } = [];
    public List<ModrinthDependencyInfo> Dependencies { get; init; } = [];
    public ModrinthFileInfo? File { get; init; }
}

public sealed class ModrinthFileInfo
{
    public string FileName { get; init; } = "";
    public string Url { get; init; } = "";
    public long Size { get; init; }
    public string Sha1 { get; init; } = "";
    public bool Primary { get; init; }
}

public sealed class ModrinthDependencyInfo
{
    public string? VersionId { get; init; }
    public string? ProjectId { get; init; }
    public string? FileName { get; init; }
    public string DependencyType { get; init; } = "required";
}

public sealed class InstalledContent
{
    public string ProjectId { get; init; } = "";
    public string VersionId { get; init; } = "";
    public string ProjectType { get; init; } = "mod";
    public string Title { get; init; } = "";
    public string FileName { get; init; } = "";
    public string? IconUrl { get; init; }
    public string? VersionNumber { get; init; }
    public bool Enabled { get; init; }
    public bool FileExists { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public long SizeBytes { get; init; }
}

public sealed class ContentProgress
{
    public string Stage { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ProjectTitle { get; init; }
    public long ReceivedBytes { get; init; }
    public long TotalBytes { get; init; }
    public bool Done { get; init; }
}

public sealed class ContentInstallResult
{
    public List<InstalledContent> Installed { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public bool Updated { get; init; }
}

public sealed class ContentManifest
{
    public List<ContentManifestItem> Items { get; set; } = [];
}

public sealed class ContentManifestItem
{
    public string ProjectId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string ProjectType { get; set; } = "mod";
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? IconUrl { get; set; }
    public string? VersionNumber { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;
    public List<string> Dependencies { get; set; } = [];
}

public sealed class ContentTypeRequest
{
    public string InstanceId { get; set; } = "";
    public string ProjectType { get; set; } = "mod";
    public string Query { get; set; } = "";
    public string Sort { get; set; } = "relevance";
    public string Source { get; set; } = "modrinth";
    public int Offset { get; set; }
    public int Limit { get; set; } = 20;
}

public sealed class ContentInstallRequest
{
    public string InstanceId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string? VersionId { get; set; }
    public string ProjectType { get; set; } = "mod";
    public string Source { get; set; } = "modrinth";
}

public sealed class ShaderInstallRequest
{
    public string InstanceId { get; set; } = "";
    public string Kind { get; set; } = "iris";
}
