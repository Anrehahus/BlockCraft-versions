using System;
using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;

namespace MinecraftClone.Player
{
    [Serializable]
    public class DroppedBlockItemData
    {
        public int type;
        public int count;
        public float x, y, z;
    }

    [Serializable]
    internal class DroppedBlockItemState
    {
        public List<DroppedBlockItemData> items = new List<DroppedBlockItemData>();
    }

    public class DroppedBlockItem : MonoBehaviour
    {
        private static readonly HashSet<DroppedBlockItem> Active = new HashSet<DroppedBlockItem>();
        private static Transform _player;
        private Renderer _renderer;
        private BlockType _type;
        private int _count;

        public static void Spawn(BlockType type, int count, Vector3 position)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = $"Dropped {type}";
            item.transform.position = position;
            item.transform.localScale = Vector3.one * 0.25f;
            var collider = item.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            var body = item.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            var drop = item.AddComponent<DroppedBlockItem>();
            drop.Init(type, count);
        }

        public static string Serialize()
        {
            var state = new DroppedBlockItemState();
            foreach (var item in Active)
                if (item != null)
                    state.items.Add(new DroppedBlockItemData
                    {
                        type = (int)item._type,
                        count = item._count,
                        x = item.transform.position.x,
                        y = item.transform.position.y,
                        z = item.transform.position.z
                    });
            return JsonUtility.ToJson(state);
        }

        public static void Deserialize(string json)
        {
            ClearAll();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var state = JsonUtility.FromJson<DroppedBlockItemState>(json);
                if (state?.items == null) return;
                foreach (var data in state.items)
                    if (data != null && Enum.IsDefined(typeof(BlockType), data.type) && data.count > 0)
                        Spawn((BlockType)data.type, data.count, new Vector3(data.x, data.y, data.z));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Не удалось загрузить выброшенные предметы: {ex.Message}");
            }
        }

        public static void ClearAll()
        {
            foreach (var item in Active)
                if (item != null) Destroy(item.gameObject);
            Active.Clear();
        }

        private void Init(BlockType type, int count)
        {
            _type = type;
            _count = count;
            _renderer = GetComponent<Renderer>();
            WorldManager wm = WorldManager.Instance;
            if (wm != null && wm.BlockDatabase.TryGetValue(type, out var data) && data != null)
            {
                Texture2D texture = data.TopTexture != null ? data.TopTexture : data.SideTexture;
                if (texture != null) _renderer.material.mainTexture = texture;
            }
            Active.Add(this);
        }

        private void Update()
        {
            if (_player == null && PlayerHealth.Local != null) _player = PlayerHealth.Local.transform;
            transform.Rotate(0f, 90f * Time.deltaTime, 0f);
            if (_player == null || (_player.position - transform.position).sqrMagnitude > 2.25f) return;
            if (PlayerHealth.Local == null || PlayerHealth.Local.IsDead) return;
            if (SurvivalInventory.TryAdd(_type, _count)) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Active.Remove(this);
            if (_renderer != null && _renderer.material != null) Destroy(_renderer.material);
        }
    }

    public static class ClassicBlockDrops
    {
        public static bool TryGet(BlockType broken, out BlockType drop, out int count)
        {
            drop = broken;
            count = 1;
            switch (broken)
            {
                case BlockType.Air:
                case BlockType.Bedrock:
                case BlockType.Water:
                case BlockType.Lava:
                    return false;
                case BlockType.Grass:
                    drop = BlockType.Dirt;
                    return true;
                case BlockType.OakLog:
                    drop = BlockType.OakPlanks;
                    count = UnityEngine.Random.Range(3, 6);
                    return true;
                case BlockType.OakLeaves:
                    if (UnityEngine.Random.value >= 0.1f) return false;
                    drop = BlockType.OakSapling;
                    return true;
                default:
                    return true;
            }
        }
    }
}
