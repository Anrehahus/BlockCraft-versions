using System.Collections;
using UnityEngine;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Player;
using MinecraftClone.UI;
using MinecraftClone.Mobs;
using MinecraftClone.Core;

namespace MinecraftClone.Networking
{
    public class NetworkPlayer : NetworkBehaviour
    {
        public const float AttackDamage = 2f;   // урон кулаком (2 = одно сердце)
        public bool AdoptedFromOffline;   // игрок уже был в мире до открытия для сети: ничего не пересоздаём

        // Вызывает владелец игрока, когда ударил моба
        public void AttackMob(Mob mob)
        {
            if (mob == null) return;

            if (NetworkClient.active && !NetworkServer.active)   // чистый клиент: просим сервер
            {
                NetworkIdentity id = mob.GetComponent<NetworkIdentity>();
                if (id != null) CmdAttackMob(id);
                return;
            }

            mob.ServerTakeDamage(AttackDamage, transform.position, GetComponent<PlayerHealth>());
        }

        [Command]
        private void CmdAttackMob(NetworkIdentity mobId)
        {
            if (mobId == null) return;
            Mob mob = mobId.GetComponent<Mob>();
            if (mob == null) return;

            PlayerHealth ph = GetComponent<PlayerHealth>();
            if (ph != null && ph.IsDead) return;

            Vector3 eye = transform.position + Vector3.up * 1.6f;
            if ((mob.CenterPos - eye).sqrMagnitude > 7f * 7f) return;   // дальше дальности удара

            mob.ServerTakeDamage(AttackDamage, transform.position, ph);
        }

        [SyncVar(hook = nameof(OnNicknameChanged))]
        private string _nickname;

        [SyncVar] private float _headPitch;
        public float HeadPitch => _headPitch;
        [SyncVar] private float _fallSpeed;
        public float FallSpeed => _fallSpeed;
        private float _fallTimer, _lastSentFall;

        [Command] private void CmdSetFallSpeed(float v) => _fallSpeed = Mathf.Clamp(v, -100f, 50f);
        [Server] public void ServerSetFallSpeed(float v) => _fallSpeed = Mathf.Clamp(v, -100f, 50f);

        private float _lastSentPitch;
        private float _pitchTimer;

        [Header("UI Никнейма")]
        [SerializeField] private float nameTagHeight = 2.1f;

