namespace Chisel.Core.Net;

public static class Mirror
{
    public const string Host = "https://bmclapi2.bangbang93.com";

    public static string? ForMojangAsset(string original)
    {
        if (original.StartsWith("https://libraries.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return Host + "/maven/" + original["https://libraries.minecraft.net/".Length..];

        if (original.StartsWith("https://resources.download.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return Host + "/assets/" + original["https://resources.download.minecraft.net/".Length..];

        if (original.StartsWith("https://piston-meta.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return Host + original["https://piston-meta.mojang.com".Length..];

        if (original.StartsWith("https://piston-data.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return Host + original["https://piston-data.mojang.com".Length..];

        if (original.StartsWith("https://launcher.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return Host + original["https://launcher.mojang.com".Length..];

        if (original.StartsWith("https://maven.minecraftforge.net/", StringComparison.OrdinalIgnoreCase))
            return Host + "/maven/" + original["https://maven.minecraftforge.net/".Length..];

        if (original.StartsWith("https://files.minecraftforge.net/maven/", StringComparison.OrdinalIgnoreCase))
            return Host + "/maven/" + original["https://files.minecraftforge.net/maven/".Length..];

        if (original.StartsWith("https://maven.neoforged.net/releases/", StringComparison.OrdinalIgnoreCase))
            return Host + "/maven/" + original["https://maven.neoforged.net/releases/".Length..];

        if (original.StartsWith("https://maven.neoforged.net/", StringComparison.OrdinalIgnoreCase))
            return Host + "/maven/" + original["https://maven.neoforged.net/".Length..];

        return null;
    }

    public static string VersionJson(string gameVersion) => $"{Host}/version/{Uri.EscapeDataString(gameVersion)}/json";
    public static string ClientJar(string gameVersion) => $"{Host}/version/{Uri.EscapeDataString(gameVersion)}/client";
    public static string Manifest() => $"{Host}/mc/game/version_manifest_v2.json";

    public static string ForgeInstaller(string gameVersion, string forgeVersion)
    {
        var pair = $"{gameVersion}-{forgeVersion}";
        return $"https://maven.minecraftforge.net/net/minecraftforge/forge/{pair}/forge-{pair}-installer.jar";
    }

    public static string NeoForgeInstaller(string version)
        => $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar";

    public static string OptiFineInstaller(string gameVersion, string type, string patch)
        => $"{Host}/optifine/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(patch)}";
}
