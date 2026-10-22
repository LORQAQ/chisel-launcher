namespace Chisel.Core.Services;

public sealed class NoteItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Reply { get; set; } = "";
    public bool Rare { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotesService
{
    private const int MaxNotes = 60;
    private readonly object _gate = new();
    private readonly string _file;

    private static readonly (string Keyword, string Reply)[] Echoes =
    [
        ("晚安", "晚安。灯留在门口，风替你看着。"),
        ("谢谢", "山收到了。它不太会说谢，就让石头稍微暖一点。"),
        ("感谢", "山收到了。它不太会说谢，就让石头稍微暖一点。"),
        ("你好", "你好。这里风大，进来坐。"),
        ("嗨", "你好。这里风大，进来坐。"),
        ("hello", "你好。这里风大，进来坐。"),
        ("凿境", "你叫了它的名字。石头里有人应了一声。"),
        ("崩溃", "去控制台看看那行红色的字。它通常比我们诚实。"),
        ("闪退", "去控制台看看那行红色的字。它通常比我们诚实。"),
        ("报错", "去控制台看看那行红色的字。它通常比我们诚实。"),
        ("bug", "去控制台看看那行红色的字。它通常比我们诚实。"),
        ("孤独", "一个人也很好。这里的树不会问你为什么来。"),
        ("孤单", "一个人也很好。这里的树不会问你为什么来。"),
        ("再见", "路会记得你走过的方向。"),
        ("拜拜", "路会记得你走过的方向。"),
        ("喜欢", "被喜欢这件事，山也有一点不好意思。"),
        ("下雨", "雨会落在你搭的屋顶上。这大概就是存档的意义。"),
        ("生日", "生日快乐。今天的蛋糕不用合成，直接吃。"),
        ("累", "累了就把背包放下。这里没有倒计时。"),
        ("考试", "先把书合上。世界不会在你离开的十分钟里长脚跑掉。"),
        ("苦力怕", "嘘。它就在你背后。"),
        ("creeper", "嘘。它就在你背后。")
    ];

    private static readonly string[] QuietReplies =
    [
        "收到了。山谷里多了一点声音。",
        "这句话被放在门边的石缝里，不会丢。",
        "风把这句话带走了，方向刚好是东边。",
        "记下了。路过的兔子似乎也听见了。",
        "门没有锁。你随时可以回来看看它。",
        "这句话让今天的石头看起来暖了一点。",
        "山没有回答，但树叶动了一下。",
        "留在这里吧。等下一场雨，它会慢慢发芽。",
        "有些话不必有下文。放在这里就很好。",
        "凿境把它记在了木头背面。",
        "这里很安静，正好放得下这句话。",
        "远处有只狐狸停下了一会儿，又走了。"
    ];

    private static readonly string[] RareReplies =
    [
        "……石头好像动了一下。也可能只是风。",
        "这句话落下的时候，远处的矿洞亮了一下。",
        "有一瞬间，地形生成器好像回头看了你一眼。"
    ];

    public NotesService(AppPaths paths) => _file = Path.Combine(paths.Root, "notes.json");

    public List<NoteItem> List()
    {
        lock (_gate)
        {
            return Load().OrderByDescending(note => note.CreatedAt).Take(12).ToList();
        }
    }

    public NoteItem Add(string? text)
    {
        text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0) throw new ArgumentException("还没有写下什么。");
        if (text.Length > 80) text = text[..79].TrimEnd() + "…";

        lock (_gate)
        {
            var notes = Load();
            var (reply, rare) = Echo(text);
            var note = new NoteItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                Reply = reply,
                Rare = rare,
                CreatedAt = DateTimeOffset.Now
            };
            notes.Insert(0, note);
            if (notes.Count > MaxNotes) notes.RemoveRange(MaxNotes, notes.Count - MaxNotes);
            AtomicJson.Write(_file, notes);
            return note;
        }
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_gate)
        {
            var notes = Load();
            var removed = notes.RemoveAll(note => string.Equals(note.Id, id, StringComparison.Ordinal)) > 0;
            if (removed) AtomicJson.Write(_file, notes);
            return removed;
        }
    }

    private static (string Reply, bool Rare) Echo(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var (keyword, reply) in Echoes)
        {
            if (keyword == "累" && (lower.Contains("积累") || lower.Contains("累计") || lower.Contains("累积") || lower.Contains("连累")))
                continue;
            if (lower.Contains(keyword, StringComparison.Ordinal))
                return (reply, keyword is "苦力怕" or "creeper" or "凿境");
        }

        var seed = 2166136261u;
        foreach (var ch in text)
        {
            seed ^= ch;
            seed = (seed ^ (seed >> 13)) * 16777619u;
        }
        var pick = (int)(seed % QuietReplies.Length);
        if (seed % 17 == 0) return (RareReplies[(int)(seed / 7 % RareReplies.Length)], true);
        return (QuietReplies[pick], false);
    }

    private List<NoteItem> Load() => AtomicJson.Read(_file, () => new List<NoteItem>());
}
