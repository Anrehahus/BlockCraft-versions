using System;
using System.Collections.Generic;
using MinecraftClone.Core;
using MinecraftClone.Items;

namespace MinecraftClone.Player
{
    // Id: номер предмета из ItemRegistry (0 = пусто)
    public struct ItemStack
    {
        public int Id;
        public int Count;

        public ItemStack(int id, int count) { Id = id; Count = count; }
        public bool IsEmpty => Id <= 0 || Count <= 0;
    }

    // Склад игрока: 9 слотов хотбара (индексы 0..8) и 27 слотов инвентаря (9..35, ряды сверху вниз).
    // Статический: у каждого клиента свой. На сервере копию хранит MinecraftNetworkManager по нику.
    public static class PlayerInventory
    {
        public const int HotbarSize = 9;
        public const int MainSize = 27;
        public const int Size = HotbarSize + MainSize;

        private static readonly ItemStack[] _slots = new ItemStack[Size];

        public static event Action Changed;

        // Чистый клиент: сервер уже прислал сохранённый инвентарь, можно слать ему изменения.
        // Пока false, свои изменения серверу не отправляются (иначе пустой склад затёр бы сохранённый).
        public static bool NetSynced;

        public static ItemStack Get(int i) => _slots[i];

        public static void Set(int i, ItemStack s)
        {
            if (s.IsEmpty) s = default;
            _slots[i] = s;
            Changed?.Invoke();
        }

        public static ItemStack[] GetAll()
        {
            var a = new ItemStack[Size];
            Array.Copy(_slots, a, Size);
            return a;
        }

        // Номер предмета в слоте хотбара или 0, если пусто
        public static int HotbarItem(int i)
        {
            if (i < 0 || i >= HotbarSize || _slots[i].IsEmpty) return 0;
            return _slots[i].Id;
        }

        public static ItemDef HotbarDef(int i)
        {
            int id = HotbarItem(i);
            return id > 0 ? ItemRegistry.Get(id) : null;
        }

        // creativeHotbarIds != null: креатив, хотбар заполняется набором. null: всё пусто.
        public static void Reset(int[] creativeHotbarIds)
        {
            for (int i = 0; i < Size; i++) _slots[i] = default;

            if (creativeHotbarIds != null)
                for (int i = 0; i < HotbarSize && i < creativeHotbarIds.Length; i++)
                    if (creativeHotbarIds[i] > 0) _slots[i] = new ItemStack(creativeHotbarIds[i], 1);

            Changed?.Invoke();
        }

        // Забрать всё (при смерти): возвращает непустые стаки, склад очищается
        public static List<ItemStack> TakeAll()
        {
            var list = new List<ItemStack>();
            for (int i = 0; i < Size; i++)
            {
                if (!_slots[i].IsEmpty) list.Add(_slots[i]);
                _slots[i] = default;
            }
            Changed?.Invoke();
            return list;
        }

        // Сколько предметов этого вида ещё поместится
        public static int RoomFor(int id)
        {
            if (id <= 0 || ItemRegistry.Get(id) == null) return 0;

            int max = ItemRegistry.MaxStackOf(id);
            int room = 0;
            for (int i = 0; i < Size; i++)
            {
                if (_slots[i].IsEmpty) room += max;
                else if (_slots[i].Id == id) room += Math.Max(0, max - _slots[i].Count);
            }
            return room;
        }

        public static bool CanAdd(int id) => RoomFor(id) > 0;

        // Кладёт предметы. Возвращает, сколько не поместилось.
        // Порядок: сначала дополняем уже начатые стаки (хотбар, потом инвентарь),
        // затем первый свободный слот: хотбар 1..9, дальше инвентарь по рядам сверху вниз.
        public static int Add(int id, int count)
        {
            if (id <= 0 || count <= 0 || ItemRegistry.Get(id) == null) return count;

            int max = ItemRegistry.MaxStackOf(id);

            for (int i = 0; i < Size && count > 0; i++)
            {
                if (_slots[i].IsEmpty || _slots[i].Id != id || _slots[i].Count >= max) continue;
                int move = Math.Min(count, max - _slots[i].Count);
                _slots[i].Count += move;
                count -= move;
            }

            for (int i = 0; i < Size && count > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;
                int put = Math.Min(count, max);
                _slots[i] = new ItemStack(id, put);
                count -= put;
            }

            Changed?.Invoke();
            return count;
        }

