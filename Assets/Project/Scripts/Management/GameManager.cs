using System.Collections;
using UnityEngine;
using Mirror;
using MinecraftClone.UI;

namespace MinecraftClone.Management
{
    public class GameManager : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private GameObject mainMenuUI;

        public void StartSingleplayerGame()
        {
            StartCoroutine(LoadWorldSequence());
        }

        private IEnumerator LoadWorldSequence()
        {
            // 1. Скрываем Главное Меню
            if (mainMenuUI != null)
                mainMenuUI.SetActive(false);

            // 2. Если используется Mirror, запускаем локальный хост
            if (NetworkManager.singleton != null && !NetworkServer.active)
            {
                NetworkManager.singleton.StartHost();
            }

            // 3. Запускаем генерацию через WorldManager (он сам создаст/переместит игрока и разморозит его)
            if (WorldManager.Instance != null)
            {
                yield return WorldManager.Instance.PrepareInitialSpawnArea(Vector3.zero);
            }
            else
            {
                Debug.LogError("[GameManager] WorldManager.Instance не найден на сцене!");
            }
        }
    }
}