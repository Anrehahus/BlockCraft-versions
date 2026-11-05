using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;

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

                        wm.ApplyBlockChange(x, y, z, BlockType.Air, false, dirty);

                        // цветок или саженец над сломанным блоком не должен висеть в воздухе
                        if (y + 1 <= maxY && wm.GetBlockAtWorldPosition(x, y + 1, z).IsPlant())
                            wm.ApplyBlockChange(x, y + 1, z, BlockType.Air, false, dirty);
                    }

            foreach (var chunk in dirty) wm.RebuildChunkMesh(chunk);
        }
    }
}