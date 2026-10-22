using System.Text.Json;
using System.Text.RegularExpressions;
using Chisel.Core.Models;

namespace Chisel.Core.Minecraft;

public sealed class LaunchPlan
{
    public required string JavaPath { get; init; }
    public required List<string> Args { get; init; }
    public required string NativesDir { get; init; }
    public required string GameDir { get; init; }
    public required string ClassPath { get; init; }
    public required int MemoryMb { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public static class LaunchPlanBuilder
{
    public static LaunchPlan Build(
        ResolvedVersion version,
        Instance instance,
        AuthUser user,
        JavaRuntime java,
        AppSettings settings,
        string pathsRoot,
        string librariesDir,
        string assetsDir,
        string versionDir,
        string gameDir,
        int memoryMb)
    {
        var warnings = new List<string>();
        var nativesDir = Path.Combine(pathsRoot, "instances", instance.Id, "natives");
        var classpathEntries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(library.Rules)) continue;
            var artifact = LibraryUtil.ArtifactFor(library);
            var relative = string.IsNullOrWhiteSpace(artifact?.Path)
                ? LibraryUtil.MavenPath(library.Name)
                : artifact!.Path!;
            var full = Path.Combine(librariesDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (artifact == null && !File.Exists(full)) continue;
            if (seen.Add(full)) classpathEntries.Add(full);
        }

        var clientJar = version.ClientJarPath;
        if (seen.Add(clientJar)) classpathEntries.Add(clientJar);
        var classpath = string.Join(Path.PathSeparator, classpathEntries);
        var assetsIndexName = version.Base.AssetIndex?.Id ?? version.Base.Assets ?? "";

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = user.Name,
            ["version_name"] = version.VersionName,
            ["game_directory"] = gameDir,
            ["assets_root"] = assetsDir,
            ["game_assets"] = Path.Combine(assetsDir, "virtual", assetsIndexName),
            ["assets_index_name"] = assetsIndexName,
            ["auth_uuid"] = user.Uuid.Replace("-", ""),
            ["auth_access_token"] = user.AccessToken,
            ["auth_session"] = $"token:{user.AccessToken}:{user.Uuid.Replace("-", "")}",
            ["clientid"] = user.ClientId ?? "",
            ["auth_xuid"] = user.Xuid ?? "",
            ["user_type"] = user.UserType,
            ["user_properties"] = "{}",
            ["version_type"] = version.Base.Type,
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = "Chisel",
            ["launcher_version"] = "0.1.0",
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = librariesDir,
            ["resolution_width"] = settings.Width.ToString(),
            ["resolution_height"] = settings.Height.ToString(),
            ["quickPlayPath"] = "",
            ["quickPlaySingleplayer"] = "",
            ["quickPlayMultiplayer"] = "",
            ["quickPlayRealms"] = ""
        };

        var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = settings.CustomResolution,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };

        var (gameArgumentNodes, jvmArgumentNodes) = VersionResolver.GetArguments(version);
        var jvmArgs = new List<string>
        {
            $"-Xms{Math.Min(512, memoryMb)}M",
            $"-Xmx{memoryMb}M"
        };
        if (java.Major >= 17)
        {
            jvmArgs.Add("-XX:+UseG1GC");
            jvmArgs.Add("-XX:MaxGCPauseMillis=200");
            jvmArgs.Add("-XX:+ParallelRefProcEnabled");
        }
        jvmArgs.AddRange(
        [
            "-Dfile.encoding=UTF-8",
            "-Dstdout.encoding=UTF-8",
            "-Dstderr.encoding=UTF-8",
            "-Dsun.stdout.encoding=UTF-8",
            "-Dsun.stderr.encoding=UTF-8"
        ]);

        if (!string.IsNullOrWhiteSpace(user.AuthServer))
        {
            if (string.IsNullOrWhiteSpace(user.AuthlibJarPath) || !File.Exists(user.AuthlibJarPath))
                throw new InvalidOperationException("外置登录组件缺失，请在账号页重新登录。");
            jvmArgs.Insert(0, $"-Dauthlibinjector.yggdrasil.prefetched={user.AuthlibPrefetched ?? ""}");
            jvmArgs.Insert(0, $"-javaagent:{user.AuthlibJarPath}={user.AuthServer}");
        }

        if (jvmArgumentNodes.Count > 0)
            jvmArgs.AddRange(EvaluateArguments(jvmArgumentNodes, placeholders, features));
        else
        {
            jvmArgs.Add($"-Djava.library.path={nativesDir}");
            jvmArgs.Add("-cp");
            jvmArgs.Add(classpath);
        }

