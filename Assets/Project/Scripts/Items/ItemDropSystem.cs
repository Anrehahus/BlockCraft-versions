using System.Collections.Generic;
using UnityEngine;
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
    // Одна сущность может держать несколько предметов (Count): стак игрока при смерти, кучка от взрыва.
    public class ItemDropSystem : MonoBehaviour
    {
        public static ItemDropSystem Instance { get; private set; }

        private const float PickupRadius = 1.5f;         // по горизонтали, у игрока на своей стороне
        private const float ServerPickupRadius = 2.6f;   // у сервера с запасом на задержку сети
        private const int BatchSize = 6;                 // сколько блоков взрыва склеивается в одну сущность

        private readonly Dictionary<int, DroppedItem> _items = new Dictionary<int, DroppedItem>();
        private readonly Dictionary<int, Mesh> _meshes = new Dictionary<int, Mesh>();
        private int _nextId = 1;

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

            foreach (var m in _pendingSpawns) SpawnEntity(m.Id, m.ItemId, m.Count, m.Pos, m.Vel, false);
            _pendingSpawns.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var m in _meshes.Values) if (m != null) Destroy(m);
            _meshes.Clear();
        }

        // ---------- Что выпадает ----------

        // Какой предмет получается из сломанного блока (0 = ничего: воздух, вода, лава, бедрок).
        // Сюда же потом можно добавить особые правила: из камня булыжник, из листвы саженец и т.п.
        public static int DropItemFor(BlockType broken)
        {
            if (broken == BlockType.Stone) broken = BlockType.Cobblestone;
            return ItemRegistry.IdForBlock(broken);
        }

        // ---------- Сервер / одиночная игра ----------

        private static bool IsPureClient => NetworkClient.active && !NetworkServer.active;

        // Один сломанный игроком блок (WorldManager.SetBlockAtWorldPosition)
        public void ServerDrop(BlockType broken, Vector3Int cell)
        {
            if (IsPureClient) return;   // клиент ничего не решает

            int itemId = DropItemFor(broken);
            if (itemId <= 0) return;

            Vector3 pos = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
            Vector3 vel = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(3f, 4.5f), Random.Range(-1.2f, 1.2f));
            ServerSpawn(itemId, broken == BlockType.StoneSlabDouble ? 2 : 1, pos, vel);
        }

        // Целый стак (смерть игрока)
        public void ServerDropStack(int itemId, int count, Vector3 pos)
        {
            if (ItemRegistry.Get(itemId) == null) Debug.LogWarning("[ItemDrop] Неизвестный предмет id=" + itemId);
            if (IsPureClient || count <= 0 || ItemRegistry.Get(itemId) == null) return;

            Vector3 vel = new Vector3(Random.Range(-2f, 2f), Random.Range(3f, 5f), Random.Range(-2f, 2f));
            ServerSpawn(itemId, count, pos, vel);
        }

        // Блоки одного типа, разрушенные взрывом: склеиваем кучками, чтобы не плодить сотни сущностей
        public void ServerDropBatch(BlockType broken, List<Vector3Int> cells, Vector3 center)
        {
            if (IsPureClient || cells == null || cells.Count == 0) return;

            int itemId = DropItemFor(broken);
            if (itemId <= 0) return;

            for (int i = 0; i < cells.Count; i += BatchSize)
            {
                int n = Mathf.Min(BatchSize, cells.Count - i);
                Vector3Int c = cells[i];
                Vector3 pos = new Vector3(c.x + 0.5f, c.y + 0.5f, c.z + 0.5f);

                Vector3 away = pos - center;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 vel = away * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(2f, 4.5f);

                ServerSpawn(itemId, n, pos, vel);
            }
        }

        private void ServerSpawn(int itemId, int count, Vector3 pos, Vector3 vel)
        {
            int id = _nextId++;
            SpawnEntity(id, itemId, count, pos, vel, true);

            if (NetworkServer.active)
                NetworkServer.SendToAll(new ItemSpawnMessage { Id = id, ItemId = itemId, Count = count, Pos = pos, Vel = vel });
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
                conn.Send(new ItemSpawnMessage { Id = it.Id, ItemId = it.ItemId, Count = it.Count, Pos = pos, Vel = vel });
            }
        }

        // Сервер: клиент просит подобрать предмет. room: сколько таких предметов у клиента ещё поместится.
        // true, если разрешено. count: сколько отдаём (если забрали не всё, сущность остаётся с остатком).
        public bool ServerTryPickup(int id, Vector3 playerPos, int room, out int itemId, out int count)
        {
            itemId = 0;
            count = 0;
            if (room <= 0) return false;
            if (!_items.TryGetValue(id, out DroppedItem it) || it == null || !it.Authoritative) return false;
            if (it.Age < DroppedItem.PickupDelay - 0.2f) return false;

            Vector3 d = it.Position - playerPos;
            float dy = d.y;
            d.y = 0f;
            if (d.sqrMagnitude > ServerPickupRadius * ServerPickupRadius || dy < -2.5f || dy > 3.5f) return false;

            itemId = it.ItemId;
            count = Mathf.Min(it.Count, room);

            if (count >= it.Count) ServerRemove(id);
            else it.Count -= count;
            return true;
        }

        // ---------- Клиент ----------

        // Точки входа для сетевых обработчиков (работают, даже если системы ещё нет)
        public static void HandleSpawn(ItemSpawnMessage m)
        {
            if (Instance != null) Instance.SpawnEntity(m.Id, m.ItemId, m.Count, m.Pos, m.Vel, false);
            else _pendingSpawns.Add(m);
        }

        public static void HandleRemove(int id)
        {
            if (Instance != null) Instance.RemoveLocal(id);
            else _pendingSpawns.RemoveAll(s => s.Id == id);
        }

        // ---------- Общее ----------

        private void SpawnEntity(int id, int itemId, int count, Vector3 pos, Vector3 vel, bool authoritative)
        {
            if (_items.ContainsKey(id)) return;
            if (ItemRegistry.Get(itemId) == null) return;   // неизвестный предмет (другая версия игры)

            var go = new GameObject("Item_" + ItemRegistry.KeyOf(itemId));
            go.transform.SetParent(transform, false);
            var e = go.AddComponent<DroppedItem>();
            e.Init(this, id, itemId, count, pos, vel, authoritative);
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
            if (PlayerHealth.Local != null && PlayerHealth.Local.IsDead) return;   // мёртвый ничего не подбирает

            var hb = PlayerHotbarUI.Instance;
            var player = hb != null ? hb.Player : null;
            if (player == null) return;

            Vector3 p = player.transform.position;

            DroppedItem target = null;
            int targetRoom = 0;
            foreach (var it in _items.Values)
            {
                if (it == null || !it.CanBePickedUp) continue;

                Vector3 d = it.Position - p;
                float dy = d.y;
                d.y = 0f;
                // по высоте окно с запасом: не важно, где у игрока центр, в ногах или посередине
                if (d.sqrMagnitude > PickupRadius * PickupRadius || dy < -1.3f || dy > 2.2f) continue;

                int room = PlayerInventory.RoomFor(it.ItemId);
                if (room <= 0) continue;

                target = it;
                targetRoom = room;
                break;
            }
            if (target == null) return;

            if (IsPureClient)
            {
                // чистый клиент: просим сервер, предметы получим в ответе
                target.MarkClaimed();
                NetworkClient.Send(new ItemPickupRequestMessage { Id = target.Id, Room = targetRoom });
            }
            else
            {
                // одиночная игра или хост: забираем сразу (если не влезло всё, остаток лежит дальше)
                int n = Mathf.Min(target.Count, targetRoom);
                PlayerInventory.Add(target.ItemId, n);
                if (n >= target.Count) ServerRemove(target.Id);
                else target.Count -= n;
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

        // Плоская картинка вместо кубика: предметы с текстурой, цветы, грибы, таблички
        public bool IsFlat(int itemId)
        {
            ItemDef def = ItemRegistry.Get(itemId);
            if (def == null) return false;
            return def.Texture != null || def.Block.IsPlant() || def.Block.IsSign();
        }

        public Mesh GetMesh(int itemId)
        {
            if (_meshes.TryGetValue(itemId, out Mesh cached) && cached != null) return cached;

            ItemDef def = ItemRegistry.Get(itemId);
            var wm = WorldManager.Instance;
            if (def == null || wm == null) return null;

            var uvMap = wm.UVMap;
            if (uvMap == null || uvMap.Count == 0) return null;   // атлас ещё не готов: не кэшируем кривой меш

            var v = new List<Vector3>();
            var t = new List<int>();
            var uv = new List<Vector2>();
            var col = new List<Color32>();

            if (IsFlat(itemId))
            {
                Texture2D tex = def.Texture;
                if (tex == null)
                {
                    if (wm.BlockDatabase == null || !wm.BlockDatabase.TryGetValue(def.Block, out BlockDataSO bd) || bd == null) return null;
                    tex = def.Block.IsSign() ? Pick(bd.SideTexture, bd.TopTexture) : Pick(bd.TopTexture, bd.SideTexture);
                }

                Rect r = SafeUV(uvMap, tex);
                Vector3 a = new Vector3(-0.5f, -0.5f, 0f), b = new Vector3(-0.5f, 0.5f, 0f);
                Vector3 c = new Vector3(0.5f, 0.5f, 0f), e = new Vector3(0.5f, -0.5f, 0f);
                AddFace(v, t, uv, col, a, b, c, e, r, 255, false);
                AddFace(v, t, uv, col, e, c, b, a, r, 255, true);
            }
            else
            {
                if (wm.BlockDatabase == null || !wm.BlockDatabase.TryGetValue(def.Block, out BlockDataSO d) || d == null) return null;

                Rect top = SafeUV(uvMap, d.TopTexture), bottom = SafeUV(uvMap, d.BottomTexture), side = SafeUV(uvMap, d.SideTexture);
                Vector3 o = new Vector3(-0.5f, -0.5f, -0.5f);   // куб 1x1x1 с центром в нуле
                AddFace(v, t, uv, col, o + new Vector3(0, 1, 0), o + new Vector3(0, 1, 1), o + new Vector3(1, 1, 1), o + new Vector3(1, 1, 0), top, FaceShade[0], false);
                AddFace(v, t, uv, col, o + new Vector3(0, 0, 1), o + new Vector3(0, 0, 0), o + new Vector3(1, 0, 0), o + new Vector3(1, 0, 1), bottom, FaceShade[1], false);
                AddFace(v, t, uv, col, o + new Vector3(1, 0, 1), o + new Vector3(1, 1, 1), o + new Vector3(0, 1, 1), o + new Vector3(0, 0, 1), side, FaceShade[2], false);
                AddFace(v, t, uv, col, o + new Vector3(0, 0, 0), o + new Vector3(0, 1, 0), o + new Vector3(1, 1, 0), o + new Vector3(1, 0, 0), side, FaceShade[3], false);
                AddFace(v, t, uv, col, o + new Vector3(1, 0, 0), o + new Vector3(1, 1, 0), o + new Vector3(1, 1, 1), o + new Vector3(1, 0, 1), side, FaceShade[4], false);
                AddFace(v, t, uv, col, o + new Vector3(0, 0, 1), o + new Vector3(0, 1, 1), o + new Vector3(0, 1, 0), o + new Vector3(0, 0, 0), side, FaceShade[5], false);

                if (def.Block == BlockType.StoneSlab)
                {
                    for (int k = 0; k < v.Count; k++)
                    {
                        // бока (грани начиная с 3-й): берём нижнюю половину текстуры, чтобы она не сплющивалась
                        if (k >= 8 && v[k].y > 0f)
                            uv[k] = new Vector2(uv[k].x, side.yMin + side.height * 0.5f);

                        // сплющиваем куб вдвое по высоте, остаёмся по центру
                        v[k] = new Vector3(v[k].x, (v[k].y + 0.5f) * 0.5f - 0.25f, v[k].z);
                    }
                }
            }

            var mesh = new Mesh { name = "Item_" + def.Key };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes[itemId] = mesh;
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