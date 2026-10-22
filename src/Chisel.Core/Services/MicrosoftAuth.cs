using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chisel.Core.Models;
using Chisel.Core.Net;

namespace Chisel.Core.Services;

public sealed class MicrosoftTokens
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class MicrosoftSession
{
    public required AuthUser User { get; init; }
    public required MicrosoftTokens Tokens { get; init; }
}

public sealed class MicrosoftAuthException : Exception
{
    public string Code { get; init; } = "";
    public MicrosoftAuthException(string message, string code = "") : base(message) => Code = code;
}

public sealed class MicrosoftAuth
{
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string Scope = "XboxLive.signin offline_access";

    public async Task<DeviceCodeChallenge> RequestDeviceCodeAsync(string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new MicrosoftAuthException("还没有配置 Microsoft 登录。请先在设置里填写 Azure 应用的客户端 ID。", "missing_client_id");

        using var timeout = Timed(ct);
        using var response = await Web.Client.PostAsync(DeviceCodeUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope
        }), timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new MicrosoftAuthException(FriendlyOAuthError(text, "无法开始 Microsoft 登录"), "device_code_failed");

        var data = JsonSerializer.Deserialize<DeviceCodeResponse>(text, AtomicJson.Compact)
                   ?? throw new MicrosoftAuthException("Microsoft 返回了空响应", "empty_response");
        if (string.IsNullOrEmpty(data.DeviceCode) || string.IsNullOrEmpty(data.UserCode))
            throw new MicrosoftAuthException("Microsoft 返回的登录信息不完整", "invalid_response");

