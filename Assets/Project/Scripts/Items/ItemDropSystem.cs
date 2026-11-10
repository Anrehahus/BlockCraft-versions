using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Networking;
using MinecraftClone.Player;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.UI;

namespace MinecraftClone.Items
{
    // Выпавшие предметы (сущности): создание, сеть, подбор.
    // Схема как у GravityBlockSystem: решает сервер/одиночная игра, клиенты получают
    // сообщения и показывают у себя копии (без NetworkIdentity и префабов).
    public class ItemDropSystem : MonoBehaviour
    {
        public static ItemDropSystem Instance { get; private set; }

        private const float PickupRadius = 1.5f;         // по горизонтали, у игрока на своей стороне
        private const float ServerPickupRadius = 2.6f;   // у сервера с запасом на задержку сети

        private readonly Dictionary<int, DroppedItem> _items = new Dictionary<int, DroppedItem>();
        private readonly Dictionary<BlockType, Mesh> _meshes = new Dictionary<BlockType, Mesh>();
        private int _nextId = 1;

        [Serializable]
        private class SaveData { public List<ItemSaveData> items = new List<ItemSaveData>(); }

        [Serializable]
        private class ItemSaveData
        {
            public int id, type;
            public Vector3 position, velocity;
            public float age;
        }

        // Сообщения, пришедшие до создания системы (клиент ещё грузит сцену)
        private static readonly List<ItemSpawnMessage> _pendingSpawns = new List<ItemSpawnMessage>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            foreach (var m in _pendingSpawns) SpawnEntity(m.Id, m.Type, m.Pos, m.Vel, false);
            _pendingSpawns.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var m in _meshes.Values) if (m != null) Destroy(m);
            _meshes.Clear();
        }

        // ---------- Что выпадает ----------

        // Какой предмет получается из сломанного блока. Все 12 вариантов таблички дают одну.
        public static BlockType DropTypeFor(BlockType broken)
        {
            if (broken == BlockType.Grass) return BlockType.Dirt;
            if (broken.IsSign()) return BlockType.SignStanding0;
            return broken;
        }

        private static bool CanDrop(BlockType t)
        {
            return t != BlockType.Air && t != BlockType.Water && t != BlockType.Lava;
        }

        // ---------- Сервер / одиночная игра ----------

        // Вызывать там, где блок реально сломан (WorldManager.SetBlockAtWorldPosition)
        public void ServerDrop(BlockType broken, Vector3Int cell)
        {
            if (NetworkClient.active && !NetworkServer.active) return;   // клиент ничего не решает

            BlockType type = DropTypeFor(broken);
            if (!CanDrop(type)) return;

            int count = 1;
            if (broken == BlockType.OakLog)
            {
                type = BlockType.OakPlanks;
                count = Random.Range(3, 6);
            }
            else if (broken == BlockType.OakLeaves)
            {
                if (Random.value >= 0.1f) return;
                type = BlockType.OakSapling;
            }

            Vector3 pos = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
            for (int i = 0; i < count; i++)
            {
                Vector3 vel = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(3f, 4.5f), Random.Range(-1.2f, 1.2f));
                int id = _nextId++;
                SpawnEntity(id, type, pos, vel, true);
                if (NetworkServer.active)
                    NetworkServer.SendToAll(new ItemSpawnMessage { Id = id, Type = type, Pos = pos, Vel = vel });
            }
        }

        public void ClearAll()
        {
            foreach (int id in new List<int>(_items.Keys)) RemoveLocal(id);
            _nextId = 1;
        }

        public string Serialize()
        {
            var data = new SaveData();
            foreach (var item in _items.Values)
            {
                if (item == null || !item.Authoritative) continue;
                item.GetState(out Vector3 pos, out Vector3 vel);
                data.items.Add(new ItemSaveData
                {
                    id = item.Id, type = (int)item.Type, position = pos, velocity = vel, age = item.Age
                });
            }
            return JsonUtility.ToJson(data);
        }

        public void Deserialize(string json)
        {
            ClearAll();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                if (data?.items == null) return;
                foreach (var saved in data.items)
                {
                    if (saved == null || !Enum.IsDefined(typeof(BlockType), saved.type)
                        || !CanDrop((BlockType)saved.type) || saved.id <= 0) continue;
                    SpawnEntity(saved.id, (BlockType)saved.type, saved.position, saved.velocity, true);
                    if (_items.TryGetValue(saved.id, out DroppedItem item)) item.RestoreAge(saved.age);
                    _nextId = Math.Max(_nextId, saved.id + 1);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Не удалось загрузить выпавшие предметы: {ex.Message}");
            }
        }

        public void ServerRemove(int id)
        {
            if (NetworkServer.active) NetworkServer.SendToAll(new ItemRemoveMessage { Id = id });
            RemoveLocal(id);
        }

        // Игрок только что зашёл: показываем ему всё, что уже лежит в мире
        public void ServerSendAllTo(NetworkConnectionToClient conn)
        {
            foreach (var it in _items.Values)
            {
                if (it == null || !it.Authoritative) continue;
                it.GetState(out Vector3 pos, out Vector3 vel);
                conn.Send(new ItemSpawnMessage { Id = it.Id, Type = it.Type, Pos = pos, Vel = vel });
            }
        }

        // Сервер: клиент просит подобрать предмет. true, если разрешено (предмет уже убран у всех).
        public bool ServerTryPickup(int id, Vector3 playerPos, out BlockType type)
        {
            type = BlockType.Air;
            if (!_items.TryGetValue(id, out DroppedItem it) || it == null || !it.Authoritative) return false;
            if (it.Age < DroppedItem.PickupDelay - 0.2f) return false;

            Vector3 d = it.Position - playerPos;
            float dy = d.y;
            d.y = 0f;
            if (d.sqrMagnitude > ServerPickupRadius * ServerPickupRadius || dy < -2.5f || dy > 3.5f) return false;

            type = it.Type;
            ServerRemove(id);
            return true;
        }

        // ---------- Клиент ----------

        // Точки входа для сетевых обработчиков (работают, даже если системы ещё нет)
        public static void HandleSpawn(ItemSpawnMessage m)
        {
            if (Instance != null) Instance.SpawnEntity(m.Id, m.Type, m.Pos, m.Vel, false);
            else _pendingSpawns.Add(m);
        }

        public static void HandleRemove(int id)
        {
            if (Instance != null) Instance.RemoveLocal(id);
            else _pendingSpawns.RemoveAll(s => s.Id == id);
        }

        // ---------- Общее ----------

        private void SpawnEntity(int id, BlockType type, Vector3 pos, Vector3 vel, bool authoritative)
        {
            if (_items.ContainsKey(id)) return;

            var go = new GameObject("Item_" + type);
            go.transform.SetParent(transform, false);
            var e = go.AddComponent<DroppedItem>();
            e.Init(this, id, type, pos, vel, authoritative);
            _items[id] = e;
        }

        private void RemoveLocal(int id)
        {
            if (_items.TryGetValue(id, out DroppedItem it) && it != null) Destroy(it.gameObject);
            _items.Remove(id);
        }

        public void Unregister(int id) => _items.Remove(id);

        // ---------- Подбор ----------

        private void Update()
        {
            if (_items.Count == 0 || InventoryUI.IsOpen) return;   // при открытом инвентаре не подбираем
            if (WorldManager.Instance == null || !WorldManager.Instance.IsWorldReady) return;

            var hb = PlayerHotbarUI.Instance;
            var player = hb != null ? hb.Player : null;
            if (player == null) return;

            Vector3 p = player.transform.position;

            DroppedItem target = null;
            foreach (var it in _items.Values)
            {
                if (it == null || !it.CanBePickedUp) continue;

                Vector3 d = it.Position - p;
                float dy = d.y;
                d.y = 0f;
                // по высоте окно с запасом: не важно, где у игрока центр, в ногах или посередине
                if (d.sqrMagnitude > PickupRadius * PickupRadius || dy < -1.3f || dy > 2.2f) continue;
                if (!PlayerInventory.CanAdd(it.Type)) continue;

                target = it;
                break;
            }
            if (target == null) return;

            if (NetworkClient.active && !NetworkServer.active)
            {
                // чистый клиент: просим сервер, предмет получим в ответе
                target.MarkClaimed();
                NetworkClient.Send(new ItemPickupRequestMessage { Id = target.Id });
            }
            else
            {
                // одиночная игра или хост: забираем сразу
                PlayerInventory.Add(target.Type, 1);
                ServerRemove(target.Id);
            }
        }

        // ---------- Мир для физики предмета ----------

        public bool IsChunkLoaded(int x, int z)
        {
            return WorldManager.Instance != null && WorldManager.Instance.IsChunkLoadedAtWorldPos(x, z);
        }

        public bool IsSolidCell(int x, int y, int z)
        {
            var wm = WorldManager.Instance;
            if (wm == null) return false;

            BlockType b = wm.GetBlockAtWorldPosition(x, y, z);
            if (b == BlockType.Air || b == BlockType.Water || b == BlockType.Lava) return false;
            if (b.IsPlant() || b.IsSign()) return false;

            if (wm.BlockDatabase != null && wm.BlockDatabase.TryGetValue(b, out BlockDataSO d) && d != null)
                return d.IsSolid;
            return true;
        }

        // ---------- Меш предмета ----------

        private static readonly byte[] FaceShade = { 255, 128, 204, 204, 153, 153 };   // верх, низ, +Z, -Z, +X, -X

        public Mesh GetMesh(BlockType type)
        {
            if (_meshes.TryGetValue(type, out Mesh cached) && cached != null) return cached;

            var wm = WorldManager.Instance;
            if (wm == null || wm.BlockDatabase == null
                || !wm.BlockDatabase.TryGetValue(type, out BlockDataSO d) || d == null) return null;

            var uvMap = wm.UVMap;
            if (uvMap == null || uvMap.Count == 0) return null;   // атлас ещё не готов: не кэшируем кривой меш

            var v = new List<Vector3>();
            var t = new List<int>();
            var uv = new List<Vector2>();
            var col = new List<Color32>();

            if (type.IsPlant() || type.IsSign())
            {
                // плоская картинка из двух сторон: цветок, гриб, табличка
                Texture2D tex = type.IsSign() ? Pick(d.SideTexture, d.TopTexture) : Pick(d.TopTexture, d.SideTexture);
                Rect r = SafeUV(uvMap, tex);
                Vector3 a = new Vector3(-0.5f, -0.5f, 0f), b = new Vector3(-0.5f, 0.5f, 0f);
                Vector3 c = new Vector3(0.5f, 0.5f, 0f), e = new Vector3(0.5f, -0.5f, 0f);
                AddFace(v, t, uv, col, a, b, c, e, r, 255, false);
                AddFace(v, t, uv, col, e, c, b, a, r, 255, true);
            }
            else
            {
                Rect top = SafeUV(uvMap, d.TopTexture), bottom = SafeUV(uvMap, d.BottomTexture), side = SafeUV(uvMap, d.SideTexture);
                Vector3 o = new Vector3(-0.5f, -0.5f, -0.5f);   // куб 1x1x1 с центром в нуле
                AddFace(v, t, uv, col, o + new Vector3(0, 1, 0), o + new Vector3(0, 1, 1), o + new Vector3(1, 1, 1), o + new Vector3(1, 1, 0), top, FaceShade[0], false);
                AddFace(v, t, uv, col, o + new Vector3(0, 0, 1), o + new Vector3(0, 0, 0), o + new Vector3(1, 0, 0), o + new Vector3(1, 0, 1), bottom, FaceShade[1], false);
                AddFace(v, t, uv, col, o + new Vector3(1, 0, 1), o + new Vector3(1, 1, 1), o + new Vector3(0, 1, 1), o + new Vector3(0, 0, 1), side, FaceShade[2], false);
                AddFace(v, t, uv, col, o + new Vector3(0, 0, 0), o + new Vector3(0, 1, 0), o + new Vector3(1, 1, 0), o + new Vector3(1, 0, 0), side, FaceShade[3], false);
                AddFace(v, t, uv, col, o + new Vector3(1, 0, 0), o + new Vector3(1, 1, 0), o + new Vector3(1, 1, 1), o + new Vector3(1, 0, 1), side, FaceShade[4], false);
                AddFace(v, t, uv, col, o + new Vector3(0, 0, 1), o + new Vector3(0, 1, 1), o + new Vector3(0, 1, 0), o + new Vector3(0, 0, 0), side, FaceShade[5], false);
            }

            var mesh = new Mesh { name = "Item_" + type };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes[type] = mesh;
            return mesh;
        }

        private static Texture2D Pick(Texture2D first, Texture2D second) => first != null ? first : second;

        private static Rect SafeUV(Dictionary<string, Rect> map, Texture2D tex)
        {
            if (tex != null && map != null && map.TryGetValue(tex.name, out Rect r)) return r;
            if (map != null) foreach (var kv in map) return kv.Value;
            return new Rect(0, 0, 1, 1);
        }

        private static void AddFace(List<Vector3> v, List<int> t, List<Vector2> uv, List<Color32> col,
                                    Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect r, byte shade, bool mirror)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);

            float x0 = mirror ? r.xMax : r.xMin, x1 = mirror ? r.xMin : r.xMax;
            uv.Add(new Vector2(x0, r.yMin)); uv.Add(new Vector2(x0, r.yMax));
            uv.Add(new Vector2(x1, r.yMax)); uv.Add(new Vector2(x1, r.yMin));

            Color32 cc = new Color32(shade, shade, shade, 255);
            col.Add(cc); col.Add(cc); col.Add(cc); col.Add(cc);
        }
    }
}
