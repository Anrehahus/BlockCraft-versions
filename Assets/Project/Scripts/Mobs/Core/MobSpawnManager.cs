using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.Networking;

namespace MinecraftClone.Mobs
{
    // Один спавнер на всех мобов. Висит на том же объекте, что и WorldManager.
    // Правила спавна каждого моба берёт из его MobDataSO (поле Spawn).
    public class MobSpawnManager : MonoBehaviour
    {
        [SerializeField] private GameObject mobPrefab;     // общий префаб с компонентом Mob

        [Header("Частота и дистанции")]
        [SerializeField] private float spawnInterval = 4f;
        [SerializeField] private float minSpawnDistance = 24f;
        [SerializeField] private float maxSpawnDistance = 48f;
        [SerializeField] private float despawnDistance = 72f;
        [SerializeField, Range(0f, 1f)] private float undergroundChance = 0.5f;

        [Header("Лимиты на одного игрока")]
        [SerializeField] private int maxHostilePerPlayer = 6;
        [SerializeField] private int maxNeutralPerPlayer = 4;
        [SerializeField] private int maxPassivePerPlayer = 8;

        [Header("Для проверки")]
        [SerializeField] private string testMobId = "creeper";

        private float _timer;
        // true на сервере и в одиночной игре без Mirror, false у чистого клиента
        private static bool CanRun => NetworkServer.active || !NetworkClient.active;

        private void Update()
        {
            if (!CanRun || mobPrefab == null) return;

            WorldManager wm = WorldManager.Instance;
            if (wm == null || !wm.IsWorldReady) return;

            _timer += Time.deltaTime;
            if (_timer < spawnInterval) return;
            _timer = 0f;

            NetworkPlayer[] players = Mob.GetPlayers();
            if (players.Length == 0) return;

            Despawn(players);
            Mob.All.RemoveAll(m => m == null);

            for (int i = 0; i < 12; i++)
                if (TrySpawnOne(wm, players)) break;
        }

        private void Despawn(NetworkPlayer[] players)
        {
            float maxSq = despawnDistance * despawnDistance;
            for (int i = Mob.All.Count - 1; i >= 0; i--)
            {
                Mob m = Mob.All[i];
                if (m == null) { Mob.All.RemoveAt(i); continue; }

                bool near = false;
                foreach (var p in players)
                {
                    if (p == null) continue;
                    if ((p.transform.position - m.transform.position).sqrMagnitude < maxSq) { near = true; break; }
                }
                if (!near) m.ServerDespawn();
            }
        }

        private bool TrySpawnOne(WorldManager wm, NetworkPlayer[] players)
        {
            NetworkPlayer p = players[Random.Range(0, players.Length)];
            if (p == null) return false;

            float ang = Random.value * Mathf.PI * 2f;
            float dist = Random.Range(minSpawnDistance, maxSpawnDistance);
            int x = Mathf.FloorToInt(p.transform.position.x + Mathf.Cos(ang) * dist);
            int z = Mathf.FloorToInt(p.transform.position.z + Mathf.Sin(ang) * dist);

            if (!wm.IsChunkLoadedAtWorldPos(x, z)) return false;

            int top = FindTop(wm, x, z);
            if (top < 1) return false;

            bool underground = Random.value < undergroundChance;
            int y;
            if (underground)
            {
                if (!FindCaveSpot(wm, x, top, z, out y)) return false;
            }
            else
            {
                BlockType ground = wm.GetBlockAtWorldPosition(x, top, z);
                if (ground == BlockType.Water || ground == BlockType.Lava || ground == BlockType.OakLeaves) return false;
                y = top + 1;
                if (!IsSpotFree(wm, x, y, z)) return false;
            }

            Vector3 pos = new Vector3(x + 0.5f, y, z + 0.5f);

            float minSq = minSpawnDistance * minSpawnDistance;
            foreach (var q in players)
                if (q != null && (q.transform.position - pos).sqrMagnitude < minSq) return false;

            MobDataSO data = PickMob(underground, players.Length);
            if (data == null) return false;

            SpawnAt(data, pos);

            // группа: остальные рядом
            int extra = Random.Range(data.Spawn.groupMin, data.Spawn.groupMax + 1) - 1;
            for (int i = 0; i < extra; i++)
            {
                int gx = x + Random.Range(-3, 4), gz = z + Random.Range(-3, 4);
                if (!wm.IsChunkLoadedAtWorldPos(gx, gz)) continue;
                if (!IsSpotFree(wm, gx, y, gz)) continue;
                SpawnAt(data, new Vector3(gx + 0.5f, y, gz + 0.5f));
            }
            return true;
        }

