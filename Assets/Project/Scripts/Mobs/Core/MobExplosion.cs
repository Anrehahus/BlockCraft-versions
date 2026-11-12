using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Player;
using MinecraftClone.Items;

namespace MinecraftClone.Mobs
{
    // Взрыв, ломающий блоки. Вызывается только на сервере. Блоки рассылаются клиентам через WorldManager.
    public static class MobExplosion
    {
        public static void Explode(Vector3 center, float radius)
        {
            if (NetworkClient.active && !NetworkServer.active) return;   // чистый клиент блоки не ломает
            WorldManager wm = WorldManager.Instance;
            if (wm == null) return;

            Vector3Int ci = new Vector3Int(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y), Mathf.FloorToInt(center.z));
            int r = Mathf.CeilToInt(radius);
            int maxY = wm.Config.ChunkHeight - 1;

            var dirty = new HashSet<Vector2Int>();

            var drops = new Dictionary<BlockType, List<Vector3Int>>();
            void Record(BlockType t, Vector3Int p)
            {
                if (!drops.TryGetValue(t, out var l)) drops[t] = l = new List<Vector3Int>();
                l.Add(p);
            }

            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (d > radius) continue;
                        if (d > radius - 1f && Random.value < 0.35f) continue;   // неровный край воронки

                        int x = ci.x + dx, y = ci.y + dy, z = ci.z + dz;
                        if (y < 0 || y > maxY) continue;

                        BlockType b = wm.GetBlockAtWorldPosition(x, y, z);
                        if (b == BlockType.Air || b == BlockType.Water || b == BlockType.Lava) continue;
                        if (wm.BlockDatabase != null && wm.BlockDatabase.TryGetValue(b, out BlockDataSO data)
                            && data != null && !data.IsBreakable) continue;

                        Record(b, new Vector3Int(x, y, z));

                        wm.ApplyBlockChange(x, y, z, BlockType.Air, false, dirty);

                        if (y + 1 <= maxY)
                        {
                            BlockType above = wm.GetBlockAtWorldPosition(x, y + 1, z);
                            if (above.IsPlant())
                            {
                                wm.ApplyBlockChange(x, y + 1, z, BlockType.Air, false, dirty);
                                Record(above, new Vector3Int(x, y + 1, z));
                            }
                        }
                    }

            foreach (var chunk in dirty) wm.RebuildChunkMesh(chunk);
            if (ItemDropSystem.Instance != null)
                foreach (var kv in drops) ItemDropSystem.Instance.ServerDropBatch(kv.Key, kv.Value, center);
        }

        // Урон игрокам от взрыва. Только сервер / одиночная игра.
        public static void DamagePlayers(Vector3 center, float radius, float minDamage, float maxDamage)
        {
            if (NetworkClient.active && !NetworkServer.active) return;

            foreach (var pl in Mob.GetPlayers())
            {
                if (pl == null) continue;

                Vector3 playerCenter = pl.transform.position + Vector3.up * 0.9f;
                float dist = Vector3.Distance(center, playerCenter);
                if (dist > radius) continue;

                PlayerHealth health = pl.GetComponentInParent<PlayerHealth>();
                if (health == null) continue;

                float damage = Mathf.Round(Mathf.Lerp(maxDamage, minDamage, dist / radius));
                health.TakeDamage(damage, center);
            }
        }

        // Урон мобам от взрыва, формула та же, что для игроков. Только сервер / одиночная игра.
        public static void DamageMobs(Vector3 center, float radius, float minDamage, float maxDamage, Mob exclude = null)
        {
            if (NetworkClient.active && !NetworkServer.active) return;

            // копия списка: пока идём по нему, мобы могут умирать и менять Mob.All
            var mobs = new List<Mob>(Mob.All);
            foreach (var m in mobs)
            {
                if (m == null || m == exclude) continue;

                float dist = Vector3.Distance(center, m.CenterPos);
                if (dist > radius) continue;

                float damage = Mathf.Round(Mathf.Lerp(maxDamage, minDamage, dist / radius));
                m.ServerTakeDamage(damage, center, null);
            }
        }
    }
}