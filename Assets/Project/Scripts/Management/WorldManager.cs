using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Core;
using MinecraftClone.Rendering;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Fluid;
using MinecraftClone.Generation;
using MinecraftClone.Physics;
using MinecraftClone.Player;
using MinecraftClone.UI;
using MinecraftClone.Networking;
using MinecraftClone.Effects;
using MinecraftClone.Audio;

namespace MinecraftClone.Management
{
    public class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance { get; private set; }

        public event Action OnWorldReady;
        public int ActiveChunkCount => chunkRenderers.Count;
        public static float GenerationProgress { get; private set; } = 0f;
        public static string GenerationStatusText { get; private set; } = "Загрузка...";

        public float FogStart => Mathf.Max(10f, (viewDistanceInChunks - 2) * worldConfig.ChunkWidth);
        public float FogEnd => Mathf.Max(30f, viewDistanceInChunks * worldConfig.ChunkWidth);

        public static int HostViewLimit = int.MaxValue;
        private int userViewDistance = 5;
        private int EffectiveView(int chosen) => Mathf.Clamp(Mathf.Min(chosen, HostViewLimit), 2, 10);

        public static void ApplyHostLimit(int chunks)
        {
            HostViewLimit = chunks;
            if (Instance != null) Instance.SetViewDistance(Instance.userViewDistance);
        }

        public void SetViewDistance(int chunks)
        {
            userViewDistance = chunks;
            viewDistanceInChunks = EffectiveView(chunks);
            SetupFog();
            if (isWorldReady && playerTransform != null) UpdateChunksAroundPlayer();
        }

        [Header("Конфигурация")]
        [SerializeField] private WorldConfigSO worldConfig;

        [Header("Границы и видимость")]
        [SerializeField] private int worldSizeInChunks = 32;
        [SerializeField] private int viewDistanceInChunks = 5;

        [Tooltip("Время (в сек), в течение которого чанк считается 'измененным' для системы сохранения")]
        [SerializeField] private float chunkResetTimeout = 300.0f;

        [Header("Компоненты генерации")]
        [SerializeField] private TerrainGenerator terrainGenerator;

        [Header("Префаб чанка")]
        [SerializeField] private ChunkRenderer chunkPrefab;

        [Header("Игрок")]
        [SerializeField] private GameObject playerPrefab;
        private Transform playerTransform;

        [Header("Материалы блоков")]
        [SerializeField] private Material blocksMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Material lavaMaterial;

        [Header("База данных блоков")]
        [SerializeField] private List<BlockDataSO> blockTypes;

        [SerializeField] private Texture2D waterAnimationStrip;
        [SerializeField] private Texture2D lavaAnimationStrip;

        private Dictionary<BlockType, BlockDataSO> blockDatabase;
        private Dictionary<Vector2Int, ChunkData> loadedChunks = new Dictionary<Vector2Int, ChunkData>();
        private Dictionary<Vector2Int, ChunkRenderer> chunkRenderers = new Dictionary<Vector2Int, ChunkRenderer>();
        private TextureStitcher stitcher;

        private Vector2Int lastPlayerChunkPos = new Vector2Int(int.MinValue, int.MinValue);
        private bool isWorldReady = false;

        private List<Vector2Int> chunksToRenderQueue = new List<Vector2Int>();
        private HashSet<Vector2Int> queuedChunksSet = new HashSet<Vector2Int>();
        private Coroutine renderQueueCoroutine;

        private Dictionary<Vector2Int, float> modifiedChunks = new Dictionary<Vector2Int, float>();

        private readonly Queue<ChunkRenderer> chunkPool = new Queue<ChunkRenderer>();
        private readonly HashSet<Vector2Int> visibleChunksCache = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> chunksToRemoveCache = new List<Vector2Int>();
        private readonly List<Vector2Int> expiredChunksCache = new List<Vector2Int>();

        private readonly HashSet<Vector2Int> pendingChunkData = new HashSet<Vector2Int>();
        public bool HasPendingChunkData => pendingChunkData.Count > 0;

        private Transform chunksParentContainer;

        private readonly HashSet<Vector3Int> spongePositions = new HashSet<Vector3Int>();

        public WorldConfigSO Config => worldConfig;
        public Dictionary<BlockType, BlockDataSO> BlockDatabase => blockDatabase;
        public Dictionary<string, Rect> UVMap => stitcher != null ? stitcher.UVMap : null;
        public int UpdatedChunksCount => modifiedChunks.Count;
        public bool IsWorldReady => isWorldReady;

        public Dictionary<Vector2Int, ChunkData> ActiveChunks => loadedChunks;

        // зона губки: 5x5 по горизонтали и 3 по высоте вокруг неё
        public bool IsNearSponge(Vector3Int p)
        {
            if (spongePositions.Count == 0) return false;
            foreach (var s in spongePositions)
                if (Mathf.Abs(p.x - s.x) <= 2 && Mathf.Abs(p.z - s.z) <= 2 && Mathf.Abs(p.y - s.y) <= 1)
                    return true;
            return false;
        }

