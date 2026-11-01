using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

namespace MinecraftClone.UI
{
    public class DebugInfoUI : MonoBehaviour
    {
        [Header("Версия")]
        [SerializeField] private string gameVersion = "0.0.17wa";

        [Header("Настройки")]
        [SerializeField] private Font customFont;
        [SerializeField] private int fontSize = 16;
        [SerializeField] private Color textColor = Color.white;

        [Header("Настройки сцены")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Header("Прямая ссылка на игрока (необязательно)")]
        [SerializeField] private Transform playerTransform;

        private float _fpsTimer = 0f;
        private int _frameCount = 0;
        private float _currentFps = 0f;
        private float _currentMsec = 0f;

        private GUIStyle _style;
        private GUIStyle _shadowStyle;

        private bool _inMainMenu;
        private string _textFps = "";
        private string _textPos = "";
        private float _textTimer;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            playerTransform = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Сбрасываем ссылку на игрока при смене сцены
            playerTransform = null;
            CheckMainMenuStatus(scene.name);
        }

        private void Start()
        {
            CheckMainMenuStatus(SceneManager.GetActiveScene().name);
            BuildStyles();
        }

        private void CheckMainMenuStatus(string sceneName)
        {
            _inMainMenu = (sceneName == mainMenuSceneName);
        }

        private void OnValidate()
        {
            BuildStyles();
        }

        private void BuildStyles()
        {
            _style = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                font = customFont,
                fontStyle = FontStyle.Normal
            };
            _style.normal.textColor = textColor;

            _shadowStyle = new GUIStyle(_style);
            _shadowStyle.normal.textColor = new Color(0.05f, 0.05f, 0.05f, 0.85f);
        }

        private void Update()
        {
            // Если мы в Главном Меню — расчёты кадров не проводим
            if (_inMainMenu)
            {
                return;
            }

            _frameCount++;
            _fpsTimer += Time.unscaledDeltaTime;

            if (_fpsTimer >= 1.0f)
            {
                _currentFps = _frameCount / _fpsTimer;
                _currentMsec = (_fpsTimer / _frameCount) * 1000.0f;
                _frameCount = 0;
                _fpsTimer = 0f;
            }

            _textTimer += Time.unscaledDeltaTime;
            if (_textTimer >= 0.25f)
            {
                _textTimer = 0f;
                Transform t = GetActivePlayerTransform();
                Vector3 p = t != null ? t.position : Vector3.zero;
                _textFps = $"{gameVersion} ({Mathf.RoundToInt(_currentFps)} fps, {Mathf.RoundToInt(_currentMsec)} ms)";
                _textPos = $"XYZ: {p.x:F1} / {p.y:F1} / {p.z:F1}";
            }
        }

        private Transform GetActivePlayerTransform()
        {
            if (playerTransform != null) return playerTransform;

            // 1. Приоритизируем локального сетевого игрока Mirror
            if (NetworkClient.localPlayer != null)
            {
                playerTransform = NetworkClient.localPlayer.transform;
                return playerTransform;
            }

            // 2. Ищем по тегу
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                return playerTransform;
            }

            // 3. Запасной вариант — главная камера
            if (Camera.main != null)
            {
                return Camera.main.transform;
            }

            return null;
        }

        private void OnGUI()
        {
            // Пропускаем отрисовку HUD, если активная сцена — Главное Меню
            if (_inMainMenu)
            {
                return;
            }
            if (RuntimeLoadingScreen.IsShown) return;

            if (_style == null || _style.font != customFont)
            {
                BuildStyles();
            }

            // Отрисовка FPS
            GUI.Label(new Rect(11, 11, 500, 25), _textFps, _shadowStyle);
            GUI.Label(new Rect(10, 10, 500, 25), _textFps, _style);

            // Отрисовка координат XYZ
            int yOffset = fontSize + 4;
            GUI.Label(new Rect(11, 11 + yOffset, 500, 25), _textPos, _shadowStyle);
            GUI.Label(new Rect(10, 10 + yOffset, 500, 25), _textPos, _style);
        }
    }
}