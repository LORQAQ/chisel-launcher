using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chisel.Core.Models;
using Chisel.Core.Net;

namespace Chisel.Core.Services;

public sealed class CurseForgeFileInfo
{
    public int ProjectId { get; init; }
    public int FileId { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public long Size { get; init; }
    public string VersionNumber { get; init; } = "";
    public string ProjectType { get; init; } = "mod";
    public string? IconUrl { get; init; }
}

public sealed class CurseForgeService
{
    private const string Api = "https://api.curseforge.com/v1";
    private const string Widget = "https://api.cfwidget.com";
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly InstanceStore _instances;
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private readonly Dictionary<int, CurseWidgetProject> _projectCache = [];
    private readonly Queue<int> _projectCacheOrder = new();
    private readonly object _manifestGate = new();
    private const int ProjectCacheLimit = 12;

    public CurseForgeService(AppPaths paths, SettingsService settings, InstanceStore instances)
    {
        _paths = paths;
        _settings = settings;
        _instances = instances;
    }

    public async Task<ModrinthSearchResult> SearchAsync(ContentTypeRequest request, CancellationToken ct)
    {
        var key = _settings.Snapshot().CurseForgeApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("CurseForge 搜索需要填入 API Key；也可以直接粘贴项目链接来添加。");
        var instance = _instances.Get(request.InstanceId) ?? throw new InvalidOperationException("实例不存在");
        var type = NormalizeType(request.ProjectType);
        if (type == "mod" && (string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(instance.Loader, "optifine", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("这个实例没有安装模组加载器，不能加载模组。");

        var limit = Math.Clamp(request.Limit, 1, 40);
        var offset = Math.Max(0, request.Offset);
        var sortField = request.Sort switch
        {
            "downloads" => 6,
            "newest" => 3,
            "updated" => 3,
            _ => 2
        };
        var url = $"{Api}/mods/search?gameId=432&classId={ClassId(type)}&searchFilter={Uri.EscapeDataString(request.Query ?? "")}" +
                  $"&gameVersion={Uri.EscapeDataString(instance.GameVersion)}&index={offset}&pageSize={limit}&sortField={sortField}&sortOrder=desc";
        var loaderType = LoaderType(instance.Loader);
        if (type == "mod" && loaderType > 0) url += $"&modLoaderType={loaderType}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(25));
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Add("x-api-key", key);
        using var response = await Web.Client.SendAsync(httpRequest, cts.Token);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"CurseForge 返回了 {(int)response.StatusCode}，请检查 API Key 是否有效。");
        var payload = await response.Content.ReadFromJsonAsync<CfSearchResponse>(AtomicJson.Compact, cts.Token)
                      ?? throw new IOException("CurseForge 返回了空内容");
        var installed = InstalledProjectIds(instance);
        return new ModrinthSearchResult
        {
            Hits = payload.Data.Select(item => new ModrinthHit
            {
                ProjectId = item.Id.ToString(),
                Title = item.Name,
                Description = item.Summary,
                Author = item.Authors.FirstOrDefault()?.Name ?? "CurseForge",
                IconUrl = item.Logo?.ThumbnailUrl ?? "",
                Downloads = item.DownloadCount,
                ProjectType = type,
                Installed = installed.Contains("cf-" + item.Id),
                InstalledCompatible = installed.Contains("cf-" + item.Id)
            }).ToList(),
            Offset = payload.Pagination?.Index ?? offset,
            Limit = limit,
            TotalHits = payload.Pagination?.TotalCount ?? payload.Data.Count
        };
    }

    public async Task<List<ModrinthVersionInfo>> GetVersionsAsync(
        string instanceId,
        string projectId,
        string projectType,
        CancellationToken ct)
    {
        var instance = _instances.Get(instanceId) ?? throw new InvalidOperationException("实例不存在");
        if (!int.TryParse(projectId, out var id)) throw new InvalidOperationException("CurseForge 项目 ID 不合法");
        var type = NormalizeType(projectType);
        var project = await GetProjectAsync(id, ct);
        return project.Files
            .Where(file => IsCompatible(file, instance.GameVersion, instance.Loader, type))
            .OrderByDescending(file => file.UploadedAt)
            .Take(60)
            .Select(file => new ModrinthVersionInfo
            {
                Id = file.Id.ToString(),
                ProjectId = id.ToString(),
                Name = file.Display,
                VersionNumber = file.Display.Length > 0 ? file.Display : file.Name,
                VersionType = file.Type,
                DatePublished = file.UploadedAt,
                GameVersions = file.Versions.Where(IsGameVersion).ToList(),
                Loaders = file.Versions.Where(IsLoaderName).ToList(),
                File = new ModrinthFileInfo
                {
                    FileName = file.Name,
                    Url = BuildCdnUrl(file.Id, file.Name),
                    Size = file.FileSize,
                    Primary = true
                }
            })
            .ToList();
    }

    public async Task<CurseForgeFileInfo> ResolveFileAsync(
        int projectId,
        int? fileId,
        string gameVersion,
        string loader,
        string projectType,
        CancellationToken ct)
    {
        var type = NormalizeType(projectType);
        var project = await GetProjectAsync(projectId, ct);
        CurseWidgetFile? file;
        if (fileId.HasValue)
        {
            file = project.Files.FirstOrDefault(x => x.Id == fileId.Value);
            if (file == null)
                throw new InvalidOperationException($"「{project.Title}」的指定文件不存在。");
        }
        else
        {
            file = project.Files
                .Where(x => IsCompatible(x, gameVersion, loader, type))
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefault();
        }
        if (file == null)
            throw new InvalidOperationException($"「{project.Title}」没有适配 {gameVersion} 的文件。");

        return new CurseForgeFileInfo
        {
            ProjectId = project.Id,
            FileId = file.Id,
            Name = SafeName(file.Name),
            Title = project.Title,
            Url = BuildCdnUrl(file.Id, file.Name),
            Size = file.FileSize,
            VersionNumber = file.Display.Length > 0 ? file.Display : file.Name,
            ProjectType = type,
            IconUrl = project.Thumbnail
        };
    }

    public void ClearCache()
    {
        lock (_projectCache)
        {
            _projectCache.Clear();
            _projectCacheOrder.Clear();
        }
    }

    public async Task<CurseWidgetProject> GetProjectAsync(int id, CancellationToken ct)
    {
        lock (_projectCache)
        {
            if (_projectCache.TryGetValue(id, out var cached)) return cached;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var project = await Web.GetJsonAsync<CurseWidgetProject>($"{Widget}/{id}", TimeSpan.FromSeconds(30), cts.Token);
        if (project.Id == 0 || string.IsNullOrWhiteSpace(project.Title))
            throw new InvalidOperationException("CurseForge 上没有找到这个项目。");
        project.Files ??= [];
        lock (_projectCache)
        {
            if (!_projectCache.ContainsKey(id))
            {
                while (_projectCache.Count >= ProjectCacheLimit && _projectCacheOrder.Count > 0)
                    _projectCache.Remove(_projectCacheOrder.Dequeue());
                _projectCache[id] = project;
                _projectCacheOrder.Enqueue(id);
            }
        }
        return project;
    }

    public async Task<CurseForgeFileInfo> ResolveByLinkAsync(
        string link,
        string gameVersion,
        string loader,
        string projectType,
        CancellationToken ct)
    {
        var text = (link ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("请粘贴 CurseForge 项目链接或 slug");
        if (int.TryParse(text, out var numeric))
            return await ResolveFileAsync(numeric, null, gameVersion, loader, projectType, ct);

        string? slug = text;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("只支持 HTTPS 链接");
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2) slug = segments[^1];
            else slug = null;
        }
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("无法从链接里读出项目名");

        var type = NormalizeType(projectType);
        var routes = new List<string>();
        if (type == "resourcepack") routes.Add("mc-resourcepacks");
        if (type == "shader") routes.Add("mc-shaderpacks");
        routes.Add("mc-mods");

        foreach (var route in routes.Distinct())
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                var project = await Web.GetJsonAsync<CurseWidgetProject>(
                    $"{Widget}/minecraft/{route}/{Uri.EscapeDataString(slug)}", TimeSpan.FromSeconds(20), cts.Token);
                if (project.Id <= 0 || string.IsNullOrWhiteSpace(project.Title)) continue;
                project.Files ??= [];
                lock (_projectCache)
                {
                    if (!_projectCache.ContainsKey(project.Id))
                    {
                        while (_projectCache.Count >= ProjectCacheLimit && _projectCacheOrder.Count > 0)
                            _projectCache.Remove(_projectCacheOrder.Dequeue());
                        _projectCache[project.Id] = project;
                        _projectCacheOrder.Enqueue(project.Id);
                    }
                }
                var file = project.Files
                    .Where(x => IsCompatible(x, gameVersion, loader, type))
                    .OrderByDescending(x => x.UploadedAt)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"「{project.Title}」没有适配 {gameVersion} 的文件。");
                return new CurseForgeFileInfo
                {
                    ProjectId = project.Id,
                    FileId = file.Id,
                    Name = SafeName(file.Name),
                    Title = project.Title,
                    Url = BuildCdnUrl(file.Id, file.Name),
                    Size = file.FileSize,
                    VersionNumber = file.Display.Length > 0 ? file.Display : file.Name,
                    ProjectType = type,
                    IconUrl = project.Thumbnail
                };
            }
            catch (InvalidOperationException) { throw; }
            catch { }
        }
        throw new InvalidOperationException("CurseForge 没有找到这个项目；可以改用项目数字 ID 或配置 API Key 搜索。");
    }

