using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

namespace MinecraftClone.Management
{
    public class MainMenuController : MonoBehaviour
    {
        [Header("Панели меню")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject worldSelectPanel;
        [SerializeField] private GameObject multiplayerPanel;

        [Header("Готовый экран загрузки")]
        [Tooltip("Перетащите сюда панель LoadingScreenPanel из Canvas")]
        [SerializeField] private GameObject loadingScreenPanel;

        [Header("Элементы UI внутри экрана загрузки")]
        [SerializeField] private Slider loadingProgressBar;
        [SerializeField] private TextMeshProUGUI loadingStatusText;

        [Header("Кнопки главного меню")]
        [SerializeField] private Button singleplayerButton;
        [SerializeField] private Button multiplayerButton;
        [SerializeField] private Button optionsButton;
        [SerializeField] private Button modsButton;
        [SerializeField] private Button quitGameButton;

        [Header("Кнопки слотов миров (5 шт)")]
        [SerializeField] private Button[] worldButtons = new Button[5];

        [Header("Управление мирами")]
        [SerializeField] private Button deleteWorldButton;
        [SerializeField] private Button backToMainMenuButton;

        [Header("Шрифт кнопки")]
        [SerializeField] private TMP_FontAsset customTmpFont;
        [SerializeField] private Font customLegacyFont;

        [Header("Элементы Сетевой игры (Multiplayer)")]
        [SerializeField] private TMP_InputField ipInputField;
        [SerializeField] private TextMeshProUGUI localServerText;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button cancelMultiplayerButton;

        [Header("Настройки загрузки сцены")]
        [SerializeField] private string gameSceneName = "GameScene";

        private bool isConnecting = false;
        private bool isDeleteMode = false;
        private int slotPendingDelete = -1;

        private GameObject confirmationModalObj;

        private void Start()
        {
            ShowMainMenu();
            UpdateWorldButtonsText();

            if (optionsButton != null) optionsButton.interactable = false;
            if (modsButton != null) modsButton.interactable = false;

            // Auto-wire Singleplayer
            if (singleplayerButton == null)
                singleplayerButton = FindButtonByNameContains("singleplayer", "одиночная");
            if (singleplayerButton != null)
            {
                singleplayerButton.onClick.RemoveAllListeners();
                singleplayerButton.onClick.AddListener(OpenSingleplayerMenu);
            }

            // Auto-wire Multiplayer
            if (multiplayerButton == null)
                multiplayerButton = FindButtonByNameContains("multiplayer", "сетевая");
            if (multiplayerButton != null)
            {
                multiplayerButton.onClick.RemoveAllListeners();
                multiplayerButton.onClick.AddListener(OpenMultiplayerMenu);
            }

            // Auto-wire Quit Game
            if (quitGameButton == null)
                quitGameButton = FindButtonByNameContains("quit", "exit", "выход");
            if (quitGameButton != null)
            {
                quitGameButton.onClick.RemoveAllListeners();
                quitGameButton.onClick.AddListener(QuitGame);
            }

            // Auto-wire Delete World Button
            if (deleteWorldButton == null)
                deleteWorldButton = FindButtonByNameContains("delete", "удалить", "trash");
            if (deleteWorldButton != null)
            {
                deleteWorldButton.onClick.RemoveAllListeners();
                deleteWorldButton.onClick.AddListener(OnDeleteButtonClicked);
            }

            // Auto-wire Back / Cancel buttons
            if (backToMainMenuButton == null)
                backToMainMenuButton = FindButtonByNameContains("back", "cancel", "отмена", "назад");
            if (backToMainMenuButton != null)
            {
                backToMainMenuButton.onClick.RemoveAllListeners();
                backToMainMenuButton.onClick.AddListener(ShowMainMenu);
            }

            if (cancelMultiplayerButton == null)
                cancelMultiplayerButton = FindButtonByNameContains("cancel", "отмена", "back", "назад");
            if (cancelMultiplayerButton != null)
            {
                cancelMultiplayerButton.onClick.RemoveAllListeners();
                cancelMultiplayerButton.onClick.AddListener(CancelMultiplayerConnection);
            }

            if (connectButton != null)
            {
                connectButton.onClick.RemoveAllListeners();
                connectButton.onClick.AddListener(ConnectToIP);
            }

            // Auto-wire loading screen panel if not assigned
            if (loadingScreenPanel == null)
            {
                Transform found = transform.Find("LoadingScreenPanel");
                if (found != null) loadingScreenPanel = found.gameObject;
            }

            // Wire 5 world slot buttons
            for (int i = 0; i < worldButtons.Length; i++)
            {
                if (worldButtons[i] == null)
                {
                    worldButtons[i] = FindButtonByNameContains($"slot{i + 1}", $"мир {i + 1}", $"слот {i + 1}");
                }

                if (worldButtons[i] != null)
                {
                    int worldIndex = i + 1;
                    worldButtons[i].onClick.RemoveAllListeners();
                    worldButtons[i].onClick.AddListener(() => OnWorldSlotClicked(worldIndex));
                }
            }
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
                    if (objName.Contains(kw) || label.Contains(kw))
                    {
                        return btn;
                    }
                }
            }
            return null;
        }

