using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;
using Mirror;
using MinecraftClone.Networking;

namespace MinecraftClone.Physics
{
    public class GravityBlockSystem : MonoBehaviour
    {
        public static GravityBlockSystem Instance { get; private set; }

        private readonly HashSet<Vector3Int> _silentRemovals = new HashSet<Vector3Int>();
        private readonly List<FallingBlockEntity> _entities = new List<FallingBlockEntity>();
        private readonly Dictionary<BlockType, Mesh> _meshes = new Dictionary<BlockType, Mesh>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void NotifyBlockChanged(Vector3Int pos)
        {
            CheckAndSchedule(pos);
            CheckAndSchedule(pos + Vector3Int.up);
        }

        private void CheckAndSchedule(Vector3Int pos)
        {
            if (WorldManager.Instance == null) return;
            if (NetworkClient.active && !NetworkServer.active) return;    // клиент ничего не решает

            BlockType type = WorldManager.Instance.GetBlockAtWorldPosition(pos.x, pos.y, pos.z);
            if (!IsGravityBlock(type)) return;
            if (!CanFallThrough(pos + Vector3Int.down)) return;

            StartFalling(pos, type);
        }

        private void StartFalling(Vector3Int pos, BlockType type)
        {
            if (NetworkServer.active)
                NetworkServer.SendToAll(new FallingBlockMessage { Pos = pos, Type = type });

            WorldManager.Instance.ApplyBlockChange(pos.x, pos.y, pos.z, BlockType.Air, rebuildMesh: true, silent: true);
            SpawnEntity(pos, type, true);
        }

        public bool IsGravityBlock(BlockType type)
        {
            if (WorldManager.Instance == null || WorldManager.Instance.BlockDatabase == null) return false;

            if (WorldManager.Instance.BlockDatabase.TryGetValue(type, out BlockDataSO data))
            {
                return data != null && data.IsGravityAffected;
            }

            return false;
        }

        public bool CanFallThrough(Vector3Int target)
        {
            if (WorldManager.Instance == null || WorldManager.Instance.Config == null) return false;

            if (target.y < 0 || target.y >= WorldManager.Instance.Config.ChunkHeight)
                return false;

            if (!WorldManager.Instance.IsChunkLoadedAtWorldPos(target.x, target.z))
                return false;

            BlockType targetType = WorldManager.Instance.GetBlockAtWorldPosition(target.x, target.y, target.z);

            return targetType == BlockType.Air ||
                   targetType == BlockType.Water ||
                   targetType == BlockType.Lava ||
                   targetType.IsPlant();
        }

        // клиент: сервер сообщил, что блок начал падать
        public void ClientSpawnFalling(Vector3Int pos, BlockType type)
        {
            _silentRemovals.Add(pos);           // следующее удаление блока в этой клетке пройдёт без частиц и звука
            SpawnEntity(pos, type, false);
        }

        public bool ConsumeSilentRemoval(Vector3Int pos) => _silentRemovals.Remove(pos);

        private void SpawnEntity(Vector3Int pos, BlockType type, bool authoritative)
        {
            var go = new GameObject("FallingBlock_" + type);
            go.transform.position = new Vector3(pos.x + 0.5f, pos.y, pos.z + 0.5f);    // pivot: центр низа блока
            var e = go.AddComponent<FallingBlockEntity>();
            e.Init(this, type, pos, authoritative);
            _entities.Add(e);
        }

        public void Unregister(FallingBlockEntity e) => _entities.Remove(e);

        // для сохранения: все падающие блоки мгновенно приземляются, чтобы не потеряться
        public void LandAllImmediately()
        {
            foreach (var e in _entities.ToArray())
                if (e != null) e.LandNow();
        }

        public Mesh GetBlockMesh(BlockType type)
        {
            if (_meshes.TryGetValue(type, out Mesh cached) && cached != null) return cached;

            var wm = WorldManager.Instance;
            if (wm == null || wm.BlockDatabase == null || !wm.BlockDatabase.TryGetValue(type, out BlockDataSO d) || d == null) return null;

            var uvMap = wm.UVMap;
            Rect top = SafeUV(uvMap, d.TopTexture), bottom = SafeUV(uvMap, d.BottomTexture), side = SafeUV(uvMap, d.SideTexture);

            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            // грани как в ChunkMeshBuilder, куб 1x1x1 с центром низа в (0,0,0)
            Vector3 o = new Vector3(-0.5f, 0f, -0.5f);
            AddFace(v, t, uv, o + new Vector3(0, 1, 0), o + new Vector3(0, 1, 1), o + new Vector3(1, 1, 1), o + new Vector3(1, 1, 0), top);
            AddFace(v, t, uv, o + new Vector3(0, 0, 1), o + new Vector3(0, 0, 0), o + new Vector3(1, 0, 0), o + new Vector3(1, 0, 1), bottom);
            AddFace(v, t, uv, o + new Vector3(1, 0, 1), o + new Vector3(1, 1, 1), o + new Vector3(0, 1, 1), o + new Vector3(0, 0, 1), side);
            AddFace(v, t, uv, o + new Vector3(0, 0, 0), o + new Vector3(0, 1, 0), o + new Vector3(1, 1, 0), o + new Vector3(1, 0, 0), side);
            AddFace(v, t, uv, o + new Vector3(1, 0, 0), o + new Vector3(1, 1, 0), o + new Vector3(1, 1, 1), o + new Vector3(1, 0, 1), side);
            AddFace(v, t, uv, o + new Vector3(0, 0, 1), o + new Vector3(0, 1, 1), o + new Vector3(0, 1, 0), o + new Vector3(0, 0, 0), side);

            var mesh = new Mesh { name = "FallingBlock_" + type };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes[type] = mesh;
            return mesh;
        }

        private static Rect SafeUV(Dictionary<string, Rect> map, Texture2D tex)
        {
            if (tex != null && map != null && map.TryGetValue(tex.name, out Rect r)) return r;
            if (map != null) foreach (var kv in map) return kv.Value;
            return new Rect(0, 0, 1, 1);
        }

        private static void AddFace(List<Vector3> v, List<int> t, List<Vector2> uv, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect r)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
            uv.Add(new Vector2(r.xMin, r.yMin)); uv.Add(new Vector2(r.xMin, r.yMax));
            uv.Add(new Vector2(r.xMax, r.yMax)); uv.Add(new Vector2(r.xMax, r.yMin));
        }

        public bool IsOccupiedByFalling(Vector3Int cell)
        {
            foreach (var e in _entities)
                if (e != null && e.Occupies(cell)) return true;
            return false;
        }
    }
}