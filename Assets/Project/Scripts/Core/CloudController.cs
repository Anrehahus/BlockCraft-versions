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

        [Header("Размер (генерируется кодом)")]
        [SerializeField] private float tileWorldSize = 768f;   // сколько блоков занимает один повтор рисунка облаков
        [SerializeField] private bool flipFacing = false;      // включите, если облака не видны снизу или сверху

        [Header("Настройки шейдера (Динамические)")]
        [SerializeField] private Color cloudColor = new Color(1f, 1f, 1f, 0.8f);
        [SerializeField] private Vector2 windSpeed = new Vector2(0.01f, 0.005f);

        private Material _cloudMaterial;
        private MeshFilter _meshFilter;
        private float _builtFar = -1f;
        private int _tiles = 1;

        private void Start()
        {
            FindPlayerCamera();

            if (cloudRenderer == null) cloudRenderer = GetComponent<MeshRenderer>();
            if (cloudRenderer != null) _cloudMaterial = cloudRenderer.material;

            _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null) _meshFilter = gameObject.AddComponent<MeshFilter>();

            // старая плоскость могла быть растянута масштабом: сбрасываем, размер задаёт код
            transform.localScale = Vector3.one;
            transform.rotation = Quaternion.identity;

            BuildPlane(GetFarDistance());
        }

        private void FindPlayerCamera()
        {
            if (playerTransform == null && Camera.main != null)
            {
                playerTransform = Camera.main.transform;
            }
        }

        private float GetFarDistance()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.farClipPlane : 1000f;
        }

        // плоскость на всю дальность камеры: целое число повторов рисунка, чтобы облака не растягивались
        private void BuildPlane(float far)
        {
            _builtFar = far;
            _tiles = Mathf.Max(2, Mathf.CeilToInt(far * 2f / tileWorldSize) + 1);
            float half = _tiles * tileWorldSize * 0.5f;

            var mesh = new Mesh { name = "CloudPlane" };
            mesh.SetVertices(new[]
            {
                new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half),
                new Vector3(half, 0f, half),   new Vector3(half, 0f, -half)
            });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            mesh.SetTriangles(flipFacing ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (_meshFilter.sharedMesh != null) Destroy(_meshFilter.sharedMesh);
            _meshFilter.sharedMesh = mesh;
        }

        private void LateUpdate()
        {
            // Попытка найти камеру/игрока, если ссылка не была найдена ранее или объект был пересоздан
            if (playerTransform == null)
            {
                FindPlayerCamera();
            }

            // 1. Облака следуют за игроком шагами по целому числу повторов рисунка: рисунок остаётся
            //    привязан к миру, а край плоскости всегда за пределами дальности камеры
            if (playerTransform != null)
            {
                Vector3 t = playerTransform.position;
                float step = tileWorldSize;
                transform.position = new Vector3(Mathf.Round(t.x / step) * step, cloudHeight, Mathf.Round(t.z / step) * step);
            }

            // на случай смены дальности камеры
            float far = GetFarDistance();
            if (!Mathf.Approximately(far, _builtFar)) BuildPlane(far);

            // 2. Параметры шейдера
            if (_cloudMaterial != null)
            {
                // Имена параметров соответствуют именам в блоке Properties шейдера (_Color, _Speed, _Tiling)
                _cloudMaterial.SetColor("_Color", cloudColor);
                _cloudMaterial.SetVector("_Speed", new Vector4(windSpeed.x, windSpeed.y, 0f, 0f));
                _cloudMaterial.SetFloat("_Tiling", _tiles);   // число повторов вдоль плоскости: размер облаков не зависит от размера плоскости
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