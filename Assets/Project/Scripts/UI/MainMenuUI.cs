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

            BuildInPlaceDeleteUI();
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
                if (btn == null) continue;
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

            CreateMinecraftButton(inPlaceDeleteContainer.transform, "OKButton", "Удалить", new Vector2(-120, -60), new Vector2(180, 50), () => {
                SaveSystem.DeleteSlot(slotPendingDelete);
                HideInPlaceDeleteUI();
                UpdateWorldSlotButtons();
                isDeleteMode = false;
                if (statusText != null) statusText.text = "Мир удален";
            });

            CreateMinecraftButton(inPlaceDeleteContainer.transform, "CancelButton", "Отмена", new Vector2(120, -60), new Vector2(180, 50), () => {
                HideInPlaceDeleteUI();
                isDeleteMode = false;
                if (statusText != null) statusText.text = "";
            });

            inPlaceDeleteContainer.SetActive(false);
        }

        private Button CreateMinecraftButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size, System.Action onClick)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);

            Button btn = btnObj.AddComponent<Button>();
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
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

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

        private IEnumerator LoadSceneAndGenerateWorld()
        {
            GameObject canvasRoot = null;

            if (loadingScreenPanel != null)
            {
                loadingScreenPanel.SetActive(true);
                canvasRoot = loadingScreenPanel.transform.root.gameObject;
                DontDestroyOnLoad(canvasRoot);
            }

            if (worldSelectPanel != null) worldSelectPanel.SetActive(false);

            SetProgressBarImmediate(0f, "Загрузка ресурсов...");

            // 1. Асинхронная загрузка сцены (0% -> 25%)
            AsyncOperation op = SceneManager.LoadSceneAsync(gameSceneName);
            float visualProgress = 0f;

            if (op != null)
            {
                while (!op.isDone)
                {
                    float target = Mathf.Clamp01(op.progress / 0.9f) * 0.25f;
                    visualProgress = Mathf.MoveTowards(visualProgress, target, Time.deltaTime * 0.8f);
                    SetProgressBarImmediate(visualProgress, "Загрузка уровня...");
                    yield return null;
                }
            }

            // 2. Ожидаем появления и готовности WorldManager (25% -> 95%)
            while (WorldManager.Instance == null)
            {
                yield return null;
            }

            while (!WorldManager.Instance.IsWorldReady)
            {
                float target = 0.25f + (WorldManager.GenerationProgress * 0.70f);
                visualProgress = Mathf.MoveTowards(visualProgress, target, Time.deltaTime * 1.5f);

                string status = WorldManager.GenerationStatusText;
                if (string.IsNullOrEmpty(status)) status = "Строим территорию...";

                int percent = Mathf.RoundToInt(visualProgress * 100f);
                SetProgressBarImmediate(visualProgress, $"{status} {percent}%");

                yield return null;
            }

            // 3. Ждем инициализации и включения камеры игрока (95% -> 100%)
            SetProgressBarImmediate(0.98f, "Поиск камеры и разморозка...");

            while (Camera.main == null || !Camera.main.isActiveAndEnabled)
            {
                yield return null;
            }

            SetProgressBarImmediate(1f, "Готово! 100%");

            // 4. Даем Unity отрисовать один полный кадр с активной камерой перед уничтожением UI
            yield return new WaitForEndOfFrame();

            // 5. Очищаем экран загрузки
            if (canvasRoot != null)
            {
                Destroy(canvasRoot);
            }
        }

        private void SetProgressBarImmediate(float progress, string text)
        {
            if (progressBarFillRect != null)
            {
                Vector2 size = progressBarFillRect.sizeDelta;
                size.x = Mathf.Clamp01(progress) * maxBarWidth;
                progressBarFillRect.sizeDelta = size;
            }

            if (loadingProgressText != null)
            {
                loadingProgressText.text = text;
            }
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

            // формат ip:порт (порт необязателен, по умолчанию 7777)
            string ip = raw;
            ushort port = 7777;
            int idx = raw.LastIndexOf(':');
            if (idx > 0 && ushort.TryParse(raw.Substring(idx + 1), out ushort parsedPort))
            {
                ip = raw.Substring(0, idx);
                port = parsedPort;
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