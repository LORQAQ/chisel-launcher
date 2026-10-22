namespace Chisel.Core.Models;

public sealed class AppSettings
{
    public int Schema { get; set; } = 1;
    public bool Onboarded { get; set; }
    public string? LastInstanceId { get; set; }
    public string? ActiveAccountId { get; set; }
    public string DownloadSource { get; set; } = "auto";
    public int DownloadConcurrency { get; set; } = 16;
    public bool AutoMemory { get; set; } = true;
    public int? MemoryMb { get; set; }
    public string? JavaPath { get; set; }
    public bool AutoDownloadJava { get; set; } = true;
    public bool CloseOnLaunch { get; set; }
    public bool ShowConsoleOnLaunch { get; set; } = true;
    public bool CustomResolution { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public string JvmArgs { get; set; } = "";
    public string MicrosoftClientId { get; set; } = "";
    public string CurseForgeApiKey { get; set; } = "";
    public int WindowWidth { get; set; } = 1360;
    public int WindowHeight { get; set; } = 860;
    public bool WindowMaximized { get; set; }
}

public sealed class AppSettingsPatch
{
    public bool? Onboarded { get; set; }
    public string? LastInstanceId { get; set; }
    public string? ActiveAccountId { get; set; }
    public string? DownloadSource { get; set; }
    public int? DownloadConcurrency { get; set; }
    public bool? AutoMemory { get; set; }
    public int? MemoryMb { get; set; }
    public string? JavaPath { get; set; }
    public bool? AutoDownloadJava { get; set; }
    public bool? CloseOnLaunch { get; set; }
    public bool? ShowConsoleOnLaunch { get; set; }
    public bool? CustomResolution { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? JvmArgs { get; set; }
    public string? MicrosoftClientId { get; set; }
    public string? CurseForgeApiKey { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool? WindowMaximized { get; set; }
}
