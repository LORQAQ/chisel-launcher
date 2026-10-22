using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chisel.Core.Models;
using Chisel.Core.Net;
using Microsoft.Win32;

namespace Chisel.Core.Minecraft;

public sealed class JavaRuntime
{
    public string Path { get; init; } = "";
    public string JavawPath { get; init; } = "";
    public int Major { get; init; }
    public string Version { get; init; } = "";
    public string Vendor { get; init; } = "";
    public bool Managed { get; init; }
    public bool Is64BitHint { get; init; } = true;
}

public sealed class JavaScanResult
{
    public List<JavaRuntime> Runtimes { get; init; } = [];
    public int RecommendedMajor { get; init; }
    public string? SelectedPath { get; init; }
    public string? Error { get; init; }
}

public sealed class JavaProgress
{
    public string Stage { get; init; } = "";
    public int Major { get; init; }
    public long ReceivedBytes { get; init; }
    public long TotalBytes { get; init; }
    public string? Message { get; init; }
    public bool Done { get; init; }
    public string? Path { get; init; }
}

public sealed class JavaManager
{
    private readonly AppPaths _paths;
    private readonly object _cacheGate = new();
    private List<JavaRuntime>? _cached;
    private DateTimeOffset _cachedAt;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public JavaManager(AppPaths paths) => _paths = paths;

    public async Task<JavaScanResult> ScanAsync(bool force, CancellationToken ct)
    {
        var runtimes = await GetRuntimesAsync(force, ct);
        return new JavaScanResult
        {
            Runtimes = runtimes,
            RecommendedMajor = 21
        };
    }

    public async Task<List<JavaRuntime>> GetRuntimesAsync(bool force, CancellationToken ct)
    {
        lock (_cacheGate)
        {
            if (!force && _cached != null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(45))
                return _cached;
        }

        var result = await Task.Run(() => Discover(ct), ct);
        lock (_cacheGate)
        {
            _cached = result;
            _cachedAt = DateTimeOffset.UtcNow;
        }
        return result;
    }

