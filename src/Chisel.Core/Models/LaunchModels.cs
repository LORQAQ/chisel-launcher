namespace Chisel.Core.Models;

public sealed class GameLogLine
{
    public string InstanceId { get; init; } = "";
    public long Sequence { get; init; }
    public string Text { get; init; } = "";
    public bool Stderr { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
}

public sealed class GameStatus
{
    public string InstanceId { get; init; } = "";
    public string State { get; init; } = "preparing";
    public string Message { get; init; } = "";
    public double Progress { get; init; }
    public long BytesReceived { get; init; }
    public long BytesTotal { get; init; }
    public int? ExitCode { get; init; }
    public CrashDiagnosis? Diagnosis { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
}

public sealed class LaunchStartResult
{
    public string InstanceId { get; init; } = "";
    public int Pid { get; init; }
    public List<string> Warnings { get; init; } = [];
    public string JavaPath { get; init; } = "";
    public int MemoryMb { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}