    public async Task<ContentInstallResult> InstallAsync(
        ContentInstallRequest request,
        Action<ContentProgress> onProgress,
        CancellationToken ct)
    {
        await _installGate.WaitAsync(ct);
        try
        {
            var instance = _instances.Get(request.InstanceId) ?? throw new InvalidOperationException("实例不存在");
            if (!int.TryParse(request.ProjectId, out var projectId)) throw new InvalidOperationException("项目 ID 不合法");
            int? fileId = int.TryParse(request.VersionId, out var parsed) ? parsed : null;
            var file = await ResolveFileAsync(projectId, fileId, instance.GameVersion, instance.Loader,
                NormalizeType(request.ProjectType), ct);
            var type = file.ProjectType;
            var gameDir = _paths.GameDir(instance.Id);
            var targetDir = ContentDir(gameDir, type);
            Directory.CreateDirectory(targetDir);
            var active = Path.Combine(targetDir, file.Name);

            onProgress(new ContentProgress
            {
                Stage = "download",
                Message = $"正在下载 {file.Name}",
                ProjectTitle = file.Title,
                TotalBytes = file.Size
            });
            await DownloadAsync(file, active, onProgress, ct);

            var manifest = LoadManifest(instance);
            manifest.Items.RemoveAll(item => string.Equals(item.ProjectId, "cf-" + projectId, StringComparison.OrdinalIgnoreCase));
            var item = new ContentManifestItem
            {
                ProjectId = "cf-" + projectId,
                VersionId = file.FileId.ToString(),
                ProjectType = type,
                Title = file.Title,
                FileName = file.Name,
                IconUrl = file.IconUrl,
                VersionNumber = file.VersionNumber,
                Enabled = true,
                InstalledAt = DateTimeOffset.Now
            };
            manifest.Items.Add(item);
            SaveManifest(instance, manifest);

            onProgress(new ContentProgress
            {
                Stage = "install",
                Message = "已放入内容目录",
                ProjectTitle = file.Title,
                ReceivedBytes = file.Size,
                TotalBytes = file.Size,
                Done = true
            });
            return new ContentInstallResult
            {
                Installed = [ToInstalled(item, gameDir)],
                Updated = false
            };
        }
        finally
        {
            _installGate.Release();
        }
    }

