using Chisel.Core.Models;

namespace Chisel.Core.Minecraft;

public static class LibraryUtil
{
    public static string Key(string name)
    {
        var parts = name.Split(':');
        if (parts.Length >= 3)
        {
            var classifier = parts.Length >= 4 ? parts[3] : "";
            return $"{parts[0]}:{parts[1]}:{classifier}".ToLowerInvariant();
        }
        return name.ToLowerInvariant();
    }

    public static string MavenPath(string name)
    {
        var raw = name;
        var extension = "jar";
        var at = raw.IndexOf('@');
        if (at > 0)
        {
            extension = raw[(at + 1)..];
            raw = raw[..at];
        }

        var parts = raw.Split(':');
        if (parts.Length < 3) return raw;
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length >= 4 ? "-" + parts[3] : "";
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}";
    }

    public static DownloadFile? ArtifactFor(Library library)
    {
        if (library.Downloads?.Artifact is { } artifact && !string.IsNullOrEmpty(artifact.Url))
        {
            if (string.IsNullOrEmpty(artifact.Path)) artifact.Path = MavenPath(library.Name);
            return artifact;
        }

        if (!string.IsNullOrWhiteSpace(library.Url))
        {
            return new DownloadFile
            {
                Path = MavenPath(library.Name),
                Url = library.Url.TrimEnd('/') + "/" + MavenPath(library.Name),
                Sha1 = library.Sha1 ?? "",
                Size = library.Size ?? 0
            };
        }

        return null;
    }

    public static string? NativeClassifier(Library library)
    {
        if (library.Natives is null) return null;
        if (!library.Natives.TryGetValue(RuleEvaluator.OsName, out var classifier)) return null;
        return classifier
            .Replace("${arch}", RuleEvaluator.OsArch == "x86" ? "32" : "64", StringComparison.OrdinalIgnoreCase);
    }

    public static DownloadFile? NativeArtifactFor(Library library)
    {
        var classifier = NativeClassifier(library);
        if (classifier is null) return null;
        if (library.Downloads?.Classifiers != null &&
            library.Downloads.Classifiers.TryGetValue(classifier, out var file) &&
            !string.IsNullOrEmpty(file.Url))
        {
            if (string.IsNullOrEmpty(file.Path))
                file.Path = MavenPath(library.Name + ":" + classifier);
            return file;
        }

        if (!string.IsNullOrWhiteSpace(library.Url))
        {
            var path = MavenPath(library.Name + ":" + classifier);
            return new DownloadFile
            {
                Path = path,
                Url = library.Url.TrimEnd('/') + "/" + path,
                Sha1 = "",
                Size = 0
            };
        }

        return null;
    }

    public static List<Library> Merge(IEnumerable<Library> baseLibraries, IEnumerable<Library>? loaderLibraries)
    {
        var merged = new List<Library>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in baseLibraries)
        {
            var key = Key(lib.Name);
            if (index.TryGetValue(key, out var i)) merged[i] = lib;
            else
            {
                index[key] = merged.Count;
                merged.Add(lib);
            }
        }
        if (loaderLibraries != null)
        {
            foreach (var lib in loaderLibraries)
            {
                var key = Key(lib.Name);
                if (index.TryGetValue(key, out var i)) merged[i] = lib;
                else
                {
                    index[key] = merged.Count;
                    merged.Add(lib);
                }
            }
        }
        return merged;
    }
}
