using Chisel.Core.Models;

namespace Chisel.Core.Services;

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();
    private AppSettings _settings;

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
        _settings = AtomicJson.Read(_paths.ConfigPath, () => new AppSettings());
        Normalize(_settings);
    }

    public AppSettings Snapshot()
    {
        lock (_gate) return Clone(_settings);
    }

    public AppSettings Get() { lock (_gate) return _settings; }

    public AppSettings Update(AppSettingsPatch patch)
    {
        lock (_gate)
        {
            if (patch.Onboarded.HasValue) _settings.Onboarded = patch.Onboarded.Value;
            if (patch.LastInstanceId != null) _settings.LastInstanceId = emptyToNull(patch.LastInstanceId);
            if (patch.ActiveAccountId != null) _settings.ActiveAccountId = emptyToNull(patch.ActiveAccountId);
            if (patch.DownloadSource != null && new[] { "auto", "official", "mirror" }.Contains(patch.DownloadSource))
                _settings.DownloadSource = patch.DownloadSource;
            if (patch.DownloadConcurrency.HasValue)
                _settings.DownloadConcurrency = Math.Clamp(patch.DownloadConcurrency.Value, 1, 32);
            if (patch.AutoMemory.HasValue) _settings.AutoMemory = patch.AutoMemory.Value;
            if (patch.MemoryMb.HasValue)
                _settings.MemoryMb = Math.Clamp(patch.MemoryMb.Value / 256 * 256, 1024, 32768);
            if (patch.JavaPath != null) _settings.JavaPath = emptyToNull(patch.JavaPath);
            if (patch.AutoDownloadJava.HasValue) _settings.AutoDownloadJava = patch.AutoDownloadJava.Value;
            if (patch.CloseOnLaunch.HasValue) _settings.CloseOnLaunch = patch.CloseOnLaunch.Value;
            if (patch.ShowConsoleOnLaunch.HasValue) _settings.ShowConsoleOnLaunch = patch.ShowConsoleOnLaunch.Value;
            if (patch.CustomResolution.HasValue) _settings.CustomResolution = patch.CustomResolution.Value;
            if (patch.Width.HasValue) _settings.Width = Math.Clamp(patch.Width.Value, 320, 7680);
            if (patch.Height.HasValue) _settings.Height = Math.Clamp(patch.Height.Value, 240, 4320);
            if (patch.JvmArgs != null) _settings.JvmArgs = patch.JvmArgs.Trim()[..Math.Min(patch.JvmArgs.Trim().Length, 2000)];
            if (patch.MicrosoftClientId != null)
            {
                var id = patch.MicrosoftClientId.Trim();
                if (id.Length == 0 || Guid.TryParse(id, out _)) _settings.MicrosoftClientId = id;
                else throw new ArgumentException("Azure 客户端 ID 应为 GUID 格式");
            }
            if (patch.CurseForgeApiKey != null)
                _settings.CurseForgeApiKey = emptyToNull(patch.CurseForgeApiKey);
            if (patch.WindowWidth.HasValue) _settings.WindowWidth = Math.Clamp(patch.WindowWidth.Value, 1040, 7680);
            if (patch.WindowHeight.HasValue) _settings.WindowHeight = Math.Clamp(patch.WindowHeight.Value, 700, 4320);
            if (patch.WindowMaximized.HasValue) _settings.WindowMaximized = patch.WindowMaximized.Value;
            Normalize(_settings);
            AtomicJson.Write(_paths.ConfigPath, _settings);
            return Clone(_settings);
        }
    }

    public void Mutate(Action<AppSettings> mutate)
    {
        lock (_gate)
        {
            mutate(_settings);
            Normalize(_settings);
            AtomicJson.Write(_paths.ConfigPath, _settings);
        }
    }

    private static void Normalize(AppSettings s)
    {
        if (!new[] { "auto", "official", "mirror" }.Contains(s.DownloadSource)) s.DownloadSource = "auto";
        s.DownloadConcurrency = Math.Clamp(s.DownloadConcurrency, 1, 32);
        if (s.MemoryMb is { } mem) s.MemoryMb = Math.Clamp(mem / 256 * 256, 1024, 32768);
        if (s.MicrosoftClientId != null && s.MicrosoftClientId.Length > 0 && !Guid.TryParse(s.MicrosoftClientId, out _))
            s.MicrosoftClientId = "";
        s.Width = Math.Clamp(s.Width, 320, 7680);
        s.Height = Math.Clamp(s.Height, 240, 4320);
    }

    private static AppSettings Clone(AppSettings s)
        => System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
               System.Text.Json.JsonSerializer.Serialize(s, AtomicJson.Options), AtomicJson.Options)!;

    private static string? emptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
