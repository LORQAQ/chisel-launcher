using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Chisel.Core.Models;

namespace Chisel.Core.Services;

public sealed class AccountsDocument
{
    public string? ActiveId { get; set; }
    public List<Account> Accounts { get; set; } = [];
}

public sealed class AccountService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Chisel.Account.v1");
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly MicrosoftAuth _auth;
    private readonly AuthlibService _authlib;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AccountsDocument _document;
    private DeviceCodeChallenge? _pendingDevice;
    private DateTimeOffset _pendingStartedAt;

    public AccountService(AppPaths paths, SettingsService settings, MicrosoftAuth auth, AuthlibService authlib)
    {
        _paths = paths;
        _settings = settings;
        _auth = auth;
        _authlib = authlib;
        _document = AtomicJson.Read(_paths.AccountsPath, () => new AccountsDocument());
        Normalize();
    }

    public List<AccountView> List()
    {
        lock (_gate)
        {
            var activeId = ResolveActiveId();
            return _document.Accounts
                .Select(a => ToView(a, string.Equals(a.Id, activeId, StringComparison.Ordinal)))
                .ToList();
        }
    }

    public AccountView? GetView(string id)
    {
        lock (_gate)
        {
            var account = Find(id);
            return account == null ? null : ToView(account, string.Equals(account.Id, ResolveActiveId(), StringComparison.Ordinal));
        }
    }

    public Account? GetActive()
    {
        lock (_gate)
        {
            var id = ResolveActiveId();
            return id == null ? null : Find(id);
        }
    }

    public void SetActive(string id)
    {
        lock (_gate)
        {
            if (Find(id) == null) throw new InvalidOperationException("账号不存在");
            _document.ActiveId = id;
            _settings.Mutate(s => s.ActiveAccountId = id);
            Save();
        }
    }

    public AccountView AddOffline(string name)
    {
        name = (name ?? "").Trim();
        if (!IsValidOfflineName(name))
            throw new ArgumentException("离线用户名需要 3–16 位，只能包含字母、数字和下划线");

        lock (_gate)
        {
            if (_document.Accounts.Any(a => a.Type == "offline" && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("已经有同名的离线账号了");

            var account = new Account
            {
                Type = "offline",
                Name = name,
                Uuid = OfflineUuid(name),
                CreatedAt = DateTimeOffset.Now
            };
            _document.Accounts.Add(account);
            if (string.IsNullOrEmpty(ResolveActiveId())) _document.ActiveId = account.Id;
            _settings.Mutate(s => s.ActiveAccountId = _document.ActiveId);
            Save();
            return ToView(account, true);
        }
    }

    public async Task<AccountView> AddAuthlibAsync(string server, string username, string password, CancellationToken ct)
    {
        var result = await _authlib.LoginAsync(server, username, password, ct);
        lock (_gate)
        {
            var existing = _document.Accounts.FirstOrDefault(a =>
                a.Type == "authlib" &&
                string.Equals(a.AuthServer, result.Server, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Uuid, result.Uuid, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new Account
                {
                    Type = "authlib",
                    Uuid = result.Uuid,
                    Name = result.Name,
                    AuthServer = result.Server,
                    AuthServerName = result.ServerName,
                    CreatedAt = DateTimeOffset.Now
                };
                _document.Accounts.Add(existing);
            }
            existing.Name = result.Name;
            existing.Uuid = result.Uuid;
            existing.AuthServer = result.Server;
            existing.AuthServerName = result.ServerName;
            existing.AccessTokenProtected = Protect(result.AccessToken);
            existing.ClientTokenProtected = Protect(result.ClientToken);
            existing.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
            _document.ActiveId = existing.Id;
            _settings.Mutate(s => s.ActiveAccountId = existing.Id);
            Save();
            return ToView(existing, true);
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            var account = Find(id) ?? throw new InvalidOperationException("账号不存在");
            _document.Accounts.Remove(account);
            if (_document.Accounts.Count > 0)
            {
                if (string.Equals(_document.ActiveId, id, StringComparison.Ordinal))
                    _document.ActiveId = _document.Accounts[0].Id;
            }
            else _document.ActiveId = null;
            _settings.Mutate(s => s.ActiveAccountId = _document.ActiveId);
            Save();
            try
            {
                var dir = Path.Combine(_paths.AccountsDir, account.Id);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
    }

    public async Task<DeviceCodeInfo> BeginMicrosoftAsync(CancellationToken ct)
    {
        var clientId = _settings.Snapshot().MicrosoftClientId;
        var challenge = await _auth.RequestDeviceCodeAsync(clientId, ct);
        lock (_gate)
        {
            _pendingDevice = challenge;
            _pendingStartedAt = DateTimeOffset.Now;
        }
        return new DeviceCodeInfo
        {
            UserCode = challenge.UserCode,
            VerificationUri = challenge.VerificationUri,
            ExpiresIn = challenge.ExpiresIn
        };
    }

    public void CancelPendingMicrosoft()
    {
        lock (_gate) _pendingDevice = null;
    }

    public async Task<AccountView> CompleteMicrosoftAsync(Action<string>? onWait, CancellationToken ct)
    {
        DeviceCodeChallenge challenge;
        lock (_gate)
        {
            challenge = _pendingDevice ?? throw new InvalidOperationException("登录流程已结束，请重新开始。");
            if (DateTimeOffset.Now - _pendingStartedAt > TimeSpan.FromSeconds(challenge.ExpiresIn))
                throw new InvalidOperationException("登录代码已过期，请重新开始。");
        }

        var tokens = await _auth.PollForTokensAsync(challenge, onWait, ct);
        var session = await _auth.CreateMinecraftSessionAsync(tokens, challenge.ClientId, ct);

        lock (_gate)
        {
            _pendingDevice = null;
            var existing = _document.Accounts.FirstOrDefault(a =>
                a.Type == "microsoft" &&
                string.Equals(a.Uuid, session.User.Uuid, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new Account
                {
                    Type = "microsoft",
                    Uuid = session.User.Uuid,
                    Name = session.User.Name,
                    CreatedAt = DateTimeOffset.Now
                };
                _document.Accounts.Add(existing);
            }

            existing.Name = session.User.Name;
            existing.ClientId = challenge.ClientId;
            existing.Xuid = session.User.Xuid;
            existing.AccessTokenProtected = Protect(session.Tokens.AccessToken);
            existing.RefreshTokenProtected = Protect(session.Tokens.RefreshToken);
            existing.ExpiresAt = session.Tokens.ExpiresAt;
            _document.ActiveId = existing.Id;
            _settings.Mutate(s => s.ActiveAccountId = existing.Id);
            Save();
            return ToView(existing, true);
        }
    }

    public async Task<AuthUser> GetLaunchUserAsync(string? accountId, CancellationToken ct)
    {
        Account account;
        lock (_gate)
        {
            var id = string.IsNullOrWhiteSpace(accountId) ? ResolveActiveId() : accountId;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("还没有添加游戏账号。");
            account = Find(id!) ?? throw new InvalidOperationException("账号不存在，请重新选择。");
            if (account.Type == "offline")
            {
                return new AuthUser
                {
                    Uuid = account.Uuid,
                    Name = account.Name,
                    AccessToken = "0",
                    UserType = "legacy"
                };
            }
        }

        if (string.Equals(account.Type, "authlib", StringComparison.OrdinalIgnoreCase))
            return await BuildAuthlibUserAsync(account, ct);

        await _refreshGate.WaitAsync(ct);
        try
        {
            lock (_gate)
            {
                if (string.Equals(account.Type, "offline", StringComparison.OrdinalIgnoreCase))
                {
                    return new AuthUser { Uuid = account.Uuid, Name = account.Name, AccessToken = "0", UserType = "legacy" };
                }
            }

            var cachedToken = Unprotect(account.AccessTokenProtected);
            if (!string.IsNullOrEmpty(cachedToken) && account.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return new AuthUser
                {
                    Uuid = account.Uuid,
                    Name = account.Name,
                    AccessToken = cachedToken,
                    UserType = "msa",
                    Xuid = account.Xuid,
                    ClientId = account.ClientId
                };
            }

            var refreshToken = Unprotect(account.RefreshTokenProtected);
            var clientId = string.IsNullOrWhiteSpace(account.ClientId)
                ? _settings.Snapshot().MicrosoftClientId
                : account.ClientId!;
            var refreshed = await _auth.RefreshAsync(refreshToken, clientId, ct);
            var session = await _auth.CreateMinecraftSessionAsync(refreshed, clientId, ct);

            lock (_gate)
            {
                account.Name = session.User.Name;
                account.Uuid = session.User.Uuid;
                account.Xuid = session.User.Xuid;
                account.ClientId = clientId;
                account.AccessTokenProtected = Protect(session.Tokens.AccessToken);
                account.RefreshTokenProtected = Protect(session.Tokens.RefreshToken);
                account.ExpiresAt = session.Tokens.ExpiresAt;
                Save();
            }
            return session.User;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<AuthUser> BuildAuthlibUserAsync(Account account, CancellationToken ct)
    {
        var server = account.AuthServer;
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException("外置登录信息不完整，请在账号页重新登录。");
        var token = Unprotect(account.AccessTokenProtected);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("外置登录令牌已丢失，请重新登录。");

        if (account.ExpiresAt == null || account.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(2))
        {
            var clientToken = Unprotect(account.ClientTokenProtected);
            var refreshed = await _authlib.RefreshAsync(server, token, clientToken, ct);
            token = refreshed.AccessToken;
            lock (_gate)
            {
                account.AccessTokenProtected = Protect(refreshed.AccessToken);
                account.ClientTokenProtected = Protect(refreshed.ClientToken);
                account.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
                if (!string.IsNullOrWhiteSpace(refreshed.Name)) account.Name = refreshed.Name;
                if (!string.IsNullOrWhiteSpace(refreshed.Uuid)) account.Uuid = refreshed.Uuid;
                account.AuthServerName = refreshed.ServerName;
                Save();
            }
        }

        var (jar, prefetched) = await _authlib.PrepareAsync(server, ct);
        return new AuthUser
        {
            Uuid = account.Uuid,
            Name = account.Name,
            AccessToken = token,
            UserType = "mojang",
            AuthServer = server,
            AuthlibJarPath = jar,
            AuthlibPrefetched = prefetched
        };
    }

    public AccountView SetSkin(string accountId, string dataUrl)
    {
        var bytes = DecodeDataUrl(dataUrl);
        if (bytes.Length < 100) throw new ArgumentException("皮肤图片太小");
        if (bytes.Length > 2 * 1024 * 1024) throw new ArgumentException("皮肤图片不能超过 2 MB");
        var (width, height) = ReadPngSize(bytes);
        if (!(width == 64 && (height == 64 || height == 32)) && !(width == 128 && height == 128))
            throw new ArgumentException("皮肤需要是 64×64 或 64×32 的 PNG");

        lock (_gate)
        {
            var account = Find(accountId) ?? throw new InvalidOperationException("账号不存在");
            var dir = Path.Combine(_paths.AccountsDir, account.Id);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "skin.png"), bytes);
            account.SkinPath = "skin.png";
            Save();
            return ToView(account, string.Equals(account.Id, ResolveActiveId(), StringComparison.Ordinal));
        }
    }

    public string? SkinFile(string accountId)
    {
        lock (_gate)
        {
            var account = Find(accountId);
            if (account?.SkinPath == null) return null;
            var path = Path.IsPathRooted(account.SkinPath)
                ? account.SkinPath
                : Path.Combine(_paths.AccountsDir, account.Id, account.SkinPath);
            return _paths.IsUnderRoot(path) && File.Exists(path) ? path : null;
        }
    }

    public static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    public static bool IsValidOfflineName(string name)
        => name.Length is >= 3 and <= 16 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private void Normalize()
    {
        _document.Accounts ??= [];
        foreach (var account in _document.Accounts)
        {
            if (string.IsNullOrEmpty(account.Id)) account.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(account.Uuid) && account.Type == "offline")
                account.Uuid = OfflineUuid(account.Name);
        }
        if (_document.ActiveId != null && Find(_document.ActiveId) == null)
            _document.ActiveId = _document.Accounts.FirstOrDefault()?.Id;
        if (_document.ActiveId == null)
            _document.ActiveId = _document.Accounts.FirstOrDefault()?.Id;
        if (string.IsNullOrEmpty(_settings.Get().ActiveAccountId) && _document.ActiveId != null)
            _settings.Mutate(s => s.ActiveAccountId = _document.ActiveId);
    }

    private string? ResolveActiveId()
    {
        var fromSettings = _settings.Get().ActiveAccountId;
        if (!string.IsNullOrEmpty(fromSettings) && Find(fromSettings) != null) return fromSettings;
        if (_document.ActiveId != null && Find(_document.ActiveId) != null) return _document.ActiveId;
        return _document.Accounts.FirstOrDefault()?.Id;
    }

    private Account? Find(string id) => _document.Accounts.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    private void Save() => AtomicJson.Write(_paths.AccountsPath, _document);

    private AccountView ToView(Account account, bool isActive) => new()
    {
        Id = account.Id,
        Type = account.Type,
        Name = account.Name,
        Uuid = account.Uuid,
        AuthServerName = account.AuthServerName,
        IsActive = isActive,
        HasSkin = SkinFile(account.Id) != null,
        ExpiresAt = account.ExpiresAt
    };

    private static string Protect(string value)
        => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    private static string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
        }
        catch { return ""; }
    }

    private static byte[] DecodeDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("皮肤需要是 PNG 图片");
        try { return Convert.FromBase64String(dataUrl[(comma + 1)..]); }
        catch { throw new ArgumentException("皮肤图片数据不完整"); }
    }

    private static (int Width, int Height) ReadPngSize(byte[] data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 24 || !data.AsSpan(0, 8).SequenceEqual(signature) ||
            data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
            throw new ArgumentException("皮肤需要是有效的 PNG 图片");
        var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        return (width, height);
    }
}
