using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Chisel.Core.Models;
using Chisel.Core.Net;

namespace Chisel.Core.Minecraft;

public sealed class ExternalLoaderResult
{
    public required MinecraftVersion Profile { get; init; }
    public required string ProfileId { get; init; }
    public required string LoaderVersion { get; init; }
}

public sealed class LoaderInstallerService
{
    private readonly AppPaths _paths;
    private readonly JavaManager _java;

    public LoaderInstallerService(AppPaths paths, JavaManager java)
    {
        _paths = paths;
        _java = java;
    }

    public async Task<ExternalLoaderResult> InstallForgeAsync(
        string gameVersion,
        string loaderVersion,
        Downloader downloader,
        DownloadProgress progress,
        Action<string> onStage,
        CancellationToken ct,
        bool useMirror)
    {
        var pair = $"{gameVersion}-{loaderVersion}";
        var url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{pair}/forge-{pair}-installer.jar";
        var result = await InstallAsync(gameVersion, "forge", loaderVersion, url, downloader, progress, onStage, ct, useMirror: useMirror, officialInstaller: true);
        return result;
    }

    public async Task<ExternalLoaderResult> InstallNeoForgeAsync(
        string gameVersion,
        string loaderVersion,
        Downloader downloader,
        DownloadProgress progress,
        Action<string> onStage,
        CancellationToken ct,
        bool useMirror)
    {
        var url = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";
        var result = await InstallAsync(gameVersion, "neoforge", loaderVersion, url, downloader, progress, onStage, ct, useMirror: useMirror);
        return result;
    }

    public async Task<ExternalLoaderResult> InstallOptiFineAsync(
        string gameVersion,
        string loaderVersion,
        string downloadUrl,
        Downloader downloader,
        DownloadProgress progress,
        Action<string> onStage,
        CancellationToken ct)
    {
        return await InstallAsync(gameVersion, "optifine", loaderVersion, downloadUrl, downloader, progress, onStage, ct, optifineHeadless: true);
    }

    private async Task<ExternalLoaderResult> InstallAsync(
        string gameVersion,
        string loader,
        string loaderVersion,
        string installerUrl,
        Downloader downloader,
        DownloadProgress progress,
        Action<string> onStage,
        CancellationToken ct,
        bool optifineHeadless = false,
        bool useMirror = false,
        bool officialInstaller = false)
    {
        var java = await PickJavaAsync(gameVersion, ct)
                   ?? throw new InvalidOperationException($"安装 {Label(loader)} 需要一个可用的 Java，请先在设置里准备。");

        var work = Path.Combine(_paths.TempDir, $"loader-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(work, "game");
        var installerPath = Path.Combine(work, "installer.jar");
        try
        {
            Directory.CreateDirectory(gameRoot);
            PrepareGameRoot(gameVersion, gameRoot);

            onStage($"正在取回 {Label(loader)} 安装器");
            var mirror = Mirror.ForMojangAsset(installerUrl);
            progress.Plan(0, 1);
            await downloader.DownloadAllAsync(
            [
                new DownloadItem
                {
                    Url = installerUrl,
                    MirrorUrl = officialInstaller ? null : mirror,
                    FallbackUrl = officialInstaller ? mirror : null,
                    Destination = installerPath,
                    Label = $"{Label(loader)} 安装器",
                    VerifyExisting = false
                }
            ], 1, progress, _ => { }, ct);

            if (useMirror) RewriteInstallerUrls(installerPath);
            onStage($"正在运行 {Label(loader)} 安装器");
            var output = await RunInstallerAsync(java.Path, installerPath, gameRoot, optifineHeadless, ct)
                         ?? throw new InvalidOperationException($"{Label(loader)} 安装器没有正常结束。");

            var versionsRoot = Path.Combine(gameRoot, "versions");
            var versionDir = FindInstalledVersion(versionsRoot, gameVersion)
                             ?? throw new InvalidOperationException(
                                 $"{Label(loader)} 安装完成后没有找到版本文件。{(string.IsNullOrWhiteSpace(output) ? "" : "\n" + output)}");

            var profilePath = Directory.EnumerateFiles(versionDir, "*.json").FirstOrDefault()
                              ?? throw new InvalidOperationException($"{Label(loader)} 版本描述缺失。");
            var profile = AtomicJson.Read<MinecraftVersion>(profilePath, () => new MinecraftVersion());
            if (string.IsNullOrEmpty(profile.Id)) profile.Id = Path.GetFileName(versionDir);
            if (!AppPaths.IsSafeSegment(profile.Id))
                throw new InvalidOperationException("加载器返回了不合法的版本名。");
            if (string.IsNullOrWhiteSpace(profile.MainClass))
                throw new InvalidOperationException("加载器版本描述缺少主类，安装可能没有完成。");

            var profileId = profile.Id;
            var targetVersionDir = _paths.LoaderDir(profileId);
            Directory.CreateDirectory(targetVersionDir);
            foreach (var file in Directory.EnumerateFiles(versionDir))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, Path.GetFileName(profilePath), StringComparison.OrdinalIgnoreCase))
                    AtomicJson.Write(Path.Combine(targetVersionDir, "profile.json"), profile);
                else
                    File.Copy(file, Path.Combine(targetVersionDir, name), overwrite: true);
            }