        // выбор моба по весам среди подходящих по месту и лимитам
        private MobDataSO PickMob(bool underground, int playerCount)
        {
            var all = MobRegistry.All;
            int total = 0;
            foreach (var d in all)
                if (CanSpawn(d, underground, playerCount)) total += d.Spawn.weight;
            if (total <= 0) return null;

            int roll = Random.Range(0, total);
            foreach (var d in all)
            {
                if (!CanSpawn(d, underground, playerCount)) continue;
                roll -= d.Spawn.weight;
                if (roll < 0) return d;
            }
            return null;
        }

        private bool CanSpawn(MobDataSO d, bool underground, int playerCount)
        {
            if (d.Spawn.weight <= 0) return false;
            if (!(underground ? d.Spawn.underground : d.Spawn.onSurface)) return false;
            return CountOf(d.Temperament) < LimitOf(d.Temperament) * playerCount;
        }

        private int LimitOf(Temperament t)
        {
            switch (t)
            {
                case Temperament.Hostile: return maxHostilePerPlayer;
                case Temperament.Neutral: return maxNeutralPerPlayer;
                default: return maxPassivePerPlayer;
            }
        }

        private static int CountOf(Temperament t)
        {
            int n = 0;
            foreach (var m in Mob.All)
                if (m != null && m.Data != null && m.Data.Temperament == t) n++;
            return n;
        }

        private void SpawnAt(MobDataSO data, Vector3 pos)
        {
            bool online = NetworkServer.active;
            GameObject go = Instantiate(mobPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.GetComponent<Mob>().Init(data, !online);
            if (online) NetworkServer.Spawn(go);
        }

        // самый верхний блок колонны, кроме воздуха и растений
        private static int FindTop(WorldManager wm, int x, int z)
        {
            for (int y = wm.Config.ChunkHeight - 1; y >= 0; y--)
            {
                BlockType b = wm.GetBlockAtWorldPosition(x, y, z);
                if (b != BlockType.Air && !b.IsPlant()) return y;
            }
            return -1;
        }

        // ищем пещеру: от случайной глубины идём вниз до первой подходящей площадки
        private static bool FindCaveSpot(WorldManager wm, int x, int top, int z, out int foundY)
        {
            foundY = 0;
            int yMax = top - 8;
            if (yMax < 5) return false;

            int y0 = Random.Range(4, yMax + 1);
            for (int y = y0; y >= 3 && y > y0 - 24; y--)
                if (IsSpotFree(wm, x, y, z)) { foundY = y; return true; }
            return false;
        }

        // y = высота ног: два блока воздуха и твёрдая земля под ногами
        private static bool IsSpotFree(WorldManager wm, int x, int y, int z)
        {
            if (wm.GetBlockAtWorldPosition(x, y, z) != BlockType.Air) return false;
            if (wm.GetBlockAtWorldPosition(x, y + 1, z) != BlockType.Air) return false;

            BlockType below = wm.GetBlockAtWorldPosition(x, y - 1, z);
            return below != BlockType.Air && below != BlockType.Water && below != BlockType.Lava && !below.IsPlant();
        }

        // правый клик по компоненту в Inspector во время игры
        [ContextMenu("Test: spawn mob (testMobId) near player")]
        [ContextMenu("Test: spawn mob (testMobId) near player")]
        private void SpawnTest()
        {
            NetworkPlayer[] players = Mob.GetPlayers();
            Debug.Log($"[SpawnTest] server={NetworkServer.active}, prefab={(mobPrefab != null)}, players={players.Length}");
            if (!CanRun || mobPrefab == null || players.Length == 0 || players[0] == null) return;

            MobDataSO data = MobRegistry.Get(testMobId);
            Debug.Log($"[SpawnTest] в реестре мобов: {MobRegistry.All.Count}, нашли '{testMobId}': {(data != null)}");
            if (data == null) { Debug.LogError($"[SpawnTest] Нет моба с id '{testMobId}'"); return; }

            Transform t = players[0].transform;
            Vector3 pos = t.position + t.forward * 10f + Vector3.up * 2f;
            SpawnAt(data, pos);
            Debug.Log($"[SpawnTest] заспавнен в {pos}, всего мобов: {Mob.All.Count}");
        }
    }
}