        [Header("Компоненты локального игрока")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private GameObject playerUI;

        private GUIStyle _nameTagStyle;
        private const float NameTagMaxDist = 64f;   // дальше этого ник не виден
        private const int NameTagMaxSize = 26;      // размер шрифта вплотную
        private const int NameTagMinSize = 12;      // размер на 64 блоках (не мельче, чтобы читался)
        private const float NameTagHiddenAlpha = 0.35f;   // прозрачность ника сквозь блоки

        private static Font _pixelFont;
        private float _tagAlpha = 1f;
        private Texture2D _backgroundTex;
        private Camera _mainCamera;

        public string Nickname => _nickname;

        private Coroutine _tpRoutine;

        private void Awake()
        {
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
            if (audioListener == null) audioListener = GetComponentInChildren<AudioListener>(true);

            if (playerUI == null)
            {
                Canvas childCanvas = GetComponentInChildren<Canvas>(true);
                if (childCanvas != null) playerUI = childCanvas.gameObject;
            }
        }

        private void Update()
        {
            if (!isLocalPlayer || playerCamera == null) return;

            _fallTimer += Time.unscaledDeltaTime;
            if (_fallTimer >= 0.2f)
            {
                _fallTimer = 0f;
                var pcv = GetComponent<PlayerController>();
                if (pcv != null && Mathf.Abs(pcv.VerticalVelocity - _lastSentFall) > 0.5f)
                {
                    _lastSentFall = pcv.VerticalVelocity;
                    CmdSetFallSpeed(_lastSentFall);
                }
            }

            _pitchTimer += Time.unscaledDeltaTime;
            if (_pitchTimer < 0.1f) return;
            _pitchTimer = 0f;

            float pitch = Mathf.DeltaAngle(0f, playerCamera.transform.localEulerAngles.x);
            if (Mathf.Abs(pitch - _lastSentPitch) < 1f) return;

            _lastSentPitch = pitch;
            CmdSetPitch(pitch);
        }

        [Command]
        private void CmdSetPitch(float pitch) => _headPitch = Mathf.Clamp(pitch, -90f, 90f);

        public void NotifySwing()
        {
            FirstPersonView fpv = GetComponent<FirstPersonView>();
            if (fpv != null) fpv.Swing();

            if (!isLocalPlayer || !NetworkClient.active) return;
            CmdSwing();
        }

        [Command]
        private void CmdSwing() => RpcSwing();

        [ClientRpc(includeOwner = false)]
        private void RpcSwing()
        {
            var model = GetComponent<PlayerModel>();
            if (model != null) model.Swing();
        }

        [Server]
        public void ServerTeleport(Vector3 pos)
        {
            // серверная копия чужого игрока обновляется сразу, чтобы следующий /tp к нему видел новую позицию
            if (!isLocalPlayer) transform.position = pos;
            TargetTeleport(connectionToClient, pos);
        }

        [TargetRpc]
        private void TargetTeleport(NetworkConnection target, Vector3 pos)
        {
            if (_tpRoutine != null) StopCoroutine(_tpRoutine);
            _tpRoutine = StartCoroutine(TeleportRoutine(pos));
        }

        // Телепорт без сети (одиночная игра)
        public void TeleportLocal(Vector3 pos)
        {
            if (_tpRoutine != null) StopCoroutine(_tpRoutine);
            _tpRoutine = StartCoroutine(TeleportRoutine(pos));
        }

        private IEnumerator TeleportRoutine(Vector3 pos)
        {
            PlayerController pc = GetComponent<PlayerController>();
            Rigidbody rb = GetComponent<Rigidbody>();
            WorldManager wm = WorldManager.Instance != null ? WorldManager.Instance : Object.FindFirstObjectByType<WorldManager>();

            // замораживаем игрока: контроллер выключен, скорость обнулена
            if (pc != null) pc.SetFrozen(true);
            if (rb != null)
            {
                if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            transform.position = pos;

            // пока под ногами нет чанков, держим игрока на месте, иначе он провалится
            if (wm != null)
            {
                yield return wm.EnsureChunksAroundPositionAsync(pos);

                float wait = 0f;
                while (wm.HasPendingChunkData && wait < 5f)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            yield return new WaitForFixedUpdate();

            if (rb != null) rb.isKinematic = false;
            if (pc != null) pc.SetFrozen(false);
            _tpRoutine = null;
        }

        public static bool IsBlockOverlappingAnyPlayer(Vector3Int pos)
        {
            const float halfWidth = 0.35f;
            const float height = 1.8f;

            foreach (var p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                Vector3 c = p.transform.position; // точка у ног

                bool overlapX = c.x + halfWidth > pos.x && c.x - halfWidth < pos.x + 1f;
                bool overlapZ = c.z + halfWidth > pos.z && c.z - halfWidth < pos.z + 1f;
                bool overlapY = c.y + height > pos.y && c.y < pos.y + 1f;

                if (overlapX && overlapZ && overlapY) return true;
            }
            return false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (isLocalPlayer) return;

            if (playerCamera != null) playerCamera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            if (playerUI != null) playerUI.SetActive(false);

            var pc = GetComponent<PlayerController>(); if (pc != null) pc.enabled = false;
            var pbi = GetComponent<PlayerBlockInteraction>(); if (pbi != null) pbi.enabled = false;
            var cc = GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            var rb = GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (playerCamera != null)
            {
                playerCamera.enabled = true;
                playerCamera.tag = "MainCamera";
            }

            if (audioListener != null) audioListener.enabled = true;
            if (playerUI != null) playerUI.SetActive(true);
            if (AdoptedFromOffline) return;   // не уничтожаем «старого» игрока, не замораживаем, не перегенерируем область

            Vector3 spawnPos = transform.position;
            GameObject[] existingPlayers = GameObject.FindGameObjectsWithTag("Player");

            foreach (var p in existingPlayers)
            {
                var otherId = p.GetComponent<NetworkIdentity>();
                if (p != gameObject && (otherId == null || otherId.netId == 0))
                {
                    spawnPos = p.transform.position;
                    Destroy(p);
                }
            }

            WorldManager wm = WorldManager.Instance != null ? WorldManager.Instance : Object.FindFirstObjectByType<WorldManager>();
            if (wm != null)
            {
                wm.SetPlayerTransform(transform);
            }

            StartCoroutine(SafeSpawnRoutine(spawnPos, wm));
        }

        private IEnumerator SafeSpawnRoutine(Vector3 spawnPos, WorldManager wm)
        {
            CharacterController controller = GetComponent<CharacterController>();
            PlayerController playerCtrl = GetComponent<PlayerController>();
            Rigidbody rb = GetComponent<Rigidbody>();

            if (playerCtrl != null)
            {
                playerCtrl.ResetVelocity();
                playerCtrl.enabled = false;
                playerCtrl.RestoreVelocityY(_fallSpeed);
            }

            if (controller != null) controller.enabled = false;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
            }

            transform.position = spawnPos;

            yield return new WaitForSeconds(0.1f);

            if (wm != null)
            {
                // позиция уже верная: хост остаётся на месте, новичок спавнится на поверхности (считает сервер),
                // вернувшийся игрок появляется в сохранённой точке
                yield return wm.PrepareInitialSpawnArea(spawnPos, spawnPos, null);
            }

            yield return new WaitForFixedUpdate();

            if (controller != null) controller.enabled = true;

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            if (playerCtrl != null)
            {
                playerCtrl.ResetVelocity();
                playerCtrl.enabled = true;
                playerCtrl.SetFrozen(false);
            }

            // Гасим экран загрузки через универсальный поиск или вызов UI
            HideLoadingScreen();

            // Блокируем курсор для игры
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            yield return null;
            yield return new WaitForSeconds(0.2f);

            if (playerCtrl != null)
            {
                playerCtrl.enabled = true;
                playerCtrl.SetFrozen(false);
            }
            if (controller != null) controller.enabled = true;

            var interaction = GetComponent<PlayerBlockInteraction>();
            if (interaction != null) interaction.enabled = true;

            Debug.Log($"[NP] spawn done: ctrl={(playerCtrl != null && playerCtrl.enabled)} frozen={(playerCtrl != null && playerCtrl.IsFrozen)} cc={(controller != null && controller.enabled)} timeScale={Time.timeScale} paused={(PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsPaused)}");
        }

        private void HideLoadingScreen()
        {
            // Сначала пробуем вызвать стандартный метод скрытия нашего RuntimeLoadingScreen, если он есть
            if (RuntimeLoadingScreen.IsShown)
            {
                RuntimeLoadingScreen.Hide();
            }

            // На всякий случай оставляем резервный поиск старых объектов UI, если они создавались вручную
            var loadingCanvas = GameObject.Find("LoadingScreen") ?? GameObject.Find("LoadingScreenUI");
            if (loadingCanvas != null)
            {
                loadingCanvas.SetActive(false);
            }
        }

        private void InitNameTagStyle()
        {
            if (_nameTagStyle != null) return;

            _backgroundTex = new Texture2D(1, 1);
            _backgroundTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.4f));
            _backgroundTex.Apply();

            _nameTagStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            _nameTagStyle.normal.textColor = Color.white;
            _nameTagStyle.normal.background = _backgroundTex;
        }