        if (version.LogConfigPath != null && File.Exists(version.LogConfigPath))
        {
            var arg = version.Base.Logging?.Client?.Argument ?? "-Dlog4j.configurationFile=${path}";
            jvmArgs.Add(arg.Replace("${path}", version.LogConfigPath));
        }

        if (!string.IsNullOrWhiteSpace(settings.JvmArgs))
            jvmArgs.AddRange(SplitArgs(settings.JvmArgs));

        List<string> gameArgs;
        if (gameArgumentNodes.Count > 0)
        {
            gameArgs = EvaluateArguments(gameArgumentNodes, placeholders, features);
        }
        else if (!string.IsNullOrWhiteSpace(version.Loader?.MinecraftArguments) ||
                 !string.IsNullOrWhiteSpace(version.Base.MinecraftArguments))
        {
            gameArgs = SplitArgs(version.Loader?.MinecraftArguments ?? version.Base.MinecraftArguments!)
                .Select(a => Substitute(a, placeholders))
                .ToList();
        }
        else
        {
            gameArgs = [];
        }

        if (settings.CustomResolution)
        {
            var hasResolutionArgs = version.Base.Arguments?.Game?.Any(e =>
                e.ValueKind == JsonValueKind.Object &&
                e.TryGetProperty("value", out var value) &&
                value.ValueKind == JsonValueKind.Array &&
                value.EnumerateArray().Any(v => v.GetString() == "--width")) == true;
            if (!hasResolutionArgs)
            {
                gameArgs.Add("--width");
                gameArgs.Add(settings.Width.ToString());
                gameArgs.Add("--height");
                gameArgs.Add(settings.Height.ToString());
            }
        }

        var requiredJava = Math.Max(instance.RequiredJava ?? 0, version.RequiredJava);
        if (java.Major < requiredJava)
            throw new InvalidOperationException($"需要 Java {requiredJava} 或更高版本，当前为 Java {java.Major}。");
        if (java.Major > version.RequiredJava && version.RequiredJava <= 16)
            warnings.Add($"这个版本建议使用 Java {version.RequiredJava}，当前 Java {java.Major} 可能不兼容旧模组。");
        var args = new List<string>(jvmArgs.Count + gameArgs.Count + 2);
        args.AddRange(jvmArgs);
        args.Add(version.MainClass);
        args.AddRange(gameArgs);

        return new LaunchPlan
        {
            JavaPath = java.Path,
            Args = args,
            NativesDir = nativesDir,
            GameDir = gameDir,
            ClassPath = classpath,
            MemoryMb = memoryMb,
            Warnings = warnings
        };
    }

    public static List<string> EvaluateArguments(
        IEnumerable<JsonElement> nodes,
        IReadOnlyDictionary<string, string> placeholders,
        IReadOnlyDictionary<string, bool> features)
    {
        var result = new List<string>();
        foreach (var node in nodes)
        {
            if (node.ValueKind == JsonValueKind.String)
            {
                result.Add(Substitute(node.GetString() ?? "", placeholders));
                continue;
            }
            if (node.ValueKind != JsonValueKind.Object) continue;
            if (node.TryGetProperty("rules", out var rulesElement))
            {
                var allowed = EvaluateRuleArray(rulesElement, features);
                if (!allowed) continue;
            }
            if (!node.TryGetProperty("value", out var value)) continue;
            if (value.ValueKind == JsonValueKind.String)
            {
                result.Add(Substitute(value.GetString() ?? "", placeholders));
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        result.Add(Substitute(item.GetString() ?? "", placeholders));
                }
            }
        }
        return result;
    }

    private static bool EvaluateRuleArray(JsonElement rules, IReadOnlyDictionary<string, bool> features)
    {
        var parsed = new List<Rule>();
        try { parsed = JsonSerializer.Deserialize<List<Rule>>(rules.GetRawText(), AtomicJson.Compact) ?? []; }
        catch
        {
            return true;
        }
        return RuleEvaluator.IsAllowed(parsed, features);
    }

    public static string Substitute(string input, IReadOnlyDictionary<string, string> placeholders)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("${")) return input;
        foreach (var (key, value) in placeholders)
            input = input.Replace("${" + key + "}", value, StringComparison.Ordinal);
        return input;
    }

    public static List<string> SplitArgs(string input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return result;
        var current = new System.Text.StringBuilder();
        var quote = '\0';
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < input.Length && (input[i + 1] == quote || input[i + 1] == '\\'))
                {
                    current.Append(input[++i]);
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
                else current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else current.Append(c);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    public static string RedactLogLine(string line)
    {
        if (line.Length > 4000) line = line[..4000] + "…";
        return Regex.Replace(line, @"(--accessToken\s+)\S+", "$1***", RegexOptions.IgnoreCase);
    }
}
