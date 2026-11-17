using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Rendering;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Physics
{
    // Горение: огонь поджигает горючие блоки рядом, распространяется только по ним и гаснет на голом камне.
    public class FireSystem : MonoBehaviour
    {
        private const float TickInterval = 0.5f;
        private const int MaxAge = 15;              // тиков до возможного затухания без горючих соседей
        private const float DieOnBareChance = 0.35f;
        private const int SpreadTries = 3;
        private const float SpreadChance = 0.5f;
        private const int MaxFiresPerTick = 300;

        private static readonly Dictionary<ChunkRenderer, Vector3Int[]> Chunks = new Dictionary<ChunkRenderer, Vector3Int[]>();
        private static FireSystem _instance;

        private readonly Dictionary<Vector3Int, int> _age = new Dictionary<Vector3Int, int>();
        private readonly List<Vector3Int> _snapshot = new List<Vector3Int>();
        private readonly List<ChunkRenderer> _dead = new List<ChunkRenderer>();
        private readonly HashSet<Vector2Int> _dirty = new HashSet<Vector2Int>();
        private float _timer;

        private static readonly Vector3Int[] Dirs =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Chunks.Clear(); _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("FireSystem");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FireSystem>();
        }

        // Реестр огня: обновляется при каждой пересборке меша чанка
        public static void SetChunkFires(ChunkRenderer r, List<Vector3Int> local, Vector3 chunkOrigin)
        {
            if (r == null) return;
            if (local.Count == 0) { Chunks.Remove(r); return; }
            int ox = Mathf.RoundToInt(chunkOrigin.x), oz = Mathf.RoundToInt(chunkOrigin.z);
            var arr = new Vector3Int[local.Count];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = new Vector3Int(ox + local[i].x, local[i].y, oz + local[i].z);
            Chunks[r] = arr;
        }

        public static void ClearChunk(ChunkRenderer r) { if (r != null) Chunks.Remove(r); }

        // Огонь можно поставить: снизу твёрдый блок или рядом горючий
        public static bool CanExistAt(WorldManager wm, int x, int y, int z)
        {
            if (IsSolid(wm, x, y - 1, z)) return true;
            return HasFlammableNeighbour(wm, new Vector3Int(x, y, z));
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            if (NetworkClient.active && !NetworkServer.active) return;   // только сервер / одиночная игра
            WorldManager wm = WorldManager.Instance;
            if (wm == null || wm.BlockDatabase == null) return;

            _snapshot.Clear();
            _dead.Clear();
            foreach (var kv in Chunks)
            {
                if (kv.Key == null) { _dead.Add(kv.Key); continue; }
                _snapshot.AddRange(kv.Value);
                if (_snapshot.Count >= MaxFiresPerTick) break;
            }
            foreach (var d in _dead) Chunks.Remove(d);
            if (_snapshot.Count == 0) { _age.Clear(); return; }

            _dirty.Clear();
            foreach (var pos in _snapshot) Tick(wm, pos);
            foreach (var c in _dirty) wm.RebuildChunkMesh(c);
        }

        private void Tick(WorldManager wm, Vector3Int pos)
        {
            if (wm.GetBlockAtWorldPosition(pos.x, pos.y, pos.z) != BlockType.Fire) { _age.Remove(pos); return; }
            if (!wm.IsChunkLoadedAtWorldPos(pos.x, pos.z)) return;

            bool flammable = HasFlammableNeighbour(wm, pos);
            if (!flammable && !IsSolid(wm, pos.x, pos.y - 1, pos.z)) { Put(wm, pos, BlockType.Air); return; }

            _age.TryGetValue(pos, out int age);
            age = Mathf.Min(age + 1, MaxAge);
            _age[pos] = age;
            if (!flammable && age >= MaxAge && Random.value < DieOnBareChance) { Put(wm, pos, BlockType.Air); return; }

            // выжигаем соседей: блок становится огнём или исчезает
            foreach (var d in Dirs)
            {
                Vector3Int np = pos + d;
                float chance = BurnChance(wm.GetBlockAtWorldPosition(np.x, np.y, np.z));
                if (chance <= 0f || Random.value >= chance) continue;
                bool toFire = Random.value < 0.4f && CanExistAt(wm, np.x, np.y, np.z);
                Put(wm, np, toFire ? BlockType.Fire : BlockType.Air);
            }

            // распространение: только в пустые клетки рядом с горючим
            for (int i = 0; i < SpreadTries; i++)
            {
                Vector3Int c = pos + new Vector3Int(Random.Range(-1, 2), Random.Range(-1, 3), Random.Range(-1, 2));
                if (c == pos) continue;
                if (wm.GetBlockAtWorldPosition(c.x, c.y, c.z) != BlockType.Air) continue;
                if (!HasFlammableNeighbour(wm, c)) continue;
                if (Random.value > SpreadChance) continue;
                Put(wm, c, BlockType.Fire);
            }
        }

        private void Put(WorldManager wm, Vector3Int p, BlockType t)
        {
            if (t != BlockType.Fire) _age.Remove(p);
            wm.ApplyBlockChange(p.x, p.y, p.z, t, false, _dirty, true);   // без звука и частиц, меши пересберём пачкой
        }

        // Шанс сгореть за тик (0 = не горит)
        private static float BurnChance(BlockType t) => t.BurnChance();

        private static bool HasFlammableNeighbour(WorldManager wm, Vector3Int p)
        {
            foreach (var d in Dirs)
            {
                Vector3Int n = p + d;
                if (BurnChance(wm.GetBlockAtWorldPosition(n.x, n.y, n.z)) > 0f) return true;
            }
            return false;
        }

        private static bool IsSolid(WorldManager wm, int x, int y, int z)
        {
            BlockType b = wm.GetBlockAtWorldPosition(x, y, z);
            if (b == BlockType.Air || b == BlockType.Water || b == BlockType.Lava
                || b.IsPlant() || b.IsSign() || b.IsTorch()) return false;
            return wm.BlockDatabase.TryGetValue(b, out BlockDataSO d) && d != null && d.IsSolid;
        }
    }
}