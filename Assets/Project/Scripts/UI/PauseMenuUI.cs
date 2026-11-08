using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Networking;

namespace MinecraftClone.UI
{
    public class PauseMenuUI : MonoBehaviour
    {
        public static PauseMenuUI Instance { get; private set; }

        private enum MenuState
        {
            Main,
            SaveSlots,
            LoadSlots,
            ConfirmOverwrite,
            OpenToLan
        }

        [Header("Шрифт и настройки")]
        [SerializeField] private Font customFont;
        [SerializeField] private int titleFontSize = 32;
        [SerializeField] private int buttonFontSize = 18;

        [Header("Настройки сцены")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Header("Настройки FPS")]
        [SerializeField] private int pauseTargetFrameRate = 60;
        private int _defaultTargetFrameRate = -1;

        [Header("Сетевое обнаружение")]
        [SerializeField] private LanDiscovery lanDiscovery;

        private bool _isPaused = false;
        private bool _isGenerating = false;
        private MenuState _currentState = MenuState.Main;
        private int _pendingSlot = -1;
        private int _currentLoadedSlot = 1;

        private bool _isMultiplayerActive = false;
        private string _portInput = "7777";
        private string _networkStatusMessage = "";

        private string _lanNick = "Player";
        private string _lanError = "";

        private string _loadingTitle = "Загрузка уровня";
        private string _loadingStatus = "Загрузка мира..";
        private float _loadingProgress = 0f;

        private GUIStyle _menuTitleStyle;
        private GUIStyle _menuTitleShadowStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _statusShadowStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _buttonShadowStyle;
        private GUIStyle _buttonTextStyle;
        private GUIStyle _disabledButtonStyle;

        private Texture2D _backgroundTexture;
        private Texture2D _dirtTileTexture;
        private Texture2D _buttonNormalTex;
        private Texture2D _buttonHoverTex;
        private Texture2D _buttonDisabledTex;
        private Texture2D _progressBarBgTex;
        private Texture2D _progressBarFillTex;

        private WorldManager _worldManager;
        private Transform _playerTransform;

        public bool IsPaused => _isPaused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            _worldManager = WorldManager.Instance != null ? WorldManager.Instance : FindFirstObjectByType<WorldManager>();
            _defaultTargetFrameRate = Application.targetFrameRate;

            if (customFont == null)
            {
                Font[] allFonts = Resources.FindObjectsOfTypeAll<Font>();
                foreach (var f in allFonts)
                {
                    if (f != null && f.name.ToLower().Contains("minecraft"))
                    {
                        customFont = f;
                        break;
                    }
                }
            }

            if (lanDiscovery == null)
            {
                lanDiscovery = GetComponent<LanDiscovery>();
                if (lanDiscovery == null)
                {
                    lanDiscovery = gameObject.AddComponent<LanDiscovery>();
                }
            }

            _backgroundTexture = CreateColorTexture(new Color(0f, 0f, 0f, 0.65f));
            _dirtTileTexture = CreateDirtTexture();

            _buttonNormalTex = Resources.Load<Texture2D>("UI/button") ?? CreateButtonTexture(new Color(0.42f, 0.42f, 0.42f, 1f));
            _buttonHoverTex = Resources.Load<Texture2D>("UI/button_hover") ?? CreateButtonTexture(new Color(0.52f, 0.52f, 0.65f, 1f));
            _buttonNormalTex.filterMode = FilterMode.Point;
            _buttonHoverTex.filterMode = FilterMode.Point;
            if (Resources.Load<Texture2D>("UI/button") == null) Debug.LogError("[Pause] UI/button НЕ найден, рисую кодом");
            if (Resources.Load<Texture2D>("UI/button_hover") == null) Debug.LogError("[Pause] UI/button_hover НЕ найден");
            _buttonDisabledTex = Resources.Load<Texture2D>("UI/button_disabled") ?? CreateButtonTexture(new Color(0.2f, 0.2f, 0.2f, 0.6f));

