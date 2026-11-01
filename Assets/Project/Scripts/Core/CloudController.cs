using UnityEngine;

namespace MinecraftClone.Environment
{
    public class CloudController : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private Transform playerTransform;
        [SerializeField] private MeshRenderer cloudRenderer;

        [Header("Настройки позиции")]
        [SerializeField] private float cloudHeight = 128f;

        [Header("Настройки шейдера (Динамические)")]
        [SerializeField] private Color cloudColor = new Color(1f, 1f, 1f, 0.8f);
        [SerializeField] private Vector2 windSpeed = new Vector2(0.01f, 0.005f);
        [SerializeField] private float tiling = 10f;

        private Material _cloudMaterial;

        private void Start()
        {
            FindPlayerCamera();

            if (cloudRenderer == null)
            {
                cloudRenderer = GetComponent<MeshRenderer>();
            }

            // Получаем экземпляр материала (инстанс), чтобы изменять его параметры из кода
            if (cloudRenderer != null)
            {
                _cloudMaterial = cloudRenderer.material;
            }
        }

        private void FindPlayerCamera()
        {
            if (playerTransform == null && Camera.main != null)
            {
                playerTransform = Camera.main.transform;
            }
        }

        private void LateUpdate()
        {
            // Попытка найти камеру/игрока, если ссылка не была найдена ранее или объект был пересоздан
            if (playerTransform == null)
            {
                FindPlayerCamera();
            }

            // 1. Движение облаков за игроком
            if (playerTransform != null)
            {
                Vector3 targetPos = playerTransform.position;
                transform.position = new Vector3(targetPos.x, cloudHeight, targetPos.z);
            }

            // 2. Изменение параметров шейдера в реальном времени
            if (_cloudMaterial != null)
            {
                // Имена параметров соответствуют именам в блоке Properties шейдера (_Color, _Speed, _Tiling)
                _cloudMaterial.SetColor("_Color", cloudColor);
                _cloudMaterial.SetVector("_Speed", new Vector4(windSpeed.x, windSpeed.y, 0f, 0f));
                _cloudMaterial.SetFloat("_Tiling", tiling);
            }
        }

        private void OnDestroy()
        {
            // Очищаем созданный инстанс материала во избежание утечки памяти
            if (_cloudMaterial != null)
            {
                Destroy(_cloudMaterial);
            }
        }
    }
}