        private void OnDestroy()
        {
            if (_backgroundTex != null)
            {
                Destroy(_backgroundTex);
                _backgroundTex = null;
            }

            _nameTagStyle = null;

            if (isLocalPlayer)
            {
                WorldManager wm = WorldManager.Instance != null ? WorldManager.Instance : Object.FindFirstObjectByType<WorldManager>();
                if (wm != null)
                {
                    wm.OnNetworkPlayerDestroyed(transform.position);
                }
            }
        }

        [Command]
        private void CmdSetNickname(string nick) => _nickname = nick;

        [Server]
        public void ServerSetNickname(string nick) => _nickname = nick;

        private void OnNicknameChanged(string oldNick, string newNick) => gameObject.name = $"Player_{newNick}";

        private static Font GetPixelFont()
        {
            if (_pixelFont != null) return _pixelFont;
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.ToLower().Contains("minecraft")) { _pixelFont = f; break; }
            return _pixelFont;
        }

        private void EnsureNameTagStyle(int size)
        {
            if (_nameTagStyle == null)
            {
                _nameTagStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                Font font = GetPixelFont();
                if (font != null) _nameTagStyle.font = font;
            }
            _nameTagStyle.fontSize = size;
        }

        // ник приглушается, если между камерой и ним есть твёрдые блоки
        private static bool IsLineBlocked(WorldManager wm, Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1f) return false;
            d /= len;

            for (float t = 0.5f; t < len - 0.3f; t += 0.4f)
            {
                Vector3 p = from + d * t;
                BlockType b = wm.GetBlockAtWorldPosition(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
                if (b == BlockType.Air || b == BlockType.Water || b == BlockType.Lava || b == BlockType.Glass
                    || b.IsPlant() || b.IsSign()) continue;
                return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            if (isLocalPlayer) return;

            if (_mainCamera == null) _mainCamera = Camera.main;
            WorldManager wm = WorldManager.Instance;
            if (_mainCamera == null || wm == null) return;

            Vector3 tagPos = transform.position + Vector3.up * nameTagHeight;
            bool blocked = IsLineBlocked(wm, _mainCamera.transform.position, tagPos);
            _tagAlpha = Mathf.MoveTowards(_tagAlpha, blocked ? NameTagHiddenAlpha : 1f, Time.unscaledDeltaTime * 6f);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (isLocalPlayer || string.IsNullOrEmpty(_nickname)) return;

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            Vector3 worldPos = transform.position + Vector3.up * nameTagHeight;
            float dist = Vector3.Distance(_mainCamera.transform.position, worldPos);
            if (dist > NameTagMaxDist) return;   // дальше 64 блоков ник не показываем

            Vector3 sp = _mainCamera.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) return;

            int size = Mathf.RoundToInt(Mathf.Lerp(NameTagMaxSize, NameTagMinSize, dist / NameTagMaxDist));
            EnsureNameTagStyle(size);

            GUIContent content = new GUIContent(_nickname);
            Vector2 sz = _nameTagStyle.CalcSize(content);
            Rect r = new Rect(Mathf.Round(sp.x - sz.x * 0.5f), Mathf.Round(Screen.height - sp.y - sz.y), sz.x, sz.y);

            // тень, как в оригинале
            float so = Mathf.Max(1f, Mathf.Round(size / 8f));
            _nameTagStyle.normal.textColor = new Color(0f, 0f, 0f, 0.75f * _tagAlpha);
            GUI.Label(new Rect(r.x + so, r.y + so, r.width, r.height), content, _nameTagStyle);

            _nameTagStyle.normal.textColor = new Color(1f, 1f, 1f, _tagAlpha);
            GUI.Label(r, content, _nameTagStyle);
        }
    }
}