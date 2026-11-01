using UnityEngine;
using MinecraftClone.Management;

namespace MinecraftClone.Player
{
    public class PlayerSpawner : MonoBehaviour
    {
        public static PlayerSpawner Instance { get; private set; }

        [Header("Префаб игрока")]
        [SerializeField] private GameObject playerPrefab;

        private Transform playerTransform;
        public Transform PlayerTransform => playerTransform;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Спавнит игрока или перемещает его в найденную точу
        /// </summary>
        public Transform SpawnOrMovePlayer(Vector3 spawnPosition)
        {
            if (playerTransform == null)
            {
                if (playerPrefab == null)
                {
                    Debug.LogError("[PlayerSpawner] Не назначен Player Prefab!");
                    return null;
                }

                GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                playerTransform = playerInstance.transform;
            }
            else
            {
                SetPlayerPosition(spawnPosition);
            }

            return playerTransform;
        }

        /// <summary>
        /// Безопасное перемещение игрока (выключает контроллеры на время телепорта)
        /// </summary>
        public void SetPlayerPosition(Vector3 targetPos)
        {
            if (playerTransform == null) return;

            CharacterController controller = playerTransform.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            }

            playerTransform.position = targetPos;

            if (controller != null) controller.enabled = true;
            if (rb != null) rb.isKinematic = false;
        }
    }
}