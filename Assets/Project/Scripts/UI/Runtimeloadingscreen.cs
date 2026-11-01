using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using MinecraftClone.Management;

namespace MinecraftClone.UI
{
    /// <summary>
    /// Экран загрузки, полностью собранный кодом. Не требует настройки в инспекторе,
    /// переживает смену сцены и сам скрывается, когда мир готов.
    /// Использование: RuntimeLoadingScreen.LoadGameScene("GameScene");
    /// </summary>
    public class RuntimeLoadingScreen : MonoBehaviour
    {
        private static RuntimeLoadingScreen _instance;

        public static bool IsShown { get; private set; }
        private GameObject _canvasRoot;
        private RectTransform _fillRect;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _statusText;
        private TMP_FontAsset _font;
        private float _shownProgress;

        public static void LoadGameScene(string sceneName)
        {
            EnsureInstance();
            _instance.StopAllCoroutines();
            _instance.StartCoroutine(_instance.LoadRoutine(sceneName));
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance.StopAllCoroutines();
            if (_instance._canvasRoot != null) _instance._canvasRoot.SetActive(false);
            IsShown = false;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;

            GameObject go = new GameObject("[RuntimeLoadingScreen]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<RuntimeLoadingScreen>();
        }

        private void Awake()
        {
            BuildUI();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                IsShown = false;
            }
        }

        // ---------- Построение интерфейса ----------

        private void BuildUI()
        {
            foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f != null && f.name.ToLower().Contains("minecraft"))
                {
                    _font = f;
                    break;
                }
            }

            _canvasRoot = new GameObject("LoadingCanvas");
            _canvasRoot.transform.SetParent(transform, false);

            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // поверх всего

            CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Фон: сетка плиток земли из тех же спрайтов panorama0..5, что и в главном меню
            RectTransform bg = CreateRect("Background", _canvasRoot.transform);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            bg.gameObject.AddComponent<Image>().color = new Color(0.20f, 0.13f, 0.08f, 1f); // запасной цвет под плитками

            BuildDirtTiles(bg);

            // Тексты
            _titleText = CreateText("Title", _canvasRoot.transform, 52f, new Vector2(0f, 70f), new Vector2(1000f, 90f));
            _statusText = CreateText("Status", _canvasRoot.transform, 28f, new Vector2(0f, -10f), new Vector2(1000f, 60f));

            // Полоса прогресса
            RectTransform barBg = CreateRect("BarBackground", _canvasRoot.transform);
            barBg.anchorMin = new Vector2(0.5f, 0.5f);
            barBg.anchorMax = new Vector2(0.5f, 0.5f);
            barBg.sizeDelta = new Vector2(500f, 12f);
            barBg.anchoredPosition = new Vector2(0f, -60f);
            barBg.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);

            _fillRect = CreateRect("BarFill", barBg);
            _fillRect.anchorMin = Vector2.zero;
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;
            _fillRect.gameObject.AddComponent<Image>().color = new Color(0.35f, 0.85f, 0.35f, 1f);

            _canvasRoot.SetActive(false);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, Vector2 anchoredPos, Vector2 size)
        {
            RectTransform rt = CreateRect(name, parent);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            TextMeshProUGUI tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (_font != null) tmp.font = _font;
            return tmp;
        }

        private void BuildDirtTiles(Transform parent)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>("Panorama");
            if (sprites == null || sprites.Length == 0)
            {
                Debug.LogWarning("[RuntimeLoadingScreen] Спрайты не найдены в Assets/Resources/Panorama — фон будет сплошным цветом.");
                return;
            }

            const float tileSize = 256f;  // такой же, как Block Size у DirtGridGenerator
            const int columns = 14;
            const int rows = 8;            // 14x8 плиток покрывают и широкие мониторы

            RectTransform grid = CreateRect("DirtGrid", parent);
            grid.anchorMin = new Vector2(0.5f, 0.5f);
            grid.anchorMax = new Vector2(0.5f, 0.5f);
            grid.sizeDelta = new Vector2(columns * tileSize, rows * tileSize);
            grid.anchoredPosition = Vector2.zero;

            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(tileSize, tileSize);
            layout.spacing = Vector2.zero;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;

            for (int i = 0; i < columns * rows; i++)
            {
                RectTransform tile = CreateRect("Tile_" + i, grid);
                Image img = tile.gameObject.AddComponent<Image>();
                img.sprite = sprites[Random.Range(0, sprites.Length)];
                img.color = new Color(0.45f, 0.45f, 0.45f, 1f); // то же затемнение, что в DirtGridGenerator
                img.raycastTarget = false;
            }
        }

        // ---------- Логика загрузки ----------

        private void SetProgress(float progress, string status)
        {
            if (_fillRect != null)
            {
                _fillRect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            }
            if (_statusText != null) _statusText.text = status;
        }

        private void SmoothProgress(float target, float speed, string status)
        {
            _shownProgress = Mathf.MoveTowards(_shownProgress, target, Time.unscaledDeltaTime * speed);
            SetProgress(_shownProgress, status);
        }

        private IEnumerator LoadRoutine(string sceneName)
        {
            _shownProgress = 0f;
            _canvasRoot.SetActive(true);
            IsShown = true;
            _titleText.text = "Загрузка мира";
            SetProgress(0f, "Подготовка...");

            // Два кадра, чтобы экран загрузки гарантированно отрисовался до тяжёлой работы
            yield return null;
            yield return null;

            // 1. Загрузка сцены (0% - 20%)
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                Debug.LogError($"[RuntimeLoadingScreen] Сцена '{sceneName}' не найдена. Добавьте её в File -> Build Settings!");
                _canvasRoot.SetActive(false);
                IsShown = false;
                yield break;
            }

            while (!op.isDone)
            {
                float target = Mathf.Clamp01(op.progress / 0.9f) * 0.2f;
                SmoothProgress(target, 0.8f, "Загрузка уровня...");
                yield return null;
            }
            Debug.Log("[LS] сцена загружена");

            // 2. Ждём, пока WorldManager сгенерирует мир (20% - 95%)
            float worldWait = 0f;
            while (WorldManager.Instance == null || !WorldManager.Instance.IsWorldReady)
            {
                worldWait += Time.unscaledDeltaTime;
                if (worldWait > 120f)
                {
                    Debug.LogError("[RuntimeLoadingScreen] Мир не стал готов за 120 секунд. Смотрите ошибки выше в Console.");
                    break;
                }

                float gen = WorldManager.Instance != null ? WorldManager.GenerationProgress : 0f;
                float target = 0.2f + gen * 0.75f;

                string status = WorldManager.Instance != null && !string.IsNullOrEmpty(WorldManager.GenerationStatusText)
                    ? WorldManager.GenerationStatusText
                    : "Строим территорию...";

                SmoothProgress(target, 1.5f, $"{status} {Mathf.RoundToInt(_shownProgress * 100f)}%");
                yield return null;
            }
            Debug.Log("[LS] мир готов, жду камеру");

            // 3. Ждём камеру игрока (не дольше 5 секунд, чтобы не зависнуть навсегда)
            float waited = 0f;
            while ((Camera.main == null || !Camera.main.isActiveAndEnabled) && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                SetProgress(0.97f, "Поиск камеры...");
                yield return null;
            }

            SetProgress(1f, "Готово! 100%");

            // Даём отрисоваться кадру с активной камерой, затем прячем экран
            yield return null;
            yield return null;

            Debug.Log("[LS] экран загрузки скрыт");
            _canvasRoot.SetActive(false);
            IsShown = false;
        }
    }
}