    private async Task DownloadAsync(CurseForgeFileInfo file, string destination, Action<ContentProgress> onProgress, CancellationToken ct)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("mediafilez.forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("edge.forgecdn.net", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("下载地址不是受信任的 CurseForge 来源");

        var temp = Path.Combine(_paths.TempDir, "cf-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var response = await HttpTransfer.GetAsync(uri.ToString(), ct);
            response.EnsureSuccessStatusCode();
            long total = 0;
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072];
                var last = 0L;
                int read;
                while ((read = await HttpTransfer.ReadAsync(input, buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    if (total - last > 512 * 1024)
                    {
                        last = total;
                        onProgress(new ContentProgress
                        {
                            Stage = "download",
                            Message = "正在下载",
                            ProjectTitle = file.Title,
                            ReceivedBytes = total,
                            TotalBytes = file.Size
                        });
                    }
                }
            }
            if (file.Size > 0 && total != file.Size)
                throw new IOException($"文件大小不符（期望 {file.Size}，收到 {total}）");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private HashSet<string> InstalledProjectIds(Instance instance)
    {
        var manifest = LoadManifest(instance);
        return manifest.Items.Select(item => item.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private ContentManifest LoadManifest(Instance instance)
    {
        lock (ContentManifestLock.Gate)
        {
            return AtomicJson.Read(ManifestPath(instance), () => new ContentManifest());
        }
    }

    private void SaveManifest(Instance instance, ContentManifest manifest)
    {
        lock (ContentManifestLock.Gate)
        {
            AtomicJson.Write(ManifestPath(instance), manifest);
        }
    }

    private string ManifestPath(Instance instance) => Path.Combine(_paths.GameDir(instance.Id), ".chisel-content.json");

    private static InstalledContent ToInstalled(ContentManifestItem item, string gameDir)
    {
        var path = Path.Combine(ContentDir(gameDir, item.ProjectType), item.FileName);
        long size = 0;
        try { if (File.Exists(path)) size = new FileInfo(path).Length; } catch { }
        return new InstalledContent
        {
            ProjectId = item.ProjectId,
            VersionId = item.VersionId,
            ProjectType = item.ProjectType,
            Title = item.Title,
            FileName = item.FileName,
            IconUrl = item.IconUrl,
            VersionNumber = item.VersionNumber,
            Enabled = true,
            FileExists = File.Exists(path),
            InstalledAt = item.InstalledAt,
            SizeBytes = size
        };
    }

    private static bool IsCompatible(CurseWidgetFile file, string gameVersion, string loader, string type)
    {
        if (file.Versions.Count > 0 &&
            !file.Versions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
            return false;
        if (type != "mod") return true;
        var wanted = loader.ToLowerInvariant() switch
        {
            "fabric" => "Fabric",
            "quilt" => "Quilt",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            _ => ""
        };
        if (wanted.Length == 0) return true;
        if (file.Versions.Count == 0) return true;
        return file.Versions.Any(v =>
            string.Equals(v, wanted, StringComparison.OrdinalIgnoreCase) ||
            (loader == "quilt" && string.Equals(v, "Fabric", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsGameVersion(string value) => value.Length > 0 && char.IsDigit(value[0]);

    private static bool IsLoaderName(string value) => value is "Fabric" or "Forge" or "NeoForge" or "Quilt";

    private static string BuildCdnUrl(int fileId, string name)
    {
        var folder = fileId / 1000;
        var leaf = fileId % 1000;
        return $"https://mediafilez.forgecdn.net/files/{folder}/{leaf:D3}/{Uri.EscapeDataString(name)}";
    }

    private static string SafeName(string name)
    {
        var safe = Path.GetFileName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
            throw new InvalidOperationException("CurseForge 返回的文件名不合法");
        return safe;
    }

    private static string ContentDir(string gameDir, string type) => type switch
    {
        "resourcepack" => Path.Combine(gameDir, "resourcepacks"),
        "shader" => Path.Combine(gameDir, "shaderpacks"),
        _ => Path.Combine(gameDir, "mods")
    };

    private static string NormalizeType(string? type) => type?.ToLowerInvariant() switch
    {
        "resourcepack" or "resource_pack" or "resource-pack" => "resourcepack",
        "shader" or "shaders" => "shader",
        _ => "mod"
    };

    private static int ClassId(string type) => type switch
    {
        "resourcepack" => 12,
        "shader" => 6552,
        _ => 6
    };

    private static int LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0
    };

    private sealed class CfSearchResponse
    {
        [JsonPropertyName("data")] public List<CfMod> Data { get; set; } = [];
        [JsonPropertyName("pagination")] public CfPagination? Pagination { get; set; }
    }

    private sealed class CfPagination
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("pageSize")] public int PageSize { get; set; }
        [JsonPropertyName("resultCount")] public int ResultCount { get; set; }
        [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    }

    private sealed class CfMod
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        [JsonPropertyName("downloadCount")] public long DownloadCount { get; set; }
        [JsonPropertyName("logo")] public CfLogo? Logo { get; set; }
        [JsonPropertyName("authors")] public List<CfAuthor> Authors { get; set; } = [];
    }

    private sealed class CfLogo
    {
        [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
    }

    private sealed class CfAuthor
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }
}

public sealed class CurseWidgetProject
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
    [JsonPropertyName("files")] public List<CurseWidgetFile> Files { get; set; } = [];
}

public sealed class CurseWidgetFile
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("display")] public string Display { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("filesize")] public long FileSize { get; set; }
    [JsonPropertyName("versions")] public List<string> Versions { get; set; } = [];
    [JsonPropertyName("uploaded_at")] public DateTimeOffset UploadedAt { get; set; }
}
