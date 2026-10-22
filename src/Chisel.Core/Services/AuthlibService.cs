using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chisel.Core.Net;

namespace Chisel.Core.Services;

public sealed class AuthlibLoginResult
{
    public required string Server { get; init; }
    public required string ServerName { get; init; }
    public required string AccessToken { get; init; }
    public required string ClientToken { get; init; }
    public required string Uuid { get; init; }
    public required string Name { get; init; }
    public required string PrefetchedMetadata { get; init; }
}

public sealed class AuthlibService
{
    private const string LatestUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _jarGate = new(1, 1);
    private readonly Dictionary<string, (string Root, string Json, DateTimeOffset At)> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _metadataGate = new();

    public AuthlibService(AppPaths paths) => _paths = paths;

    public string JarPath => Path.Combine(_paths.SharedDir, "authlib-injector.jar");

    public async Task<AuthlibLoginResult> LoginAsync(string server, string username, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("请填写邮箱与密码。");
        var (root, metadataJson) = await ResolveServerAsync(server, ct);
        var metadata = ParseMetadata(metadataJson);
        var body = new
        {
            agent = new { name = "Minecraft", version = 1 },
            username = username.Trim(),
            password,
            requestUser = false
        };
        var response = await PostAsync<YggdrasilAuthResponse>(root + "/authserver/authenticate", body, ct,
            "用户名或密码不正确，或服务器拒绝了这次登录。");
        if (string.IsNullOrWhiteSpace(response.AccessToken) || response.SelectedProfile == null)
            throw new InvalidOperationException("服务器没有返回可用的角色，请确认账号下已经创建角色。");
        return new AuthlibLoginResult
        {
            Server = root,
            ServerName = metadata.ServerName,
            AccessToken = response.AccessToken,
            ClientToken = string.IsNullOrWhiteSpace(response.ClientToken) ? Guid.NewGuid().ToString("N") : response.ClientToken,
            Uuid = FormatUuid(response.SelectedProfile.Id),
            Name = response.SelectedProfile.Name,
            PrefetchedMetadata = Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataJson))
        };
    }

    public async Task<AuthlibLoginResult> RefreshAsync(string server, string accessToken, string clientToken, CancellationToken ct)
    {
        var (root, metadataJson) = await ResolveServerAsync(server, ct);
        var metadata = ParseMetadata(metadataJson);
        var response = await PostAsync<YggdrasilAuthResponse>(root + "/authserver/refresh", new
        {
            accessToken,
            clientToken,
            requestUser = false
        }, ct, "外置登录已失效，请在账号页重新登录。");
        if (string.IsNullOrWhiteSpace(response.AccessToken))
            throw new InvalidOperationException("外置登录已失效，请重新登录。");
        return new AuthlibLoginResult
        {
            Server = root,
            ServerName = metadata.ServerName,
            AccessToken = response.AccessToken,
            ClientToken = string.IsNullOrWhiteSpace(response.ClientToken) ? clientToken : response.ClientToken,
            Uuid = response.SelectedProfile == null ? "" : FormatUuid(response.SelectedProfile.Id),
            Name = response.SelectedProfile?.Name ?? "",
            PrefetchedMetadata = Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataJson))
        };
    }

    public async Task<(string JarPath, string Prefetched)> PrepareAsync(string server, CancellationToken ct)
    {
        var jar = await EnsureJarAsync(ct);
        var (_, metadataJson) = await ResolveServerAsync(server, ct);
        return (jar, Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataJson)));
    }

    public async Task<string> EnsureJarAsync(CancellationToken ct)
    {
        var path = JarPath;
        if (File.Exists(path) && new FileInfo(path).Length > 100 * 1024) return path;

        await _jarGate.WaitAsync(ct);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 100 * 1024) return path;
            var latest = await Web.GetJsonAsync<AuthlibLatest>(LatestUrl, TimeSpan.FromSeconds(20), ct);
            if (string.IsNullOrWhiteSpace(latest.DownloadUrl) || !latest.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new IOException("authlib-injector 下载地址无效");
            var temp = Path.Combine(_paths.TempDir, "authlib-" + Guid.NewGuid().ToString("N") + ".jar");
            try
            {
                using var response = await HttpTransfer.GetAsync(latest.DownloadUrl, ct);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await HttpTransfer.ReadAsync(input, buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        sha.AppendData(buffer, 0, read);
                    }
                    if (!string.IsNullOrWhiteSpace(latest.Checksums?.Sha256))
                    {
                        var actual = Convert.ToHexString(sha.GetHashAndReset());
                        if (!string.Equals(actual, latest.Checksums.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("authlib-injector 校验失败");
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Move(temp, path, overwrite: true);
                return path;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
        finally
        {
            _jarGate.Release();
        }
    }

    private async Task<(string Root, string Json)> ResolveServerAsync(string server, CancellationToken ct)
    {
        var input = (server ?? "").Trim().TrimEnd('/');
        lock (_metadataGate)
        {
            if (_metadataCache.TryGetValue(input, out var cached) &&
                DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(30))
                return (cached.Root, cached.Json);
        }
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("外置登录地址需要是 HTTPS 链接");
        var candidates = new List<string> { input };
        if (!input.EndsWith("/api/yggdrasil", StringComparison.OrdinalIgnoreCase))
            candidates.Add(input + "/api/yggdrasil");

        Exception? last = null;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await Web.Client.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode) continue;
                var text = await response.Content.ReadAsStringAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{')) continue;
                _ = ParseMetadata(text);
                var root = candidate.TrimEnd('/');
                lock (_metadataGate)
                {
                    var entry = (root, text, DateTimeOffset.UtcNow);
                    _metadataCache[input] = entry;
                    _metadataCache[root] = entry;
                }
                return (root, text);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException(
            "没有连上外置登录服务器。地址通常以 /api/yggdrasil 结尾。" +
            (last == null ? "" : $"（{Web.Truncate(last.Message, 100)}）"));
    }

    private static AuthlibMetadata ParseMetadata(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var name = "外置登录";
            if (doc.RootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("serverName", out var serverName) &&
                serverName.ValueKind == JsonValueKind.String)
                name = serverName.GetString() ?? name;
            return new AuthlibMetadata { ServerName = name };
        }
        catch
        {
            throw new InvalidOperationException("外置登录服务器返回的元数据无法识别。");
        }
    }

    private static async Task<T> PostAsync<T>(string url, object body, CancellationToken ct, string failure)
    {
        try
        {
            return await Web.PostJsonAsync<T>(url, body, ct, timeout: TimeSpan.FromSeconds(25));
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
        {
            throw new InvalidOperationException(failure, ex);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(failure + " " + Web.Truncate(ex.Message, 120), ex);
        }
    }

    private static string FormatUuid(string raw)
    {
        var hex = new string((raw ?? "").Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        if (hex.Length != 32) return raw ?? "";
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private sealed class AuthlibMetadata
    {
        public string ServerName { get; init; } = "";
    }

    private sealed class AuthlibLatest
    {
        [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("checksums")] public AuthlibChecksums? Checksums { get; set; }
    }

    private sealed class AuthlibChecksums
    {
        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    }

    private sealed class YggdrasilAuthResponse
    {
        [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("clientToken")] public string ClientToken { get; set; } = "";
        [JsonPropertyName("selectedProfile")] public YggdrasilProfile? SelectedProfile { get; set; }
    }

    private sealed class YggdrasilProfile
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }
}