        private void AbsorbWaterAround(int sx, int sy, int sz)
        {
            var dirty = new HashSet<Vector2Int>();
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        int x = sx + dx, y = sy + dy, z = sz + dz;
                        if (GetBlockAtWorldPosition(x, y, z) == BlockType.Water)
                            ApplyBlockChange(x, y, z, BlockType.Air, false, dirty);
                    }
            foreach (var c in dirty) RebuildChunkMesh(c);
        }

        private void Awake()
        {
            userViewDistance = GameSettings.ViewDistance;
            viewDistanceInChunks = EffectiveView(userViewDistance);

            if (Instance != null && Instance != this)
            {
                // Остаток прошлой сессии: убираем старый компонент, новый берёт управление
                Debug.LogWarning("[WorldManager] Найден WorldManager из прошлой сессии — заменяем его новым.");
                Destroy(Instance);
            }
            Instance = this;

            // Меню паузы — на ОТДЕЛЬНОМ объекте (раньше оно вешалось на объект WorldManager)
            if (PauseMenuUI.Instance == null && FindFirstObjectByType<PauseMenuUI>() == null)
            {
                new GameObject("[PauseMenu]").AddComponent<PauseMenuUI>();
            }

            if (ChatUI.Instance == null && FindFirstObjectByType<ChatUI>() == null)
            {
                new GameObject("[Chat]").AddComponent<ChatUI>();
            }

            if (FindFirstObjectByType<PlayerListUI>() == null)
            {
                new GameObject("[PlayerList]").AddComponent<PlayerListUI>();
            }

            if (FindFirstObjectByType<PlayerHotbarUI>() == null)
            {
                new GameObject("[Hotbar]").AddComponent<PlayerHotbarUI>();
            }

            if (FindFirstObjectByType<InventoryUI>() == null)
            {
                new GameObject("[Inventory]").AddComponent<InventoryUI>();
            }

            if (FindFirstObjectByType<MusicPlayer>() == null)
            {
                new GameObject("[Music]").AddComponent<MusicPlayer>();
            }

            // SaveSystem должен существовать всегда, иначе загрузка и сохранение молча не работают
            if (SaveSystem.Instance == null && FindFirstObjectByType<SaveSystem>() == null)
            {
                new GameObject("[SaveSystem]").AddComponent<SaveSystem>();
            }

            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            GameObject container = new GameObject("[ChunksContainer]");
            chunksParentContainer = container.transform;
            chunksParentContainer.position = Vector3.zero;
            chunksParentContainer.rotation = Quaternion.identity;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;

            if (terrainGenerator == null)
            {
                terrainGenerator = GetComponent<TerrainGenerator>();
                if (terrainGenerator == null)
                {
                    terrainGenerator = gameObject.AddComponent<TerrainGenerator>();
                }
            }

            InitializeBlockDatabase();
            BuildTextureAtlas();
        }

        private void Start()
        {
            Debug.Log($"[WM] Start: NetworkClient.active={NetworkClient.active}, NetworkServer.active={NetworkServer.active}");

            SetupFog();

            // чистый клиент ждёт seed от сервера, свой мир не создаёт
            if (NetworkClient.active && !NetworkServer.active) return;

            if (isWorldReady) return;

            // Единственный источник правды — SelectedWorldIndex (1..5).
            // Старый ключ SelectedWorldSlot мог «залипнуть» и вести на чужой слот.
            int slotIndex = Mathf.Clamp(WorldSlotSelection.Slot, 1, 5);
            bool slotExists = SaveSystem.DoesSlotExist(slotIndex);

            Debug.Log($"<color=cyan>[WorldManager] Выбран слот №{slotIndex}, сохранение существует: {slotExists}</color>");

            if (slotExists)
            {
                WorldBlocksData blocksData = SaveSystem.LoadWorldBlocksStatic(slotIndex);
                PlayerSaveData playerData = SaveSystem.LoadPlayerDataStatic(slotIndex);

                if (blocksData != null)
                {
                    LoadWorldFromData(blocksData, playerData);
                    return;
                }

                Debug.LogError($"[WorldManager] Сохранение слота №{slotIndex} не прочиталось, создаём новый мир.");
            }

            GenerateNewWorld();
        }

        private void Update()
        {
            if (transform.position != Vector3.zero)
            {
                transform.position = Vector3.zero;
            }

            UpdateModifiedChunksTimeout();

            if (!isWorldReady) return;

            if (playerTransform == null)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    SetPlayerTransform(playerObj.transform);
                }
                return;
            }

            Vector2Int currentChunkPos = GetChunkCoordFromPosition(playerTransform.position);

            if (currentChunkPos != lastPlayerChunkPos)
            {
                lastPlayerChunkPos = currentChunkPos;
                UpdateChunksAroundPlayer();
            }
        }

        private void OnDestroy()
        {
            OnHostOrClientStopped();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void OnHostOrClientStopped()
        {
            isWorldReady = false;
            playerTransform = null;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            ClearWorld();
        }

        public void SetPlayerTransform(Transform newPlayer)
        {
            playerTransform = newPlayer;
            if (playerTransform != null)
            {
                lastPlayerChunkPos = GetChunkCoordFromPosition(playerTransform.position);
                UpdateChunksAroundPlayer();
            }
        }

        public void OnNetworkPlayerDestroyed(Vector3 lastPosition)
        {
            playerTransform = null;

            GameObject existingPlayer = GameObject.FindGameObjectWithTag("Player");
            if (existingPlayer == null && playerPrefab != null && !NetworkServer.active)
            {
                GameObject playerInstance = Instantiate(playerPrefab, lastPosition, Quaternion.identity);
                SetPlayerTransform(playerInstance.transform);
            }
            else if (existingPlayer != null)
            {
                SetPlayerTransform(existingPlayer.transform);
            }

            StartCoroutine(EnsureChunksAroundPositionAsync(lastPosition));
        }

        public IEnumerator PrepareInitialSpawnArea(Vector3 spawnPosition, Vector3? explicitSpawnPos = null, Vector2? explicitRotation = null)
        {
            isWorldReady = false;
            Debug.Log("[WM] Prepare: старт");
            GenerationProgress = 0f;
            GenerationStatusText = "Генерация местности...";

            Vector2Int centerChunk = GetChunkCoordFromPosition(spawnPosition);
            int halfWorld = worldSizeInChunks / 2;

            // Фаза 1 (0-60%): данные чанков с запасом +1, чтобы у краёв правильно считались соседи
            int genRadius = viewDistanceInChunks + 1;
            int totalGen = (genRadius * 2 + 1) * (genRadius * 2 + 1);
            int genDone = 0;

            for (int x = centerChunk.x - genRadius; x <= centerChunk.x + genRadius; x++)
            {
                for (int z = centerChunk.y - genRadius; z <= centerChunk.y + genRadius; z++)
                {
                    genDone++;
                    GenerationProgress = 0.6f * Mathf.Clamp01((float)genDone / totalGen);

                    if (!IsChunkWithinBounds(x, z, halfWorld)) continue;

                    GetOrCreateChunkData(new Vector2Int(x, z));
                    if (genDone % 2 == 0) yield return null;
                }
            }

            Debug.Log("[WM] Prepare: данные чанков готовы");
            // Фаза 2 (60-95%): построение мешей
            GenerationStatusText = "Строим территорию...";
            int totalRender = (viewDistanceInChunks * 2 + 1) * (viewDistanceInChunks * 2 + 1);
            int rendered = 0;

            for (int x = centerChunk.x - viewDistanceInChunks; x <= centerChunk.x + viewDistanceInChunks; x++)
            {
                for (int z = centerChunk.y - viewDistanceInChunks; z <= centerChunk.y + viewDistanceInChunks; z++)
                {
                    rendered++;
                    GenerationProgress = 0.6f + 0.35f * Mathf.Clamp01((float)rendered / totalRender);

                    if (!IsChunkWithinBounds(x, z, halfWorld)) continue;

                    Vector2Int chunkPos = new Vector2Int(x, z);
                    if (!chunkRenderers.ContainsKey(chunkPos))
                    {
                        RenderChunkImmediate(chunkPos);
                    }

                    if (rendered % 2 == 0) yield return null;
                }
            }

            Debug.Log("[WM] Prepare: меши построены");
            GenerationStatusText = "Спавн игрока и регистрация камеры...";

            // 1. Создаем/перемещаем игрока
            SpawnOrMovePlayer(centerChunk, explicitSpawnPos, explicitRotation);

            // 2. Ждем один кадр, чтобы префаб игрока активировал свою Camera и присвоил ей Tag "MainCamera"
            yield return null;

            // чистый клиент ждёт точные данные чанков от сервера (не дольше 10 с)
            float netWait = 0f;
            while (HasPendingChunkData && netWait < 10f)
            {
                netWait += Time.unscaledDeltaTime;
                GenerationStatusText = "Синхронизация с сервером...";
                yield return null;
            }

            if (renderQueueCoroutine != null) StopCoroutine(renderQueueCoroutine);
            renderQueueCoroutine = StartCoroutine(ProcessRenderQueue());

            // 3. Размораживаем управление
            if (playerTransform != null)
            {
                PlayerController pc = playerTransform.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.SetFrozen(false);
                }
            }

            GenerationProgress = 1f;
            GenerationStatusText = "Готово!";
            yield return null;

            // 4. Мир полностью готов
            Debug.Log("[WM] Prepare: мир готов");
            isWorldReady = true;
            OnWorldReady?.Invoke();

            yield return null;
            yield return null;
            RuntimeLoadingScreen.Hide();
        }

        public IEnumerator EnsureChunksAroundPositionAsync(Vector3 position)
        {
            Vector2Int centerChunk = GetChunkCoordFromPosition(position);
            int halfWorld = worldSizeInChunks / 2;
            int chunksCreatedThisFrame = 0;

            for (int x = centerChunk.x - viewDistanceInChunks; x <= centerChunk.x + viewDistanceInChunks; x++)
            {
                for (int z = centerChunk.y - viewDistanceInChunks; z <= centerChunk.y + viewDistanceInChunks; z++)
                {
                    if (!IsChunkWithinBounds(x, z, halfWorld)) continue;

                    Vector2Int chunkPos = new Vector2Int(x, z);
                    GetOrCreateChunkData(chunkPos);

                    if (!chunkRenderers.ContainsKey(chunkPos))
                    {
                        RenderChunkImmediate(chunkPos);
                        chunksCreatedThisFrame++;

                        if (chunksCreatedThisFrame >= 4)
                        {
                            chunksCreatedThisFrame = 0;
                            yield return null;
                        }
                    }
                }
            }

            lastPlayerChunkPos = centerChunk;
            UpdateChunksAroundPlayer();

            if (renderQueueCoroutine == null)
            {
                renderQueueCoroutine = StartCoroutine(ProcessRenderQueue());
            }
        }

        public void EnsureChunksAroundPosition(Vector3 position)
        {
            StartCoroutine(EnsureChunksAroundPositionAsync(position));
        }

        public void ClearWorld()
        {
            isWorldReady = false;
            GenerationProgress = 0f;

            if (renderQueueCoroutine != null)
            {
                StopCoroutine(renderQueueCoroutine);
                renderQueueCoroutine = null;
            }

            chunksToRenderQueue.Clear();
            queuedChunksSet.Clear();

            foreach (var kvp in chunkRenderers)
            {
                if (kvp.Value != null)
                {
                    DespawnChunkRenderer(kvp.Value);
                }
            }

            chunkRenderers.Clear();
            loadedChunks.Clear();
            pendingChunkData.Clear();
            modifiedChunks.Clear();
            lastPlayerChunkPos = new Vector2Int(int.MinValue, int.MinValue);

            spongePositions.Clear();
        }

        public void GenerateNewWorld(int? newSeed = null)
        {
            ClearWorld();

            if (worldConfig == null || chunkPrefab == null) return;

            worldConfig.Seed = newSeed.HasValue ? newSeed.Value : UnityEngine.Random.Range(-100000, 100000);

            if (NetworkClient.active && !NetworkServer.active) return; // подготовку области запустит NetworkPlayer

            Vector2Int centerChunk = Vector2Int.zero;

            lastPlayerChunkPos = centerChunk;
            StartCoroutine(PrepareInitialSpawnArea(Vector3.zero));
        }

        private void RegisterSponges(ChunkData chunk)
        {
            for (int x = 0; x < chunk.Width; x++)
                for (int y = 0; y < chunk.Height; y++)
                    for (int z = 0; z < chunk.Width; z++)
                        if (chunk.Blocks[x, y, z] == BlockType.Sponge)
                            spongePositions.Add(new Vector3Int(chunk.Position.x * chunk.Width + x, y, chunk.Position.y * chunk.Width + z));
        }

        public void LoadWorldFromData(WorldBlocksData blocksData, PlayerSaveData playerData)
        {
            if (blocksData == null) return;

            ClearWorld();

            worldConfig.Seed = blocksData.Seed;

            if (blocksData.Chunks != null && SaveSystem.Instance != null)
            {
                foreach (var chunkSave in blocksData.Chunks)
                {
                    Vector2Int chunkPos = new Vector2Int(chunkSave.ChunkX, chunkSave.ChunkZ);
                    ChunkData chunkData = GetOrCreateChunkData(chunkPos, generateTerrain: false);
                    SaveSystem.Instance.DeserializeBlocks(chunkData, chunkSave);
                    RegisterSponges(chunkData);
                }
            }

            Vector3 spawnPosition = Vector3.zero;
            Vector2? spawnRotation = null;

            if (playerData != null)
            {
                spawnPosition = playerData.Position;
                spawnRotation = new Vector2(playerData.rotX, playerData.rotY);
            }
            else
            {
                spawnPosition = GetValidSpawnPosition(Vector2Int.zero);
            }

            StartCoroutine(PrepareInitialSpawnArea(spawnPosition, spawnPosition, spawnRotation));
        }

        private Vector3 GetSurfaceOrSavedPosition(Vector3 savedPos)
        {
            int worldX = Mathf.FloorToInt(savedPos.x);
            int worldZ = Mathf.FloorToInt(savedPos.z);

            int surfaceY = GetHighestBlockYAt(worldX, worldZ);
            if (surfaceY == -1) surfaceY = 64;

            return new Vector3(worldX + 0.5f, surfaceY + 1.1f, worldZ + 0.5f);
        }

        public void LoadWorldFromData(WorldBlocksData blocksData, string saveName = "")
        {
            LoadWorldFromData(blocksData, (PlayerSaveData)null);
        }

        private void GenerateChunkDataAround(Vector2Int center, int radius)
        {
            int halfWorld = worldSizeInChunks / 2;

            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                for (int z = center.y - radius; z <= center.y + radius; z++)
                {
                    if (IsChunkWithinBounds(x, z, halfWorld))
                    {
                        GetOrCreateChunkData(new Vector2Int(x, z));
                    }
                }
            }
        }

        private void EnsureNeighborData(Vector2Int c)
        {
            int half = worldSizeInChunks / 2;
            Vector2Int[] n = { new Vector2Int(c.x + 1, c.y), new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x, c.y + 1), new Vector2Int(c.x, c.y - 1) };
            foreach (var p in n) if (IsChunkWithinBounds(p.x, p.y, half)) GetOrCreateChunkData(p);
        }

        private void UpdateChunksAroundPlayer()
        {
            Vector2Int playerChunk = lastPlayerChunkPos;
            int halfWorld = worldSizeInChunks / 2;

            visibleChunksCache.Clear();

            for (int x = playerChunk.x - viewDistanceInChunks; x <= playerChunk.x + viewDistanceInChunks; x++)
            {
                for (int z = playerChunk.y - viewDistanceInChunks; z <= playerChunk.y + viewDistanceInChunks; z++)
                {
                    if (!IsChunkWithinBounds(x, z, halfWorld))
                        continue;

                    float dx = x - playerChunk.x, dz = z - playerChunk.y;
                    float r = viewDistanceInChunks + 0.5f;
                    if (dx * dx + dz * dz > r * r) continue;

                    Vector2Int chunkPos = new Vector2Int(x, z);
                    visibleChunksCache.Add(chunkPos);

                    if (!chunkRenderers.ContainsKey(chunkPos) && !queuedChunksSet.Contains(chunkPos))
                    {
                        chunksToRenderQueue.Add(chunkPos);
                        queuedChunksSet.Add(chunkPos);
                    }
                }
            }

            chunksToRenderQueue.Sort((a, b) =>
            {
                float distA = Vector2Int.Distance(a, playerChunk);
                float distB = Vector2Int.Distance(b, playerChunk);
                return distB.CompareTo(distA);
            });

            chunksToRemoveCache.Clear();
            foreach (var kvp in chunkRenderers)
            {
                if (!visibleChunksCache.Contains(kvp.Key))
                {
                    chunksToRemoveCache.Add(kvp.Key);
                }
            }

            foreach (Vector2Int pos in chunksToRemoveCache)
            {
                if (chunkRenderers.TryGetValue(pos, out ChunkRenderer renderer))
                {
                    if (renderer != null)
                    {
                        DespawnChunkRenderer(renderer);
                    }
                    chunkRenderers.Remove(pos);
                }
            }

            for (int i = chunksToRenderQueue.Count - 1; i >= 0; i--)
            {
                Vector2Int pos = chunksToRenderQueue[i];
                if (!visibleChunksCache.Contains(pos))
                {
                    chunksToRenderQueue.RemoveAt(i);
                    queuedChunksSet.Remove(pos);
                }
            }
        }

        private bool IsChunkWithinBounds(int chunkX, int chunkZ, int halfWorld)
        {
            return chunkX >= -halfWorld && chunkX < halfWorld &&
                   chunkZ >= -halfWorld && chunkZ < halfWorld;
        }

        public bool IsInsideWorld(float worldX, float worldZ)
        {
            if (worldConfig == null) return false;
            int cx = Mathf.FloorToInt(worldX / worldConfig.ChunkWidth);
            int cz = Mathf.FloorToInt(worldZ / worldConfig.ChunkWidth);
            return IsChunkWithinBounds(cx, cz, worldSizeInChunks / 2);
        }

        public Vector3 GetValidSpawnPosition(Vector2Int centerChunkPos)
        {
            if (worldConfig == null) return new Vector3(0, 65f, 0);

            int baseWorldX = (centerChunkPos.x * worldConfig.ChunkWidth) + (worldConfig.ChunkWidth / 2);
            int baseWorldZ = (centerChunkPos.y * worldConfig.ChunkWidth) + (worldConfig.ChunkWidth / 2);

            Vector3Int validSpawnBlock = FindSolidSpawnPosition(baseWorldX, baseWorldZ);
            return new Vector3(validSpawnBlock.x + 0.5f, validSpawnBlock.y + 1.1f, validSpawnBlock.z + 0.5f);
        }

        private void SpawnOrMovePlayer(Vector2Int centerChunkPos, Vector3? explicitSpawnPos = null, Vector2? explicitRotation = null)
        {
            Vector3 spawnPosition = explicitSpawnPos.HasValue ? explicitSpawnPos.Value : GetValidSpawnPosition(centerChunkPos);

            if (playerTransform == null)
            {
                GameObject existingPlayer = GameObject.FindGameObjectWithTag("Player");
                if (existingPlayer != null)
                {
                    playerTransform = existingPlayer.transform;
                }
            }

            bool isNetworkActive = NetworkServer.active || NetworkClient.active;

            if (playerTransform == null && !isNetworkActive)
            {
                if (playerPrefab != null)
                {
                    Quaternion spawnRot = explicitRotation.HasValue ? Quaternion.Euler(explicitRotation.Value.x, explicitRotation.Value.y, 0) : Quaternion.identity;
                    GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRot);
                    playerTransform = playerInstance.transform;
                }
            }

            if (playerTransform != null)
            {
                PlayerController pc = playerTransform.GetComponent<PlayerController>();

                if (pc != null)
                {
                    pc.SetFrozen(true);
                    pc.ResetVelocity();
                }

                CharacterController controller = playerTransform.GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;

                Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                }

                playerTransform.position = spawnPosition;

                if (explicitRotation.HasValue)
                {
                    if (pc != null)
                    {
                        pc.SetRotation(explicitRotation.Value);
                    }
                    else
                    {
                        playerTransform.rotation = Quaternion.Euler(explicitRotation.Value.x, explicitRotation.Value.y, 0);
                    }
                }

                if (controller != null) controller.enabled = true;
                if (rb != null) rb.isKinematic = false;
            }

            lastPlayerChunkPos = GetChunkCoordFromPosition(spawnPosition);
        }

        private Vector3Int FindSolidSpawnPosition(int targetWorldX, int targetWorldZ)
        {
            int highestY = GetHighestBlockYAt(targetWorldX, targetWorldZ);

            if (highestY != -1)
            {
                return new Vector3Int(targetWorldX, highestY, targetWorldZ);
            }

            int fallbackHeight = (worldConfig != null) ? Mathf.Min(64, worldConfig.ChunkHeight - 2) : 64;
            return new Vector3Int(targetWorldX, fallbackHeight, targetWorldZ);
        }

        private void RenderChunkImmediate(Vector2Int chunkPos)
        {
            ChunkData chunkData = GetOrCreateChunkData(chunkPos);
            Vector3 worldPosition = new Vector3(chunkPos.x * worldConfig.ChunkWidth, 0, chunkPos.y * worldConfig.ChunkWidth);

            ChunkRenderer chunkObject = SpawnChunkRenderer(worldPosition);
            chunkObject.name = $"Chunk_{chunkPos.x}_{chunkPos.y}";

            chunkRenderers[chunkPos] = chunkObject;

            var uvMap = stitcher != null ? stitcher.UVMap : null;
            chunkObject.RenderChunk(chunkData, blockDatabase, uvMap, this, blocksMaterial, waterMaterial, lavaMaterial);
        }

        private IEnumerator ProcessRenderQueue()
        {
            while (true)
            {
                if (isWorldReady && chunksToRenderQueue.Count > 0)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int processedCount = 0;
                    while (chunksToRenderQueue.Count > 0 && (processedCount == 0 || sw.Elapsed.TotalMilliseconds < 5.0))
                    {
                        int lastIndex = chunksToRenderQueue.Count - 1;
                        Vector2Int chunkPos = chunksToRenderQueue[lastIndex];
                        chunksToRenderQueue.RemoveAt(lastIndex);
                        queuedChunksSet.Remove(chunkPos);

                        if (!chunkRenderers.ContainsKey(chunkPos))
                        {
                            EnsureNeighborData(chunkPos);
                            RenderChunkImmediate(chunkPos);
                            processedCount++;
                        }
                    }
                }

                yield return null;
            }
        }

        private ChunkData GetOrCreateChunkData(Vector2Int chunkPos, bool generateTerrain = true)
        {
            if (!loadedChunks.TryGetValue(chunkPos, out ChunkData chunkData))
            {
                chunkData = new ChunkData(chunkPos, worldConfig);

                if (generateTerrain)
                {
                    if (terrainGenerator != null)
                    {
                        terrainGenerator.GenerateChunkTerrain(chunkData);
                    }
                    TreeGenerator.GenerateTrees(chunkData);
                }

                loadedChunks.Add(chunkPos, chunkData);

                if (generateTerrain && NetworkClient.active && !NetworkServer.active && NetworkClient.isConnected)
                {
                    pendingChunkData.Add(chunkPos);
                    NetworkClient.Send(new ChunkRequestMessage { X = chunkPos.x, Z = chunkPos.y });
                }
            }

            return chunkData;
        }

        private int GetHighestBlockYAt(int worldX, int worldZ)
        {
            if (worldConfig == null) return -1;

            int chunkSize = worldConfig.ChunkWidth;
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkZ = Mathf.FloorToInt((float)worldZ / chunkSize);

            Vector2Int chunkPos = new Vector2Int(chunkX, chunkZ);

            ChunkData chunk = GetOrCreateChunkData(chunkPos);
            if (chunk == null) return -1;

            int localX = GetLocalCoord(worldX, chunkSize);
            int localZ = GetLocalCoord(worldZ, chunkSize);

            for (int y = worldConfig.ChunkHeight - 1; y >= 0; y--)
            {
                BlockType type = chunk.GetBlock(localX, y, localZ);

                // Воздух и саженец/растения сверху пропускаем
                if (type == BlockType.Air || type.IsPlant()) continue;

                // Лаву пропускаем, на ней спавниться нельзя
                if (type == BlockType.Lava) continue;

                // Первый непустой блок сверху: это и есть самая верхняя точка колонны
                // (земля, крона дерева или поверхность воды)
                if (type == BlockType.Bedrock) return -1;
                return y;
            }

            return -1;
        }

        private bool IsSolidGroundForSpawn(BlockType type)
        {
            return type != BlockType.Air &&
                   type != BlockType.Water &&
                   type != BlockType.Lava &&
                   type != BlockType.OakSapling &&
                   type != BlockType.Bedrock;
        }

        private void UpdateModifiedChunksTimeout()
        {
            if (modifiedChunks.Count == 0) return;

            expiredChunksCache.Clear();
            float currentTime = Time.time;

            foreach (var kvp in modifiedChunks)
            {
                if (currentTime - kvp.Value >= chunkResetTimeout)
                    expiredChunksCache.Add(kvp.Key);
            }

            for (int i = 0; i < expiredChunksCache.Count; i++)
            {
                modifiedChunks.Remove(expiredChunksCache[i]);
            }
        }

        private void SetupFog()
        {
            if (worldConfig == null) return;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            Color fogColor = new Color(0.6f, 0.8f, 1f);
            RenderSettings.fogColor = fogColor;

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.backgroundColor = fogColor;
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.nearClipPlane = 0.1f;
                mainCam.farClipPlane = 1000f;
            }

            float chunkSize = worldConfig.ChunkWidth;
            RenderSettings.fogStartDistance = Mathf.Max(10f, (viewDistanceInChunks - 2) * chunkSize);
            RenderSettings.fogEndDistance = Mathf.Max(30f, viewDistanceInChunks * chunkSize);
        }

        private void InitializeBlockDatabase()
        {
            blockDatabase = new Dictionary<BlockType, BlockDataSO>();
            if (blockTypes == null) return;

            foreach (var blockData in blockTypes)
            {
                if (blockData != null && !blockDatabase.ContainsKey(blockData.Type))
                    blockDatabase.Add(blockData.Type, blockData);
            }
        }

        private void BuildTextureAtlas()
        {
            if (blockDatabase == null) return;

            HashSet<Texture2D> allTextures = new HashSet<Texture2D>();
            foreach (var block in blockDatabase.Values)
            {
                if (block == null) continue;
                if (block.TopTexture != null) allTextures.Add(block.TopTexture);
                if (block.BottomTexture != null) allTextures.Add(block.BottomTexture);
                if (block.SideTexture != null) allTextures.Add(block.SideTexture);
            }

            if (allTextures.Count > 0)
            {
                stitcher = new TextureStitcher(4);
                stitcher.StitchTextures(new List<Texture2D>(allTextures).ToArray(), blocksMaterial);

                ApplyAtlas(waterMaterial);
                ApplyAtlas(lavaMaterial);
                SetupBlockAnimation(BlockType.Water, waterAnimationStrip, 0.1f);
                SetupBlockAnimation(BlockType.Lava, lavaAnimationStrip, 0.15f);
            }
        }

        private void ApplyAtlas(Material m)
        {
            if (m == null || stitcher == null) return;
            m.mainTexture = stitcher.AtlasTexture;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", stitcher.AtlasTexture); // URP
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", stitcher.AtlasTexture); // Built-in
        }

        private void SetupBlockAnimation(BlockType type, Texture2D strip, float frameTime)
        {
            if (strip == null) { Debug.LogWarning($"[Anim] Лента для {type} не назначена в инспекторе WorldManager."); return; }
            if (blockDatabase == null || !blockDatabase.TryGetValue(type, out BlockDataSO d) || d == null)
            { Debug.LogWarning($"[Anim] BlockDataSO для {type} не найден."); return; }

            Texture2D baseTex = d.TopTexture != null ? d.TopTexture : d.SideTexture;
            if (baseTex == null || !stitcher.UVMap.TryGetValue(baseTex.name, out Rect uv))
            { Debug.LogWarning($"[Anim] Текстура блока {type} не найдена в атласе."); return; }

            gameObject.AddComponent<TextureAnimator>().Init(stitcher.AtlasTexture, uv, strip, frameTime);
            Debug.Log($"[Anim] Анимация {type} запущена, текстура '{baseTex.name}'.");
        }

        public Vector2Int GetChunkCoordFromPosition(Vector3 position)
        {
            if (worldConfig == null) return Vector2Int.zero;

            int chunkSize = worldConfig.ChunkWidth;
            int chunkX = Mathf.FloorToInt(position.x / chunkSize);
            int chunkZ = Mathf.FloorToInt(position.z / chunkSize);
            return new Vector2Int(chunkX, chunkZ);
        }

        public static Vector3Int GetBlockPositionFromWorldPos(Vector3 position)
        {
            return new Vector3Int(
                Mathf.FloorToInt(position.x),
                Mathf.FloorToInt(position.y),
                Mathf.FloorToInt(position.z)
            );
        }

        public static Vector3 GetBlockCenterWorldPos(Vector3Int blockPos)
        {
            return new Vector3(blockPos.x + 0.5f, blockPos.y + 0.5f, blockPos.z + 0.5f);
        }

        public ChunkData GetChunkAtWorldPos(int worldX, int worldZ)
        {
            if (worldConfig == null) return null;
            int chunkSize = worldConfig.ChunkWidth;
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkZ = Mathf.FloorToInt((float)worldZ / chunkSize);

            loadedChunks.TryGetValue(new Vector2Int(chunkX, chunkZ), out ChunkData chunk);
            return chunk;
        }

        public int GetLocalCoord(int worldCoord, int chunkSize)
        {
            int local = worldCoord % chunkSize;
            return local < 0 ? local + chunkSize : local;
        }

        #region Object Pooling
        private ChunkRenderer SpawnChunkRenderer(Vector3 worldPosition)
        {
            ChunkRenderer renderer;
            if (chunkPool.Count > 0)
            {
                renderer = chunkPool.Dequeue();
                renderer.transform.SetParent(chunksParentContainer, false);
                renderer.transform.position = worldPosition;
                renderer.gameObject.SetActive(true);
            }
            else
            {
                renderer = Instantiate(chunkPrefab, worldPosition, Quaternion.identity, chunksParentContainer);
            }
            return renderer;
        }

        private void DespawnChunkRenderer(ChunkRenderer renderer)
        {
            renderer.ClearMesh();
            renderer.gameObject.SetActive(false);
            chunkPool.Enqueue(renderer);
        }
        #endregion

        public ChunkData GetChunkData(Vector2Int chunkPos)
        {
            loadedChunks.TryGetValue(chunkPos, out ChunkData chunk);
            return chunk;
        }

        public ChunkData GetOrGenerateChunk(Vector2Int pos) => GetOrCreateChunkData(pos);

        public void BroadcastBlock(Vector3Int pos, BlockType type)
        {
            if (NetworkServer.active)
                NetworkServer.SendToAll(new BlockChangeMessage { Pos = pos, Type = type });
        }

        public void ApplyChunkFromServer(int cx, int cz, int[] rle)
        {
            Vector2Int pos = new Vector2Int(cx, cz);
            pendingChunkData.Remove(pos);

            if (!loadedChunks.TryGetValue(pos, out ChunkData chunk) || SaveSystem.Instance == null) return;

            SaveSystem.Instance.DeserializeBlocks(chunk, new ChunkSaveData { ChunkX = cx, ChunkZ = cz, CompressedBlocks = rle });

            RebuildChunkMesh(pos);
            RebuildChunkMesh(new Vector2Int(cx - 1, cz));
            RebuildChunkMesh(new Vector2Int(cx + 1, cz));
            RebuildChunkMesh(new Vector2Int(cx, cz - 1));
            RebuildChunkMesh(new Vector2Int(cx, cz + 1));
        }

        public void RebuildChunkMesh(Vector2Int chunkPos)
        {
            if (loadedChunks.TryGetValue(chunkPos, out ChunkData chunkData) &&
                chunkRenderers.TryGetValue(chunkPos, out ChunkRenderer renderer))
            {
                var uvMap = stitcher != null ? stitcher.UVMap : null;
                renderer.RenderChunk(chunkData, blockDatabase, uvMap, this, blocksMaterial, waterMaterial, lavaMaterial);
            }
        }

        public bool IsChunkLoadedAtWorldPos(int worldX, int worldZ)
        {
            if (worldConfig == null) return false;

            int chunkSize = worldConfig.ChunkWidth;
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkZ = Mathf.FloorToInt((float)worldZ / chunkSize);

            return loadedChunks.ContainsKey(new Vector2Int(chunkX, chunkZ));
        }

        public BlockType GetBlockAtWorldPosition(int worldX, int worldY, int worldZ)
        {
            if (worldConfig == null) return BlockType.Air;

            if (worldY < 0 || worldY >= worldConfig.ChunkHeight)
                return BlockType.Air;

            int chunkSize = worldConfig.ChunkWidth;
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkZ = Mathf.FloorToInt((float)worldZ / chunkSize);

            if (loadedChunks.TryGetValue(new Vector2Int(chunkX, chunkZ), out ChunkData chunk))
            {
                int localX = GetLocalCoord(worldX, chunkSize);
                int localZ = GetLocalCoord(worldZ, chunkSize);

                return chunk.GetBlock(localX, worldY, localZ);
            }

            return BlockType.Air;
        }

        public bool SetBlockAtWorldPosition(int worldX, int worldY, int worldZ, BlockType newType)
        {
            if (worldConfig == null) return false;

            if (worldY < 0 || worldY >= worldConfig.ChunkHeight)
                return false;

            BlockType currentBlock = GetBlockAtWorldPosition(worldX, worldY, worldZ);

            if (newType != BlockType.Air)
            {
                if (currentBlock != BlockType.Air && currentBlock != BlockType.Water && currentBlock != BlockType.Lava)
                    return false;
            }

            if (newType.IsPlant())
            {
                if (currentBlock != BlockType.Air) return false;
                BlockType blockBelow = GetBlockAtWorldPosition(worldX, worldY - 1, worldZ);
                if (!CanPlantOn(newType, blockBelow)) return false;
            }

            // чистый клиент: локальная проверка пройдена, отправляем запрос серверу.
            // Блок изменится у нас, когда сервер подтвердит и разошлёт изменение всем.
            if (NetworkClient.active && !NetworkServer.active)
            {
                NetworkClient.Send(new BlockChangeRequestMessage { Pos = new Vector3Int(worldX, worldY, worldZ), Type = newType });
                return true;
            }

            ApplyBlockChange(worldX, worldY, worldZ, newType, rebuildMesh: true);
            if (newType == BlockType.Sponge) AbsorbWaterAround(worldX, worldY, worldZ);

            if (newType == BlockType.Air && worldY + 1 < worldConfig.ChunkHeight)
            {
                if (GetBlockAtWorldPosition(worldX, worldY + 1, worldZ).IsPlant())
                {
                    ApplyBlockChange(worldX, worldY + 1, worldZ, BlockType.Air, rebuildMesh: true);
                }
            }

            return true;
        }

        private static bool CanPlantOn(BlockType plant, BlockType below)
        {
            if (plant == BlockType.BrownMushroom || plant == BlockType.RedMushroom)
                return below != BlockType.Air && below != BlockType.Water && below != BlockType.Lava
                       && below != BlockType.OakLeaves && !below.IsPlant();

            return below == BlockType.Dirt || below == BlockType.Grass; // саженец, одуванчик, мак
        }

        private void SpawnBreakEffect(Vector3Int pos, BlockType oldType)
        {
            if (blockDatabase == null || !blockDatabase.TryGetValue(oldType, out BlockDataSO d) || d == null) return;

            Texture2D tex = d.TopTexture != null ? d.TopTexture : d.SideTexture;
            if (tex == null) return;

            BlockParticleSpawner.SpawnBreakParticles(new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f), tex);
        }

        public void ApplyBlockChange(int worldX, int worldY, int worldZ, BlockType type, bool rebuildMesh = true, HashSet<Vector2Int> dirtyChunks = null)
        {
            if (worldConfig == null) return;

            int chunkSize = worldConfig.ChunkWidth;
            int chunkX = Mathf.FloorToInt((float)worldX / chunkSize);
            int chunkZ = Mathf.FloorToInt((float)worldZ / chunkSize);
            Vector2Int chunkPos = new Vector2Int(chunkX, chunkZ);

            if (loadedChunks.TryGetValue(chunkPos, out ChunkData chunk))
            {
                int localX = GetLocalCoord(worldX, chunkSize);
                int localZ = GetLocalCoord(worldZ, chunkSize);

                BlockType oldType = chunk.GetBlock(localX, worldY, localZ);
                chunk.SetBlock(localX, worldY, localZ, type);
                Vector3Int spongeKey = new Vector3Int(worldX, worldY, worldZ);
                if (type == BlockType.Sponge) spongePositions.Add(spongeKey);
                else if (oldType == BlockType.Sponge) spongePositions.Remove(spongeKey);

                if (oldType == BlockType.Sponge && type != BlockType.Sponge
                    && FluidSystem.Instance != null && !(NetworkClient.active && !NetworkServer.active))
                {
                    FluidSystem.Instance.WakeWaterAround(new Vector3Int(worldX, worldY, worldZ), 3, 2, 3);
                }

                if (type == BlockType.Air && oldType != BlockType.Air && oldType != BlockType.Water && oldType != BlockType.Lava)
                {
                    SpawnBreakEffect(new Vector3Int(worldX, worldY, worldZ), oldType);
                }

                Vector3 soundPos = new Vector3(worldX + 0.5f, worldY + 0.5f, worldZ + 0.5f);
                if (type == BlockType.Air) BlockSounds.Play(oldType, soundPos, 0.8f, 0.8f);   // ломание
                else if (type != oldType) BlockSounds.Play(type, soundPos, 0.8f, 1.0f);       // установка

                BroadcastBlock(new Vector3Int(worldX, worldY, worldZ), type);

                modifiedChunks[chunkPos] = Time.time;

                Vector3Int blockPos = new Vector3Int(worldX, worldY, worldZ);

                NotifyPhysicsSystemsAround(blockPos);

                if (rebuildMesh)
                {
                    TriggerChunkUpdate(chunkPos);

                    if (localX == 0) TriggerChunkUpdate(new Vector2Int(chunkX - 1, chunkZ));
                    if (localX == chunkSize - 1) TriggerChunkUpdate(new Vector2Int(chunkX + 1, chunkZ));
                    if (localZ == 0) TriggerChunkUpdate(new Vector2Int(chunkX, chunkZ - 1));
                    if (localZ == chunkSize - 1) TriggerChunkUpdate(new Vector2Int(chunkX, chunkZ + 1));
                }
                else if (dirtyChunks != null)
                {
                    AddChunkToDirtyIfLoaded(chunkPos, dirtyChunks);

                    if (localX == 0) AddChunkToDirtyIfLoaded(new Vector2Int(chunkX - 1, chunkZ), dirtyChunks);
                    if (localX == chunkSize - 1) AddChunkToDirtyIfLoaded(new Vector2Int(chunkX + 1, chunkZ), dirtyChunks);
                    if (localZ == 0) AddChunkToDirtyIfLoaded(new Vector2Int(chunkX, chunkZ - 1), dirtyChunks);
                    if (localZ == chunkSize - 1) AddChunkToDirtyIfLoaded(new Vector2Int(chunkX, chunkZ + 1), dirtyChunks);
                }
            }
        }

        private void NotifyPhysicsSystemsAround(Vector3Int pos)
        {
            if (NetworkClient.active && !NetworkServer.active) return;

            if (FluidSystem.Instance != null) FluidSystem.Instance.NotifyBlockChanged(pos);
            if (GravityBlockSystem.Instance != null) GravityBlockSystem.Instance.NotifyBlockChanged(pos);
        }

        public void AddChunkToDirtyIfLoaded(Vector2Int chunkPos, HashSet<Vector2Int> dirtyChunks = null)
        {
            if (loadedChunks.ContainsKey(chunkPos) && dirtyChunks != null)
            {
                dirtyChunks.Add(chunkPos);
            }
        }

        private void TriggerChunkUpdate(Vector2Int chunkPos)
        {
            if (loadedChunks.ContainsKey(chunkPos))
            {
                RebuildChunkMesh(chunkPos);
                modifiedChunks[chunkPos] = Time.time;
            }
        }
    }
}