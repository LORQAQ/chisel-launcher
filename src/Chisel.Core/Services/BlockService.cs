using System.IO.Compression;
using Chisel.Core.Models;

namespace Chisel.Core.Services;

public sealed class BlockService
{
    private readonly AppPaths _paths;
    private readonly InstanceStore _instances;
    private readonly object _gate = new();
    private (string Key, string? DataUrl)? _cache;

    private static readonly BlockDefinition[] Blocks =
    [
        new("stone", "石头", "Stone", "最不起眼的一块，却撑起了大多数人的第一间屋子。", "#7f807c", "stone.png"),
        new("deepslate", "深板岩", "Deepslate", "越往下走，颜色越沉。它有耐心，等你自己决定挖到哪一层。", "#4b4c52", "deepslate.png"),
        new("moss_block", "苔藓块", "Moss Block", "旧墙脚上的一小片绿。时间在这里不声不响地留下证据。", "#5d7a3e", "moss_block.png"),
        new("copper_block", "铜块", "Block of Copper", "刚放下去时很亮，过一阵子会慢慢变旧，正好。", "#b06f51", "copper_block.png"),
        new("amethyst_block", "紫水晶块", "Amethyst Block", "在洞穴深处发光，不急着被谁找到。", "#8a6faf", "amethyst_block.png"),
        new("calcite", "方解石", "Calcite", "夹在石层之间的一道浅色，像山体里的一次停顿。", "#b6b4aa", "calcite.png"),
        new("tuff", "凝灰岩", "Tuff", "火山灰压了很多年，才变得这么安静。", "#6d6d66", "tuff.png"),
        new("dark_oak_planks", "深色橡木木板", "Dark Oak Planks", "颜色沉，纹理紧。适合做书桌、地板，或者一间不想被打扰的屋子。", "#3f2c1a", "dark_oak_planks.png"),
        new("bookshelf", "书架", "Bookshelf", "真正读过的书不必摆满，留一点空位更好。", "#785736", "bookshelf.png"),
        new("redstone_block", "红石块", "Block of Redstone", "沉默的时候也在供电，只是没人听见。", "#a51f1d", "redstone_block.png"),
        new("lapis_block", "青金石块", "Lapis Lazuli Block", "颜色像很深的傍晚，适合放在附魔台边上。", "#2c4f9d", "lapis_block.png"),
        new("honeycomb_block", "蜂巢块", "Honeycomb Block", "一格一格地垒起来，不慌，最后总会有形状。", "#df9a3a", "honeycomb_block.png")
    ];

    public BlockService(AppPaths paths, InstanceStore instances)
    {
        _paths = paths;
        _instances = instances;
    }

    public DailyBlock GetDaily(string? instanceId)
    {
        var day = DateOnly.FromDateTime(DateTime.Now).DayNumber;
        var definition = Blocks[day % Blocks.Length];
        var instance = string.IsNullOrWhiteSpace(instanceId) ? null : _instances.Get(instanceId);
        instance ??= _instances.LoadAll().FirstOrDefault(i =>
            File.Exists(Path.Combine(_paths.VersionDir(i.GameVersion), "version.json")) &&
            File.Exists(Path.Combine(_paths.VersionDir(i.GameVersion), "client.jar")));

        string? dataUrl = null;
        string? gameVersion = null;
        if (instance != null)
        {
            var texture = LoadTexture(instance, definition.Texture);
            dataUrl = texture.DataUrl;
            gameVersion = texture.Version;
        }

        return new DailyBlock
        {
            Id = definition.Id,
            NameZh = definition.NameZh,
            NameEn = definition.NameEn,
            Note = definition.Note,
            DataUrl = dataUrl,
            GameVersion = gameVersion,
            Accent = definition.Accent
        };
    }

    private (string? DataUrl, string? Version) LoadTexture(Instance instance, string texture)
    {
        var versionDir = _paths.VersionDir(instance.GameVersion);
        var versionJsonPath = Path.Combine(versionDir, "version.json");
        string? assetId = null;
        if (File.Exists(versionJsonPath))
        {
            var baseVersion = AtomicJson.Read<MinecraftVersion>(versionJsonPath, () => new MinecraftVersion());
            assetId = baseVersion.AssetIndex?.Id;
        }

        var cacheKey = $"{instance.GameVersion}:{assetId}:{texture}";
        lock (_gate)
        {
            if (_cache is { } cached && cached.Key == cacheKey) return (cached.DataUrl, instance.GameVersion);
        }

        byte[]? bytes = null;
        if (!string.IsNullOrEmpty(assetId))
        {
            var indexPath = Path.Combine(_paths.AssetsDir, "indexes", Path.GetFileName(assetId) + ".json");
            if (File.Exists(indexPath))
            {
                try
                {
                    var index = AtomicJson.Read<AssetIndexFile>(indexPath, () => new AssetIndexFile());
                    if (index.Objects.TryGetValue("minecraft/textures/block/" + texture, out var obj) && obj.Hash.Length >= 4)
                    {
                        var objectPath = Path.Combine(_paths.AssetsDir, "objects", obj.Hash[..2], obj.Hash);
                        if (File.Exists(objectPath)) bytes = File.ReadAllBytes(objectPath);
                    }
                }
                catch { }
            }
        }

        if (bytes == null)
        {
            var jar = Path.Combine(versionDir, "client.jar");
            if (File.Exists(jar))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(jar);
                    var entry = archive.GetEntry("assets/minecraft/textures/block/" + texture);
                    if (entry != null)
                    {
                        using var stream = entry.Open();
                        using var memory = new MemoryStream();
                        stream.CopyTo(memory);
                        bytes = memory.ToArray();
                    }
                }
                catch { }
            }
        }

        var dataUrl = bytes is { Length: > 24 } ? "data:image/png;base64," + Convert.ToBase64String(bytes) : null;
        lock (_gate) _cache = (cacheKey, dataUrl);
        return (dataUrl, instance.GameVersion);
    }

    private sealed record BlockDefinition(
        string Id,
        string NameZh,
        string NameEn,
        string Note,
        string Accent,
        string Texture);
}
