using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Chisel.Core.Models;

namespace Chisel.Core.Services;

public sealed class InstanceStore
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();
    private static readonly Regex SafeId = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$", RegexOptions.Compiled);
    private static readonly Regex UnsafeChars = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);

    public InstanceStore(AppPaths paths) => _paths = paths;

    public List<Instance> LoadAll()
    {
        lock (_gate)
        {
            var list = new List<Instance>();
            if (!Directory.Exists(_paths.InstancesDir)) return list;
            foreach (var dir in Directory.EnumerateDirectories(_paths.InstancesDir))
            {
                var file = Path.Combine(dir, "instance.json");
                if (!File.Exists(file)) continue;
                var instance = AtomicJson.Read<Instance>(file, () => new Instance());
                if (string.IsNullOrEmpty(instance.Id)) instance.Id = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(instance.DisplayName)) instance.DisplayName = instance.Id;
                list.Add(instance);
            }
            return list.OrderByDescending(i => i.LastPlayedAt ?? i.CreatedAt).ToList();
        }
    }

    public Instance? Get(string id)
    {
        ValidateId(id);
        lock (_gate)
        {
            var file = _paths.InstanceFile(id);
            if (!File.Exists(file)) return null;
            var instance = AtomicJson.Read<Instance>(file, () => new Instance());
            if (string.IsNullOrEmpty(instance.Id)) instance.Id = id;
            return instance;
        }
    }

    public InstanceSummary Summarize(Instance instance, bool includeSize = false)
    {
        ValidateId(instance.Id);
        var gameDir = _paths.GameDir(instance.Id);
        var mods = CountFiles(Path.Combine(gameDir, "mods"), "*.jar*");
        var saves = CountDirectoriesWithFile(Path.Combine(gameDir, "saves"), "level.dat");
        var ready = File.Exists(Path.Combine(_paths.VersionDir(instance.GameVersion), "client.jar"))
                    && File.Exists(Path.Combine(_paths.VersionDir(instance.GameVersion), "version.json"));
        if (!string.Equals(instance.Loader, "vanilla", StringComparison.OrdinalIgnoreCase) && instance.ProfileId != null)
            ready &= File.Exists(Path.Combine(_paths.LoaderDir(instance.ProfileId), "profile.json"));

        var javaReady = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(instance.JavaPathOverride) && File.Exists(instance.JavaPathOverride))
                javaReady = true;
        }
        catch { }

        return new InstanceSummary
        {
            Instance = instance,
            GameDir = gameDir,
            Ready = ready,
            JavaReady = javaReady,
            ModCount = mods,
            SaveCount = saves,
            SizeBytes = includeSize ? DirectorySize(Path.Combine(_paths.InstanceDir(instance.Id), "game")) : 0
        };
    }

    public string CreateId(string gameVersion, string loader)
    {
        lock (_gate)
        {
            var baseId = UnsafeChars.Replace($"{gameVersion}-{loader}".Replace("-vanilla", ""), "-").Trim('-', '.');
            if (baseId.Length == 0) baseId = "instance";
            if (baseId.Length > 60) baseId = baseId[..60];
            var id = baseId;
            var n = 2;
            while (Directory.Exists(_paths.InstanceDir(id))) id = $"{baseId}-{n++}";
            return id;
        }
    }

    public void Save(Instance instance)
    {
        ValidateId(instance.Id);
        lock (_gate)
        {
            Directory.CreateDirectory(_paths.InstanceDir(instance.Id));
            AtomicJson.Write(_paths.InstanceFile(instance.Id), instance);
        }
    }

    public void SetLastPlayed(string id, long addSeconds, int? exitCode, string? exitReason)
    {
        ValidateId(id);
        lock (_gate)
        {
            var instance = GetUnlocked(id);
            if (instance is null) return;
            instance.LastPlayedAt = DateTimeOffset.Now;
            instance.PlaySeconds += Math.Max(0, addSeconds);
            instance.LastExitCode = exitCode;
            instance.LastExitReason = exitReason;
            AtomicJson.Write(_paths.InstanceFile(id), instance);
        }
    }

    public void Rename(string id, string name)
    {
        ValidateId(id);
        name = name.Trim();
        if (name.Length is < 1 or > 40) throw new ArgumentException("名字应为 1–40 个字符");
        lock (_gate)
        {
            var instance = GetUnlocked(id) ?? throw new InvalidOperationException("实例不存在");
            instance.DisplayName = name;
            AtomicJson.Write(_paths.InstanceFile(id), instance);
        }
    }

    public void Delete(string id)
    {
        ValidateId(id);
        lock (_gate)
        {
            var dir = _paths.InstanceDir(id);
            if (!Directory.Exists(dir)) return;
            if (!MoveToRecycleBin(dir))
                throw new IOException("无法把实例移到回收站。请关闭占用该文件夹的程序后重试。");
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Contains("..", StringComparison.Ordinal) || !SafeId.IsMatch(id))
            throw new ArgumentException("实例 ID 不合法");
    }

    private Instance? GetUnlocked(string id)
    {
        var file = _paths.InstanceFile(id);
        if (!File.Exists(file)) return null;
        var instance = AtomicJson.Read<Instance>(file, () => new Instance());
        if (string.IsNullOrEmpty(instance.Id)) instance.Id = id;
        return instance;
    }

    private static int CountFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Length : 0; }
        catch { return 0; }
    }

    private static int CountDirectoriesWithFile(string dir, string file)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            return Directory.GetDirectories(dir).Count(d => File.Exists(Path.Combine(d, file)));
        }
        catch { return 0; }
    }

    private static long DirectorySize(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f =>
            {
                try { return f.Length; } catch { return 0; }
            });
        }
        catch { return 0; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    private static bool MoveToRecycleBin(string path)
    {
        try
        {
            var op = new ShFileOpStruct
            {
                wFunc = 3,
                pFrom = path + "\0\0",
                fFlags = 0x0040 | 0x0010 | 0x0400
            };
            return SHFileOperation(ref op) == 0;
        }
        catch { return false; }
    }

    public static string FormatPlaytime(long seconds)
    {
        if (seconds < 60) return "不到一分钟";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分钟";
        return $"{ts.Minutes} 分钟";
    }
}
