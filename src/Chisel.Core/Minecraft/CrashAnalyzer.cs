using System.Text;
using System.Text.RegularExpressions;
using Chisel.Core.Models;

namespace Chisel.Core.Minecraft;

public sealed class CrashAnalyzer
{
    private readonly AppPaths _paths;
    private static readonly Regex ModName = new(@"(?:Mod|mod|Mod ID|mod id)\s*['“""]?([A-Za-z0-9_.\- ]{2,48})['”""]?", RegexOptions.Compiled);

    public CrashAnalyzer(AppPaths paths) => _paths = paths;

    public CrashDiagnosis Diagnose(Instance instance, int exitCode)
    {
        var gameDir = _paths.GameDir(instance.Id);
        var latestLog = Path.Combine(gameDir, "logs", "latest.log");
        var crashReport = NewestFile(Path.Combine(gameDir, "crash-reports"), "crash-*.txt");
        var hsErr = NewestFile(gameDir, "hs_err_pid*.log") ?? NewestFile(_paths.InstanceDir(instance.Id), "hs_err_pid*.log");

        var logText = ReadTail(latestLog, 280_000);
        var crashText = crashReport == null ? "" : ReadTail(crashReport, 220_000);
        var hsText = hsErr == null ? "" : ReadTail(hsErr, 100_000);
        var text = string.Join("\n", crashText, logText, hsText);

        var suggestions = new List<string>();
        var suspected = ExtractMods(text);

        string title = "这次没能进去";
        string summary;
        string severity = "error";
        string? evidence = null;

        if (Contains(text, "OutOfMemoryError", "Java heap space", "GC overhead limit exceeded", "failed to allocate"))
        {
            title = "内存不够用了";
            summary = "游戏在申请内存时被 Java 拒绝，通常是分配的内存偏小，或模组/光影占用了过多内存。";
            evidence = FirstLine(text, "OutOfMemoryError", "Java heap space", "GC overhead limit exceeded");
            suggestions.Add("在实例设置里把内存调大 1024–2048 MB，但不要超过物理内存的七成。");
            suggestions.Add("减少同时加载的光影和大型模组，或改用 Sodium 这类优化模组。");
        }
        else if (Contains(text, "UnsupportedClassVersionError", "has been compiled by a more recent version", "class file version", "requires Java"))
        {
            title = "Java 版本对不上";
            summary = "某个类或模组需要更高版本的 Java，也可能是实例指向了旧版 Java。";
            evidence = FirstLine(text, "UnsupportedClassVersionError", "class file version", "requires Java");
            suggestions.Add("在设置 → Java 中让启动器自动选择运行时，或手动指定匹配的 Java 版本。");
            suggestions.Add("如果刚刚更新了游戏版本，重新安装一次加载器通常能一并更新依赖。");
        }
        else if (Contains(text, "Incompatible mod set", "Mod resolution encountered", "Missing or unsupported mandatory dependencies",
                     "DuplicateModsFoundException", "Mixin apply failed", "Mixin transformation", "NoSuchMethodError", "NoClassDefFoundError",
                     "InjectionError", "fabric-loader"))
        {
            title = "模组之间没有谈拢";
            summary = "加载器在组装模组时发生冲突，常见原因是模组版本与游戏/加载器不匹配，或缺少前置。";
            evidence = FirstLine(text, "Incompatible mod set", "Mod resolution", "Missing or unsupported", "Mixin", "NoSuchMethodError", "NoClassDefFoundError");
            suggestions.Add("按下面的线索检查同名或功能重复的模组，先只保留必需的前置再逐个加回。");
            suggestions.Add("确认模组支持当前游戏版本和加载器；在内容页安装会自动选择兼容版本。");
            if (suspected.Count > 0)
                suggestions.Add("可以从这几个名字开始排查：" + string.Join("、", suspected.Take(4)) + "。");
        }
        else if (Contains(text, "GLFW error", "Failed to create window", "Pixel format not accelerated", "WGL", "OpenGL",
                     "Failed to initialize GLFW", "Couldn't set pixel format"))
        {
            title = "图形窗口没有打开";
            summary = "游戏在创建 OpenGL 窗口时失败，通常与显卡驱动、远程桌面或双显卡切换有关。";
            evidence = FirstLine(text, "GLFW error", "Failed to create window", "Pixel format", "OpenGL", "WGL");
            suggestions.Add("更新显卡驱动；笔记本请确认游戏使用独立显卡运行。");
            suggestions.Add("如果正在使用远程桌面，尝试在本机屏幕上启动，或关闭远程会话后重试。");
        }
        else if (Contains(text, "Access is denied", "AccessDeniedException", "The process cannot access the file", "being used by another process"))
        {
            title = "文件被占用了";
            summary = "游戏无法读写实例文件，可能被杀毒软件、同步盘或另一个启动进程占用。";
            evidence = FirstLine(text, "Access is denied", "AccessDeniedException", "cannot access the file", "being used by another process");
            suggestions.Add("关闭可能占用游戏目录的软件，或把启动器数据目录加入杀毒软件白名单。");
            suggestions.Add("确认没有另一个 Minecraft 进程正在使用同一个实例。");
        }
        else if (Contains(text, "ZipException", "invalid CEN header", "invalid entry", "Corrupted", "SHA-1", "ClassNotFoundException",
                     "Could not find or load main class", "NoClassDefFoundError: net/minecraft"))
        {
            title = "游戏文件不完整";
            summary = "启动器找不到或无法读取某个运行文件，可能是上次安装中断、磁盘写入失败或文件被修改。";
            evidence = FirstLine(text, "ZipException", "invalid CEN header", "Corrupted", "ClassNotFoundException", "Could not find or load main class");
            suggestions.Add("在版本库中对该实例执行一次修复/重新安装。");
            suggestions.Add("检查磁盘空间和杀毒软件隔离记录，确认文件没有被移走。");
        }
        else if (Contains(text, "Invalid session", "Failed to verify username", "AuthenticationException", "invalid_token"))
        {
            title = "账号凭据失效了";
            summary = "服务器或 Mojang 服务没有接受当前登录凭据，正版账号的令牌可能已过期。";
            evidence = FirstLine(text, "Invalid session", "Failed to verify username", "AuthenticationException", "invalid_token");
            suggestions.Add("在账号页重新登录 Microsoft 账号，然后再启动。");
            suggestions.Add("离线账号只能进入开启了离线验证的服务器。");
        }
        else
        {
            summary = exitCode switch
            {
                -1073741819 => "游戏进程发生了访问冲突（0xC0000005），常见于显卡驱动、模组注入或本地库不兼容。",
                -1073740791 => "游戏进程因栈溢出或安全异常（0xC0000409）退出，可能是模组或驱动导致。",
                -1073741515 => "游戏缺少运行所需的 DLL，可能是图形运行库或系统组件不完整。",
                1 => "游戏进程异常退出。下面的日志末尾通常能找到第一处真正的报错。",
                _ => $"游戏以退出码 {exitCode} 结束。"
            };
            severity = "warning";
            suggestions.Add("查看下方控制台的最后几十行，找到第一个 Exception 或 Caused by。");
            suggestions.Add("若刚安装了模组或光影，先禁用最近加入的内容再试一次。");
        }

        return new CrashDiagnosis
        {
            Severity = severity,
            Title = title,
            Summary = summary,
            Evidence = Truncate(evidence, 500),
            Suggestions = suggestions,
            SuspectedMods = suspected.Take(8).ToList(),
            LatestLogPath = File.Exists(latestLog) ? latestLog : null,
            CrashReportPath = crashReport,
            ExitCode = exitCode
        };
    }

    private static List<string> ExtractMods(string text)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ModName.Matches(text))
        {
            var name = match.Groups[1].Value.Trim().Trim('\'', '"', '“', '”', ' ');
            if (name.Length < 2) continue;
            if (name.Equals("resolution", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("loading", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("list", StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(name)) found.Add(name);
            if (found.Count >= 20) break;
        }
        return found;
    }

    private static string? FirstLine(string text, params string[] needles)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            foreach (var needle in needles)
            {
                if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return line.Trim();
            }
        }
        return null;
    }

    private static bool Contains(string text, params string[] needles)
        => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max] + "…";

    private string? NewestFile(string dir, string pattern)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            return Directory.EnumerateFiles(dir, pattern)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string ReadTail(string? path, int maxBytes)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(stream.Length, maxBytes);
            stream.Seek(-length, SeekOrigin.End);
            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var firstNewline = text.IndexOf('\n');
            return firstNewline > 0 && firstNewline < 400 ? text[(firstNewline + 1)..] : text;
        }
        catch { return ""; }
    }
}
