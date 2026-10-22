namespace Chisel.Core.Models;

public sealed class Instance
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "vanilla";
    public string? LoaderVersion { get; set; }
    public string? ProfileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastPlayedAt { get; set; }
    public long PlaySeconds { get; set; }
    public string? LastExitReason { get; set; }
    public int? LastExitCode { get; set; }
    public int? RequiredJava { get; set; }
    public string? JavaPathOverride { get; set; }
    public int? MemoryMbOverride { get; set; }
}

public sealed class InstanceSummary
{
    public required Instance Instance { get; init; }
    public string GameDir { get; init; } = "";
    public bool Ready { get; init; }
    public bool JavaReady { get; init; }
    public int ModCount { get; init; }
    public int SaveCount { get; init; }
    public long SizeBytes { get; init; }
    public string? Violation { get; init; }
}

public sealed class InstanceRenameRequest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
