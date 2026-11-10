using System;
using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Player
{
    public static class SurvivalInventory
    {
        public const int SlotCount = 36;
        public const int StackLimit = 64;

        [Serializable]
        private class State
        {
            public int[] types = new int[SlotCount];
            public int[] counts = new int[SlotCount];
            public int[] hotbar = new int[9];
            public int arrows;
        }

        private static State _state = new State();
        public static int Arrows => _state.arrows;

        public static bool ConsumeArrow()
        {
            if (_state.arrows <= 0) return false;
            _state.arrows--;
            return true;
        }

        public static void Reset()
        {
            _state = new State { arrows = 20 };
            for (int i = 0; i < SlotCount; i++) _state.types[i] = -1;
            for (int i = 0; i < _state.hotbar.Length; i++) _state.hotbar[i] = (int)BlockType.Air;
            RefreshHotbar();
        }

        public static bool TryAdd(BlockType type, int amount)
        {
            if (type == BlockType.Air || amount <= 0) return false;

            int room = 0;
            for (int i = 0; i < SlotCount; i++)
                room += _state.counts[i] == 0 ? StackLimit :
                    _state.types[i] == (int)type ? StackLimit - _state.counts[i] : 0;
            if (room < amount) return false;

            int remaining = amount;
            for (int i = 0; i < SlotCount && remaining > 0; i++)
            {
                if (_state.types[i] != (int)type || _state.counts[i] >= StackLimit) continue;
                int add = Math.Min(remaining, StackLimit - _state.counts[i]);
                _state.counts[i] += add;
                remaining -= add;
            }
            for (int i = 0; i < SlotCount && remaining > 0; i++)
            {
                if (_state.counts[i] != 0) continue;
                int add = Math.Min(remaining, StackLimit);
                _state.types[i] = (int)type;
                _state.counts[i] = add;
                remaining -= add;
            }

            bool onHotbar = false;
            foreach (int hotbarType in _state.hotbar)
                if (hotbarType == (int)type) { onHotbar = true; break; }
            if (!onHotbar)
                for (int i = 0; i < _state.hotbar.Length; i++)
                    if (_state.hotbar[i] == (int)BlockType.Air)
                    {
                        _state.hotbar[i] = (int)type;
                        break;
                    }
            RefreshHotbar();
            return true;
        }

        public static int Count(BlockType type)
        {
            int total = 0;
            for (int i = 0; i < SlotCount; i++)
                if (_state.types[i] == (int)type) total += _state.counts[i];
            return total;
        }

        public static bool Consume(BlockType type, int amount = 1)
        {
            if (amount <= 0 || Count(type) < amount) return false;
            for (int i = 0; i < SlotCount && amount > 0; i++)
            {
                if (_state.types[i] != (int)type) continue;
                int take = Math.Min(amount, _state.counts[i]);
                _state.counts[i] -= take;
                amount -= take;
                if (_state.counts[i] == 0) _state.types[i] = -1;
            }
            if (Count(type) == 0)
                for (int i = 0; i < _state.hotbar.Length; i++)
                    if (_state.hotbar[i] == (int)type) _state.hotbar[i] = (int)BlockType.Air;
            RefreshHotbar();
            return true;
        }

        public static void SetHotbarBlock(int slot, BlockType type)
        {
            if (slot < 0 || slot >= _state.hotbar.Length) return;
            _state.hotbar[slot] = (int)type;
            RefreshHotbar();
        }

        public static List<BlockType> GetAvailableTypes()
        {
            var result = new List<BlockType>();
            for (int i = 0; i < SlotCount; i++)
            {
                if (_state.counts[i] <= 0) continue;
                var type = (BlockType)_state.types[i];
                if (!result.Contains(type)) result.Add(type);
            }
            return result;
        }

        public static string Serialize() => JsonUtility.ToJson(_state);

        public static void Deserialize(string json)
        {
            Reset();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                State loaded = JsonUtility.FromJson<State>(json);
                if (loaded == null || loaded.types == null || loaded.counts == null || loaded.hotbar == null
                    || loaded.types.Length != SlotCount || loaded.counts.Length != SlotCount || loaded.hotbar.Length != 9)
                    return;
                _state = loaded;
                _state.arrows = Mathf.Max(0, _state.arrows);
                for (int i = 0; i < SlotCount; i++)
                {
                    _state.counts[i] = Mathf.Clamp(_state.counts[i], 0, StackLimit);
                    if (_state.counts[i] == 0 || !Enum.IsDefined(typeof(BlockType), _state.types[i]))
                    {
                        _state.counts[i] = 0;
                        _state.types[i] = -1;
                    }
                }
                for (int i = 0; i < _state.hotbar.Length; i++)
                    if (!Enum.IsDefined(typeof(BlockType), _state.hotbar[i])) _state.hotbar[i] = (int)BlockType.Air;
                RefreshHotbar();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Не удалось загрузить инвентарь Survival Test: {ex.Message}");
            }
        }

        public static void RefreshHotbar()
        {
            if (PlayerBlockInteraction.HotbarBlocks == null) return;
            for (int i = 0; i < PlayerBlockInteraction.HotbarBlocks.Length; i++)
                PlayerBlockInteraction.HotbarBlocks[i] = (BlockType)_state.hotbar[i];
        }
    }
}
