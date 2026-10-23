using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chisel.Core;
using Chisel.Core.Minecraft;
using Chisel.Core.Models;
using Chisel.Core.Net;
using Chisel.Core.Services;
using Chisel.Core.Util;

namespace Chisel.App;

public interface IHostWindow
{
    bool IsMaximized { get; }
    void PostEvent(string name, object? data);
    void PostJson(string json);
    bool CopyText(string text);
    void Minimize();
    void ToggleMaximize();
    void CloseWindow();
    void DragWindow();
    Task<string?> PickJavaAsync();
    Task<string?> PickModpackAsync();
    Task<string?> PickImageAsync();
    void RevealInExplorer(string path);
    void OpenExternal(string url);
}

public sealed class Bridge
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int HistoryLimit = 1800;
    private readonly AppPaths _paths;
    private readonly IHostWindow _host;
    private readonly SettingsService _settings;
    private readonly InstanceStore _instances;
    private readonly VersionCatalogService _catalog;
    private readonly JavaManager _java;
    private readonly AccountService _accounts;
    private readonly InstallerService _installer;
    private readonly GameLauncher _launcher;
    private readonly ModrinthService _modrinth;
    private readonly CurseForgeService _curseForge;
    private readonly ModpackService _modpacks;
    private readonly LocalPackScanner _packScanner;
    private readonly LoaderInstallerService _loaderInstaller;
    private readonly AuthlibService _authlib;
    private readonly NewsService _news;
    private readonly BlockService _blocks;
    private readonly NotesService _notes;

    private readonly object _stateGate = new();
    private readonly object _logGate = new();
    private readonly object _sizeGate = new();
    private Dictionary<string, long>? _sizes;
    private DateTimeOffset _sizesAt;
    private readonly List<GameLogLine> _logPending = [];
    private readonly LinkedList<GameLogLine> _logHistory = new();
    private readonly Dictionary<string, CrashDiagnosis> _diagnoses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _contentJobs = new();
    private readonly System.Threading.Timer _logTimer;
    private GameStatus? _lastStatus;
    private string? _logInstanceId;
    private CancellationTokenSource? _installCts;
    private string? _installJobId;
    private CancellationTokenSource? _authCts;
    private CancellationTokenSource? _javaCts;
    private CancellationTokenSource? _shaderCts;
    private bool _disposed;

    public Bridge(AppPaths paths, IHostWindow host)
    {
        _paths = paths;
        _host = host;
        _settings = new SettingsService(paths);
        _instances = new InstanceStore(paths);
        _catalog = new VersionCatalogService(paths, _settings);
        _java = new JavaManager(paths);
        var auth = new MicrosoftAuth();
        _authlib = new AuthlibService(paths);
        _accounts = new AccountService(paths, _settings, auth, _authlib);
        _curseForge = new CurseForgeService(paths, _settings, _instances);
        _modpacks = new ModpackService(paths, _settings, _curseForge);
        _packScanner = new LocalPackScanner(paths);
        _loaderInstaller = new LoaderInstallerService(paths, _java);
        _installer = new InstallerService(paths, _settings, _catalog, _instances, _java, _loaderInstaller, _modpacks);
        var resolver = new VersionResolver(paths, _catalog);
        var crash = new CrashAnalyzer(paths);
        _launcher = new GameLauncher(paths, _settings, _instances, _java, _accounts, resolver, crash);
        _modrinth = new ModrinthService(paths, _instances);
        _news = new NewsService(paths);
        _blocks = new BlockService(paths, _instances);
        _notes = new NotesService(paths);
        _logTimer = new System.Threading.Timer(_ => FlushLogs(), null, 250, 160);
    }

    public AppSettings SettingsSnapshot() => _settings.Snapshot();

    public void EmitEvent(string name, object? data)
    {
        if (_disposed) return;
        try { _host.PostEvent(name, data); } catch { }
    }

    public void SaveWindowState(int width, int height, bool maximized)
    {
        try
        {
            _settings.Update(new AppSettingsPatch
            {
                WindowWidth = width,
                WindowHeight = height,
                WindowMaximized = maximized
            });
        }
        catch { }
    }

    public void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        try { _logTimer.Dispose(); } catch { }
        CancelAndDispose(ref _installCts);
        CancelAndDispose(ref _authCts);
        CancelAndDispose(ref _javaCts);
        CancelAndDispose(ref _shaderCts);
        foreach (var job in _contentJobs.Values)
        {
            try { job.Cancel(); } catch { }
        }
    }

    public async Task HandleAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        string? id = null;
        var method = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("id", out var idElement))
                id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : idElement.GetRawText();
            if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
                return;
            method = methodElement.GetString() ?? "";
            var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : default;

            var result = await Task.Run(() => DispatchAsync(method, parameters), CancellationToken.None)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(id)) Respond(id, result);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(id)) RespondError(id, ex);
            else LogError(method, ex);
        }
    }

    private void Respond(string id, object? result)
    {
        var json = JsonSerializer.Serialize(new { id, ok = true, result }, JsonOptions);
        _host.PostJson(json);
    }

    private void RespondError(string id, Exception ex)
    {
        var error = ex switch
        {
            OperationCanceledException => new { message = "操作已取消。", code = "cancelled" },
            UnauthorizedAccessException => new { message = "没有权限访问这个文件或文件夹。", code = "access_denied" },
            HttpRequestException => new { message = "网络请求失败，请检查网络后重试。", code = "network" },
            _ => new { message = Friendly(ex), code = "failed" }
        };
        var json = JsonSerializer.Serialize(new { id, ok = false, error }, JsonOptions);
        _host.PostJson(json);
    }

    private static string Friendly(Exception ex)
    {
        while (ex is AggregateException { InnerExceptions.Count: > 0 } aggregate)
            ex = aggregate.InnerExceptions[0];
        var message = (ex.Message ?? "").Replace("\r", "").Trim();
        if (message.Length > 500) message = message[..500] + "…";
        return string.IsNullOrEmpty(message) ? "操作没有完成，请稍后重试。" : message;
    }

    private void LogError(string method, Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(_paths.LogsDir, "app.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] bridge:{method}\n{ex}\n\n");
        }
        catch { }
    }

    private async Task<object?> DispatchAsync(string method, JsonElement p)
    {
        switch (method)
        {
            case "app.bootstrap":
                return Bootstrap();
            case "app.ping":
                return new { pong = true, at = DateTimeOffset.Now };

            case "catalog.list":
                return await CatalogAsync(p);
            case "version.inspect":
                return await _catalog.InspectAsync(Param<VersionQuery>(p).GameVersion, CancellationToken.None);

            case "instance.install":
                return StartInstall(Param<InstallRequest>(p));
            case "install.cancel":
                return CancelInstall();
            case "instance.list":
                return ListInstances();
            case "instance.sizes":
                return InstanceSizes();
            case "instance.get":
                return GetInstance(Param<IdQuery>(p).Id);
            case "instance.update":
                return UpdateInstance(Param<InstanceUpdateRequest>(p));
            case "instance.rename":
            {
                var request = Param<InstanceRenameRequest>(p);
                _instances.Rename(request.Id, request.Name);
                EmitEvent("instances.changed", null);
                return GetInstance(request.Id);
            }
            case "instance.delete":
            {
                var request = Param<IdQuery>(p);
                DeleteInstance(request.Id);
                return new { deleted = true };
            }
            case "instance.openFolder":
            {
                var instance = RequireInstance(Param<IdQuery>(p).Id);
                _host.RevealInExplorer(_paths.GameDir(instance.Id));
                return new { opened = true };
            }
            case "instance.icon":
                return InstanceIcon(Param<IdQuery>(p).Id);
            case "instance.chooseIcon":
                return await ChooseInstanceIconAsync(Param<IdQuery>(p).Id);
            case "instance.clearIcon":
            {
                var instance = RequireInstance(Param<IdQuery>(p).Id);
                var icon = Path.Combine(_paths.InstanceDir(instance.Id), "icon.png");
                try { if (File.Exists(icon)) File.Delete(icon); } catch { }
                EmitEvent("instances.changed", null);
                return new { cleared = true };
            }

            case "launch.start":
                return StartLaunch(Param<IdQuery>(p).Id);
            case "launch.stop":
                return new { stopped = _launcher.Stop() };
            case "launch.state":
                return LaunchState();

            case "logs.history":
                return LogHistory(Param<LogHistoryQuery>(p).InstanceId);
            case "logs.openFolder":
            {
                var request = Param<LogHistoryQuery>(p);
                if (!string.IsNullOrWhiteSpace(request.InstanceId))
                {
                    var instance = RequireInstance(request.InstanceId!);
                    var dir = Path.Combine(_paths.GameDir(instance.Id), "logs");
                    Directory.CreateDirectory(dir);
                    _host.RevealInExplorer(dir);
                }
                else _host.RevealInExplorer(_paths.LogsDir);
                return new { opened = true };
            }

            case "settings.update":
            {
                var settings = _settings.Update(Param<AppSettingsPatch>(p));
                EmitEvent("settings.changed", settings);
                return settings;
            }
            case "settings.snapshot":
                return _settings.Snapshot();

            case "java.scan":
                return await ScanJavaAsync(Param<FlagQuery>(p).Force);
            case "java.describe":
                return DescribeJava(Param<PathQuery>(p).Path);
            case "java.addPath":
                return AddJavaPath(Param<PathQuery>(p).Path);
            case "java.download":
                return StartJavaDownload(Param<JavaDownloadQuery>(p));
            case "java.cancel":
                return CancelJava();

            case "accounts.list":
                return AccountsPayload();
            case "accounts.addOffline":
            {
                var view = _accounts.AddOffline(Param<OfflineAccountRequest>(p).Name);
                EmitEvent("accounts.changed", AccountsPayload());
                return view;
            }
            case "accounts.addAuthlib":
            {
                var request = Param<AuthlibAccountRequest>(p);
                var view = await _accounts.AddAuthlibAsync(request.Server, request.Username, request.Password, CancellationToken.None);
                EmitEvent("accounts.changed", AccountsPayload());
                EmitEvent("accounts.authlib.done", new { account = view });
                return view;
            }
            case "accounts.select":
            {
                var request = Param<IdQuery>(p);
                _accounts.SetActive(request.Id);
                EmitEvent("accounts.changed", AccountsPayload());
                return AccountsPayload();
            }
            case "accounts.remove":
            {
                var request = Param<IdQuery>(p);
                _accounts.Remove(request.Id);
                EmitEvent("accounts.changed", AccountsPayload());
                return AccountsPayload();
            }
            case "accounts.setSkin":
            {
                var request = Param<SetSkinRequest>(p);
                var view = _accounts.SetSkin(request.AccountId, request.DataUrl);
                EmitEvent("accounts.changed", AccountsPayload());
                return view;
            }
            case "accounts.skin":
                return SkinPayload(Param<IdQuery>(p).Id);
            case "accounts.microsoft.begin":
                return await BeginMicrosoftAsync();
            case "accounts.microsoft.cancel":
                CancelMicrosoft();
                return new { cancelled = true };

            case "modrinth.search":
                return Param<ContentTypeRequest>(p).Source == "curseforge"
                    ? await _curseForge.SearchAsync(Param<ContentTypeRequest>(p), CancellationToken.None)
                    : await _modrinth.SearchAsync(Param<ContentTypeRequest>(p), CancellationToken.None);
            case "modrinth.versions":
            {
                var request = Param<ContentVersionsQuery>(p);
                return request.Source == "curseforge"
                    ? await _curseForge.GetVersionsAsync(request.InstanceId, request.ProjectId,
                        request.ProjectType, CancellationToken.None)
                    : await _modrinth.GetVersionsAsync(request.InstanceId, request.ProjectId,
                        request.ProjectType, CancellationToken.None);
            }
            case "curseforge.installLink":
            {
                var request = Param<CurseLinkQuery>(p);
                var instance = RequireInstance(request.InstanceId);
                var file = await _curseForge.ResolveByLinkAsync(request.Link, instance.GameVersion,
                    instance.Loader, request.ProjectType, CancellationToken.None);
                var job = StartContentInstall(new ContentInstallRequest
                {
                    InstanceId = instance.Id,
                    ProjectId = file.ProjectId.ToString(),
                    VersionId = file.FileId.ToString(),
                    ProjectType = file.ProjectType,
                    Source = "curseforge"
                }, update: false);
                return new { file, job };
            }
            case "modpack.pick":
            {
                var path = await _host.PickModpackAsync();
                if (path == null) return new { path = (string?)null, preview = (object?)null };
                return new { path = (string?)path, preview = (object?)_modpacks.Inspect(path) };
            }
            case "modpack.inspect":
                return _modpacks.Inspect(Param<PathQuery>(p).Path);
            case "modpack.scan":
                return await Task.Run(() => _packScanner.Scan(null, CancellationToken.None), CancellationToken.None);
            case "modpack.importLocal":
            {
                var request = Param<LocalImportRequest>(p);
                if (string.IsNullOrWhiteSpace(request.Path) || !Directory.Exists(request.Path))
                    throw new ArgumentException("要导入的实例目录不存在");
                return StartInstall(new InstallRequest
                {
                    GameVersion = request.GameVersion,
                    Loader = request.Loader,
                    LoaderVersion = request.LoaderVersion,
                    DisplayName = request.Name,
                    LocalInstancePath = request.Path
                });
            }
            case "shader.recommend":
                return ShaderRecommendation(Param<IdQuery>(p).Id);
            case "shader.install":
                return InstallShaderSupportAsync(Param<ShaderInstallRequest>(p));
            case "modrinth.install":
                return StartContentInstall(Param<ContentInstallRequest>(p), update: false);
            case "modrinth.update":
                return StartContentInstall(Param<ContentInstallRequest>(p), update: true);
            case "modrinth.list":
                return _modrinth.ListInstalled(Param<IdQuery>(p).Id);
            case "modrinth.toggle":
            {
                var request = Param<ToggleContentQuery>(p);
                var item = _modrinth.Toggle(request.InstanceId, request.ProjectId, request.Enabled);
                EmitEvent("content.changed", new { instanceId = request.InstanceId });
                return item;
            }
            case "modrinth.remove":
            {
                var request = Param<ContentQuery>(p);
                _modrinth.Remove(request.InstanceId, request.ProjectId);
                InvalidateSizes();
                EmitEvent("content.changed", new { instanceId = request.InstanceId });
                return new { removed = true };
            }
            case "content.cancel":
                return CancelContent(Param<IdQuery>(p).Id);

            case "news.list":
                return await NewsAsync(Param<FlagQuery>(p).Force);
            case "block.daily":
                return _blocks.GetDaily(Param<OptionalIdQuery>(p).InstanceId);
            case "notes.list":
                return new { notes = _notes.List() };
            case "notes.add":
                return _notes.Add(Param<NoteQuery>(p).Text);
            case "notes.remove":
                return new { removed = _notes.Remove(Param<IdQuery>(p).Id) };
            case "crash.last":
                return LastCrash(Param<IdQuery>(p).Id);

            case "dialog.pickJava":
                return new { path = await _host.PickJavaAsync() };
            case "shell.openPath":
                return OpenPath(Param<PathQuery>(p));
            case "shell.openUrl":
            {
                var url = Param<UrlQuery>(p).Url;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new ArgumentException("只能打开 HTTPS 链接");
                _host.OpenExternal(uri.ToString());
                return new { opened = true };
            }
            case "shell.openData":
                _host.RevealInExplorer(_paths.Root);
                return new { opened = true };
            case "clipboard.write":
            {
                var text = Param<ClipboardQuery>(p).Text;
                if (text.Length > 1_000_000) throw new ArgumentException("这段文字太长，剪贴板放不下。");
                return new { copied = _host.CopyText(text) };
            }

            case "window.minimize":
                _host.Minimize();
                return new { minimized = true };
            case "window.toggleMaximize":
                _host.ToggleMaximize();
                return new { maximized = _host.IsMaximized };
            case "window.close":
                _host.CloseWindow();
                return new { closing = true };
            case "window.drag":
                _host.DragWindow();
                return new { dragging = true };
            default:
                throw new InvalidOperationException($"未知的请求：{method}");
        }
    }

    private object Bootstrap()
    {
        var settings = _settings.Snapshot();
        var accounts = _accounts.List();
        return new
        {
            app = new
            {
                name = "凿境",
                codename = "CHISEL",
                version = "0.1.0",
                dataRoot = _paths.Root,
                logsDir = _paths.LogsDir
            },
            settings,
            accounts = new
            {
                items = accounts,
                activeId = accounts.FirstOrDefault(a => a.IsActive)?.Id
            },
            instances = ListInstances(),
            running = LaunchState(),
            memory = new
            {
                totalMb = SystemMemory.TotalPhysicalMb(),
                availableMb = SystemMemory.AvailablePhysicalMb(),
                recommendedMb = SystemMemory.RecommendedGameMemoryMb(),
                defaultMb = SystemMemory.DefaultGameMemoryMb()
            },
            window = new { maximized = _host.IsMaximized },
            system = new
            {
                os = RuleEvaluator.OsName,
                arch = RuleEvaluator.OsArch,
                version = Environment.OSVersion.VersionString
            }
        };
    }

    private async Task<object> CatalogAsync(JsonElement p)
    {
        var force = Param<FlagQuery>(p).Force;
        var manifest = await _catalog.GetManifestAsync(force, CancellationToken.None);
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_paths.VersionsDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(_paths.VersionsDir))
            {
                if (File.Exists(Path.Combine(dir, "version.json")) &&
                    File.Exists(Path.Combine(dir, "client.jar")))
                    installed.Add(Path.GetFileName(dir));
            }
        }
        return new
        {
            latest = manifest.Latest,
            versions = manifest.Versions,
            installedIds = installed
        };
    }

    private List<object> ListInstances()
    {
        var running = _launcher.RunningInstanceId;
        var list = new List<object>();
        foreach (var instance in _instances.LoadAll())
        {
            try
            {
                var summary = _instances.Summarize(instance);
                long cachedSize = 0;
                lock (_sizeGate)
                {
                    if (_sizes != null) _sizes.TryGetValue(instance.Id, out cachedSize);
                }
                list.Add(new
                {
                    instance,
                    gameDir = summary.GameDir,
                    ready = summary.Ready,
                    javaReady = summary.JavaReady,
                    modCount = summary.ModCount,
                    saveCount = summary.SaveCount,
                    sizeBytes = cachedSize,
                    playtimeLabel = InstanceStore.FormatPlaytime(instance.PlaySeconds),
                    loaderLabel = LoaderLabel(instance.Loader),
                    hasIcon = File.Exists(Path.Combine(_paths.InstanceDir(instance.Id), "icon.png")),
                    iconVersion = IconVersion(instance.Id),
                    running = string.Equals(running, instance.Id, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch (Exception ex)
            {
                LogError("instance.list", ex);
            }
        }
        return list;
    }

    private object InstanceSizes()
    {
        lock (_sizeGate)
        {
            if (_sizes != null && DateTimeOffset.UtcNow - _sizesAt < TimeSpan.FromMinutes(3))
                return new { sizes = _sizes, cached = true };
        }

        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in _instances.LoadAll())
        {
            try
            {
                sizes[instance.Id] = _instances.Summarize(instance, includeSize: true).SizeBytes;
            }
            catch (Exception ex)
            {
                LogError("instance.sizes", ex);
            }
        }

        lock (_sizeGate)
        {
            _sizes = sizes;
            _sizesAt = DateTimeOffset.UtcNow;
        }
        return new { sizes, cached = false };
    }

    private void InvalidateSizes()
    {
        lock (_sizeGate) _sizes = null;
    }

    private object GetInstance(string id)
    {
        var instance = RequireInstance(id);
        var summary = _instances.Summarize(instance, includeSize: true);
        CrashDiagnosis? diagnosis = null;
        lock (_stateGate) _diagnoses.TryGetValue(instance.Id, out diagnosis);
        return new
        {
            instance,
            gameDir = summary.GameDir,
            ready = summary.Ready,
            javaReady = summary.JavaReady,
            modCount = summary.ModCount,
            saveCount = summary.SaveCount,
            sizeBytes = summary.SizeBytes,
            playtimeLabel = InstanceStore.FormatPlaytime(instance.PlaySeconds),
            loaderLabel = LoaderLabel(instance.Loader),
            hasIcon = File.Exists(Path.Combine(_paths.InstanceDir(instance.Id), "icon.png")),
            iconVersion = IconVersion(instance.Id),
            running = string.Equals(_launcher.RunningInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase),
            lastDiagnosis = diagnosis
        };
    }

    private object UpdateInstance(InstanceUpdateRequest request)
    {
        var instance = RequireInstance(request.Id);
        if (request.DisplayName != null)
        {
            _instances.Rename(instance.Id, request.DisplayName);
            instance = RequireInstance(request.Id);
        }
        if (request.MemoryMb.HasValue)
        {
            instance.MemoryMbOverride = request.MemoryMb.Value <= 0
                ? null
                : Math.Clamp(request.MemoryMb.Value / 256 * 256, 1024, 32768);
        }
        if (request.ClearJava) instance.JavaPathOverride = null;
        else if (request.JavaPath != null)
        {
            if (request.JavaPath.Length == 0) instance.JavaPathOverride = null;
            else
            {
                var runtime = _java.Describe(request.JavaPath) ?? throw new ArgumentException("这个路径不是可用的 Java");
                instance.JavaPathOverride = runtime.Path;
            }
        }
        _instances.Save(instance);
        EmitEvent("instances.changed", null);
        return GetInstance(instance.Id);
    }

    private void DeleteInstance(string id)
    {
        var instance = RequireInstance(id);
        if (string.Equals(_launcher.RunningInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase)
            || _launcher.IsRunning)
            throw new InvalidOperationException("游戏正在运行，先结束游戏再删除实例。");
        _instances.Delete(instance.Id);
        InvalidateSizes();
        lock (_stateGate) _diagnoses.Remove(instance.Id);
        if (string.Equals(_settings.Snapshot().LastInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase))
            _settings.Update(new AppSettingsPatch { LastInstanceId = "" });
        EmitEvent("instances.changed", null);
    }

    private object StartInstall(InstallRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GameVersion))
            throw new ArgumentException("请选择要安装的游戏版本");
        if (!string.IsNullOrWhiteSpace(request.ModpackPath))
        {
            var path = request.ModpackPath!;
            if (!File.Exists(path)) throw new ArgumentException("整合包文件不存在");
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not ".mrpack" and not ".zip") throw new ArgumentException("只支持 .mrpack 或 .zip 整合包");
        }
        if (!string.IsNullOrWhiteSpace(request.LocalInstancePath) && !Directory.Exists(request.LocalInstancePath))
            throw new ArgumentException("要导入的实例目录不存在");
        if (_installCts != null) throw new InvalidOperationException("已经有一个安装任务在进行中。");

        var jobId = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource();
        _installCts = cts;
        _installJobId = jobId;

        _ = Task.Run(async () =>
        {
            try
            {
                var instance = await _installer.InstallAsync(request, jobId,
                    progress => EmitEvent("install.progress", progress), cts.Token);
                InvalidateSizes();
                EmitEvent("install.done", new
                {
                    jobId,
                    instanceId = instance.Id,
                    instance = GetInstance(instance.Id)
                });
                EmitEvent("instances.changed", null);
            }
            catch (OperationCanceledException)
            {
                EmitEvent("install.cancelled", new { jobId });
            }
            catch (Exception ex)
            {
                LogError("instance.install", ex);
                EmitEvent("install.failed", new { jobId, message = Friendly(ex) });
            }
            finally
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_installCts, cts))
                    {
                        _installCts = null;
                        _installJobId = null;
                    }
                }
                cts.Dispose();
            }
        });
        return new { jobId };
    }

    private object CancelInstall()
    {
        var jobId = _installJobId;
        try { _installCts?.Cancel(); } catch { }
        return new { cancelled = jobId != null, jobId };
    }

    private object StartLaunch(string instanceId)
    {
        var instance = RequireInstance(instanceId);
        if (_launcher.IsRunning)
            throw new InvalidOperationException("已经有一个游戏在运行了。");
        if (_installCts != null)
            throw new InvalidOperationException("正在安装，等安装完成后再启动。");

        _settings.Update(new AppSettingsPatch { LastInstanceId = instance.Id });
        lock (_logGate)
        {
            _logPending.Clear();
            _logHistory.Clear();
            _logInstanceId = instance.Id;
        }
        lock (_stateGate) _lastStatus = null;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _launcher.StartAsync(instance.Id, OnGameLog, OnGameStatus, CancellationToken.None);
                EmitEvent("launch.started", result);
                EmitEvent("instances.changed", null);
                if (_settings.Snapshot().CloseOnLaunch)
                {
                    await Task.Delay(600);
                    _host.Minimize();
                }
            }
            catch (Exception ex)
            {
                LogError("launch.start", ex);
                OnGameStatus(new GameStatus
                {
                    InstanceId = instance.Id,
                    State = "failed",
                    Message = Friendly(ex),
                    Progress = 1
                });
            }
        });
        return new { accepted = true, instanceId = instance.Id };
    }

    private object LaunchState()
    {
        lock (_stateGate)
        {
            return new
            {
                running = _launcher.IsRunning,
                instanceId = _launcher.RunningInstanceId,
                status = _lastStatus
            };
        }
    }

    private void OnGameStatus(GameStatus status)
    {
        lock (_stateGate)
        {
            _lastStatus = status;
            if (status.Diagnosis != null) _diagnoses[status.InstanceId] = status.Diagnosis;
        }
        EmitEvent("launch.status", status);
        if (status.State is "exited" or "failed")
        {
            FlushLogs();
            EmitEvent("instances.changed", null);
        }
    }

    private void OnGameLog(GameLogLine line)
    {
        lock (_logGate)
        {
            _logPending.Add(line);
            _logHistory.AddLast(line);
            while (_logHistory.Count > HistoryLimit) _logHistory.RemoveFirst();
            if (_logPending.Count > 2000) _logPending.RemoveRange(0, _logPending.Count - 2000);
        }
    }

    private void FlushLogs()
    {
        if (_disposed) return;
        List<GameLogLine> lines;
        string? instanceId;
        lock (_logGate)
        {
            if (_logPending.Count == 0) return;
            lines = _logPending.ToList();
            _logPending.Clear();
            instanceId = _logInstanceId;
        }
        EmitEvent("launch.logs", new { instanceId, lines });
    }

    private object LogHistory(string? instanceId)
    {
        lock (_logGate)
        {
            if (_logHistory.Count > 0 &&
                (string.IsNullOrWhiteSpace(instanceId) ||
                 string.Equals(_logInstanceId, instanceId, StringComparison.OrdinalIgnoreCase)))
            {
                return new { source = "memory", instanceId = _logInstanceId, lines = _logHistory.ToList() };
            }
        }

        var lines = new List<GameLogLine>();
        try
        {
            var pattern = string.IsNullOrWhiteSpace(instanceId) ? "launch-*.log" : $"launch-{instanceId}-*.log";
            if (!string.IsNullOrWhiteSpace(instanceId)) RequireInstance(instanceId);
            var newest = Directory.Exists(_paths.LogsDir)
                ? Directory.EnumerateFiles(_paths.LogsDir, pattern)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (newest != null)
            {
                var all = File.ReadLines(newest.FullName, Encoding.UTF8).TakeLast(400).ToList();
                var sequence = 0L;
                foreach (var text in all)
                {
                    lines.Add(new GameLogLine
                    {
                        InstanceId = instanceId ?? "",
                        Sequence = ++sequence,
                        Text = text,
                        At = newest.LastWriteTime
                    });
                }
            }
        }
        catch { }
        return new { source = "file", instanceId, lines };
    }

    private async Task<object> ScanJavaAsync(bool force)
    {
        var result = await _java.ScanAsync(force, CancellationToken.None);
        var settings = _settings.Snapshot();
        var selected = _java.Pick(result.Runtimes, result.RecommendedMajor, settings.JavaPath);
        return new
        {
            runtimes = result.Runtimes,
            recommendedMajor = result.RecommendedMajor,
            selectedPath = selected?.Path,
            preferredPath = settings.JavaPath
        };
    }

    private object DescribeJava(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择一个 java.exe");
        var runtime = _java.Describe(path) ?? throw new ArgumentException("没有从这个路径识别到 Java");
        return runtime;
    }

    private object AddJavaPath(string path)
    {
        var runtime = _java.Describe(path) ?? throw new ArgumentException("没有从这个路径识别到 Java");
        var settings = _settings.Update(new AppSettingsPatch { JavaPath = runtime.Path });
        EmitEvent("settings.changed", settings);
        return runtime;
    }

    private object CancelJava()
    {
        var active = _javaCts != null;
        try { _javaCts?.Cancel(); } catch { }
        return new { cancelled = active };
    }

    private object StartJavaDownload(JavaDownloadQuery query)
    {
        var major = query.Major is >= 8 and <= 25 ? query.Major : 21;
        if (_javaCts != null) throw new InvalidOperationException("已经有一个 Java 下载任务在进行中。");
        var cts = new CancellationTokenSource();
        _javaCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<JavaProgress>(p => EmitEvent("java.progress", p));
                var runtime = await _java.DownloadAsync(major, progress, cts.Token);
                EmitEvent("java.done", runtime);
            }
            catch (OperationCanceledException) { EmitEvent("java.cancelled", new { major }); }
            catch (Exception ex)
            {
                LogError("java.download", ex);
                EmitEvent("java.failed", new { major, message = Friendly(ex) });
            }
            finally
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_javaCts, cts)) _javaCts = null;
                }
                cts.Dispose();
            }
        });
        return new { accepted = true, major };
    }

    private object AccountsPayload()
    {
        var accounts = _accounts.List();
        return new
        {
            items = accounts,
            activeId = accounts.FirstOrDefault(a => a.IsActive)?.Id,
            microsoftReady = !string.IsNullOrWhiteSpace(_settings.Snapshot().MicrosoftClientId),
            curseForgeReady = !string.IsNullOrWhiteSpace(_settings.Snapshot().CurseForgeApiKey)
        };
    }

    private object SkinPayload(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("账号不存在");
        var file = _accounts.SkinFile(accountId);
        string? dataUrl = null;
        if (file != null)
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length is > 0 and < 2 * 1024 * 1024)
                dataUrl = "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        return new { accountId, dataUrl };
    }

    private async Task<object> BeginMicrosoftAsync()
    {
        CancelMicrosoft();
        var info = await _accounts.BeginMicrosoftAsync(CancellationToken.None);
        var separator = info.VerificationUri.Contains('?') ? '&' : '?';
        var complete = $"{info.VerificationUri}{separator}otc={Uri.EscapeDataString(info.UserCode)}";
        var copied = _host.CopyText(info.UserCode);
        var opened = false;
        try
        {
            _host.OpenExternal(complete);
            opened = true;
        }
        catch { }

        var cts = new CancellationTokenSource();
        _authCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                var account = await _accounts.CompleteMicrosoftAsync(
                    message => EmitEvent("auth.waiting", new { message }), cts.Token);
                EmitEvent("auth.complete", new { account, accounts = AccountsPayload() });
                EmitEvent("accounts.changed", AccountsPayload());
            }
            catch (OperationCanceledException) { EmitEvent("auth.cancelled", null); }
            catch (Exception ex)
            {
                LogError("accounts.microsoft.complete", ex);
                EmitEvent("auth.failed", new { message = Friendly(ex) });
            }
            finally
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_authCts, cts)) _authCts = null;
                }
                cts.Dispose();
            }
        });
        return new DeviceCodeInfo
        {
            UserCode = info.UserCode,
            VerificationUri = info.VerificationUri,
            VerificationUriComplete = complete,
            ExpiresIn = info.ExpiresIn,
            Copied = copied,
            Opened = opened
        };
    }

    private void CancelMicrosoft()
    {
        _accounts.CancelPendingMicrosoft();
        try
        {
            lock (_stateGate)
            {
                _authCts?.Cancel();
            }
        }
        catch { }
    }

    private object ShaderRecommendation(string instanceId)
    {
        var instance = RequireInstance(instanceId);
        var installed = _modrinth.ListInstalled(instance.Id);
        var iris = installed.Any(item =>
            item.Title.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            item.ProjectId.Equals("YL57xq9U", StringComparison.OrdinalIgnoreCase));
        var optifine = string.Equals(instance.Loader, "optifine", StringComparison.OrdinalIgnoreCase);
        var fabricLike = instance.Loader is "fabric" or "quilt";
        return new
        {
            kind = fabricLike ? "iris" : "optifine",
            installed = fabricLike ? iris : optifine,
            supported = fabricLike || string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase),
            label = fabricLike ? "Iris" : "OptiFine",
            hint = fabricLike
                ? "安装 Iris 与 Sodium，为光影提供支持"
                : string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase)
                    ? "安装 OptiFine，原版即可使用光影"
                    : "Forge / NeoForge 实例暂不支持一键光影"
        };
    }

    private object InstallShaderSupportAsync(ShaderInstallRequest request)
    {
        var instance = RequireInstance(request.InstanceId);
        var kind = (request.Kind ?? "").ToLowerInvariant();
        if (kind == "iris")
        {
            if (instance.Loader is not ("fabric" or "quilt"))
                throw new InvalidOperationException("Iris 只能安装在 Fabric 或 Quilt 实例中。");
            var job = StartContentInstall(new ContentInstallRequest
            {
                InstanceId = instance.Id,
                ProjectId = "YL57xq9U",
                ProjectType = "mod",
                Source = "modrinth"
            }, update: false);
            return new { kind, job };
        }

        if (kind != "optifine")
            throw new ArgumentException("未知的光影支持类型");
        if (!string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OptiFine 一键安装只支持原版实例。");
        if (_shaderCts != null)
            throw new InvalidOperationException("已经有一个光影组件正在安装。");

        var jobId = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource();
        _shaderCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                EmitEvent("shader.progress", new { jobId, stage = "resolve", message = "正在查找 OptiFine 版本" });
                var builds = await Web.GetJsonAsync<List<OptiFineBuild>>(
                    $"{Mirror.Host}/optifine/{Uri.EscapeDataString(instance.GameVersion)}",
                    TimeSpan.FromSeconds(20), cts.Token);
                var build = builds.FirstOrDefault(x => !x.FileName.StartsWith("preview_", StringComparison.OrdinalIgnoreCase))
                            ?? builds.FirstOrDefault()
                            ?? throw new InvalidOperationException("没有找到适配这个版本的 OptiFine。");
                var url = Mirror.OptiFineInstaller(instance.GameVersion, build.Type, build.Patch);
                var progress = new DownloadProgress();
                var downloader = new Downloader(_settings.Snapshot().DownloadSource);
                var result = await _loaderInstaller.InstallOptiFineAsync(
                    instance.GameVersion,
                    $"{build.Type}_{build.Patch}",
                    url,
                    downloader,
                    progress,
                    label => EmitEvent("shader.progress", new
                    {
                        jobId,
                        stage = "install",
                        message = label,
                        receivedBytes = progress.CompletedBytes,
                        totalBytes = progress.TotalBytes,
                        bytesPerSecond = progress.BytesPerSecond
                    }),
                    cts.Token);
                instance.Loader = "optifine";
                instance.LoaderVersion = $"{build.Type}_{build.Patch}";
                instance.ProfileId = result.ProfileId;
                _instances.Save(instance);
                InvalidateSizes();
                EmitEvent("instances.changed", null);
                EmitEvent("shader.done", new { jobId, kind = "optifine", instanceId = instance.Id, version = instance.LoaderVersion });
            }
            catch (OperationCanceledException) { EmitEvent("shader.cancelled", new { jobId }); }
            catch (Exception ex)
            {
                LogError("shader.install", ex);
                EmitEvent("shader.failed", new { jobId, message = Friendly(ex) });
            }
            finally
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_shaderCts, cts)) _shaderCts = null;
                }
                cts.Dispose();
            }
        });
        return new { kind, jobId };
    }

    private object StartContentInstall(ContentInstallRequest request, bool update)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceId)) throw new ArgumentException("请先选择一个实例");
        if (string.IsNullOrWhiteSpace(request.ProjectId)) throw new ArgumentException("项目 ID 为空");
        RequireInstance(request.InstanceId);

        var requestId = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource();
        _contentJobs[requestId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                ContentInstallResult result;
                if (update && request.ProjectId.StartsWith("cf-", StringComparison.OrdinalIgnoreCase))
                {
                    result = await _curseForge.InstallAsync(new ContentInstallRequest
                    {
                        InstanceId = request.InstanceId,
                        ProjectId = request.ProjectId[3..],
                        ProjectType = request.ProjectType,
                        Source = "curseforge"
                    }, value => EmitEvent("content.progress", new { requestId, progress = value }), cts.Token);
                }
                else if (update)
                {
                    var instance = RequireInstance(request.InstanceId);
                    result = await _modrinth.InstallOneForUpdateAsync(instance, request.ProjectId,
                        request.ProjectType, cts.Token);
                }
                else if (request.Source == "curseforge")
                {
                    result = await _curseForge.InstallAsync(request, value =>
                        EmitEvent("content.progress", new { requestId, progress = value }), cts.Token);
                }
                else
                {
                    result = await _modrinth.InstallAsync(request, value =>
                        EmitEvent("content.progress", new { requestId, progress = value }), cts.Token);
                }
                InvalidateSizes();
                EmitEvent("content.done", new { requestId, result, instanceId = request.InstanceId });
                EmitEvent("content.changed", new { instanceId = request.InstanceId });
                EmitEvent("instances.changed", null);
            }
            catch (OperationCanceledException)
            {
                EmitEvent("content.cancelled", new { requestId });
            }
            catch (Exception ex)
            {
                LogError("modrinth.install", ex);
                EmitEvent("content.failed", new { requestId, message = Friendly(ex), instanceId = request.InstanceId });
            }
            finally
            {
                _contentJobs.TryRemove(requestId, out _);
                cts.Dispose();
            }
        });
        return new { accepted = true, requestId };
    }

    private object CancelContent(string requestId)
    {
        if (!_contentJobs.TryGetValue(requestId, out var cts)) return new { cancelled = false };
        try { cts.Cancel(); } catch { }
        return new { cancelled = true };
    }

    private async Task<object> NewsAsync(bool force)
    {
        var newsTask = SafeNewsAsync(() => _news.GetNewsAsync(force, CancellationToken.None));
        var patchTask = SafeNewsAsync(() => _news.GetPatchNotesAsync(force, CancellationToken.None));
        await Task.WhenAll(newsTask, patchTask);
        var news = await newsTask;
        var patches = await patchTask;
        if (news.Count == 0 && patches.Count == 0)
            throw new IOException("这次没有取到远方的消息，请稍后再试。");
        return new { news, patches };
    }

    private async Task<List<NewsItem>> SafeNewsAsync(Func<Task<List<NewsItem>>> action)
    {
        try { return await action(); }
        catch { return []; }
    }

    private object? LastCrash(string instanceId)
    {
        var instance = RequireInstance(instanceId);
        lock (_stateGate)
        {
            if (_diagnoses.TryGetValue(instance.Id, out var cached)) return cached;
        }
        if (!string.Equals(instance.LastExitReason, "crash", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var diagnosis = new CrashAnalyzer(_paths).Diagnose(instance, instance.LastExitCode ?? -1);
            lock (_stateGate) _diagnoses[instance.Id] = diagnosis;
            return diagnosis;
        }
        catch { return null; }
    }

    private object OpenPath(PathQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Path)) throw new ArgumentException("路径为空");
        var full = Path.GetFullPath(query.Path);
        if (!_paths.IsUnderRoot(full))
            throw new InvalidOperationException("只能打开启动器数据目录下的文件");
        _host.RevealInExplorer(full);
        return new { opened = true };
    }

    private Instance RequireInstance(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("缺少实例 ID");
        return _instances.Get(id) ?? throw new InvalidOperationException("实例不存在");
    }

    private long IconVersion(string instanceId)
    {
        try
        {
            var icon = Path.Combine(_paths.InstanceDir(instanceId), "icon.png");
            return File.Exists(icon) ? File.GetLastWriteTimeUtc(icon).Ticks : 0;
        }
        catch { return 0; }
    }

    private object InstanceIcon(string instanceId)
    {
        var instance = RequireInstance(instanceId);
        var icon = Path.Combine(_paths.InstanceDir(instance.Id), "icon.png");
        if (!File.Exists(icon)) return new { dataUrl = (string?)null };
        try
        {
            var info = new FileInfo(icon);
            if (info.Length <= 0 || info.Length > 2 * 1024 * 1024) return new { dataUrl = (string?)null };
            return new { dataUrl = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(icon)) };
        }
        catch
        {
            return new { dataUrl = (string?)null };
        }
    }

    private async Task<object?> ChooseInstanceIconAsync(string instanceId)
    {
        var instance = RequireInstance(instanceId);
        var path = await _host.PickImageAsync();
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) throw new ArgumentException("图片文件不存在");
        var info = new FileInfo(path);
        if (info.Length > 16 * 1024 * 1024) throw new ArgumentException("图片太大，请选择 16 MB 以内的图片");
        var destination = Path.Combine(_paths.InstanceDir(instance.Id), "icon.png");
        try
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                return InstanceIcon(instance.Id);
            using var input = File.OpenRead(path);
            using var source = Image.FromStream(input);
            if (source.Width < 8 || source.Height < 8) throw new ArgumentException("图片尺寸太小");
            using var bitmap = new Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var side = Math.Min(source.Width, source.Height);
                var sourceRect = new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
                graphics.DrawImage(source, new Rectangle(0, 0, 256, 256), sourceRect, GraphicsUnit.Pixel);
            }
            bitmap.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException("无法读取这张图片：" + ex.Message, ex);
        }
        EmitEvent("instances.changed", null);
        return InstanceIcon(instance.Id);
    }

    private static string LoaderLabel(string loader)
        => loader.ToLowerInvariant() switch
        {
            "fabric" => "Fabric",
            "quilt" => "Quilt",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "optifine" => "OptiFine",
            _ => "原版"
        };

    private static T Param<T>(JsonElement element) where T : new()
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions) ?? new T();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("请求参数不完整：" + ex.Message);
        }
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var value = source;
        source = null;
        if (value == null) return;
        try { value.Cancel(); } catch { }
        try { value.Dispose(); } catch { }
    }

    private sealed class VersionQuery
    {
        public string GameVersion { get; set; } = "";
    }

    private sealed class IdQuery
    {
        public string Id { get; set; } = "";
    }

    private sealed class OptionalIdQuery
    {
        public string? InstanceId { get; set; }
    }

    private sealed class LogHistoryQuery
    {
        public string? InstanceId { get; set; }
    }

    private sealed class FlagQuery
    {
        public bool Force { get; set; }
    }

    private sealed class PathQuery
    {
        public string Path { get; set; } = "";
    }

    private sealed class LocalImportRequest
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string Loader { get; set; } = "vanilla";
        public string? LoaderVersion { get; set; }
    }

    private sealed class ClipboardQuery
    {
        public string Text { get; set; } = "";
    }

    private sealed class NoteQuery
    {
        public string Text { get; set; } = "";
    }

    private sealed class UrlQuery
    {
        public string Url { get; set; } = "";
    }

    private sealed class JavaDownloadQuery
    {
        public int Major { get; set; } = 21;
    }

    private sealed class ContentVersionsQuery
    {
        public string InstanceId { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string ProjectType { get; set; } = "mod";
        public string Source { get; set; } = "modrinth";
    }

    private sealed class CurseLinkQuery
    {
        public string InstanceId { get; set; } = "";
        public string Link { get; set; } = "";
        public string ProjectType { get; set; } = "mod";
    }

    private sealed class OptiFineBuild
    {
        [JsonPropertyName("mcversion")] public string McVersion { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("patch")] public string Patch { get; set; } = "";
        [JsonPropertyName("filename")] public string FileName { get; set; } = "";
    }

    private class ContentQuery
    {
        public string InstanceId { get; set; } = "";
        public string ProjectId { get; set; } = "";
    }

    private sealed class ToggleContentQuery : ContentQuery
    {
        public bool Enabled { get; set; }
    }

    private sealed class InstanceUpdateRequest
    {
        public string Id { get; set; } = "";
        public string? DisplayName { get; set; }
        public int? MemoryMb { get; set; }
        public string? JavaPath { get; set; }
        public bool ClearJava { get; set; }
    }
}
