using System.Diagnostics;
using System.Text;
using Chisel.Core.Models;
using Chisel.Core.Services;
using Chisel.Core.Util;

namespace Chisel.Core.Minecraft;

public sealed class GameLauncher
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly InstanceStore _instances;
    private readonly JavaManager _java;
    private readonly AccountService _accounts;
    private readonly VersionResolver _resolver;
    private readonly CrashAnalyzer _crash;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _processGate = new();
    private Process? _process;
    private StopFlag? _stopFlag;
    private long _sequence;

    public GameLauncher(
        AppPaths paths,
        SettingsService settings,
        InstanceStore instances,
        JavaManager java,
        AccountService accounts,
        VersionResolver resolver,
        CrashAnalyzer crash)
    {
        _paths = paths;
        _settings = settings;
        _instances = instances;
        _java = java;
        _accounts = accounts;
        _resolver = resolver;
        _crash = crash;
    }

    public bool IsRunning
    {
        get
        {
            lock (_processGate)
            {
                try { return _process is { HasExited: false }; }
                catch { return false; }
            }
        }
    }

    public string? RunningInstanceId { get; private set; }

    public async Task<LaunchStartResult> StartAsync(
        string instanceId,
        Action<GameLogLine> onLog,
        Action<GameStatus> onStatus,
        CancellationToken ct)
    {
        await _startGate.WaitAsync(ct);
        try
        {
            if (IsRunning) throw new InvalidOperationException("已经有一个游戏在运行了。");
            var instance = _instances.Get(instanceId) ?? throw new InvalidOperationException("实例不存在");

            Status(onStatus, instance.Id, "checking", "正在核对版本与文件", 0.02);
            var settings = _settings.Snapshot();
            var resolved = await _resolver.ResolveAsync(instance, ct);
            var requiredJava = Math.Max(resolved.RequiredJava, instance.RequiredJava ?? 0);
            var gameDir = _paths.GameDir(instance.Id);
            var versionDir = _paths.VersionDir(instance.GameVersion);
            var warnings = new List<string>();

            ValidateFiles(resolved, instance, versionDir);

            Status(onStatus, instance.Id, "java", $"正在准备 Java {requiredJava}", 0.12);            var runtime = await ResolveJavaAsync(instance, settings, requiredJava, onStatus, ct);

            Status(onStatus, instance.Id, "account", "正在准备账号", 0.32);
            var user = await _accounts.GetLaunchUserAsync(settings.ActiveAccountId, ct);

            var memory = ResolveMemory(instance, settings);
            if (memory > SystemMemory.TotalPhysicalMb() * 0.7)
                warnings.Add("分配的内存超过了物理内存的七成，系统和其他程序可能会变得卡顿。");

            var plan = LaunchPlanBuilder.Build(
                resolved, instance, user, runtime, settings,
                _paths.Root, _paths.LibrariesDir, _paths.AssetsDir, versionDir, gameDir, memory);
            warnings.AddRange(plan.Warnings);

            var missing = plan.ClassPath
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !File.Exists(p))
                .Take(4)
                .ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException("运行文件缺失：" + string.Join("、", missing.Select(Path.GetFileName)) + "。请重新安装这个实例。");

            Directory.CreateDirectory(plan.NativesDir);
            Directory.CreateDirectory(gameDir);
            _instances.SetLastPlayed(instance.Id, 0, null, null);

            var launchStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var logPath = Path.Combine(_paths.LogsDir, $"launch-{instance.Id}-{launchStamp}.log");
            var capture = !settings.CloseOnLaunch;

            var psi = new ProcessStartInfo
            {
                FileName = runtime.Path,
                WorkingDirectory = gameDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = capture,
                RedirectStandardError = capture
            };
            if (capture)
            {
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
            }
            foreach (var arg in plan.Args) psi.ArgumentList.Add(arg);

            Status(onStatus, instance.Id, "starting", "正在打开游戏窗口", 0.9);
            Process process;
            try
            {
                process = Process.Start(psi) ?? throw new InvalidOperationException("系统没有创建游戏进程。");
            }
            catch (Exception ex)
            {
                Status(onStatus, instance.Id, "failed", "启动失败：" + ex.Message, 1);
                throw new InvalidOperationException("无法启动 Java 进程：" + ex.Message, ex);
            }

            var flag = new StopFlag();
            var startedAt = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();
            lock (_processGate)
            {
                _process = process;
                _stopFlag = flag;
                RunningInstanceId = instance.Id;
                _sequence = 0;
            }

            Status(onStatus, instance.Id, "running", "游戏已启动", 1);
            _ = Task.Run(() => MonitorAsync(process, instance, flag, capture, stopwatch, logPath, plan, onLog, onStatus));

            return new LaunchStartResult
            {
                InstanceId = instance.Id,
                Pid = process.Id,
                Warnings = warnings,
                JavaPath = runtime.Path,
                MemoryMb = memory,
                StartedAt = startedAt
            };
        }
        finally
        {
            _startGate.Release();
        }
    }

    public bool Stop()
    {
        Process? process;
        StopFlag? flag;
        lock (_processGate)
        {
            process = _process;
            flag = _stopFlag;
        }
        if (process == null) return false;
        if (flag != null) flag.Stopped = true;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return true;
            }
        }
        catch { }
        return false;
    }

    private async Task<JavaRuntime> ResolveJavaAsync(
        Instance instance,
        AppSettings settings,
        int requiredJava,
        Action<GameStatus> onStatus,
        CancellationToken ct)
    {
        var runtimes = await _java.GetRuntimesAsync(false, ct);
        var preferred = instance.JavaPathOverride ?? settings.JavaPath;
        var runtime = _java.Pick(runtimes, requiredJava, preferred);

        if (runtime != null && requiredJava <= 8 && runtime.Major > 8 && settings.AutoDownloadJava)
            runtime = null;

        if (runtime != null) return runtime;
        if (!settings.AutoDownloadJava)
            throw new InvalidOperationException($"没有找到 Java {requiredJava}。请安装对应的 Java，或在设置里允许自动下载。");

        Status(onStatus, instance.Id, "downloading-java", $"正在下载 Java {requiredJava} 运行时", 0.15);
        var progress = new CallbackProgress<JavaProgress>(p =>
        {
            if (p.Done) return;
            var fraction = p.TotalBytes > 0 ? 0.15 + 0.15 * Math.Clamp((double)p.ReceivedBytes / p.TotalBytes, 0, 1) : 0.2;
            if (string.Equals(p.Stage, "download", StringComparison.Ordinal))
            {
                Status(onStatus, instance.Id, "downloading-java",
                    $"正在下载 Java {p.Major} · {FormatBytes(p.ReceivedBytes)} / {FormatBytes(p.TotalBytes)}",
                    fraction, p.ReceivedBytes, p.TotalBytes);
            }
            else
            {
                Status(onStatus, instance.Id, "downloading-java", p.Message ?? "正在安装 Java 运行时", 0.28, p.ReceivedBytes, p.TotalBytes);
            }
        });

        try
        {
            var downloaded = await _java.DownloadAsync(requiredJava, progress, ct);
            Status(onStatus, instance.Id, "java", $"Java {downloaded.Major} 已就绪", 0.3);
            return downloaded;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Java 运行时下载失败：" + ex.Message + "。可以在设置里手动指定 Java。", ex);
        }
    }

    private void ValidateFiles(ResolvedVersion version, Instance instance, string versionDir)
    {
        if (!File.Exists(Path.Combine(versionDir, "version.json")))
            throw new InvalidOperationException("版本描述缺失，请在实例库中重新安装。");
        if (!File.Exists(version.ClientJarPath))
            throw new InvalidOperationException("客户端文件缺失，请在实例库中重新安装。");
        if (version.AssetIndexPath != null && !File.Exists(version.AssetIndexPath))
            throw new InvalidOperationException("资源索引缺失，请在版本库中重新安装。");
        if (string.IsNullOrEmpty(version.MainClass))
            throw new InvalidOperationException("版本描述里没有主类，无法启动。");

        var missing = new List<string>();
        foreach (var library in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(library.Rules)) continue;
            var artifact = LibraryUtil.ArtifactFor(library);
            var relative = string.IsNullOrWhiteSpace(artifact?.Path)
                ? LibraryUtil.MavenPath(library.Name)
                : artifact!.Path!;
            var full = Path.Combine(_paths.LibrariesDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) missing.Add(Path.GetFileName(full));
            if (missing.Count >= 4) break;
        }
        if (missing.Count > 0)
            throw new InvalidOperationException("运行库缺失：" + string.Join("、", missing) + "。请重新安装这个实例。");
    }

    private static int ResolveMemory(Instance instance, AppSettings settings)
    {
        var memory = instance.MemoryMbOverride
                     ?? (settings.AutoMemory ? SystemMemory.DefaultGameMemoryMb() : settings.MemoryMb)
                     ?? SystemMemory.DefaultGameMemoryMb();
        return Math.Clamp(memory / 256 * 256, 1024, 32768);
    }

    private async Task MonitorAsync(
        Process process,
        Instance instance,
        StopFlag flag,
        bool capture,
        Stopwatch stopwatch,
        string logPath,
        LaunchPlan plan,
        Action<GameLogLine> onLog,
        Action<GameStatus> onStatus)
    {
        StreamWriter? writer = null;
        var writerLock = new object();
        try
        {
            try
            {
                writer = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine($"Chisel 0.1.0 · {instance.DisplayName}");
                writer.WriteLine($"Java: {plan.JavaPath}");
                writer.WriteLine($"内存: {plan.MemoryMb} MB · 启动: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
                foreach (var warning in plan.Warnings) writer.WriteLine("提醒: " + warning);
                writer.WriteLine(new string('-', 60));
            }
            catch { writer = null; }

            void Write(string text)
            {
                if (writer == null) return;
                try
                {
                    lock (writerLock) writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
                }
                catch { }
            }

            var outputTask = capture
                ? PumpAsync(process.StandardOutput, false, instance.Id, onLog, Write)
                : Task.CompletedTask;
            var errorTask = capture
                ? PumpAsync(process.StandardError, true, instance.Id, onLog, Write)
                : Task.CompletedTask;

            await process.WaitForExitAsync().ConfigureAwait(false);
            try { await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch { }

            stopwatch.Stop();
            var exitCode = -1;
            try { exitCode = process.ExitCode; } catch { }
            var stopped = flag.Stopped;
            var crash = exitCode != 0 && !stopped;
            var reason = stopped ? "stopped" : crash ? "crash" : "normal";
            var seconds = Math.Max(0, (long)stopwatch.Elapsed.TotalSeconds);
            _instances.SetLastPlayed(instance.Id, seconds, exitCode, reason);

            CrashDiagnosis? diagnosis = null;
            if (crash)
            {
                try { diagnosis = _crash.Diagnose(instance, exitCode); }
                catch { }
            }

            Status(onStatus, instance.Id,
                crash ? "failed" : "exited",
                stopped ? "你结束了这次游戏" : crash ? "游戏异常退出" : "游戏正常退出",
                1, exitCode: exitCode, diagnosis: diagnosis);

            if (writer != null)
            {
                Write(crash ? $"进程异常退出，退出码 {exitCode}" : stopped ? "进程已被结束" : "进程正常退出");
            }
        }
        catch (Exception ex)
        {
            Status(onStatus, instance.Id, "failed", "游戏进程意外中断：" + ex.Message, 1);
        }
        finally
        {
            try { writer?.Dispose(); } catch { }
            try { process.Dispose(); } catch { }
            lock (_processGate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _stopFlag = null;
                    RunningInstanceId = null;
                }
            }
        }
    }

    private async Task PumpAsync(StreamReader reader, bool stderr, string instanceId, Action<GameLogLine> onLog, Action<string> write)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var safe = LaunchPlanBuilder.RedactLogLine(line);
                write(safe);
                var payload = new GameLogLine
                {
                    InstanceId = instanceId,
                    Sequence = Interlocked.Increment(ref _sequence),
                    Text = safe,
                    Stderr = stderr,
                    At = DateTimeOffset.Now
                };
                try { onLog(payload); } catch { }
            }
        }
        catch { }
    }

    private static void Status(
        Action<GameStatus> onStatus,
        string instanceId,
        string state,
        string message,
        double progress,
        long received = 0,
        long total = 0,
        int? exitCode = null,
        CrashDiagnosis? diagnosis = null)
    {
        try
        {
            onStatus(new GameStatus
            {
                InstanceId = instanceId,
                State = state,
                Message = message,
                Progress = Math.Clamp(progress, 0, 1),
                BytesReceived = received,
                BytesTotal = total,
                ExitCode = exitCode,
                Diagnosis = diagnosis
            });
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private sealed class StopFlag
    {
        public volatile bool Stopped;
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
