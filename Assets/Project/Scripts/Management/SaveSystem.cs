using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MinecraftClone.Player;
using MinecraftClone.Core;
using MinecraftClone.Physics;
using MinecraftClone.Items;

namespace MinecraftClone.Management
{
    public class SaveSystem : MonoBehaviour
    {
        public static SaveSystem Instance { get; private set; }

        [Header("Настройки сохранения")]
        [SerializeField] private string saveFolderName = "saves";

        private string saveFolderPath;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this); // было Destroy(gameObject) — это сносило весь объект вместе с соседними скриптами
                return;
            }
            Instance = this;
            // DontDestroyOnLoad(gameObject);  <- УДАЛИТЬ

            InitSavePath();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void InitSavePath()
        {
            try
            {
                DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
                string basePath = projectRoot != null ? projectRoot.FullName : Application.dataPath;

                saveFolderPath = Path.Combine(basePath, saveFolderName);

                if (!Directory.Exists(saveFolderPath))
                {
                    Directory.CreateDirectory(saveFolderPath);
                    Debug.Log($"<color=cyan>[SaveSystem] Создана корневая папка сохранений: {saveFolderPath}</color>");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Ошибка инициализации пути сохранений: {ex.Message}");
            }
        }

        public string GetSlotFolderPath(int slotIndex)
        {
            if (string.IsNullOrEmpty(saveFolderPath)) InitSavePath();
            return Path.Combine(saveFolderPath, $"world_{slotIndex}");
        }

        public static bool DoesSlotExist(int slotIndex)
        {
            if (Instance != null) return Instance.CheckSlotExists(slotIndex);

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            string basePath = projectRoot != null ? projectRoot.FullName : Application.dataPath;
            string path = Path.Combine(basePath, "saves", $"world_{slotIndex}", "level.dat");

            return File.Exists(path) && new FileInfo(path).Length > 0;
        }

        public static void DeleteSlot(int slotIndex)
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            string basePath = projectRoot != null ? projectRoot.FullName : Application.dataPath;
            string worldFolder = Path.Combine(basePath, "saves", $"world_{slotIndex}");

            if (Directory.Exists(worldFolder))
            {
                try
                {
                    Directory.Delete(worldFolder, true);
                    Debug.Log($"<color=yellow>[SaveSystem] Слот №{slotIndex} успешно удален.</color>");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SaveSystem] Ошибка удаления слота №{slotIndex}: {ex.Message}");
                }
            }
        }

        public bool CheckSlotExists(int slotIndex)
        {
            string folder = GetSlotFolderPath(slotIndex);
            string levelPath = Path.Combine(folder, "level.dat");
            return Directory.Exists(folder) && File.Exists(levelPath) && new FileInfo(levelPath).Length > 0;
        }

        // --- Гарантированное синхронное сохранение ---

        public bool SaveWorldFileSync(int slotIndex, Dictionary<Vector2Int, ChunkData> loadedChunks, int seed, Vector3 playerPosition, Vector2 playerRotation = default)
        {
            if (GravityBlockSystem.Instance != null) GravityBlockSystem.Instance.LandAllImmediately();
            InitSavePath();
            string worldFolder = GetSlotFolderPath(slotIndex);

            try
            {
                if (!Directory.Exists(worldFolder))
                {
                    Directory.CreateDirectory(worldFolder);
                }

                string[] subDirs = new string[]
                {
                    "advancements", "data", "datapacks", "DIM1", "DIM-1",
                    "dimensions", "entities", "ftbquests", "ftbteams",
                    "playerdata", "poi", "region", "serverconfig", "stats"
                };

                foreach (var dir in subDirs)
                {
                    string subPath = Path.Combine(worldFolder, dir);
                    if (!Directory.Exists(subPath)) Directory.CreateDirectory(subPath);
                }

                float velY = 0f;
                if (PlayerHealth.Local != null)
                {
                    var pcv = PlayerHealth.Local.GetComponent<PlayerController>();
                    if (pcv != null) velY = pcv.VerticalVelocity;
                }

                WorldSaveData saveData = new WorldSaveData
                {
                    WorldName = $"Мир {slotIndex}",
                    Seed = seed,
                    GameMode = WorldManager.Instance != null ? (int)WorldManager.Instance.GameMode : (int)WorldGameMode.Legacy,
                    InventoryJson = WorldManager.Instance != null && WorldManager.Instance.GameMode == WorldGameMode.Classic024SurvivalTest
                        ? PlayerInventory.Serialize() : string.Empty,
                    DroppedItemsJson = WorldManager.Instance != null && WorldManager.Instance.GameMode == WorldGameMode.Classic024SurvivalTest
                        ? (ItemDropSystem.Instance != null ? ItemDropSystem.Instance.Serialize() : string.Empty) : string.Empty,
                    PlayerPositionX = playerPosition.x,
                    PlayerPositionY = playerPosition.y,
                    PlayerPositionZ = playerPosition.z,
                    PlayerRotX = playerRotation.x,
                    PlayerRotY = playerRotation.y,
                    PlayerVelY = velY,
                    PlayerDead = PlayerHealth.Local != null && PlayerHealth.Local.IsDead,
                    PlayerScore = PlayerHealth.Local != null ? PlayerHealth.Local.Score : 0,
                    Chunks = new List<ChunkSaveData>()
                };

                if (loadedChunks != null && loadedChunks.Count > 0)
                {
                    foreach (var kvp in loadedChunks)
                    {
                        Vector2Int chunkPos = kvp.Key;
                        ChunkData chunk = kvp.Value;
                        if (chunk == null) continue;

                        saveData.Chunks.Add(new ChunkSaveData
                        {
                            ChunkX = chunkPos.x,
                            ChunkZ = chunkPos.y,
                            CompressedBlocks = CompressBlocksRLE(chunk)
                        });
                    }
                }

                string levelJson = JsonUtility.ToJson(saveData, false);
                string levelPath = Path.Combine(worldFolder, "level.dat");

                string tmp = levelPath + ".tmp";
                File.WriteAllText(tmp, levelJson);
                if (File.Exists(levelPath)) File.Copy(levelPath, Path.Combine(worldFolder, "level.dat_old"), true);
                File.Copy(tmp, levelPath, true);
                File.Delete(tmp);
                File.WriteAllText(Path.Combine(worldFolder, "session.lock"), DateTime.Now.Ticks.ToString());

                Debug.Log($"<color=green>[SaveSystem] УСПЕШНО сохранен слот {slotIndex}! Размер файла: {new FileInfo(levelPath).Length} байт.</color>");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] КРИТИЧЕСКАЯ ОШИБКА сохранения слота {slotIndex}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public void SaveWorld(int slotIndex, Dictionary<Vector2Int, ChunkData> loadedChunks, int seed, Vector3 playerPosition, Vector2 playerRotation = default)
        {
            SaveWorldFileSync(slotIndex, loadedChunks, seed, playerPosition, playerRotation);
        }

        // --- Загрузка мира ---

        public WorldSaveData LoadWorld(int slotIndex)
        {
            string worldFolder = GetSlotFolderPath(slotIndex);
            string levelPath = Path.Combine(worldFolder, "level.dat");

            if (!File.Exists(levelPath))
            {
                Debug.LogWarning($"[SaveSystem] level.dat не найден в слоте {slotIndex}: {levelPath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(levelPath);
                if (string.IsNullOrEmpty(json)) return null;

                WorldSaveData saveData = JsonUtility.FromJson<WorldSaveData>(json);
                return saveData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Ошибка чтения level.dat в слоте {slotIndex}: {ex.Message}");
                return null;
            }
        }

        public static WorldSaveData LoadWorldStatic(int slotIndex)
        {
            if (Instance != null) return Instance.LoadWorld(slotIndex);

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            string basePath = projectRoot != null ? projectRoot.FullName : Application.dataPath;
            string levelPath = Path.Combine(basePath, "saves", $"world_{slotIndex}", "level.dat");
            if (!File.Exists(levelPath)) return null;

            try
            {
                string json = File.ReadAllText(levelPath);
                return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<WorldSaveData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Ошибка чтения level.dat в слоте {slotIndex}: {ex.Message}");
                return null;
            }
        }

        public WorldBlocksData LoadWorldBlocks(int slotIndex)
        {
            WorldSaveData saveData = LoadWorld(slotIndex);
            if (saveData == null) return null;

            return new WorldBlocksData
            {
                Seed = saveData.Seed,
                Chunks = saveData.Chunks
            };
        }

        public static WorldBlocksData LoadWorldBlocksStatic(int slotIndex)
        {
            if (Instance != null) return Instance.LoadWorldBlocks(slotIndex);

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            string basePath = projectRoot != null ? projectRoot.FullName : Application.dataPath;
            string levelPath = Path.Combine(basePath, "saves", $"world_{slotIndex}", "level.dat");

            if (!File.Exists(levelPath)) return null;

            try
            {
                string json = File.ReadAllText(levelPath);
                if (string.IsNullOrEmpty(json)) return null;

                WorldSaveData saveData = JsonUtility.FromJson<WorldSaveData>(json);
                if (saveData == null) return null;

                return new WorldBlocksData
                {
                    Seed = saveData.Seed,
                    Chunks = saveData.Chunks
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Ошибка чтения level.dat в слоте {slotIndex}: {ex.Message}");
                return null;
            }
        }

        public PlayerSaveData LoadPlayerData(int slotIndex)
        {
            WorldSaveData saveData = LoadWorld(slotIndex);
            if (saveData == null) return null;

            return new PlayerSaveData
            {
                velY = saveData.PlayerVelY,
                posX = saveData.PlayerPositionX,
                posY = saveData.PlayerPositionY,
                posZ = saveData.PlayerPositionZ,
                rotX = saveData.PlayerRotX,
                rotY = saveData.PlayerRotY,
                dead = saveData.PlayerDead,
                score = saveData.PlayerScore
            };
        }

        public static PlayerSaveData LoadPlayerDataStatic(int slotIndex)
        {
            if (Instance != null) return Instance.LoadPlayerData(slotIndex);

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            string basePath = projectRoot != null ? projectRoot.FullName : Application.dataPath;
            string levelPath = Path.Combine(basePath, "saves", $"world_{slotIndex}", "level.dat");

            if (!File.Exists(levelPath)) return null;

            try
            {
                string json = File.ReadAllText(levelPath);
                if (string.IsNullOrEmpty(json)) return null;

                WorldSaveData saveData = JsonUtility.FromJson<WorldSaveData>(json);
                if (saveData == null) return null;

                return new PlayerSaveData
                {
                    velY = saveData.PlayerVelY,
                    posX = saveData.PlayerPositionX,
                    posY = saveData.PlayerPositionY,
                    posZ = saveData.PlayerPositionZ,
                    rotX = saveData.PlayerRotX,
                    rotY = saveData.PlayerRotY,
                    dead = saveData.PlayerDead,
                    score = saveData.PlayerScore
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Ошибка чтения level.dat в слоте {slotIndex}: {ex.Message}");
                return null;
            }
        }

        // --- RLE-сжатие ---

        public int[] CompressBlocksRLE(ChunkData chunk)
        {
            if (chunk == null || chunk.Blocks == null) return new int[0];

            int width = chunk.Width;
            int height = chunk.Height;
            List<int> compressed = new List<int>();

            int currentBlockType = -1;
            int currentRunLength = 0;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < width; z++)
                    {
                        int blockType = (int)chunk.Blocks[x, y, z];
                        if (blockType == currentBlockType)
                        {
                            currentRunLength++;
                        }
                        else
                        {
                            if (currentRunLength > 0)
                            {
                                compressed.Add(currentRunLength);
                                compressed.Add(currentBlockType);
                            }
                            currentBlockType = blockType;
                            currentRunLength = 1;
                        }
                    }
                }
            }

            if (currentRunLength > 0)
            {
                compressed.Add(currentRunLength);
                compressed.Add(currentBlockType);
            }

            return compressed.ToArray();
        }

        public void DeserializeBlocks(ChunkData chunk, ChunkSaveData chunkSaveData)
        {
            if (chunk == null || chunkSaveData == null) return;

            if (chunkSaveData.CompressedBlocks != null && chunkSaveData.CompressedBlocks.Length > 0)
            {
                DeserializeRLEBlocks(chunk, chunkSaveData.CompressedBlocks);
            }
            else if (chunkSaveData.Blocks != null)
            {
                DeserializeLegacyBlocks(chunk, chunkSaveData.Blocks);
            }
        }

        private void DeserializeRLEBlocks(ChunkData chunk, int[] compressed)
        {
            int width = chunk.Width;
            int height = chunk.Height;
            int totalBlocks = width * height * width;
            int currentBlockIndex = 0;

            for (int i = 0; i < compressed.Length; i += 2)
            {
                if (i + 1 >= compressed.Length) break;

                int count = compressed[i];
                BlockType blockType = (BlockType)compressed[i + 1];

                for (int c = 0; c < count; c++)
                {
                    if (currentBlockIndex >= totalBlocks) break;

                    int x = currentBlockIndex / (height * width);
                    int remainder = currentBlockIndex % (height * width);
                    int y = remainder / width;
                    int z = remainder % width;

                    chunk.Blocks[x, y, z] = blockType;
                    currentBlockIndex++;
                }
            }
        }

        private void DeserializeLegacyBlocks(ChunkData chunk, int[] blocks)
        {
            int width = chunk.Width;
            int height = chunk.Height;
            int index = 0;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < width; z++)
                    {
                        if (index < blocks.Length)
                        {
                            chunk.Blocks[x, y, z] = (BlockType)blocks[index++];
                        }
                    }
                }
            }
        }
    }

    [Serializable]
    public class WorldSaveData
    {
        public float PlayerVelY;
        public string WorldName;
        public int Seed;
        public int GameMode;
        public string InventoryJson;
        public string DroppedItemsJson;
        public float PlayerPositionX;
        public float PlayerPositionY;
        public float PlayerPositionZ;
        public float PlayerRotX;
        public float PlayerRotY;
        public bool PlayerDead;
        public int PlayerScore;
        public List<ChunkSaveData> Chunks;
    }

    [Serializable]
    public class WorldBlocksData
    {
        public int Seed;
        public List<ChunkSaveData> Chunks;
    }

    [Serializable]
    public class PlayerSaveData
    {
        public float velY;
        public bool dead;
        public int score;
        public float posX;
        public float posY;
        public float posZ;
        public float rotX;
        public float rotY;

        public Vector3 Position => new Vector3(posX, posY, posZ);
    }

    [Serializable]
    public class ChunkSaveData
    {
        public int ChunkX;
        public int ChunkZ;
        public int[] CompressedBlocks;
        public int[] Blocks;
    }
}