        // Расход при использовании (установка блока, еда)
        public static bool ConsumeFromHotbar(int slot, int n)
        {
            if (slot < 0 || slot >= HotbarSize || _slots[slot].IsEmpty || _slots[slot].Count < n) return false;

            _slots[slot].Count -= n;
            if (_slots[slot].Count <= 0) _slots[slot] = default;
            Changed?.Invoke();
            return true;
        }

        // ---------- Сеть: номера предметов (одинаковы у всех, у кого одна версия игры) ----------

        public static void Encode(ItemStack[] slots, out int[] ids, out int[] counts)
        {
            ids = new int[Size];
            counts = new int[Size];
            if (slots == null) return;

            for (int i = 0; i < Size && i < slots.Length; i++)
            {
                if (slots[i].IsEmpty) continue;
                ids[i] = slots[i].Id;
                counts[i] = slots[i].Count;
            }
        }

        // Безопасно читает данные из сети: мусор и неизвестные предметы пропускает
        public static ItemStack[] Decode(int[] ids, int[] counts)
        {
            var result = new ItemStack[Size];
            if (ids == null || counts == null) return result;

            int n = Math.Min(Size, Math.Min(ids.Length, counts.Length));
            for (int i = 0; i < n; i++)
            {
                ItemDef def = ItemRegistry.Get(ids[i]);
                if (def == null || counts[i] <= 0) continue;
                result[i] = new ItemStack(def.Id, Math.Min(counts[i], def.MaxStack));
            }
            return result;
        }

        // Заменяет весь склад (данные из сети). null, null даёт пустой склад.
        public static void LoadFrom(int[] ids, int[] counts)
        {
            Array.Copy(Decode(ids, counts), _slots, Size);
            Changed?.Invoke();
        }

        // ---------- Файлы: ключи предметов (строки), переживают любые изменения номеров ----------

        public static void EncodeKeys(ItemStack[] slots, out string[] keys, out int[] counts)
        {
            keys = new string[Size];
            counts = new int[Size];
            for (int i = 0; i < Size; i++) keys[i] = "";
            if (slots == null) return;

            for (int i = 0; i < Size && i < slots.Length; i++)
            {
                if (slots[i].IsEmpty) continue;
                keys[i] = ItemRegistry.KeyOf(slots[i].Id);
                counts[i] = slots[i].Count;
            }
        }

        public static ItemStack[] DecodeKeys(string[] keys, int[] counts)
        {
            var result = new ItemStack[Size];
            if (keys == null || counts == null) return result;

            int n = Math.Min(Size, Math.Min(keys.Length, counts.Length));
            for (int i = 0; i < n; i++)
            {
                ItemDef def = ItemRegistry.Find(keys[i]);
                if (def == null || counts[i] <= 0) continue;
                result[i] = new ItemStack(def.Id, Math.Min(counts[i], def.MaxStack));
            }
            return result;
        }

        // Читает сохранение. Старый формат (номера BlockType) тоже понимает.
        public static ItemStack[] DecodeSave(string[] keys, int[] legacyBlockTypes, int[] counts)
        {
            if (keys != null && keys.Length > 0) return DecodeKeys(keys, counts);

            var result = new ItemStack[Size];
            if (legacyBlockTypes == null || counts == null) return result;

            int n = Math.Min(Size, Math.Min(legacyBlockTypes.Length, counts.Length));
            for (int i = 0; i < n; i++)
            {
                if (legacyBlockTypes[i] <= 0 || counts[i] <= 0) continue;
                if (!Enum.IsDefined(typeof(BlockType), legacyBlockTypes[i])) continue;

                int id = ItemRegistry.IdForBlock((BlockType)legacyBlockTypes[i]);
                if (id <= 0) continue;
                result[i] = new ItemStack(id, Math.Min(counts[i], ItemRegistry.MaxStackOf(id)));
            }
            return result;
        }

        public static void LoadFromSave(string[] keys, int[] legacyBlockTypes, int[] counts)
        {
            Array.Copy(DecodeSave(keys, legacyBlockTypes, counts), _slots, Size);
            Changed?.Invoke();
        }
    }
}