            _progressBarBgTex = CreateColorTexture(new Color(0.2f, 0.2f, 0.2f, 1f));
            _progressBarFillTex = CreateColorTexture(new Color(0.35f, 0.85f, 0.35f, 1f));

            RebuildStyles();
            Unpause();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            Time.timeScale = 1f;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_backgroundTexture != null) Destroy(_backgroundTexture);
            if (_dirtTileTexture != null) Destroy(_dirtTileTexture);
            if (_buttonDisabledTex != null) Destroy(_buttonDisabledTex);
            if (_progressBarBgTex != null) Destroy(_progressBarBgTex);
            if (_progressBarFillTex != null) Destroy(_progressBarFillTex);
        }

        public void SetCurrentLoadedSlot(int slotIndex)
        {
            _currentLoadedSlot = Mathf.Clamp(slotIndex, 1, 5);
        }

        private int GetActiveSlot()
        {
            return Mathf.Clamp(WorldSlotSelection.Slot, 1, 5);
        }

        private void SetActiveSlot(int slot)
        {
            WorldSlotSelection.Slot = Mathf.Clamp(slot, 1, 5);
        }

        private Texture2D CreateColorTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private Texture2D CreateDirtTexture()
        {
            int size = 16;
            Texture2D tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Point;

            Color baseDirt = new Color(0.34f, 0.23f, 0.15f, 1f);
            Color darkDirt = new Color(0.24f, 0.15f, 0.09f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isDark = (x * 7 + y * 13) % 5 == 0 || (x * 3 + y * 11) % 7 == 0;
                    tex.SetPixel(x, y, isDark ? darkDirt : baseDirt);
                }
            }
            tex.Apply();
            return tex;
        }

        private Texture2D CreateButtonTexture(Color baseColor)
        {
            int width = 200;
            int height = 32;
            Texture2D tex = new Texture2D(width, height);

            Color darkBorder = new Color(0.12f, 0.12f, 0.12f, 1f);
            Color highlightBorder = new Color(0.6f, 0.6f, 0.6f, 1f);
            Color shadowBorder = new Color(0.25f, 0.25f, 0.25f, 1f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                        tex.SetPixel(x, y, darkBorder);
                    else if (y == height - 2 || x == 1)
                        tex.SetPixel(x, y, highlightBorder);
                    else if (y == 1 || x == width - 2)
                        tex.SetPixel(x, y, shadowBorder);
                    else
                        tex.SetPixel(x, y, baseColor);
                }
            }
            tex.Apply();
            return tex;
        }

        private void RebuildStyles()
        {
            _menuTitleStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = titleFontSize,
                fontStyle = FontStyle.Normal
            };
            _menuTitleStyle.normal.textColor = Color.white;
            if (customFont != null) _menuTitleStyle.font = customFont;

            _menuTitleShadowStyle = new GUIStyle(_menuTitleStyle);
            _menuTitleShadowStyle.normal.textColor = new Color(0.2f, 0.2f, 0.2f, 1f);

            _statusStyle = new GUIStyle(_menuTitleStyle) { fontSize = 16 };
            _statusShadowStyle = new GUIStyle(_menuTitleShadowStyle) { fontSize = 16 };

            _buttonStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = buttonFontSize,
                fontStyle = FontStyle.Normal,
                border = new RectOffset(3, 3, 3, 3)
            };
            _buttonStyle.normal.background = _buttonNormalTex;
            _buttonStyle.hover.background = _buttonHoverTex;
            _buttonStyle.active.background = _buttonHoverTex;
            _buttonStyle.normal.textColor = Color.clear; // Текст рисуем вручную через DrawButtonText
            _buttonStyle.hover.textColor = Color.clear;
            if (customFont != null) _buttonStyle.font = customFont;

            _buttonShadowStyle = new GUIStyle(_buttonStyle);
            _buttonShadowStyle.normal.background = null;
            _buttonShadowStyle.hover.background = null;
            _buttonShadowStyle.normal.textColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            _buttonShadowStyle.hover.textColor = new Color(0.2f, 0.2f, 0.2f, 1f);

            _buttonTextStyle = new GUIStyle(_buttonShadowStyle);
            _buttonTextStyle.normal.textColor = Color.white;

            _disabledButtonStyle = new GUIStyle(_buttonStyle);
            _disabledButtonStyle.normal.background = _buttonDisabledTex;
            _disabledButtonStyle.hover.background = _buttonDisabledTex;
            _disabledButtonStyle.active.background = _buttonDisabledTex;
            _disabledButtonStyle.normal.textColor = Color.clear;
            _disabledButtonStyle.hover.textColor = Color.clear;
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name == mainMenuSceneName) return;

            if (!_isGenerating && !ChatUI.IsOpen && !InventoryUI.IsOpen && !SettingsMenuUI.IsOpen && !DeathScreenUI.IsShown
                && SettingsMenuUI.ClosedFrame != Time.frameCount
                && InventoryUI.ClosedFrame != Time.frameCount
                && ChatUI.ClosedFrame != Time.frameCount
                && Input.GetKeyDown(KeyCode.Escape))
            {
                if (_isPaused && _currentState != MenuState.Main)
                {
                    SwitchMenuState(MenuState.Main);
                }
                else
                {
                    TogglePause();
                }
            }
        }

        public void TogglePause()
        {
            if (_isPaused) Unpause();
            else Pause();
        }

        private void Pause()
        {
            _isPaused = true;
            SwitchMenuState(MenuState.Main);

            if (NetworkServer.active || NetworkClient.active || _isMultiplayerActive)
            {
                Time.timeScale = 1f;
            }
            else
            {
                Time.timeScale = 0f;
            }

            Application.targetFrameRate = pauseTargetFrameRate;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Unpause()
        {
            _isPaused = false;
            SwitchMenuState(MenuState.Main);
            Time.timeScale = 1f;
            Application.targetFrameRate = _defaultTargetFrameRate;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void SwitchMenuState(MenuState newState)
        {
            _currentState = newState;
            GUI.FocusControl(null);
        }

        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name == mainMenuSceneName) return;

            if (SettingsMenuUI.IsOpen) return; // меню настроек рисуется поверх

            if (!_isPaused && !_isGenerating) return;

            if (_menuTitleStyle == null || _menuTitleStyle.font == null)
            {
                RebuildStyles();
            }

            if (_isGenerating)
            {
                DrawLoadingScreen();
                return;
            }

            switch (_currentState)
            {
                case MenuState.Main:
                    DrawPauseMenu();
                    break;
                case MenuState.SaveSlots:
                    DrawSlotsMenu("Выберите слот", isSaveMode: true);
                    break;
                case MenuState.LoadSlots:
                    DrawSlotsMenu("Выберите слот", isSaveMode: false);
                    break;
                case MenuState.ConfirmOverwrite:
                    DrawConfirmOverwriteMenu();
                    break;
                case MenuState.OpenToLan:
                    DrawOpenToLanMenu();
                    break;
            }
        }

        private void DrawPauseMenu()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _backgroundTexture);

            string menuText = "Меню паузы";
            float centerX = Screen.width / 2f;
            float topY = Screen.height * 0.15f;

            GUI.Label(new Rect(centerX - 198, topY + 2, 400, 50), menuText, _menuTitleShadowStyle);
            GUI.Label(new Rect(centerX - 200, topY, 400, 50), menuText, _menuTitleStyle);

            float buttonWidth = Mathf.Min(400f, Screen.width * 0.6f);
            float buttonHeight = 42f;
            float buttonSpacing = 15f;
            float startY = topY + 80f;

            // Кнопка 1: Вернуться к игре
            DrawMenuButton(new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight), "Вернуться к игре", OnBackToGameClicked);
            startY += buttonHeight + buttonSpacing;

            // Кнопка 2: Настройки
            DrawMenuButton(new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight), "Настройки", OnSettingsClicked);
            startY += buttonHeight + buttonSpacing;

            if (!NetworkClient.active || NetworkServer.active)
            {
                Rect lanRect = new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight);
                if (_isMultiplayerActive) DrawDisabledButton(lanRect, "Открыт для сети");
                else DrawMenuButton(lanRect, "Открыть для сети", OnOpenToLanClicked);
                startY += buttonHeight + buttonSpacing;
            }

            // Кнопка 3: Главное меню
            DrawMenuButton(new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight), "Главное меню", OnExitToMainMenuClicked);

            if (!string.IsNullOrEmpty(_networkStatusMessage))
            {
                startY += buttonHeight + 20f;
                GUI.Label(new Rect(centerX - 298, startY + 2, 600, 30), _networkStatusMessage, _statusShadowStyle);
                GUI.Label(new Rect(centerX - 300, startY, 600, 30), _networkStatusMessage, _statusStyle);
            }
        }

        private void OnSettingsClicked()
        {
            SettingsMenuUI.Open(null);
        }

        private void DrawSlotsMenu(string title, bool isSaveMode)
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _backgroundTexture);

            float centerX = Screen.width / 2f;
            float topY = Screen.height * 0.08f;

            GUI.Label(new Rect(centerX - 198, topY + 2, 400, 50), title, _menuTitleShadowStyle);
            GUI.Label(new Rect(centerX - 200, topY, 400, 50), title, _menuTitleStyle);

            float buttonWidth = Mathf.Min(400f, Screen.width * 0.6f);
            float buttonHeight = 38f;
            float buttonSpacing = 8f;
            float startY = topY + 60f;

            for (int slot = 1; slot <= 5; slot++)
            {
                int currentSlot = slot;
                bool slotExists = SaveSystem.DoesSlotExist(currentSlot);
                string slotText = slotExists ? $"Мир {currentSlot}" : $"Слот {currentSlot} (Пусто)";

                if (isSaveMode)
                {
                    DrawMenuButton(new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight), slotText, () => OnSlotSelected(currentSlot, true));
                }
                else
                {
                    if (slotExists)
                    {
                        DrawMenuButton(new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight), slotText, () => OnSlotSelected(currentSlot, false));
                    }
                    else
                    {
                        DrawDisabledButton(new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight), slotText);
                    }
                }

                startY += buttonHeight + buttonSpacing;
            }

            startY += 10f;
            DrawMenuButton(new Rect(centerX - buttonWidth / 2f, startY, buttonWidth, buttonHeight), "Отмена", () => SwitchMenuState(MenuState.Main));
        }

        private void DrawConfirmOverwriteMenu()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _backgroundTexture);

            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;

            string line1 = $"Перезаписать Мир {_pendingSlot}?";
            string line2 = "Старый мир будет удален навсегда!";

            GUI.Label(new Rect(centerX - 298, centerY - 70 + 2, 600, 40), line1, _menuTitleShadowStyle);
            GUI.Label(new Rect(centerX - 300, centerY - 70, 600, 40), line1, _menuTitleStyle);

            GUI.Label(new Rect(centerX - 298, centerY - 30 + 2, 600, 30), line2, _statusShadowStyle);
            GUI.Label(new Rect(centerX - 300, centerY - 30, 600, 30), line2, _statusStyle);

            float buttonWidth = 180f;
            float buttonHeight = 40f;

            DrawMenuButton(new Rect(centerX - buttonWidth - 10, centerY + 30, buttonWidth, buttonHeight), "Да", () =>
            {
                ExecuteSave(_pendingSlot);
                SwitchMenuState(MenuState.Main);
            });

            DrawMenuButton(new Rect(centerX + 10, centerY + 30, buttonWidth, buttonHeight), "Нет", () =>
            {
                SwitchMenuState(MenuState.SaveSlots);
            });
        }

        private void DrawLoadingScreen()
        {
            float scale = 4f;
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, Screen.width, Screen.height),
                _dirtTileTexture,
                new Rect(0, 0, Screen.width / (16f * scale), Screen.height / (16f * scale))
            );

            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;

            GUI.Label(new Rect(centerX - 298, centerY - 80 + 2, 600, 50), _loadingTitle, _menuTitleShadowStyle);
            GUI.Label(new Rect(centerX - 300, centerY - 80, 600, 50), _loadingTitle, _menuTitleStyle);

            GUI.Label(new Rect(centerX - 298, centerY - 20 + 2, 600, 40), _loadingStatus, _statusShadowStyle);
            GUI.Label(new Rect(centerX - 300, centerY - 20, 600, 40), _loadingStatus, _statusStyle);

            float barWidth = 360f;
            float barHeight = 6f;
            float barX = centerX - barWidth / 2f;
            float barY = centerY + 30f;

            GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), _progressBarBgTex);

            float fillWidth = barWidth * Mathf.Clamp01(_loadingProgress);
            GUI.DrawTexture(new Rect(barX, barY, fillWidth, barHeight), _progressBarFillTex);
        }

        private void DrawMenuButton(Rect rect, string text, System.Action onClick)
        {
            bool clicked = McGui.ButtonFrame(rect, _buttonNormalTex, _buttonHoverTex);
            bool hover = rect.Contains(Event.current.mousePosition);
            DrawButtonText(rect, text, hover ? new Color(1f, 1f, 0.6f) : Color.white);
            if (clicked) onClick?.Invoke();
        }

        private void DrawButtonText(Rect rect, string text, Color color)
        {
            float o = Mathf.Max(2f, buttonFontSize / 10f);                      // сдвиг тени
            GUI.contentColor = new Color(0.25f, 0.25f, 0.25f, color.a);
            GUI.Label(new Rect(rect.x + o, rect.y + o, rect.width, rect.height), text, _buttonShadowStyle);
            GUI.contentColor = color;
            GUI.Label(rect, text, _buttonTextStyle);
            GUI.contentColor = Color.white;
        }

        private void DrawDisabledButton(Rect rect, string text)
        {
            McGui.DrawSliced(rect, _buttonDisabledTex);
            DrawButtonText(rect, text, new Color(0.5f, 0.5f, 0.5f));
        }

        private void OnSaveLevelClicked() => SwitchMenuState(MenuState.SaveSlots);
        private void OnLoadLevelClicked() => SwitchMenuState(MenuState.LoadSlots);

        private void OnSaveAndExitClicked()
        {
            ExecuteSave(_currentLoadedSlot);
            OnExitToMainMenuClicked();
        }

        private void OnExitToMainMenuClicked()
        {
            StartCoroutine(ExitToMainMenuRoutine());
        }

        public void ExitToMainMenu() => OnExitToMainMenuClicked();

        private IEnumerator ExitToMainMenuRoutine()
        {
            MinecraftNetworkManager.ExpectedDisconnect = true;

            // Чистый клиент чужой мир не сохраняет
            bool isPureClient = NetworkClient.active && !NetworkServer.active;

            if (!isPureClient)
            {
                _networkStatusMessage = "Сохранение мира...";
                yield return null; // кадр, чтобы надпись успела отрисоваться

                // Сохраняем ДО остановки сети и выгрузки сцены, иначе чанки будут очищены
                ExecuteSave(GetActiveSlot());
            }

            // Сбрасываем паузу БЕЗ захвата курсора
            _isPaused = false;
            _networkStatusMessage = "";
            Time.timeScale = 1f;
            Application.targetFrameRate = _defaultTargetFrameRate;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (lanDiscovery != null)
            {
                lanDiscovery.StopDiscovery();
            }

            if (NetworkManager.singleton != null)
            {
                if (NetworkServer.active && NetworkClient.active)
                {
                    NetworkManager.singleton.StopHost();
                }
                else if (NetworkClient.active)
                {
                    NetworkManager.singleton.StopClient();
                }
            }

            yield return null;

            // после StopHost Mirror мог уже сам загрузить меню (Offline Scene), второй раз не грузим
            if (!string.IsNullOrEmpty(mainMenuSceneName)
                && SceneManager.GetActiveScene().name != mainMenuSceneName)
            {
                yield return null;   // даём Mirror закончить собственную загрузку
                if (SceneManager.GetActiveScene().name != mainMenuSceneName)
                    SceneManager.LoadScene(mainMenuSceneName);
            }
        }

        private void OnOpenToLanClicked()
        {
            _lanNick = PlayerPrefs.GetString("PlayerNickname", "Player");
            _lanError = "";
            SwitchMenuState(MenuState.OpenToLan);
        }

        private void DrawOpenToLanMenu()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _backgroundTexture);

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;

            GUI.Label(new Rect(cx - 298, cy - 130 + 2, 600, 40), "Открыть для сети", _menuTitleShadowStyle);
            GUI.Label(new Rect(cx - 300, cy - 130, 600, 40), "Открыть для сети", _menuTitleStyle);

            if (!string.IsNullOrEmpty(_lanError))
                GUI.Label(new Rect(cx - 300, cy - 25, 600, 30), _lanError, _statusStyle);

            float bw = Mathf.Min(400f, Screen.width * 0.6f);
            DrawMenuButton(new Rect(cx - bw / 2f, cy + 10f, bw, 42f), "Открыть", ConfirmOpenToLan);
            DrawMenuButton(new Rect(cx - bw / 2f, cy + 10f + 42f + 15f, bw, 42f), "Отмена", () => SwitchMenuState(MenuState.Main));
        }

        private void ConfirmOpenToLan()
        {
            string nick = LauncherArgs.HasNickname ? LauncherArgs.Nickname : PlayerPrefs.GetString("PlayerNickname", "Player");
            if (string.IsNullOrWhiteSpace(nick)) nick = "Player";
            if (NetworkManager.singleton == null) { _lanError = "NetworkManager не найден"; return; }

            PlayerPrefs.SetString("PlayerNickname", nick);
            PlayerPrefs.Save();

            int port = Transport.active is PortTransport cur ? cur.Port : 7777;
            if (!NetworkServer.active)
            {
                port = FindFreePort(7777);
                if (Transport.active is PortTransport pt) pt.Port = (ushort)port;
            }

            _isMultiplayerActive = true;
            if (!NetworkServer.active) NetworkManager.singleton.StartHost();

            if (lanDiscovery != null) lanDiscovery.StartBroadcasting($"Мир {nick}", port);

            _networkStatusMessage = "Мир открыт для сети";
            Unpause();
            ChatUI.AddMessage($"Ваш мир открыт для сети, порт для подключения: {port}", 1);
        }

        private static string GetLocalIP()
        {
            try
            {
                foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ip.ToString();
            }
            catch { }
            return "localhost";
        }

        private static bool IsPortFree(int port)
        {
            try { var tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port); tcp.Start(); tcp.Stop(); }
            catch { return false; }
            try { using (var udp = new System.Net.Sockets.UdpClient(port)) { } }
            catch { return false; }
            return true;
        }

        private static int FindFreePort(int start)
        {
            for (int p = start; p < start + 100; p++)
                if (IsPortFree(p)) return p;
            return start;
        }

        private void OnLeaveMultiplayerClicked()
        {
            StartCoroutine(LeaveMultiplayerRoutine());
        }

        private IEnumerator LeaveMultiplayerRoutine()
        {
            MinecraftNetworkManager.ExpectedDisconnect = true;
            _isMultiplayerActive = false;

            if (lanDiscovery != null)
            {
                lanDiscovery.StopDiscovery();
            }

            _networkStatusMessage = "Отключение от сети...";

            if (NetworkManager.singleton != null)
            {
                if (NetworkServer.active && NetworkClient.active)
                {
                    NetworkManager.singleton.StopHost();
                }
                else if (NetworkClient.active)
                {
                    NetworkManager.singleton.StopClient();
                }
            }

            yield return new WaitForSecondsRealtime(0.1f);

            _networkStatusMessage = "Сеть закрыта.";
            SwitchMenuState(MenuState.Main);
        }

        private void OnSlotSelected(int slotIndex, bool isSaveMode)
        {
            if (isSaveMode)
            {
                bool exists = SaveSystem.DoesSlotExist(slotIndex);

                if (exists)
                {
                    _pendingSlot = slotIndex;
                    SwitchMenuState(MenuState.ConfirmOverwrite);
                }
                else
                {
                    ExecuteSave(slotIndex);
                    SwitchMenuState(MenuState.Main);
                }
            }
            else
            {
                StartCoroutine(LoadLevelRoutine(slotIndex));
            }
        }

        private void ExecuteSave(int slotIndex)
        {
            _worldManager = WorldManager.Instance != null ? WorldManager.Instance : FindFirstObjectByType<WorldManager>();

            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
            }

            if (_worldManager == null)
            {
                Debug.LogWarning("[PauseMenuUI] WorldManager не найден, сохранение отменено.");
                return;
            }

            if (SaveSystem.Instance == null)
            {
                new GameObject("[SaveSystem]").AddComponent<SaveSystem>();
            }

            Vector3 pos = _playerTransform != null ? _playerTransform.position : Vector3.zero;
            Vector2 rot = _playerTransform != null
                ? new Vector2(_playerTransform.eulerAngles.x, _playerTransform.eulerAngles.y)
                : Vector2.zero;
            int seed = _worldManager.Config != null ? _worldManager.Config.Seed : 0;

            SaveSystem.Instance.SaveWorldFileSync(slotIndex, _worldManager.ActiveChunks, seed, pos, rot);

            _currentLoadedSlot = slotIndex;
            SetActiveSlot(slotIndex);

            if (NetworkServer.active && MinecraftNetworkManager.Instance != null)
                MinecraftNetworkManager.Instance.SaveAllPlayers();
        }

        private IEnumerator LoadLevelRoutine(int slotIndex)
        {
            _isGenerating = true;
            _loadingTitle = "Загрузка уровня";
            _loadingProgress = 0f;

            string[] steps = { "Чтение сохранения..", "Очистка текущего мира..", "Загрузка чанков..", "Перестройка мешей..", "Спавн игрока.." };

            for (int i = 0; i < steps.Length; i++)
            {
                _loadingStatus = steps[i];
                float targetProgress = (float)(i + 1) / steps.Length;

                if (i == 1 && _worldManager != null && SaveSystem.Instance != null)
                {
                    WorldBlocksData blocksData = SaveSystem.Instance.LoadWorldBlocks(slotIndex);
                    PlayerSaveData playerData = SaveSystem.Instance.LoadPlayerData(slotIndex);

                    if (blocksData != null)
                    {
                        _worldManager.LoadWorldFromData(blocksData, playerData);
                    }
                }

                while (_loadingProgress < targetProgress)
                {
                    _loadingProgress = Mathf.MoveTowards(_loadingProgress, targetProgress, Time.unscaledDeltaTime * 2.5f);
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(0.08f);
            }

            _currentLoadedSlot = slotIndex;
            SetActiveSlot(slotIndex);
            _loadingStatus = "Готово!";
            _loadingProgress = 1f;
            yield return new WaitForSecondsRealtime(0.2f);

            _isGenerating = false;
            Unpause();
        }

        private void OnBackToGameClicked() => Unpause();
    }
}