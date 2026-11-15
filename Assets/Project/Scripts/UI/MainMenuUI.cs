using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Networking;

namespace MinecraftClone.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Панели из Hierarchy")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject worldSelectPanel;
        [SerializeField] private GameObject multiplayerPanel;

        [Header("Экран загрузки (Loading Screen)")]
        [SerializeField] private GameObject loadingScreenPanel;
        [SerializeField] private RectTransform progressBarFillRect;
        [SerializeField] private TextMeshProUGUI loadingProgressText;
        [SerializeField] private float maxBarWidth = 320f;

        [Header("Поля ввода (MultiplayerPanel)")]
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private TMP_InputField ipInput;

        [Header("Кнопки MainMenuPanel")]
        [SerializeField] private Button singleplayerBtn;
        [SerializeField] private Button multiplayerBtn;
        [SerializeField] private Button quitBtn;

        [Header("Слоты Миров (5 кнопок)")]
        [SerializeField] private Button[] worldSlotButtons = new Button[5];

        [Header("Управление мирами")]
        [SerializeField] private Button deleteWorldButton;
        [SerializeField] private Button backFromWorldSelectBtn;

        [Header("Кнопки MultiplayerPanel")]
        [SerializeField] private Button connectButton;
        [SerializeField] private Button backFromMultiplayerBtn;

        [Header("Текст статуса / предупреждений")]
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Имя сцены для загрузки")]
        [SerializeField] private string gameSceneName = "GameScene";

        [Header("Шрифт кнопок слотов")]
        [SerializeField] private TMP_FontAsset customTmpFont;
        [SerializeField] private Font customLegacyFont;

        [SerializeField] private Button optionsBtn;

        private bool isDeleteMode = false;
        private int slotPendingDelete = -1;

        private GameObject inPlaceDeleteContainer;
        private TextMeshProUGUI inPlaceWarningText;

        private void OnEnable()
        {
            ResetCursor();
        }

        private void Update()
        {
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
            {
                ResetCursor();
            }

            if (!string.IsNullOrEmpty(MinecraftNetworkManager.LastError))
            {
                if (statusText != null) statusText.text = MinecraftNetworkManager.LastError;
                MinecraftNetworkManager.LastError = "";
            }
        }

        private void Start()
        {
            ResetCursor();
            Time.timeScale = 1f;

            ShowMainMenu();
            UpdateWorldSlotButtons();

            if (ipInput != null) ipInput.text = PlayerPrefs.GetString("LastIP", "localhost");
            if (nicknameInput != null)
            {
                nicknameInput.text = PlayerPrefs.GetString("PlayerNickname", "Player");
                // ник задан лаунчером: показываем его, но менять нельзя
                if (LauncherArgs.HasNickname) nicknameInput.interactable = false;
            }

            if (singleplayerBtn == null) singleplayerBtn = FindButtonByNameContains("singleplayer", "одиночная");
            if (multiplayerBtn == null) multiplayerBtn = FindButtonByNameContains("multiplayer", "сетевая");
            if (quitBtn == null) quitBtn = FindButtonByNameContains("quit", "exit", "выход");
            if (deleteWorldButton == null) deleteWorldButton = FindButtonByNameContains("delete", "удалить", "trash");
            if (backFromWorldSelectBtn == null) backFromWorldSelectBtn = FindButtonByNameContains("back", "cancel", "отмена", "назад");
            if (backFromMultiplayerBtn == null) backFromMultiplayerBtn = FindButtonByNameContains("back", "cancel", "отмена", "назад");
            if (connectButton == null) connectButton = FindButtonByNameContains("connect", "подключиться");

            if (singleplayerBtn != null) singleplayerBtn.onClick.AddListener(OpenWorldSelect);
            if (multiplayerBtn != null) multiplayerBtn.onClick.AddListener(OpenMultiplayer);
            if (quitBtn != null) quitBtn.onClick.AddListener(QuitGame);
            if (deleteWorldButton != null) deleteWorldButton.onClick.AddListener(OnDeleteButtonClicked);
            if (backFromWorldSelectBtn != null) backFromWorldSelectBtn.onClick.AddListener(ShowMainMenu);
            if (backFromMultiplayerBtn != null) backFromMultiplayerBtn.onClick.AddListener(ShowMainMenu);
            if (connectButton != null) connectButton.onClick.AddListener(StartMultiplayerClient);
            if (optionsBtn == null) optionsBtn = FindButtonByNameContains("options", "настройки");
            if (optionsBtn != null) { optionsBtn.interactable = true; optionsBtn.onClick.AddListener(OpenSettings); }

            for (int i = 0; i < worldSlotButtons.Length; i++)
            {
                if (worldSlotButtons[i] == null)
                {
                    worldSlotButtons[i] = FindButtonByNameContains($"slot{i + 1}", $"мир {i + 1}", $"слот {i + 1}");
                }

                int slotIndex = i;
                if (worldSlotButtons[i] != null)
                {
                    worldSlotButtons[i].onClick.AddListener(() => OnWorldSlotClicked(slotIndex));
                }
            }

            if (multiplayerPanel != null) BuildDirtBackground(multiplayerPanel.transform);
            BuildInPlaceDeleteUI();
        }

        private void OpenSettings()
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false); // чтобы кнопки под настройками не нажимались
            MinecraftClone.UI.SettingsMenuUI.Open(ShowMainMenu, true);
        }

        public void ResetCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private Button FindButtonByNameContains(params string[] keywords)
        {
            Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();
            foreach (var btn in allButtons)
            {
                if (btn == null || !btn.gameObject.scene.IsValid()) continue;
                string objName = btn.gameObject.name.ToLower();
                TMP_Text tmp = btn.GetComponentInChildren<TMP_Text>();
                Text txt = btn.GetComponentInChildren<Text>();
                string label = tmp != null ? tmp.text.ToLower() : (txt != null ? txt.text.ToLower() : "");

                foreach (var kw in keywords)
                {
                    if (objName.Contains(kw) || label.Contains(kw)) return btn;
                }
            }
            return null;
        }

        #region Встроенный интерфейс удаления
        private void BuildInPlaceDeleteUI()
        {
            if (worldSelectPanel == null) return;

            inPlaceDeleteContainer = new GameObject("InPlaceDeleteContainer");
            inPlaceDeleteContainer.transform.SetParent(worldSelectPanel.transform, false);

            RectTransform rect = inPlaceDeleteContainer.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            BuildDirtBackground(inPlaceDeleteContainer.transform);

            TMP_FontAsset tmpFontAsset = null;
            TMP_FontAsset[] allTmpFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var tf in allTmpFonts)
            {
                if (tf != null && tf.name.ToLower().Contains("minecraft"))
                {
                    tmpFontAsset = tf;
                    break;
                }
            }

            GameObject textObj = new GameObject("WarningText");
            textObj.transform.SetParent(inPlaceDeleteContainer.transform, false);
            inPlaceWarningText = textObj.AddComponent<TextMeshProUGUI>();
            inPlaceWarningText.fontSize = 26;
            inPlaceWarningText.alignment = TextAlignmentOptions.Center;
            inPlaceWarningText.color = Color.yellow;
            inPlaceWarningText.enableWordWrapping = true;
            if (tmpFontAsset != null) inPlaceWarningText.font = tmpFontAsset;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.1f, 0.5f);
            textRect.anchorMax = new Vector2(0.9f, 0.8f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            CreateMinecraftButton(inPlaceDeleteContainer.transform, "OKButton", "Удалить", new Vector2(-170, -60), new Vector2(320, 48), () => {
                SaveSystem.DeleteSlot(slotPendingDelete);
                HideInPlaceDeleteUI();
                UpdateWorldSlotButtons();
                isDeleteMode = false;
                if (statusText != null) statusText.text = "Мир удален";
            }, tmpFontAsset);

            CreateMinecraftButton(inPlaceDeleteContainer.transform, "CancelButton", "Отмена", new Vector2(170, -60), new Vector2(320, 48), () => {
                HideInPlaceDeleteUI();
                isDeleteMode = false;
                if (statusText != null) statusText.text = "";
            }, tmpFontAsset);

            inPlaceDeleteContainer.SetActive(false);
        }

        private Button CreateMinecraftButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size, System.Action onClick, TMP_FontAsset font = null)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            Image img = btnObj.AddComponent<Image>();
            img.sprite = Resources.Load<Sprite>("UI/button");
            img.type = Image.Type.Sliced;
            img.color = Color.white;

            Button btn = btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.spriteState = new SpriteState
            {
                highlightedSprite = Resources.Load<Sprite>("UI/button_hover"),
                pressedSprite = Resources.Load<Sprite>("UI/button_hover"),
                selectedSprite = Resources.Load<Sprite>("UI/button"),
                disabledSprite = Resources.Load<Sprite>("UI/button_disabled")
            };
            btn.onClick.AddListener(() => onClick?.Invoke());

            RectTransform rect = btnObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.4f);
            rect.anchorMax = new Vector2(0.5f, 0.4f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 24;                          // было 20
            if (font != null) tmp.font = font;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            btnObj.AddComponent<MinecraftButtonHover>();   // перед return btn; даёт тень, hover и рамку
            return btn;
        }

        private void ShowInPlaceDeleteUI(int slotIndex)
        {
            slotPendingDelete = slotIndex;

            foreach (Transform child in worldSelectPanel.transform)
            {
                if (child.gameObject != inPlaceDeleteContainer)
                {
                    child.gameObject.SetActive(false);
                }
            }

            if (inPlaceWarningText != null)
            {
                inPlaceWarningText.text = $"Вы уверены, что хотите удалить Мир {slotIndex}?\nЭто действие нельзя отменить!";
            }

            if (inPlaceDeleteContainer != null)
            {
                inPlaceDeleteContainer.SetActive(true);
            }
        }

        private void HideInPlaceDeleteUI()
        {
            if (inPlaceDeleteContainer != null)
            {
                inPlaceDeleteContainer.SetActive(false);
            }

            foreach (Transform child in worldSelectPanel.transform)
            {
                if (child.gameObject != inPlaceDeleteContainer)
                {
                    child.gameObject.SetActive(true);
                }
            }
        }
        #endregion

        public void UpdateWorldSlotButtons()
        {
            for (int i = 0; i < worldSlotButtons.Length; i++)
            {
                if (worldSlotButtons[i] == null) continue;

                int realSlot = i + 1;
                bool exists = SaveSystem.DoesSlotExist(realSlot);

                TMP_Text tmpText = worldSlotButtons[i].GetComponentInChildren<TMP_Text>();
                Text legacyText = worldSlotButtons[i].GetComponentInChildren<Text>();

                string label = exists ? $"Мир {realSlot}" : "- Пусто -";

                if (tmpText != null)
                {
                    tmpText.text = label;
                    tmpText.color = Color.white;
                    if (customTmpFont != null) tmpText.font = customTmpFont;
                }
                else if (legacyText != null)
                {
                    legacyText.text = label;
                    legacyText.color = Color.white;
                    if (customLegacyFont != null) legacyText.font = customLegacyFont;
                }
            }
        }

        public void ShowMainMenu()
        {
            ResetCursor();
            isDeleteMode = false;
            HideInPlaceDeleteUI();
            if (statusText != null) statusText.text = "";
            if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
            if (worldSelectPanel != null) worldSelectPanel.SetActive(false);
            if (multiplayerPanel != null) multiplayerPanel.SetActive(false);
        }

        public void OpenWorldSelect()
        {
            ResetCursor();
            isDeleteMode = false;
            HideInPlaceDeleteUI();
            if (statusText != null) statusText.text = "";
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (worldSelectPanel != null) worldSelectPanel.SetActive(true);
            if (multiplayerPanel != null) multiplayerPanel.SetActive(false);
            UpdateWorldSlotButtons();
        }

        public void OpenMultiplayer()
        {
            ResetCursor();
            isDeleteMode = false;
            HideInPlaceDeleteUI();
            if (statusText != null) statusText.text = "";
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (worldSelectPanel != null) worldSelectPanel.SetActive(false);
            if (multiplayerPanel != null) multiplayerPanel.SetActive(true);
        }

        private void OnDeleteButtonClicked()
        {
            isDeleteMode = true;
            if (statusText != null) statusText.text = "Выберите слот для удаления";
        }

        private void OnWorldSlotClicked(int slotIndex)
        {
            int realSlotIndex = slotIndex + 1;

            if (isDeleteMode)
            {
                if (SaveSystem.DoesSlotExist(realSlotIndex))
                {
                    ShowInPlaceDeleteUI(realSlotIndex);
                }
                else
                {
                    isDeleteMode = false;
                    if (statusText != null) statusText.text = "";
                }
                return;
            }

            WorldSlotSelection.Slot = realSlotIndex;
            Debug.Log($"[MainMenuUI] Нажат элемент массива №{slotIndex} -> слот мира №{realSlotIndex}");

            RuntimeLoadingScreen.LoadGameScene(gameSceneName);
        }

        public void StartMultiplayerClient()
        {
            string raw = (ipInput != null && !string.IsNullOrWhiteSpace(ipInput.text)) ? ipInput.text.Trim() : "localhost";
            string nick = LauncherArgs.HasNickname
                ? LauncherArgs.Nickname
                : ((nicknameInput != null && !string.IsNullOrWhiteSpace(nicknameInput.text)) ? nicknameInput.text.Trim() : "");
            if (string.IsNullOrEmpty(nick))
            {
                if (statusText != null) statusText.text = "Введите ник!";
                return;
            }

            if (NetworkClient.active)
            {
                ConnectionMessage.Show("Подключение уже идёт. Подожди или нажми «Назад».");
                return;
            }

            // формат ip:порт (порт необязателен, по умолчанию 7777)
            string ip = raw;
            ushort port = 7777;
            int idx = raw.LastIndexOf(':');
            if (idx > 0 && ushort.TryParse(raw.Substring(idx + 1), out ushort parsedPort))
            {
                ip = raw.Substring(0, idx);
                port = parsedPort;
            }

            if (idx > 0 && raw.IndexOf(':') == idx && !ushort.TryParse(raw.Substring(idx + 1), out _))
            {
                ConnectionMessage.Show("Неверный порт. Формат: 192.168.0.5:7777 (порт необязателен).");
                return;
            }

            PlayerPrefs.SetString("LastIP", raw);
            PlayerPrefs.SetString("PlayerNickname", nick);

            PlayerPrefs.Save();

            if (NetworkManager.singleton == null)
            {
                if (statusText != null) statusText.text = "NetworkManager не найден на сцене";
                return;
            }

            if (Transport.active is PortTransport pt) pt.Port = port;

            NetworkManager.singleton.networkAddress = ip;
            NetworkManager.singleton.StartClient();

            if (statusText != null) statusText.text = "Подключение...";
            StartCoroutine(ConnectWatchdog(ip, port));
        }

        // если за 12 секунд подключиться не вышло, не оставляем игрока гадать
        private IEnumerator ConnectWatchdog(string address, ushort port)
        {
            float t = 0f;
            while (t < 12f)
            {
                if (!NetworkClient.active || NetworkClient.isConnected) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            NetworkManager.singleton.StopClient();
            if (statusText != null) statusText.text = "";
            ConnectionMessage.Show($"Сервер {address}:{port} не отвечает 12 секунд. Проверь IP и порт, запущен ли хост, не блокирует ли его брандмауэр.");
        }

        private static void BuildDirtBackground(Transform parent)
        {
            if (parent.Find("DirtGrid") != null) return;
            Sprite[] sprites = Resources.LoadAll<Sprite>("Panorama");
            if (sprites == null || sprites.Length == 0) return;

            const float tile = 256f; const int cols = 14, rows = 8;
            var gridGo = new GameObject("DirtGrid", typeof(RectTransform));
            gridGo.transform.SetParent(parent, false);
            gridGo.transform.SetAsFirstSibling();            // позади всего остального
            var rt = (RectTransform)gridGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(cols * tile, rows * tile);
            rt.anchoredPosition = Vector2.zero;

            var layout = gridGo.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(tile, tile);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = cols;

            for (int i = 0; i < cols * rows; i++)
            {
                var t = new GameObject("Tile_" + i, typeof(RectTransform), typeof(Image));
                t.transform.SetParent(gridGo.transform, false);
                var img = t.GetComponent<Image>();
                img.sprite = sprites[UnityEngine.Random.Range(0, sprites.Length)];
                img.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                img.raycastTarget = false;
            }
        }

        public void QuitGame()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}