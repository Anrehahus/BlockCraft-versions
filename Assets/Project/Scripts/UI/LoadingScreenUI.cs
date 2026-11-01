using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MinecraftClone.UI
{
    public class LoadingScreenUI : MonoBehaviour
    {
        public static LoadingScreenUI Instance { get; private set; }

        [Header("UI Элементы")]
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TextMeshProUGUI progressText;
        [SerializeField] private TextMeshProUGUI statusText;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Показывает или скрывает экран загрузки. При включении сбрасывает прогресс в 0.
        /// </summary>
        public void Show(bool visible, string initialStatus = "Загрузка мира...")
        {
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(visible);
            }

            if (visible)
            {
                SetProgress(0f, initialStatus);
            }
        }

        /// <summary>
        /// Обновляет шкалу и процент загрузки (значение progress от 0.0 до 1.0)
        /// </summary>
        public void SetProgress(float progress, string status = null)
        {
            float clampedProgress = Mathf.Clamp01(progress);

            if (progressBar != null)
            {
                progressBar.value = clampedProgress;
            }

            if (progressText != null)
            {
                progressText.text = $"{Mathf.RoundToInt(clampedProgress * 100f)}%";
            }

            if (!string.IsNullOrEmpty(status) && statusText != null)
            {
                statusText.text = status;
            }
        }
    }
}