using System.Runtime.InteropServices;
using Chisel.Core.Models;

namespace Chisel.Core.Minecraft;

public static class RuleEvaluator
{
    public static string OsName => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "osx" : "linux";

    public static string OsArch => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    };

    public static bool IsAllowed(List<Rule>? rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0) return true;
        var hasAllow = rules.Any(r => string.Equals(r.Action, "allow", StringComparison.OrdinalIgnoreCase));
        var result = !hasAllow;
        foreach (var rule in rules)
        {
            if (!Matches(rule, features)) continue;
            result = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    public static bool Matches(Rule rule, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rule.Os != null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name) && !string.Equals(rule.Os.Name, OsName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(rule.Os.Arch) && !matchesArch(rule.Os.Arch))
                return false;
            if (!string.IsNullOrEmpty(rule.Os.Version) && !MatchesVersionRange(rule.Os.Version))
                return false;
        }

        if (rule.Features != null)
        {
            foreach (var (key, expected) in rule.Features)
            {
                var actual = features != null && features.TryGetValue(key, out var v) && v;
                if (actual != expected) return false;
            }
        }

        return true;
    }

    private static bool matchesArch(string arch)
        => arch.ToLowerInvariant() switch
        {
            "x86" => OsArch == "x86",
            "x64" or "amd64" => OsArch == "x64",
            "arm64" => OsArch == "arm64",
            _ => true
        };

    public static bool MatchesVersionRange(string range)
    {
        var current = Environment.OSVersion.Version;
        range = range.Trim();
        if (range.Length == 0) return true;

        try
        {
            if (range.StartsWith('{'))
            {
                var min = ExtractJsonNumber(range, "min");
                var max = ExtractJsonNumber(range, "max");
                if (min != null && current < min) return false;
                if (max != null && current > max) return false;
                return true;
            }

            if (Version.TryParse(range.TrimStart('^', '~', '='), out var exact))
                return current >= exact;
        }
        catch { }
        return true;
    }

    private static Version? ExtractJsonNumber(string json, string key)
    {
        var idx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var colon = json.IndexOf(':', idx);
        if (colon < 0) return null;
        var quote1 = json.IndexOf('"', colon + 1);
        if (quote1 < 0) return null;
        var quote2 = json.IndexOf('"', quote1 + 1);
        if (quote2 < 0) return null;
        return Version.TryParse(json[(quote1 + 1)..quote2], out var v) ? v : null;
    }
}
