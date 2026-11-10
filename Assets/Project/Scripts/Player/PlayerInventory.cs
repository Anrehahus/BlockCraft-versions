using System;
using MinecraftClone.Core;

namespace MinecraftClone.Player
{
    public struct ItemStack
    {
        public BlockType Type;
        public int Count;

        public ItemStack(BlockType type, int count) { Type = type; Count = count; }
        public bool IsEmpty => Type == BlockType.Air || Count <= 0;
    }

    // Склад игрока: 9 слотов хотбара (индексы 0..8) и 27 слотов инвентаря (9..35, ряды сверху вниз).
    // Статический, как раньше был HotbarBlocks: у каждого клиента свой.
    public static class PlayerInventory
    {
        public const int HotbarSize = 9;
        public const int MainSize = 27;
        public const int Size = HotbarSize + MainSize;
        public const int MaxStack = 99;

        private static readonly ItemStack[] _slots = new ItemStack[Size];

        public static event Action Changed;

        public static ItemStack Get(int i) => _slots[i];

        public static void Set(int i, ItemStack s)
        {
            if (s.IsEmpty) s = default;
            _slots[i] = s;
            Changed?.Invoke();
        }

        // Тип в слоте хотбара или Air, если пусто
        public static BlockType HotbarType(int i)
        {
            if (i < 0 || i >= HotbarSize || _slots[i].IsEmpty) return BlockType.Air;
            return _slots[i].Type;
        }

        // Очистка при входе в мир. creativeHotbar != null: креатив, хотбар заполняется набором.
        public static void Reset(BlockType[] creativeHotbar)
        {
            for (int i = 0; i < Size; i++) _slots[i] = default;

            if (creativeHotbar != null)
                for (int i = 0; i < HotbarSize && i < creativeHotbar.Length; i++)
                    _slots[i] = new ItemStack(creativeHotbar[i], 1);

            Changed?.Invoke();
        }

        // Есть ли место хотя бы для одного предмета этого типа
        public static bool CanAdd(BlockType type)
        {
            if (type == BlockType.Air) return false;
            for (int i = 0; i < Size; i++)
            {
                if (_slots[i].IsEmpty) return true;
                if (_slots[i].Type == type && _slots[i].Count < MaxStack) return true;
            }
            return false;
        }

        // Кладёт предметы. Возвращает, сколько не поместилось.
        // Порядок: сначала дополняем уже начатые стаки (хотбар, потом инвентарь),
        // затем первый свободный слот: хотбар 1..9, дальше инвентарь по рядам сверху вниз.
        public static int Add(BlockType type, int count)
        {
            if (type == BlockType.Air || count <= 0) return count;

            for (int i = 0; i < Size && count > 0; i++)
            {
                if (_slots[i].IsEmpty || _slots[i].Type != type || _slots[i].Count >= MaxStack) continue;
                int move = Math.Min(count, MaxStack - _slots[i].Count);
                _slots[i].Count += move;
                count -= move;
            }

            for (int i = 0; i < Size && count > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;
                int put = Math.Min(count, MaxStack);
                _slots[i] = new ItemStack(type, put);
                count -= put;
            }

            Changed?.Invoke();
            return count;
        }

        // Расход при установке блока
        public static bool ConsumeFromHotbar(int slot, int n)
        {
            if (slot < 0 || slot >= HotbarSize || _slots[slot].IsEmpty || _slots[slot].Count < n) return false;

            _slots[slot].Count -= n;
            if (_slots[slot].Count <= 0) _slots[slot] = default;
            Changed?.Invoke();
            return true;
        }
    }
}