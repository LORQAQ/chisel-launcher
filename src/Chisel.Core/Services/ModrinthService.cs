using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chisel.Core.Models;
using Chisel.Core.Net;

namespace Chisel.Core.Services;

public sealed class ModrinthService
{
    private const string Api = "https://api.modrinth.com/v2";
    private readonly AppPaths _paths;
    private readonly InstanceStore _instances;
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private readonly object _manifestGate = new();

    public ModrinthService(AppPaths paths, InstanceStore instances)
    {
        _paths = paths;
        _instances = instances;
    }

    public async Task<ModrinthSearchResult> SearchAsync(ContentTypeRequest request, CancellationToken ct)
    {
        var instance = _instances.Get(request.InstanceId) ?? throw new InvalidOperationException("实例不存在");
        var type = NormalizeType(request.ProjectType);
        if (type == "mod" && (string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(instance.Loader, "optifine", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("这个实例没有安装模组加载器，不能加载模组。");

        var facets = new List<List<string>> { new() { "project_type:" + type } };
        if (!string.IsNullOrWhiteSpace(instance.GameVersion))
            facets.Add(new() { "versions:" + instance.GameVersion });
        if (type == "mod")
        {
            facets.Add(string.Equals(instance.Loader, "quilt", StringComparison.OrdinalIgnoreCase)
                ? new() { "categories:quilt", "categories:fabric" }
                : new() { "categories:fabric" });
        }

        var limit = Math.Clamp(request.Limit, 1, 40);
        var offset = Math.Max(0, request.Offset);
        var sort = request.Sort switch
        {
            "downloads" => "downloads",
            "newest" => "newest",
            "updated" => "updated",
            "follows" => "follows",
            _ => "relevance"
        };
        var url = $"{Api}/search?query={Uri.EscapeDataString(request.Query ?? "")}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets, AtomicJson.Compact))}" +
                  $"&index={sort}&limit={limit}&offset={offset}";

        var response = await GetJsonAsync<SearchResponse>(url, TimeSpan.FromSeconds(20), ct);
        var installed = InstalledProjectIds(instance);
        return new ModrinthSearchResult
        {
            Hits = response.Hits.Select(h => new ModrinthHit
            {
                ProjectId = h.ProjectId,
                Slug = h.Slug,
                Title = h.Title,
                Description = h.Description,
                Author = h.Author,
                IconUrl = h.IconUrl ?? "",
                Downloads = h.Downloads,
                Follows = h.Follows,
                Categories = h.DisplayCategories ?? h.Categories ?? [],
                ProjectType = h.ProjectType,
                Versions = h.Versions,
                LatestVersion = h.LatestVersion,
                Color = h.Color,
                Installed = installed.Contains(h.ProjectId),
                InstalledCompatible = installed.Contains(h.ProjectId)
            }).ToList(),
            Offset = response.Offset,
            Limit = response.Limit,
            TotalHits = response.TotalHits
        };
    }

    public async Task<List<ModrinthVersionInfo>> GetVersionsAsync(
        string instanceId,
        string projectId,
        string projectType,
        CancellationToken ct)
    {
        var instance = _instances.Get(instanceId) ?? throw new InvalidOperationException("实例不存在");
        var type = NormalizeType(projectType);
        var versions = await FetchVersionsAsync(projectId, instance, type, ct);
        return versions
            .OrderByDescending(v => string.Equals(v.VersionType, "release", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.DatePublished)
            .Select(ToVersionInfo)
            .ToList();
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
            if (string.IsNullOrWhiteSpace(request.ProjectId)) throw new ArgumentException("项目 ID 为空");
            var result = new ContentInstallResult();
            var manifest = LoadManifest(instance);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installedItems = new List<InstalledContent>();
            var wasExisting = manifest.Items.Any(i => string.Equals(i.ProjectId, request.ProjectId, StringComparison.OrdinalIgnoreCase));
            await InstallOneAsync(instance, request.ProjectId, request.VersionId, NormalizeType(request.ProjectType),
                manifest, visited, installedItems, result.Warnings, 0, onProgress, ct);

            var main = installedItems.FirstOrDefault(i => string.Equals(i.ProjectId, request.ProjectId, StringComparison.OrdinalIgnoreCase));
            return new ContentInstallResult
            {
                Installed = main == null ? installedItems : [main],
                Warnings = result.Warnings,
                Updated = wasExisting
            };
        }
        finally
        {
            _installGate.Release();
        }
    }

    public async Task<ContentInstallResult> InstallOneForUpdateAsync(
        Instance instance,
        string projectId,
        string projectType,
        CancellationToken ct)
    {
        await _installGate.WaitAsync(ct);
        try
        {
            var result = new ContentInstallResult();
            var manifest = LoadManifest(instance);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installedItems = new List<InstalledContent>();
            await InstallOneAsync(instance, projectId, null, NormalizeType(projectType), manifest, visited,
                installedItems, result.Warnings, 0, _ => { }, ct);
            return new ContentInstallResult
            {
                Installed = installedItems,
                Warnings = result.Warnings,
                Updated = true
            };
        }
        finally { _installGate.Release(); }
    }

    public List<InstalledContent> ListInstalled(string instanceId)
    {
        var instance = _instances.Get(instanceId) ?? throw new InvalidOperationException("实例不存在");
        var manifest = LoadManifest(instance);
        var gameDir = _paths.GameDir(instance.Id);
        return manifest.Items.Select(item =>
        {
            var (active, disabled) = ContentFiles(gameDir, item);
            var exists = File.Exists(active) || File.Exists(disabled);
            return new InstalledContent
            {
                ProjectId = item.ProjectId,
                VersionId = item.VersionId,
                ProjectType = item.ProjectType,
                Title = item.Title,
                FileName = item.FileName,
                IconUrl = item.IconUrl,
                VersionNumber = item.VersionNumber,
                Enabled = exists && File.Exists(active),
                FileExists = exists,
                InstalledAt = item.InstalledAt,
                SizeBytes = SafeLength(active) is var a && a > 0 ? a : SafeLength(disabled)
            };
        }).OrderBy(i => i.Title).ToList();
    }

    public InstalledContent Toggle(string instanceId, string projectId, bool enabled)
    {
        var instance = _instances.Get(instanceId) ?? throw new InvalidOperationException("实例不存在");
        lock (ContentManifestLock.Gate)
        {
            var manifest = LoadManifest(instance);
            var item = manifest.Items.FirstOrDefault(i => string.Equals(i.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("没有在实例清单里找到这个内容");
            var (active, disabled) = ContentFiles(_paths.GameDir(instance.Id), item);
            if (enabled)
            {
                if (!File.Exists(active) && File.Exists(disabled)) File.Move(disabled, active);
                if (!File.Exists(active)) throw new InvalidOperationException("内容文件已丢失，请重新安装");
            }
            else
            {
                if (File.Exists(active)) File.Move(active, disabled, overwrite: true);
            }
            item.Enabled = enabled;
            SaveManifest(instance, manifest);
            var result = ListInstalled(instance.Id).FirstOrDefault(i => string.Equals(i.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
            return result ?? throw new InvalidOperationException("内容状态更新失败");
        }
    }

    public void Remove(string instanceId, string projectId)
    {
        var instance = _instances.Get(instanceId) ?? throw new InvalidOperationException("实例不存在");
        lock (ContentManifestLock.Gate)
        {
            var manifest = LoadManifest(instance);
            var item = manifest.Items.FirstOrDefault(i => string.Equals(i.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("没有在实例清单里找到这个内容");
            var (active, disabled) = ContentFiles(_paths.GameDir(instance.Id), item);
            try { if (File.Exists(active)) File.Delete(active); } catch { }
            try { if (File.Exists(disabled)) File.Delete(disabled); } catch { }
            manifest.Items.Remove(item);
            SaveManifest(instance, manifest);
        }
    }

    private async Task InstallOneAsync(
        Instance instance,
        string projectId,
        string? versionId,
        string requestedType,
        ContentManifest manifest,
        HashSet<string> visited,
        List<InstalledContent> installed,
        List<string> warnings,
        int depth,
        Action<ContentProgress> onProgress,
        CancellationToken ct)
    {
        if (depth > 3)
        {
            warnings.Add("依赖层级过深，已停止继续解析。");
            return;
        }
        if (!visited.Add(projectId)) return;

        onProgress(new ContentProgress { Stage = "resolve", Message = "正在确认兼容版本", ProjectTitle = null });
        var project = await GetProjectAsync(projectId, ct);
        var type = project.ProjectType.ToLowerInvariant() switch
        {
            "mod" => "mod",
            "resourcepack" => "resourcepack",
            "shader" => "shader",
            _ => throw new InvalidOperationException($"「{project.Title}」的内容类型暂不支持。")
        };
        if (type == "mod" && (string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(instance.Loader, "optifine", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"「{project.Title}」是模组，但这个实例没有加载器。");

        var gameDir = _paths.GameDir(instance.Id);
        var targetDir = ContentDir(gameDir, type);
        Directory.CreateDirectory(targetDir);

        var versions = await FetchVersionsAsync(projectId, instance, type, ct);
        ModrinthVersion version;
        if (!string.IsNullOrWhiteSpace(versionId))
        {
            version = versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException($"找不到这个版本，或它不兼容 {instance.GameVersion}。");
        }
        else
        {
            version = versions.FirstOrDefault()
                      ?? throw new InvalidOperationException($"「{project.Title}」没有适配 {instance.GameVersion} 的文件。");
        }

        var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file == null || string.IsNullOrWhiteSpace(file.Url))
            throw new InvalidOperationException($"「{project.Title}」的版本没有可下载文件。");

        var existing = manifest.Items.FirstOrDefault(i =>
            string.Equals(i.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        var (existingActive, existingDisabled) = existing == null ? ("", "") : ContentFiles(gameDir, existing);
        if (existing != null &&
            string.Equals(existing.VersionId, version.Id, StringComparison.OrdinalIgnoreCase) &&
            (File.Exists(existingActive) || File.Exists(existingDisabled)))
        {
            installed.Add(ToInstalled(existing, gameDir));
            return;
        }

        var fileName = SafeFileName(file.FileName, type);
        var activePath = Path.Combine(targetDir, fileName);
        var disabledPath = activePath + ".disabled";
        var conflict = manifest.Items.Any(i =>
            !string.Equals(i.ProjectId, projectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (conflict || (File.Exists(activePath) && existing == null))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            fileName = $"{stem}-{projectId[..Math.Min(6, projectId.Length)]}{Path.GetExtension(fileName)}";
            activePath = Path.Combine(targetDir, fileName);
            disabledPath = activePath + ".disabled";
        }

        onProgress(new ContentProgress
        {
            Stage = "download",
            Message = $"正在下载 {file.FileName}",
            ProjectTitle = project.Title,
            TotalBytes = file.Size
        });
        await DownloadFileAsync(file, activePath, project.Title, onProgress, ct);

        if (existing != null)
        {
            if (!string.Equals(existingActive, activePath, StringComparison.OrdinalIgnoreCase))
            {
                try { if (File.Exists(existingActive)) File.Delete(existingActive); } catch { }
            }
            if (!string.Equals(existingDisabled, activePath, StringComparison.OrdinalIgnoreCase))
            {
                try { if (File.Exists(existingDisabled)) File.Delete(existingDisabled); } catch { }
            }
            manifest.Items.Remove(existing);
        }
        try { if (File.Exists(disabledPath)) File.Delete(disabledPath); } catch { }

        var item = new ContentManifestItem
        {
            ProjectId = project.ProjectId,
            VersionId = version.Id,
            ProjectType = type,
            Title = project.Title,
            FileName = fileName,
            IconUrl = project.IconUrl,
            VersionNumber = version.VersionNumber,
            Enabled = true,
            InstalledAt = DateTimeOffset.Now,
            Dependencies = version.Dependencies
                .Where(d => string.Equals(d.DependencyType, "required", StringComparison.OrdinalIgnoreCase) && d.ProjectId != null)
                .Select(d => d.ProjectId!)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        manifest.Items.Add(item);
        SaveManifest(instance, manifest);
        installed.Add(ToInstalled(item, gameDir));
        onProgress(new ContentProgress
        {
            Stage = "install",
            Message = $"已放入 {TypeLabel(type)}",
            ProjectTitle = project.Title,
            ReceivedBytes = file.Size,
            TotalBytes = file.Size
        });

        foreach (var dependency in version.Dependencies)
        {
            if (!string.Equals(dependency.DependencyType, "required", StringComparison.OrdinalIgnoreCase)) continue;
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(dependency.ProjectId))
            {
                warnings.Add($"「{project.Title}」有一个需要你手动选择的前置。");
                continue;
            }
            try
            {
                await InstallOneAsync(instance, dependency.ProjectId!, dependency.VersionId, type, manifest, visited,
                    installed, warnings, depth + 1, onProgress, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                warnings.Add("前置没有装好：" + Web.Truncate(ex.Message, 160));
            }
        }
    }

    private async Task<List<ModrinthVersion>> FetchVersionsAsync(
        string projectId,
        Instance instance,
        string type,
        CancellationToken ct)
    {
        var url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version" +
                  $"?game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { instance.GameVersion }))}";
        if (type == "mod")
        {
            var loaders = string.Equals(instance.Loader, "quilt", StringComparison.OrdinalIgnoreCase)
                ? new[] { "quilt", "fabric" }
                : new[] { "fabric" };
            url += $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(loaders))}";
        }

        var versions = await GetJsonAsync<List<ModrinthVersion>>(url, TimeSpan.FromSeconds(25), ct);
        var filtered = versions.Where(v =>
                (v.GameVersions.Count == 0 || v.GameVersions.Contains(instance.GameVersion, StringComparer.OrdinalIgnoreCase)) &&
                (type != "mod" || v.Loaders.Count == 0 || v.Loaders.Any(l =>
                    string.Equals(l, instance.Loader, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(instance.Loader, "quilt", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(l, "fabric", StringComparison.OrdinalIgnoreCase)))))
            .ToList();

        return filtered
            .OrderByDescending(v => string.Equals(v.VersionType, "release", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.DatePublished)
            .ToList();
    }

    private async Task<ModrinthProjectResponse> GetProjectAsync(string projectId, CancellationToken ct)
    {
        var project = await GetJsonAsync<ModrinthProjectResponse>(
            $"{Api}/project/{Uri.EscapeDataString(projectId)}", TimeSpan.FromSeconds(20), ct);
        if (string.IsNullOrEmpty(project.ProjectId)) throw new InvalidOperationException("Modrinth 项目信息为空");
        if (string.IsNullOrWhiteSpace(project.IconUrl) && !string.IsNullOrWhiteSpace(project.Slug))
            project.IconUrl = $"https://cdn.modrinth.com/data/{project.ProjectId}/icon.png";
        return project;
    }

    private async Task DownloadFileAsync(
        ModrinthFile file,
        string destination,
        string title,
        Action<ContentProgress> onProgress,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("cdn-raw.modrinth.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("下载地址不是受信任的 Modrinth 来源");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = Path.Combine(_paths.TempDir, "content-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var response = await HttpTransfer.GetAsync(uri.ToString(), ct);
            response.EnsureSuccessStatusCode();
            long total = 0;
            string? actualHash = null;
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            {
                var buffer = new byte[81920];
                var lastReport = 0L;
                int read;
                while ((read = await HttpTransfer.ReadAsync(input, buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.AppendData(buffer, 0, read);
                    total += read;
                    if (total - lastReport > 512 * 1024)
                    {
                        lastReport = total;
                        onProgress(new ContentProgress
                        {
                            Stage = "download",
                            Message = "正在下载",
                            ProjectTitle = title,
                            ReceivedBytes = total,
                            TotalBytes = file.Size
                        });
                    }
                }
                actualHash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            }

            if (file.Size > 0 && total != file.Size)
                throw new IOException($"文件大小不符（期望 {file.Size}，收到 {total}）");
            if (!string.IsNullOrWhiteSpace(file.Hashes?.Sha1) &&
                !string.Equals(actualHash, file.Hashes.Sha1, StringComparison.OrdinalIgnoreCase))
                throw new IOException("文件校验失败，请重试");

            File.Move(temp, destination, overwrite: true);
            onProgress(new ContentProgress
            {
                Stage = "download",
                Message = "下载完成",
                ProjectTitle = title,
                ReceivedBytes = total,
                TotalBytes = file.Size,
                Done = true
            });
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static InstalledContent ToInstalled(ContentManifestItem item, string gameDir)
    {
        var (active, disabled) = ContentFiles(gameDir, item);
        return new InstalledContent
        {
            ProjectId = item.ProjectId,
            VersionId = item.VersionId,
            ProjectType = item.ProjectType,
            Title = item.Title,
            FileName = item.FileName,
            IconUrl = item.IconUrl,
            VersionNumber = item.VersionNumber,
            Enabled = File.Exists(active),
            FileExists = File.Exists(active) || File.Exists(disabled),
            InstalledAt = item.InstalledAt,
            SizeBytes = SafeLength(active) is var a && a > 0 ? a : SafeLength(disabled)
        };
    }

    private HashSet<string> InstalledProjectIds(Instance instance)
    {
        var manifest = LoadManifest(instance);
        return manifest.Items
            .Where(i => i.FileName.Length > 0)
            .Select(i => i.ProjectId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private ContentManifest LoadManifest(Instance instance)
    {
        lock (ContentManifestLock.Gate)
        {
            var path = ManifestPath(instance);
            return AtomicJson.Read(path, () => new ContentManifest());
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

    private static (string Active, string Disabled) ContentFiles(string gameDir, ContentManifestItem item)
    {
        var fileName = SafeFileName(item.FileName, item.ProjectType);
        var active = Path.Combine(ContentDir(gameDir, item.ProjectType), fileName);
        return (active, active + ".disabled");
    }

    private static string ContentDir(string gameDir, string type) => type switch
    {
        "resourcepack" => Path.Combine(gameDir, "resourcepacks"),
        "shader" => Path.Combine(gameDir, "shaderpacks"),
        _ => Path.Combine(gameDir, "mods")
    };

    private static string SafeFileName(string fileName, string type)
    {
        var name = Path.GetFileName((fileName ?? "").Trim());
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 180)
            throw new InvalidOperationException("文件名不合法");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var allowed = type == "mod" ? extension == ".jar" : extension is ".zip" or ".jar";
        if (!allowed) throw new InvalidOperationException("文件类型不支持");
        return name;
    }

    private static string NormalizeType(string? type) => type?.ToLowerInvariant() switch
    {
        "resourcepack" or "resource_pack" or "resource-pack" => "resourcepack",
        "shader" or "shaders" => "shader",
        _ => "mod"
    };

    private static string TypeLabel(string type) => type switch
    {
        "resourcepack" => "资源包",
        "shader" => "光影",
        _ => "模组"
    };

    private static long SafeLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static ModrinthVersionInfo ToVersionInfo(ModrinthVersion v)
    {
        var file = v.Files.FirstOrDefault(f => f.Primary) ?? v.Files.FirstOrDefault();
        return new ModrinthVersionInfo
        {
            Id = v.Id,
            ProjectId = v.ProjectId,
            Name = v.Name,
            VersionNumber = v.VersionNumber,
            VersionType = v.VersionType,
            DatePublished = v.DatePublished,
            GameVersions = v.GameVersions,
            Loaders = v.Loaders,
            Changelog = string.IsNullOrWhiteSpace(v.Changelog) ? [] : [v.Changelog],
            Dependencies = v.Dependencies.Select(d => new ModrinthDependencyInfo
            {
                VersionId = d.VersionId,
                ProjectId = d.ProjectId,
                FileName = d.FileName,
                DependencyType = d.DependencyType
            }).ToList(),
            File = file == null ? null : new ModrinthFileInfo
            {
                FileName = file.FileName,
                Url = file.Url,
                Size = file.Size,
                Sha1 = file.Hashes?.Sha1 ?? "",
                Primary = file.Primary
            }
        };
    }

    private static async Task<T> GetJsonAsync<T>(string url, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var response = await Web.Client.GetAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Modrinth 请求失败（{(int)response.StatusCode}）");
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, AtomicJson.Compact, cts.Token);
        return value ?? throw new IOException("Modrinth 返回了空内容");
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("hits")] public List<SearchHit> Hits { get; set; } = [];
        [JsonPropertyName("offset")] public int Offset { get; set; }
        [JsonPropertyName("limit")] public int Limit { get; set; }
        [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
    }

    private sealed class SearchHit
    {
        [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("author")] public string Author { get; set; } = "";
        [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
        [JsonPropertyName("downloads")] public long Downloads { get; set; }
        [JsonPropertyName("follows")] public long Follows { get; set; }
        [JsonPropertyName("categories")] public List<string>? Categories { get; set; }
        [JsonPropertyName("display_categories")] public List<string>? DisplayCategories { get; set; }
        [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "";
        [JsonPropertyName("versions")] public List<string> Versions { get; set; } = [];
        [JsonPropertyName("latest_version")] public string? LatestVersion { get; set; }
        [JsonPropertyName("color")] public long? Color { get; set; }
    }

    private sealed class ModrinthProjectResponse
    {
        [JsonPropertyName("id")] public string ProjectId { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "";
        [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
        [JsonPropertyName("downloads")] public long Downloads { get; set; }
        [JsonPropertyName("color")] public long Color { get; set; }
    }

    private sealed class ModrinthVersion
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
        [JsonPropertyName("version_type")] public string VersionType { get; set; } = "";
        [JsonPropertyName("date_published")] public DateTimeOffset DatePublished { get; set; }
        [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = [];
        [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = [];
        [JsonPropertyName("changelog")] public string? Changelog { get; set; }
        [JsonPropertyName("dependencies")] public List<ModrinthDependency> Dependencies { get; set; } = [];
        [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = [];
    }

    private sealed class ModrinthDependency
    {
        [JsonPropertyName("version_id")] public string? VersionId { get; set; }
        [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
        [JsonPropertyName("file_name")] public string? FileName { get; set; }
        [JsonPropertyName("dependency_type")] public string DependencyType { get; set; } = "required";
    }

    private sealed class ModrinthFile
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("filename")] public string FileName { get; set; } = "";
        [JsonPropertyName("primary")] public bool Primary { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("hashes")] public ModrinthHashes? Hashes { get; set; }
    }

    private sealed class ModrinthHashes
    {
        [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    }
}