    private List<JavaRuntime> Discover(CancellationToken ct)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in new[] { "JAVA_HOME", "JDK_HOME", "GRAALVM_HOME", "JRE_HOME" })
            AddJavaHome(Environment.GetEnvironmentVariable(variable), candidates);

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var exe = Path.Combine(dir.Trim().Trim('"'), "java.exe");
                if (File.Exists(exe)) candidates.Add(exe);
            }
            catch { }
        }

        AddRegistryCandidates(candidates, ct);

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var roots = new List<string>
        {
            _paths.JavaDir,
            Path.Combine(roaming, ".minecraft", "runtime"),
            Path.Combine(roaming, ".minecraft", "jre"),
            Path.Combine(roaming, ".minecraft", "java"),
            Path.Combine(roaming, ".hmcl"),
            Path.Combine(profile, ".hmcl"),
            Path.Combine(roaming, "PrismLauncher", "java"),
            Path.Combine(roaming, "MultiMC", "java"),
            Path.Combine(roaming, "com.modrinth.theseus", "meta", "jre"),
            Path.Combine(roaming, "PCL"),
            Path.Combine(roaming, "PCL2"),
            Path.Combine(local, "PCL"),
            Path.Combine(local, "Programs", "Eclipse Adoptium"),
            Path.Combine(local, "Programs", "Microsoft"),
            Path.Combine(local, "Programs", "Zulu"),
            Path.Combine(local, "Programs", "BellSoft"),
            Path.Combine(local, "Programs", "Amazon Corretto"),
            Path.Combine(profile, ".jdks"),
            Path.Combine(programFiles, "Java"),
            Path.Combine(programFiles, "Eclipse Adoptium"),
            Path.Combine(programFiles, "Eclipse Foundation"),
            Path.Combine(programFiles, "Microsoft"),
            Path.Combine(programFiles, "Amazon Corretto"),
            Path.Combine(programFiles, "Zulu"),
            Path.Combine(programFiles, "BellSoft"),
            Path.Combine(programFiles, "Semeru"),
            Path.Combine(programFiles, "IBM"),
            Path.Combine(programFiles, "RedHat"),
            Path.Combine(programFiles, "SapMachine"),
            Path.Combine(programFiles, "OpenJDK"),
            Path.Combine(programFiles, "GraalVM"),
            Path.Combine(programFiles, "Common Files", "Oracle", "Java"),
            Path.Combine(programFilesX86, "Java"),
            Path.Combine(programFilesX86, "Minecraft Launcher", "runtime"),
            Path.Combine(programFilesX86, "Minecraft Launcher", "jre"),
            Path.Combine(programFiles, "Minecraft Launcher", "runtime"),
            Path.Combine(local, "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime")
        };

        try
        {
            var packages = Path.Combine(local, "Packages");
            if (Directory.Exists(packages))
            {
                foreach (var dir in Directory.EnumerateDirectories(packages, "Microsoft.4297127D64EC6_*"))
                    roots.Add(Path.Combine(dir, "LocalCache", "Local", "runtime"));
            }
        }
        catch { }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var exe in FindJavaExes(root, 5, ct)) candidates.Add(exe);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        var list = new List<JavaRuntime>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var full = Path.GetFullPath(exe);
                if (!seen.Add(full)) continue;
                var runtime = Describe(full);
                if (runtime != null) list.Add(runtime);
            }
            catch { }
        }

        return list
            .GroupBy(r => Path.GetDirectoryName(Path.GetDirectoryName(r.Path)!) ?? r.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(r => r.Managed).First())
            .OrderByDescending(r => r.Managed)
            .ThenByDescending(r => r.Major)
            .ThenByDescending(r => r.Is64BitHint)
            .ThenBy(r => r.Path)
            .ToList();
    }

    private static void AddRegistryCandidates(HashSet<string> candidates, CancellationToken ct)
    {
        var roots = new[]
        {
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\Eclipse Adoptium\JDK",
            @"SOFTWARE\Eclipse Foundation\JDK",
            @"SOFTWARE\Azul Systems\Zulu",
            @"SOFTWARE\BellSoft\LibericaJDK",
            @"SOFTWARE\Amazon Corretto",
            @"SOFTWARE\Semeru",
            @"SOFTWARE\Microsoft\JDK",
            @"SOFTWARE\Oracle\Java"
        };

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var path in roots)
                    {
                        using var key = baseKey.OpenSubKey(path);
                        if (key != null) WalkRegistry(key, candidates, 0, ct);
                    }
                }
                catch { }
            }
        }
    }

    private static void WalkRegistry(RegistryKey key, HashSet<string> candidates, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 2) return;
        foreach (var valueName in new[] { "JavaHome", "Path", "InstallationPath" })
        {
            try
            {
                if (key.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                    AddJavaPathCandidate(value, candidates);
            }
            catch { }
        }
        try
        {
            foreach (var name in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(name);
                if (child != null) WalkRegistry(child, candidates, depth + 1, ct);
            }
        }
        catch { }
    }

    private static void AddJavaPathCandidate(string value, HashSet<string> candidates)
    {
        try
        {
            var path = value.Trim().Trim('"');
            if (path.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(path)) candidates.Add(path);
                return;
            }
            var direct = Path.Combine(path, "java.exe");
            if (File.Exists(direct)) { candidates.Add(direct); return; }
            AddJavaHome(path, candidates);
        }
        catch { }
    }

    private static void AddJavaHome(string? home, HashSet<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(home)) return;
        try
        {
            var exe = Path.Combine(home.Trim().Trim('"'), "bin", "java.exe");
            if (File.Exists(exe)) candidates.Add(exe);
        }
        catch { }
    }

    private static IEnumerable<string> FindJavaExes(string root, int depth, CancellationToken ct)
    {
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && visited < 12000)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, d) = queue.Dequeue();
            visited++;
            var exe = Path.Combine(dir, "bin", "java.exe");
            if (File.Exists(exe)) yield return exe;
            if (d >= depth) continue;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(child);
                    if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("assets", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("libraries", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("versions", StringComparison.OrdinalIgnoreCase))
                        continue;
                    queue.Enqueue((child, d + 1));
                }
            }
            catch { }
        }
    }

    public JavaRuntime? Describe(string javaExe)
    {
        try
        {
            var full = Path.GetFullPath(javaExe);
            if (!File.Exists(full)) return null;
            var bin = Path.GetDirectoryName(full)!;
            var home = Path.GetDirectoryName(bin)!;
            var javaw = Path.Combine(bin, "javaw.exe");

            var version = "";
            var vendor = "";
            var osArch = "";
            var release = Path.Combine(home, "release");
            if (File.Exists(release))
            {
                foreach (var line in File.ReadLines(release))
                {
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = line[..idx].Trim();
                    var val = line[(idx + 1)..].Trim().Trim('"');
                    if (key == "JAVA_VERSION") version = val;
                    else if (key == "IMPLEMENTOR") vendor = val;
                    else if (key == "OS_ARCH") osArch = val;
                }
            }

            var major = ParseMajor(version);
            if (major == 0)
            {
                var properties = ReadJavaProperties(full);
                if (properties.TryGetValue("java.version", out var detected)) version = detected;
                if (properties.TryGetValue("java.vendor", out var detectedVendor)) vendor = detectedVendor;
                if (properties.TryGetValue("os.arch", out var detectedArch)) osArch = detectedArch;
                major = ParseMajor(version);
            }
            if (major == 0)
            {
                var m = Regex.Match(home, @"(?:jdk|jre|java)[-_]?(?:(\d+)(?:\.(\d+))?)", RegexOptions.IgnoreCase);
                if (m.Success)
                    major = m.Groups[1].Value == "1" && m.Groups[2].Success
                        ? int.Parse(m.Groups[2].Value)
                        : int.Parse(m.Groups[1].Value);
            }
            if (major == 0) return null;
            if (major == 1)
            {
                var m = Regex.Match(version, @"^1\.(\d+)");
                if (m.Success) major = int.Parse(m.Groups[1].Value);
            }

            if (string.IsNullOrWhiteSpace(vendor))
            {
                vendor = home.Contains("Adoptium", StringComparison.OrdinalIgnoreCase) || home.Contains("Temurin", StringComparison.OrdinalIgnoreCase)
                    ? "Eclipse Temurin"
                    : home.Contains("corretto", StringComparison.OrdinalIgnoreCase) ? "Amazon Corretto"
                    : home.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? "Microsoft OpenJDK"
                    : home.Contains("Zulu", StringComparison.OrdinalIgnoreCase) ? "Azul Zulu"
                    : home.Contains("BellSoft", StringComparison.OrdinalIgnoreCase) || home.Contains("Liberica", StringComparison.OrdinalIgnoreCase) ? "BellSoft Liberica"
                    : home.Contains("Semeru", StringComparison.OrdinalIgnoreCase) || home.Contains("IBM", StringComparison.OrdinalIgnoreCase) ? "IBM Semeru"
                    : home.Contains("GraalVM", StringComparison.OrdinalIgnoreCase) ? "GraalVM"
                    : home.Contains("SapMachine", StringComparison.OrdinalIgnoreCase) ? "SapMachine"
                    : home.Contains("Java", StringComparison.OrdinalIgnoreCase) ? "Oracle / OpenJDK"
                    : "OpenJDK";
            }

            return new JavaRuntime
            {
                Path = full,
                JavawPath = File.Exists(javaw) ? javaw : full,
                Major = major,
                Version = string.IsNullOrEmpty(version) ? major.ToString() : version,
                Vendor = vendor,
                Managed = full.StartsWith(Path.GetFullPath(_paths.JavaDir), StringComparison.OrdinalIgnoreCase),
                Is64BitHint = Is64BitRuntime(full, osArch)
            };
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadJavaProperties(string javaExe)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaExe,
                    Arguments = "-XshowSettings:properties -version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!process.Start()) return result;
            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(4000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { process.WaitForExit(1000); } catch { }
            }
            var stdout = outTask.GetAwaiter().GetResult();
            var stderr = errTask.GetAwaiter().GetResult();
            foreach (var line in (stderr + "\n" + stdout).Split('\n'))
            {
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();
                if (key.StartsWith("java.", StringComparison.Ordinal) ||
                    key is "os.arch" or "sun.arch.data.model")
                    result[key] = value;
            }
        }
        catch { }
        return result;
    }

    private static bool Is64BitRuntime(string javaExe, string osArch)
    {
        if (osArch.Contains("64", StringComparison.OrdinalIgnoreCase)) return true;
        if (javaExe.Contains("x86", StringComparison.OrdinalIgnoreCase) &&
            !javaExe.Contains("x86_64", StringComparison.OrdinalIgnoreCase) &&
            !javaExe.Contains("amd64", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            using var stream = new FileStream(javaExe, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40) return true;
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset + 6 > stream.Length) return true;
            stream.Seek(peOffset + 4, SeekOrigin.Begin);
            var machine = reader.ReadUInt16();
            return machine is 0x8664 or 0xAA64;
        }
        catch
        {
            return true;
        }
    }

    private static int ParseMajor(string version)
    {
        version = version.Trim();
        if (version.StartsWith("1."))
        {
            var part = version.Split('.');
            return part.Length > 1 && int.TryParse(part[1], out var v) ? v : 0;
        }
        var first = version.Split('.', '-', '+', '_')[0];
        return int.TryParse(first, out var major) ? major : 0;
    }

    public JavaRuntime? Pick(IEnumerable<JavaRuntime> runtimes, int requiredMajor, string? preferredPath)
    {
        var all = runtimes.ToList();
        var list = Environment.Is64BitOperatingSystem
            ? all.Where(r => r.Is64BitHint).ToList()
            : all;
        if (list.Count == 0) list = all;

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var normalized = preferredPath;
            try { normalized = Path.GetFullPath(preferredPath); } catch { }
            var preferred = list.FirstOrDefault(r =>
                string.Equals(r.Path, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.JavawPath, normalized, StringComparison.OrdinalIgnoreCase));
            if (preferred != null) return preferred;

            var described = Describe(preferredPath);
            if (described != null) return described;
        }

        var exact = list.Where(r => r.Major == requiredMajor)
            .OrderByDescending(r => r.Managed)
            .ThenByDescending(r => r.Is64BitHint)
            .FirstOrDefault();
        if (exact != null) return exact;

        if (requiredMajor <= 8)
        {
            var j8 = list.Where(r => r.Major == 8)
                .OrderByDescending(r => r.Managed)
                .ThenByDescending(r => r.Is64BitHint)
                .FirstOrDefault();
            if (j8 != null) return j8;
        }

        return list.Where(r => r.Major > requiredMajor)
            .OrderBy(r => r.Major)
            .ThenByDescending(r => r.Managed)
            .ThenByDescending(r => r.Is64BitHint)
            .FirstOrDefault();
    }

    public async Task<JavaRuntime> DownloadAsync(int requiredMajor, IProgress<JavaProgress> progress, CancellationToken ct)
    {
        var temp = Path.Combine(_paths.TempDir, "java-" + Guid.NewGuid().ToString("N"));
        await _downloadGate.WaitAsync(ct);
        try
        {
            var existing = DescribeManaged(requiredMajor);
            if (existing != null)
            {
                progress.Report(new JavaProgress { Stage = "done", Major = requiredMajor, Done = true, Path = existing.Path });
                return existing;
            }

            var adoptiumMajor = MapToAdoptium(requiredMajor);
            var api = $"https://api.adoptium.net/v3/assets/latest/{adoptiumMajor}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
            var assets = await Web.GetJsonAsync<List<AdoptiumAsset>>(api, TimeSpan.FromSeconds(25), ct);
            var package = assets.FirstOrDefault()?.Binary?.Package
                          ?? throw new IOException("没有找到可用的 Java 运行时");

            var target = Path.Combine(_paths.JavaDir, requiredMajor.ToString());
            Directory.CreateDirectory(temp);
            var zipPath = Path.Combine(temp, "runtime.zip");
            progress.Report(new JavaProgress
            {
                Stage = "download",
                Major = requiredMajor,
                TotalBytes = package.Size,
                Message = package.Name
            });

            using (var response = await HttpTransfer.GetAsync(package.Link, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long received = 0;
                int read;
                var lastReport = 0L;
                while ((read = await HttpTransfer.ReadAsync(input, buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.AppendData(buffer, 0, read);
                    received += read;
                    if (received - lastReport > 1024 * 1024)
                    {
                        lastReport = received;
                        progress.Report(new JavaProgress
                        {
                            Stage = "download",
                            Major = requiredMajor,
                            ReceivedBytes = received,
                            TotalBytes = package.Size,
                            Message = package.Name
                        });
                    }
                }
                if (package.Size > 0 && received != package.Size)
                    throw new IOException("Java 运行时下载不完整");
                if (!string.IsNullOrEmpty(package.Checksum))
                {
                    var actual = Convert.ToHexString(sha.GetHashAndReset());
                    if (!string.Equals(actual, package.Checksum, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Java 运行时校验失败");
                }
            }

            progress.Report(new JavaProgress { Stage = "extract", Major = requiredMajor, Message = "正在解压 Java 运行时" });
            var extractDir = Path.Combine(temp, "extract");
            Directory.CreateDirectory(extractDir);
            ExtractZipSafe(zipPath, extractDir, ct);

            var javaExe = FindJavaExes(extractDir, 4, ct).FirstOrDefault()
                          ?? throw new IOException("Java 运行时中没有找到 java.exe");
            var runtimeHome = Path.GetDirectoryName(Path.GetDirectoryName(javaExe)!)!;
            if (Directory.Exists(target))
            {
                try { Directory.Delete(target, recursive: true); } catch { }
            }
            Directory.Move(runtimeHome, target);
            try { Directory.Delete(temp, recursive: true); } catch { }

            var described = DescribeManaged(requiredMajor)
                            ?? throw new IOException("Java 运行时安装后无法识别");
            AtomicJson.Write(Path.Combine(target, "chisel-runtime.json"), new
            {
                major = requiredMajor,
                version = described.Version,
                source = "Adoptium",
                installedAt = DateTimeOffset.Now
            });
            progress.Report(new JavaProgress { Stage = "done", Major = requiredMajor, Done = true, Path = described.Path });
            return described;
        }
        catch
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            throw;
        }
        finally { _downloadGate.Release(); }
    }

    private JavaRuntime? DescribeManaged(int major)
    {
        var dir = Path.Combine(_paths.JavaDir, major.ToString());
        if (!Directory.Exists(dir)) return null;
        try
        {
            var exe = FindJavaExes(dir, 4, CancellationToken.None).FirstOrDefault();
            return exe == null ? null : Describe(exe);
        }
        catch { return null; }
    }

    private static int MapToAdoptium(int requiredMajor)
    {
        if (requiredMajor <= 8) return 8;
        if (requiredMajor <= 11) return 11;
        if (requiredMajor <= 17) return 17;
        if (requiredMajor <= 21) return 21;
        return 25;
    }

    private static void ExtractZipSafe(string zipPath, string destination, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var targetPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Java 压缩包包含非法路径");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private sealed class AdoptiumAsset
    {
        public AdoptiumBinary? Binary { get; set; }
    }

    private sealed class AdoptiumBinary
    {
        public AdoptiumPackage? Package { get; set; }
    }

    private sealed class AdoptiumPackage
    {
        public string Name { get; set; } = "";
        public string Link { get; set; } = "";
        public string Checksum { get; set; } = "";
        public long Size { get; set; }
    }
}