        public void ShowMainMenu()
        {
            isConnecting = false;
            isDeleteMode = false;
            if (localServerText != null) localServerText.text = "";
            SetPanelActive(mainMenuPanel);
        }

        public void OpenSingleplayerMenu()
        {
            isConnecting = false;
            isDeleteMode = false;
            if (localServerText != null) localServerText.text = "";
            UpdateWorldButtonsText();
            SetPanelActive(worldSelectPanel);
        }

        public void OpenMultiplayerMenu()
        {
            isConnecting = false;
            isDeleteMode = false;
            if (localServerText != null) localServerText.text = "";
            SetPanelActive(multiplayerPanel);
            if (ipInputField != null && string.IsNullOrEmpty(ipInputField.text))
            {
                ipInputField.text = "localhost";
            }
            UpdateLocalServerDisplay("Поиск локального сервера...");
        }

        private void SetPanelActive(GameObject targetPanel)
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(mainMenuPanel == targetPanel);
            if (worldSelectPanel != null) worldSelectPanel.SetActive(worldSelectPanel == targetPanel);
            if (multiplayerPanel != null) multiplayerPanel.SetActive(multiplayerPanel == targetPanel);
            if (loadingScreenPanel != null) loadingScreenPanel.SetActive(loadingScreenPanel == targetPanel);
        }

        public void UpdateWorldButtonsText()
        {
            for (int i = 0; i < worldButtons.Length; i++)
            {
                if (worldButtons[i] == null) continue;

                int slotIndex = i + 1;
                bool exists = SaveSystem.DoesSlotExist(slotIndex);

                TMP_Text tmpText = worldButtons[i].GetComponentInChildren<TMP_Text>();
                Text legacyText = worldButtons[i].GetComponentInChildren<Text>();

                string label = exists ? $"Мир {slotIndex}" : "- Пусто -";

                if (tmpText != null)
                {
                    tmpText.text = label;
                    tmpText.color = Color.white;
                    if (customTmpFont != null)
                    {
                        tmpText.font = customTmpFont;
                    }
                }
                else if (legacyText != null)
                {
                    legacyText.text = label;
                    legacyText.color = Color.white;
                    if (customLegacyFont != null)
                    {
                        customLegacyFont = legacyText.font;
                    }
                }
            }
        }

        private void ShowDeleteConfirmationDialog(int slotIndex)
        {
            slotPendingDelete = slotIndex;

            if (confirmationModalObj == null)
            {
                GameObject canvasObj = new GameObject("ConfirmationCanvas");
                Canvas canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 9998;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(canvasObj);

                GameObject bgObj = new GameObject("ModalBackground");
                bgObj.transform.SetParent(canvasObj.transform, false);
                Image bgImg = bgObj.AddComponent<Image>();
                bgImg.color = new Color(0f, 0f, 0f, 0.7f);

                RectTransform bgRect = bgObj.GetComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = Vector2.zero;
                bgRect.offsetMax = Vector2.zero;

                GameObject windowObj = new GameObject("ModalWindow");
                windowObj.transform.SetParent(bgObj.transform, false);
                Image winImg = windowObj.AddComponent<Image>();
                winImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

                RectTransform winRect = windowObj.GetComponent<RectTransform>();
                winRect.anchorMin = new Vector2(0.5f, 0.5f);
                winRect.anchorMax = new Vector2(0.5f, 0.5f);
                winRect.sizeDelta = new Vector2(460, 220);
                winRect.anchoredPosition = Vector2.zero;

                GameObject textObj = new GameObject("ConfirmationText");
                textObj.transform.SetParent(windowObj.transform, false);
                TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
                tmp.text = "Вы уверены? Мир исчезнет навсегда!";
                tmp.fontSize = 20;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.enableWordWrapping = true;

                RectTransform textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.5f, 0.7f);
                textRect.anchorMax = new Vector2(0.5f, 0.7f);
                textRect.sizeDelta = new Vector2(420, 80);
                textRect.anchoredPosition = Vector2.zero;

                CreateButton(windowObj.transform, "OKButton", "ОК", new Vector2(-100, -50), new Vector2(140, 40), () => {
                    SaveSystem.DeleteSlot(slotPendingDelete);
                    UpdateWorldButtonsText();
                    CloseDeleteConfirmationDialog();
                    isDeleteMode = false;
                    if (localServerText != null) localServerText.text = "";
                    Debug.Log($"[MainMenuController] Слот №{slotPendingDelete} удален.");
                });

                CreateButton(windowObj.transform, "CancelButton", "Отмена", new Vector2(100, -50), new Vector2(140, 40), () => {
                    CloseDeleteConfirmationDialog();
                    isDeleteMode = false;
                    if (localServerText != null) localServerText.text = "";
                    Debug.Log("[MainMenuController] Удаление отменено.");
                });

                confirmationModalObj = bgObj;
            }

            confirmationModalObj.SetActive(true);
        }

        private void CloseDeleteConfirmationDialog()
        {
            if (confirmationModalObj != null)
            {
                confirmationModalObj.SetActive(false);
            }
        }

        private GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size, System.Action onClick)
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
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 18;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return btnObj;
        }

        private void OnDeleteButtonClicked()
        {
            isDeleteMode = true;
            if (localServerText != null)
            {
                localServerText.text = "удаление...";
            }
            Debug.Log("[MainMenuController] Режим удаления активирован: нажмите на слот мира для удаления.");
        }

        private void OnWorldSlotClicked(int slotIndex)
        {
            if (isConnecting) return;

            if (isDeleteMode)
            {
                if (SaveSystem.DoesSlotExist(slotIndex))
                {
                    ShowDeleteConfirmationDialog(slotIndex);
                }
                else
                {
                    Debug.Log($"[MainMenuController] Слот №{slotIndex} уже пуст.");
                    isDeleteMode = false;
                    if (localServerText != null) localServerText.text = "";
                }
                return;
            }

            Debug.Log($"Запуск мира из слота №{slotIndex}");

            PlayerPrefs.SetInt("SelectedWorldSlot", slotIndex);
            PlayerPrefs.Save();

            isConnecting = true;

            // Включаем готовый экран загрузки
            SetPanelActive(loadingScreenPanel);

            // Запускаем асинхронную подгрузку сцены напрямую
            StartCoroutine(LoadSceneRoutine(gameSceneName));
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            UpdateLoadingProgress(0.05f, "Загрузка ресурсов мира...");
            yield return new WaitForSeconds(0.1f);

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
            asyncLoad.allowSceneActivation = true;

            while (!asyncLoad.isDone)
            {
                float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
                UpdateLoadingProgress(progress, $"Загрузка мира... {(int)(progress * 100)}%");
                yield return null;
            }
        }

        public void UpdateLoadingProgress(float progress, string statusText)
        {
            if (loadingProgressBar != null) loadingProgressBar.value = progress;
            if (loadingStatusText != null) loadingStatusText.text = statusText;
        }

        private void ConnectToIP()
        {
            if (isConnecting) return;

            string ip = ipInputField != null ? ipInputField.text.Trim() : "localhost";
            if (string.IsNullOrEmpty(ip)) ip = "localhost";

            if (Mirror.NetworkManager.singleton != null)
            {
                if (Mirror.Transport.active == null)
                {
                    Debug.LogError("[MainMenuController] Ошибка: на объекте NetworkManager не назначен Transport!");
                    return;
                }

                isConnecting = true;
                SetPanelActive(loadingScreenPanel);
                UpdateLoadingProgress(0.2f, $"Подключение к {ip}...");

                Mirror.NetworkManager.singleton.networkAddress = ip;
                Mirror.NetworkManager.singleton.StartClient();
            }
            else
            {
                Debug.LogError("[MainMenuController] NetworkManager не найден на сцене.");
            }
        }

        private void CancelMultiplayerConnection()
        {
            if (Mirror.NetworkManager.singleton != null && isConnecting)
            {
                Mirror.NetworkManager.singleton.StopClient();
            }
            ShowMainMenu();
        }

        public void UpdateLocalServerDisplay(string serverInfo)
        {
            if (localServerText != null)
            {
                localServerText.text = serverInfo;
            }
        }

        public void QuitGame()
        {
            Debug.Log("[MainMenuController] Выход из игры...");
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}