        return new DeviceCodeChallenge
        {
            UserCode = data.UserCode,
            VerificationUri = string.IsNullOrEmpty(data.VerificationUri) ? "https://microsoft.com/link" : data.VerificationUri,
            DeviceCode = data.DeviceCode,
            ExpiresIn = data.ExpiresIn <= 0 ? 900 : data.ExpiresIn,
            Interval = Math.Max(3, data.Interval),
            ClientId = clientId
        };
    }

    public async Task<MicrosoftTokens> PollForTokensAsync(
        DeviceCodeChallenge challenge,
        Action<string>? onWait,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(challenge.ExpiresIn);
        var interval = Math.Max(3, challenge.Interval);
        var waited = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            waited += interval;
            onWait?.Invoke(waited >= 30 ? "仍在等待浏览器里的确认" : "等待确认登录");

            using var timeout = Timed(ct);
            using var response = await Web.Client.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = challenge.ClientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = challenge.DeviceCode
            }), timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var token = JsonSerializer.Deserialize<TokenResponse>(text, AtomicJson.Compact)
                            ?? throw new MicrosoftAuthException("Microsoft 返回了空响应", "empty_response");
                return ToTokens(token);
            }

            var error = TryReadString(text, "error");
            var description = TryReadString(text, "error_description");
            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "expired_token":
                    throw new MicrosoftAuthException("登录代码已过期，请重新获取。", "expired_token");
                case "authorization_declined":
                    throw new MicrosoftAuthException("登录已被取消。", "authorization_declined");
                default:
                    throw new MicrosoftAuthException(
                        string.IsNullOrWhiteSpace(description) ? "Microsoft 登录失败，请稍后重试。" : Clean(description),
                        error);
            }
        }

        throw new MicrosoftAuthException("登录代码已过期，请重新获取。", "expired_token");
    }

    public async Task<MicrosoftTokens> RefreshAsync(string refreshToken, string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(clientId))
            throw new MicrosoftAuthException("登录状态已失效，请重新登录 Microsoft 账号。", "missing_refresh_token");

        using var timeout = Timed(ct);
        using var response = await Web.Client.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        }), timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new MicrosoftAuthException(FriendlyOAuthError(text, "Microsoft 登录已失效，请重新登录。"), "refresh_failed");

        var token = JsonSerializer.Deserialize<TokenResponse>(text, AtomicJson.Compact)
                    ?? throw new MicrosoftAuthException("Microsoft 返回了空响应", "empty_response");
        return ToTokens(token, refreshToken);
    }

    public async Task<MicrosoftSession> CreateMinecraftSessionAsync(MicrosoftTokens tokens, string clientId, CancellationToken ct)
    {
        var xbl = await PostXboxAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + tokens.AccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, ct).ConfigureAwait(false);

        var xsts = await PostXboxAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xbl.Token }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(xsts.Token) || string.IsNullOrWhiteSpace(xsts.Uhs))
            throw new MicrosoftAuthException("Xbox 身份验证没有返回有效凭据。", "xsts_empty");

        var identity = $"XBL3.0 x={xsts.Uhs};{xsts.Token}";
        var minecraft = await PostJsonSafeAsync<MinecraftTokenResponse>(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = identity }, ct, "无法登录 Minecraft 服务");

        if (string.IsNullOrWhiteSpace(minecraft.AccessToken))
            throw new MicrosoftAuthException("Minecraft 服务没有返回访问令牌。", "minecraft_token_empty");

        var profile = await GetProfileSafeAsync(minecraft.AccessToken, ct);
        var uuid = string.IsNullOrWhiteSpace(profile.Id) ? "" : FormatUuid(profile.Id);
        if (uuid.Length == 0 || string.IsNullOrWhiteSpace(profile.Name))
            throw new MicrosoftAuthException("这个账号没有可用的 Minecraft 档案。", "no_profile");

        var minecraftTokens = new MicrosoftTokens
        {
            AccessToken = minecraft.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(600, minecraft.ExpiresIn))
        };

        return new MicrosoftSession
        {
            User = new AuthUser
            {
                Uuid = uuid,
                Name = profile.Name,
                AccessToken = minecraft.AccessToken,
                UserType = "msa",
                Xuid = xsts.Xuid,
                ClientId = clientId
            },
            Tokens = minecraftTokens
        };
    }

    private async Task<MinecraftProfile> GetProfileSafeAsync(string accessToken, CancellationToken ct)
    {
        using var timeout = Timed(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Web.Client.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new MicrosoftAuthException("这个账号还没有购买 Minecraft: Java 版。", "not_entitled");
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new MicrosoftAuthException("Minecraft 登录凭据无效，请重新登录。", "profile_unauthorized");
        if (!response.IsSuccessStatusCode)
            throw new MicrosoftAuthException("暂时无法读取 Minecraft 档案，请稍后再试。", "profile_failed");

        var profile = await response.Content.ReadFromJsonAsync<MinecraftProfile>(AtomicJson.Compact, ct);
        return profile ?? throw new MicrosoftAuthException("Minecraft 档案为空。", "empty_profile");
    }

    private async Task<XboxResponse> PostXboxAsync(string url, object body, CancellationToken ct)
    {
        using var timeout = Timed(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-xbl-contract-version", "1");
        using var response = await Web.Client.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw MapXboxError(text);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var token = root.TryGetProperty("Token", out var tokenElement) ? tokenElement.GetString() ?? "" : "";
        var uhs = "";
        var xuid = "";
        if (root.TryGetProperty("DisplayClaims", out var claims) &&
            claims.TryGetProperty("xui", out var xui) &&
            xui.ValueKind == JsonValueKind.Array &&
            xui.GetArrayLength() > 0)
        {
            var first = xui[0];
            if (first.TryGetProperty("uhs", out var uhsElement)) uhs = uhsElement.GetString() ?? "";
            if (first.TryGetProperty("xid", out var xidElement)) xuid = xidElement.GetString() ?? "";
        }
        return new XboxResponse { Token = token, Uhs = uhs, Xuid = xuid };
    }

    private static MicrosoftAuthException MapXboxError(string text)
    {
        var xerr = 0L;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("XErr", out var xerrElement) && xerrElement.TryGetInt64(out var parsed))
                xerr = parsed;
        }
        catch { }

        return xerr switch
        {
            2148916233 => new MicrosoftAuthException("这个 Microsoft 账号还没有 Xbox 档案。请先在 xbox.com 登录一次并创建档案。", "no_xbox_profile"),
            2148916235 => new MicrosoftAuthException("当前地区无法使用 Xbox Live。", "region_blocked"),
            2148916236 => new MicrosoftAuthException("需要先完成 Xbox 成人验证。", "adult_verification"),
            2148916237 => new MicrosoftAuthException("需要先完成 Xbox 成人验证。", "adult_verification"),
            2148916238 => new MicrosoftAuthException("这是未成年人账号，需要先加入 Microsoft 家庭组。", "child_account"),
            _ => new MicrosoftAuthException("Xbox 身份验证失败，请稍后重试。", "xbox_failed")
        };
    }

    private static async Task<T> PostJsonSafeAsync<T>(string url, object body, CancellationToken ct, string failure)
    {
        try
        {
            return await Web.PostJsonAsync<T>(url, body, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new MicrosoftAuthException($"{failure}：{Web.Truncate(ex.Message, 180)}", "request_failed");
        }
    }

    private static MicrosoftTokens ToTokens(TokenResponse token, string? fallbackRefresh = null)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new MicrosoftAuthException("Microsoft 没有返回访问令牌。", "token_empty");
        return new MicrosoftTokens
        {
            AccessToken = token.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? fallbackRefresh ?? "" : token.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(600, token.ExpiresIn))
        };
    }

    private static string FriendlyOAuthError(string text, string fallback)
    {
        var description = TryReadString(text, "error_description");
        return string.IsNullOrWhiteSpace(description) ? fallback : Clean(description);
    }

    private static string Clean(string value)
    {
        var dot = value.IndexOf('.');
        var cleaned = dot > 20 ? value[..dot] : value;
        return cleaned.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static CancellationTokenSource Timed(CancellationToken ct, int seconds = 20)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    private static string TryReadString(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var element) ? element.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    private static string FormatUuid(string raw)
    {
        var hex = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        if (hex.Length != 32) return "";
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
        [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
        [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class MinecraftTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class MinecraftProfile
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    private sealed class XboxResponse
    {
        public string Token { get; init; } = "";
        public string Uhs { get; init; } = "";
        public string Xuid { get; init; } = "";
    }
}
