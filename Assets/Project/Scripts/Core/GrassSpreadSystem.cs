using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Networking;

namespace MinecraftClone.Mobs
{
    public class GrassSpreadSystem : MonoBehaviour
    {
        private const float TickInterval = 0.25f;
        private const int SamplesPerPlayer = 60;
        private const int Radius = 16;
        private const int VerticalRange = 8;
        private const float SpreadChance = 0.3f;   // шанс на каждую подходящую выборку
        private const int MinLightToSpread = 9;   // уровень света 0..9: при меньшем трава не распространяется

        private static GrassSpreadSystem _instance;
        private float _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("GrassSpreadSystem");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GrassSpreadSystem>();
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            WorldManager wm = WorldManager.Instance;
            if (wm == null) return;
            if (NetworkClient.active && !NetworkServer.active) return;   // только сервер / одиночная игра

            foreach (var pl in Mob.GetPlayers())
            {
                if (pl == null) continue;
                Vector3 c = pl.transform.position;
                int cx = Mathf.FloorToInt(c.x), cy = Mathf.FloorToInt(c.y), cz = Mathf.FloorToInt(c.z);

                for (int i = 0; i < SamplesPerPlayer; i++)
                {
                    int x = cx + Random.Range(-Radius, Radius + 1);
                    int z = cz + Random.Range(-Radius, Radius + 1);
                    if (!wm.IsChunkLoadedAtWorldPos(x, z)) continue;
                    TrySpread(wm, x, cy, z);
                }
            }
        }

        private static void TrySpread(WorldManager wm, int x, int cy, int z)
        {
            // верхний непустой блок колонки рядом с игроком
            int y = -1;
            for (int yy = cy + VerticalRange; yy >= Mathf.Max(1, cy - VerticalRange); yy--)
            {
                BlockType b = wm.GetBlockAtWorldPosition(x, yy, z);
                if (b == BlockType.Air) continue;
                if (b == BlockType.Dirt) y = yy;
                break;
            }
            if (y < 0) return;
            if (wm.GetBlockAtWorldPosition(x, y + 1, z) != BlockType.Air) return;
            if (wm.GetLightAtWorldPosition(new Vector3(x + 0.5f, y + 1.5f, z + 0.5f)) < MinLightToSpread) return;
            if (Random.value > SpreadChance) return;
            if (!HasGrassNeighbour(wm, x, y, z)) return;

            wm.ApplyBlockChange(x, y, z, BlockType.Grass);
        }

        private static bool HasGrassNeighbour(WorldManager wm, int x, int y, int z)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (IsOpenGrass(wm, x + 1, y + dy, z) || IsOpenGrass(wm, x - 1, y + dy, z) ||
                    IsOpenGrass(wm, x, y + dy, z + 1) || IsOpenGrass(wm, x, y + dy, z - 1))
                    return true;
            }
            return false;
        }

        private static bool IsOpenGrass(WorldManager wm, int x, int y, int z)
            => wm.GetBlockAtWorldPosition(x, y, z) == BlockType.Grass
            && wm.GetBlockAtWorldPosition(x, y + 1, z) == BlockType.Air;
    }
}