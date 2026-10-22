namespace Chisel.Core.Models;

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "offline";
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string? SkinPath { get; set; }
    public string? Xuid { get; set; }
    public string? ClientId { get; set; }
    public string? AuthServer { get; set; }
    public string? AuthServerName { get; set; }
    public string? AccessTokenProtected { get; set; }
    public string? RefreshTokenProtected { get; set; }
    public string? ClientTokenProtected { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class AccountView
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "offline";
    public string Name { get; init; } = "";
    public string Uuid { get; init; } = "";
    public string? AuthServerName { get; init; }
    public bool IsActive { get; init; }
    public bool HasSkin { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class AuthlibAccountRequest
{
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class OfflineAccountRequest
{
    public string Name { get; set; } = "";
}

public sealed class SetSkinRequest
{
    public string AccountId { get; set; } = "";
    public string DataUrl { get; set; } = "";
}

public sealed class AuthUser
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string UserType { get; set; } = "msa";
    public string? Xuid { get; set; }
    public string? ClientId { get; set; }
    public string? AuthServer { get; set; }
    public string? AuthlibJarPath { get; set; }
    public string? AuthlibPrefetched { get; set; }
}

public sealed class DeviceCodeChallenge
{
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public int ExpiresIn { get; set; }
    public int Interval { get; set; } = 5;
    public string ClientId { get; set; } = "";
}

public sealed class DeviceCodeInfo
{
    public string UserCode { get; init; } = "";
    public string VerificationUri { get; init; } = "";
    public string VerificationUriComplete { get; init; } = "";
    public int ExpiresIn { get; init; }
    public bool Copied { get; init; }
    public bool Opened { get; init; }
}
