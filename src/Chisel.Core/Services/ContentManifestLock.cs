namespace Chisel.Core.Services;

internal static class ContentManifestLock
{
    public static readonly object Gate = new();
}
