using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Items
{
    // Готовое описание предмета в игре
    public sealed class ItemDef
    {
        public int Id;               // номер на время игры: склад, предметы в мире, сеть
        public string Key;           // стабильное имя для сохранений: "block/Stone", "raw_pork"
        public string Name;
        public int MaxStack;
        public BlockType Block;      // какой блок ставит (Air = не ставится)
        public float FoodHeal;       // >0: съедобное, столько здоровья восстанавливает
        public Texture2D Texture;    // картинка у предметов, которые не блоки
    }

    // Список всех предметов. Собирается один раз при старте мира (WorldManager.Awake).
    //  - на каждый блок из базы блоков автоматически создаётся предмет, номер равен значению BlockType;
    //  - предметы из Resources/Items получают номера с 1000 по порядку ключей.
    // В сохранениях используются ключи (строки), поэтому порядок и номера можно менять свободно.
    public static class ItemRegistry
    {
        public const int FirstCustomId = 1000;
        public const int DefaultStack = 99;

        private static readonly Dictionary<int, ItemDef> _byId = new Dictionary<int, ItemDef>();
        private static readonly Dictionary<string, ItemDef> _byKey = new Dictionary<string, ItemDef>();
        private static readonly Dictionary<BlockType, ItemDef> _byBlock = new Dictionary<BlockType, ItemDef>();
        private static readonly List<ItemDef> _all = new List<ItemDef>();
        private static readonly List<Texture2D> _textures = new List<Texture2D>();

        public static bool Ready { get; private set; }
        public static IReadOnlyList<ItemDef> All => _all;
        public static IEnumerable<Texture2D> Textures => _textures;   // для атласа

        public static void Build(Dictionary<BlockType, BlockDataSO> blockDb)
        {
            _byId.Clear(); _byKey.Clear(); _byBlock.Clear(); _all.Clear(); _textures.Clear();

            // 1. предметы-блоки
            if (blockDb != null)
            {
                var types = new List<BlockType>(blockDb.Keys);
                types.Sort();
                foreach (BlockType t in types)
                {
                    BlockDataSO d = blockDb[t];
                    if (d == null || t == BlockType.Air || t == BlockType.Bedrock || t == BlockType.Water || t == BlockType.Lava) continue;
                    if (t.IsSign() && t != BlockType.SignStanding0) continue;   // табличка одна, а не 12

                    if (t == BlockType.StoneSlabDouble) continue;

                    Add(new ItemDef
                    {
                        Id = (int)t,
                        Key = "block/" + t,
                        Name = string.IsNullOrEmpty(d.BlockName) ? t.ToString() : d.BlockName,
                        MaxStack = DefaultStack,
                        Block = t
                    });
                }
            }

            // 2. остальные предметы
            var assets = new List<ItemDataSO>(Resources.LoadAll<ItemDataSO>("Items"));
            assets.Sort((a, b) => string.CompareOrdinal(KeyOf(a), KeyOf(b)));

            int next = FirstCustomId;
            foreach (ItemDataSO so in assets)
            {
                string key = KeyOf(so);
                if (_byKey.ContainsKey(key))
                {
                    Debug.LogWarning($"[ItemRegistry] Повторяющийся ключ предмета '{key}' ({so.name}), пропускаю.");
                    continue;
                }

                Add(new ItemDef
                {
                    Id = next++,
                    Key = key,
                    Name = string.IsNullOrEmpty(so.DisplayName) ? key : so.DisplayName,
                    MaxStack = Mathf.Max(1, so.MaxStack),
                    Block = so.PlaceBlock,
                    FoodHeal = Mathf.Max(0f, so.FoodHeal),
                    Texture = so.Texture
                });
                if (so.Texture != null) _textures.Add(so.Texture);
            }

            Ready = true;
            Debug.Log($"[ItemRegistry] Предметов: {_all.Count} (из них вне блоков: {next - FirstCustomId}).");
        }

        private static string KeyOf(ItemDataSO so) => string.IsNullOrWhiteSpace(so.Key) ? so.name : so.Key.Trim();

        private static void Add(ItemDef def)
        {
            _byId[def.Id] = def;
            _byKey[def.Key] = def;
            _all.Add(def);
            if (def.Block != BlockType.Air && !_byBlock.ContainsKey(def.Block)) _byBlock[def.Block] = def;
        }

        // ---------- Поиск ----------

        public static ItemDef Get(int id) => _byId.TryGetValue(id, out ItemDef d) ? d : null;
        public static ItemDef Find(string key) => !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out ItemDef d) ? d : null;
        public static int IdOf(string key) { var d = Find(key); return d != null ? d.Id : 0; }
        public static string KeyOf(int id) { var d = Get(id); return d != null ? d.Key : ""; }

        // Что выпадает из сломанного блока (0 = ничего)
        public static int IdForBlock(BlockType broken)
        {
            if (broken == BlockType.StoneSlabDouble) broken = BlockType.StoneSlab;
            if (broken.IsSign()) broken = BlockType.SignStanding0;
            return _byBlock.TryGetValue(broken, out ItemDef d) ? d.Id : 0;
        }

        // Какой блок ставит предмет (Air, если не ставит)
        public static BlockType BlockOf(int id) { var d = Get(id); return d != null ? d.Block : BlockType.Air; }

        public static int MaxStackOf(int id) { var d = Get(id); return d != null ? d.MaxStack : DefaultStack; }
        public static float FoodHealOf(int id) { var d = Get(id); return d != null ? d.FoodHeal : 0f; }

        // Предметы, на которые ссылается код
        public static int RawPork => IdOf("raw_pork");
    }
}