            onStage("正在合并运行库");
            CopyLibraries(Path.Combine(gameRoot, "libraries"), _paths.LibrariesDir, ct);
            return new ExternalLoaderResult
            {
                Profile = profile,
                ProfileId = profileId,
                LoaderVersion = loaderVersion
            };
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { }
        }
    }

    private async Task<JavaRuntime?> PickJavaAsync(string gameVersion, CancellationToken ct)
    {
        try
        {
            var baseVersion = AtomicJson.Read<MinecraftVersion>(
                Path.Combine(_paths.VersionDir(gameVersion), "version.json"), () => new MinecraftVersion());
            var required = baseVersion.JavaVersion?.MajorVersion ?? 8;
            var runtimes = await _java.GetRuntimesAsync(false, ct);
            return _java.Pick(runtimes, required, null);
        }
        catch
        {
            return null;
        }
    }

    private void PrepareGameRoot(string gameVersion, string gameRoot)
    {
        var versionDir = Path.Combine(gameRoot, "versions", gameVersion);
        Directory.CreateDirectory(versionDir);
        var sourceVersion = Path.Combine(_paths.VersionDir(gameVersion), "version.json");
        var sourceJar = Path.Combine(_paths.VersionDir(gameVersion), "client.jar");
        if (File.Exists(sourceVersion)) File.Copy(sourceVersion, Path.Combine(versionDir, gameVersion + ".json"), overwrite: true);
        if (File.Exists(sourceJar)) File.Copy(sourceJar, Path.Combine(versionDir, gameVersion + ".jar"), overwrite: true);

        AtomicJson.Write(Path.Combine(gameRoot, "launcher_profiles.json"), new
        {
            profiles = new Dictionary<string, object>
            {
                ["chisel"] = new
                {
                    name = "Chisel",
                    type = "custom",
                    lastVersionId = gameVersion,
                    created = DateTimeOffset.UtcNow,
                    lastUsed = DateTimeOffset.UtcNow
                }
            },
            selectedProfile = "chisel",
            clientToken = Guid.NewGuid().ToString("N"),
            authenticationDatabase = new Dictionary<string, object>()
        });
    }

    private async Task<string?> RunInstallerAsync(
        string javaPath,
        string installerPath,
        string gameRoot,
        bool optifineHeadless,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = gameRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = !optifineHeadless,
                RedirectStandardError = !optifineHeadless
            }
        };
        if (optifineHeadless)
        {
            var helperDir = Path.Combine(Path.GetDirectoryName(installerPath)!, "helper");
            Directory.CreateDirectory(helperDir);
            using var resource = typeof(LoaderInstallerService).Assembly
                .GetManifestResourceStream("Chisel.Core.OptiFineHeadless.class")
                ?? throw new InvalidOperationException("OptiFine 安装组件缺失。");
            await using (var helper = File.Create(Path.Combine(helperDir, "OptiFineHeadless.class")))
                await resource.CopyToAsync(helper, ct);
            process.StartInfo.ArgumentList.Add("-Djava.awt.headless=true");
            process.StartInfo.ArgumentList.Add("-cp");
            process.StartInfo.ArgumentList.Add(installerPath + Path.PathSeparator + helperDir);
            process.StartInfo.ArgumentList.Add("OptiFineHeadless");
            process.StartInfo.ArgumentList.Add(gameRoot);
        }
        else
        {
            process.StartInfo.ArgumentList.Add("-jar");
            process.StartInfo.ArgumentList.Add(installerPath);
            process.StartInfo.ArgumentList.Add("--installClient");
            process.StartInfo.ArgumentList.Add(gameRoot);
        }

        var lines = new Queue<string>();
        var logPath = Path.Combine(gameRoot, "chisel-installer.log");
        void Push(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
            lock (lines)
            {
                lines.Enqueue(line.Trim());
                while (lines.Count > 40) lines.Dequeue();
            }
        }

        try
        {
            if (!process.Start()) return null;
            try { process.StandardInput.Close(); } catch { }
            var outputTask = Task.CompletedTask;
            var errorTask = Task.CompletedTask;
            if (!optifineHeadless)
            {
                outputTask = Task.Run(async () =>
                {
                    while (await process.StandardOutput.ReadLineAsync(ct) is { } line) Push(line);
                }, ct);
                errorTask = Task.Run(async () =>
                {
                    while (await process.StandardError.ReadLineAsync(ct) is { } line) Push(line);
                }, ct);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(optifineHeadless ? 6 : 20));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("安装器运行超时。");
            }

            try { await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(5), ct); } catch { }
            if (process.ExitCode != 0)
            {
                string tail;
                lock (lines) tail = string.Join("\n", lines.TakeLast(12));
                throw new InvalidOperationException(
                    $"安装器以退出码 {process.ExitCode} 结束。{(string.IsNullOrWhiteSpace(tail) ? "" : "\n" + tail)}");
            }

            lock (lines) return string.Join("\n", lines.TakeLast(12));
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException("无法运行加载器安装器：" + ex.Message, ex);
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }
    }

    private static void RewriteInstallerUrls(string installerPath)
    {
        var temp = installerPath + ".rewritten";
        try
        {
            using (var source = ZipFile.OpenRead(installerPath))
            using (var target = new ZipArchive(File.Create(temp), ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries)
                {
                    var created = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var output = created.Open();
                    using var input = entry.Open();
                    if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        input.CopyTo(output);
                        continue;
                    }
                    using var reader = new StreamReader(input, Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    text = text
                        .Replace("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                        .Replace("https://maven.neoforged.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                        .Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                        .Replace("https://files.minecraftforge.net/maven", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                        .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.OrdinalIgnoreCase)
                        .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)
                        .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)
                        .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets", StringComparison.OrdinalIgnoreCase);
                    using var writer = new StreamWriter(output, new UTF8Encoding(false));
                    writer.Write(text);
                }
            }
            File.Move(temp, installerPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static string? FindInstalledVersion(string versionsRoot, string gameVersion)
    {
        try
        {
            if (!Directory.Exists(versionsRoot)) return null;
            return Directory.EnumerateDirectories(versionsRoot)
                .Where(dir => !string.Equals(Path.GetFileName(dir), gameVersion, StringComparison.OrdinalIgnoreCase))
                .Where(dir => Directory.EnumerateFiles(dir, "*.json").Any())
                .OrderByDescending(dir => Directory.GetLastWriteTimeUtc(dir))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void CopyLibraries(string source, string target, CancellationToken ct)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            try
            {
                if (File.Exists(destination) && new FileInfo(destination).Length == new FileInfo(file).Length) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
            catch { }
        }
    }

    private static string Label(string loader) => loader switch
    {
        "neoforge" => "NeoForge",
        "optifine" => "OptiFine",
        _ => "Forge"
    };
}
