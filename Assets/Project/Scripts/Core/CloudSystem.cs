using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Management;

namespace MinecraftClone.Environment
{
    // Облака, сгенерированные кодом: блочные, с толщиной, плывут по ветру, привязаны к миру,
    // постепенно растворяются в тумане. Создаётся сам из WorldManager.
    public class CloudSystem : MonoBehaviour
    {
        [Header("Положение")]
        [Tooltip("Высота нижней грани облаков, в блоках")]
        [SerializeField] private float cloudHeight = 192f;
        [Tooltip("Толщина облаков, в блоках")]
        [SerializeField] private float thickness = 4f;

        [Header("Форма")]
        [Tooltip("Размер одной клетки облака в блоках (в Minecraft 12)")]
        [SerializeField] private float cellSize = 12f;
        [Tooltip("Размер облачных масс в клетках: больше значит крупнее облака")]
        [SerializeField] private float featureSize = 8f;
        [Tooltip("Доля неба под облаками: 0 нет облаков, 1 сплошная облачность")]
        [Range(0f, 1f)][SerializeField] private float coverage = 0.45f;
        [Tooltip("Зерно: другое число даёт другой рисунок облаков")]
        [SerializeField] private int seed = 1337;

        [Header("Движение")]
        [Tooltip("Скорость ветра, блоков в секунду (X, Z)")]
        [SerializeField] private Vector2 windSpeed = new Vector2(1.2f, 0.3f);

        [Header("Вид")]
        [SerializeField] private Color cloudColor = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("Во сколько раз дальше, чем туман земли, видны облака (они высоко, поэтому туман для них мягче)")]
        [SerializeField] private float fogDistanceMultiplier = 3f;

        private const float TopShade = 1.0f, BottomShade = 0.7f, SideXShade = 0.9f, SideZShade = 0.8f;

        private GameObject _go;
        private Mesh _mesh;
        private Material _mat;
        private Vector2 _wind;
        private int _bi = int.MinValue, _bj = int.MinValue, _bn = -1;
        private float _bsig = -1f;

        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<int> _t = new List<int>();

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            WorldManager wm = WorldManager.Instance;
            if (cam == null || wm == null || wm.Config == null)
            {
                if (_go != null) _go.SetActive(false);
                return;
            }

            if (_go == null && !Create()) { enabled = false; return; }
            _go.SetActive(true);

            _wind += windSpeed * Time.deltaTime;

            float maxEnd = wm.FogEnd * fogDistanceMultiplier;
            int n = Mathf.Clamp(Mathf.CeilToInt(maxEnd / cellSize) + 1, 3, 40);

            Vector3 cp = cam.transform.position;
            int pi = Mathf.FloorToInt((cp.x - _wind.x) / cellSize);
            int pj = Mathf.FloorToInt((cp.z - _wind.y) / cellSize);

            float sig = coverage * 1000f + featureSize * 31f + cellSize * 7f + thickness * 13f + seed;
            if (pi != _bi || pj != _bj || n != _bn || sig != _bsig)
            {
                Rebuild(pi, pj, n);
                _bi = pi; _bj = pj; _bn = n; _bsig = sig;
            }

            // сетка перестраивается только при смене клетки, между этим облака плавно едут целиком
            _go.transform.position = new Vector3(pi * cellSize + _wind.x, cloudHeight, pj * cellSize + _wind.y);

            // туман берём из настроек сцены: под водой и в лаве облака пропадают сами
            _mat.SetColor("_Color", cloudColor);
            _mat.SetColor("_FogColor", RenderSettings.fogColor);
            _mat.SetFloat("_FogStart", RenderSettings.fogStartDistance * fogDistanceMultiplier);
            _mat.SetFloat("_FogEnd", RenderSettings.fogEndDistance * fogDistanceMultiplier);
        }

        private bool Create()
        {
            Shader sh = Resources.Load<Shader>("Shaders/CodeClouds");
            if (sh == null) sh = Shader.Find("Custom/CodeClouds");
            if (sh == null)
            {
                Debug.LogError("[Clouds] Шейдер Custom/CodeClouds не найден. Файл должен лежать в Assets/Resources/Shaders/CodeClouds.shader");
                return false;
            }

            _mat = new Material(sh) { renderQueue = 3000 };
            _mesh = new Mesh { name = "Clouds", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

            _go = new GameObject("Clouds");
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return true;
        }

        // ---------- Шум ----------

        private float Hash(int x, int z)
        {
            uint h = (uint)x * 374761393u + (uint)z * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0x1000000;
        }

        private float ValueNoise(float x, float z)
        {
            int xi = Mathf.FloorToInt(x), zi = Mathf.FloorToInt(z);
            float fx = x - xi, fz = z - zi;
            fx = fx * fx * (3f - 2f * fx);
            fz = fz * fz * (3f - 2f * fz);
            float a = Mathf.Lerp(Hash(xi, zi), Hash(xi + 1, zi), fx);
            float b = Mathf.Lerp(Hash(xi, zi + 1), Hash(xi + 1, zi + 1), fx);
            return Mathf.Lerp(a, b, fz);
        }

        private bool IsCloud(int i, int j)
        {
            float f = Mathf.Max(1f, featureSize);
            float d = 0.65f * ValueNoise(i / f, j / f) + 0.35f * ValueNoise(i / (f * 0.45f) + 31.7f, j / (f * 0.45f) + 17.3f);
            return d > Mathf.Lerp(0.72f, 0.28f, coverage);
        }

        // ---------- Сетка ----------

        private void Rebuild(int pi, int pj, int n)
        {
            int size = 2 * n + 3;   // с рамкой в одну клетку, чтобы знать соседей
            var solid = new bool[size, size];
            for (int a = -n - 1; a <= n + 1; a++)
                for (int b = -n - 1; b <= n + 1; b++)
                    solid[a + n + 1, b + n + 1] = IsCloud(pi + a, pj + b);

            _v.Clear(); _c.Clear(); _t.Clear();
            float h = thickness;

            for (int a = -n; a <= n; a++)
                for (int b = -n; b <= n; b++)
                {
                    int ia = a + n + 1, ib = b + n + 1;
                    if (!solid[ia, ib]) continue;

                    float x0 = a * cellSize, x1 = x0 + cellSize;
                    float z0 = b * cellSize, z1 = z0 + cellSize;

                    // грани как в ChunkMeshBuilder
                    Face(new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, h, z0), TopShade);
                    Face(new Vector3(x0, 0, z1), new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), BottomShade);

                    if (!solid[ia, ib + 1]) Face(new Vector3(x1, 0, z1), new Vector3(x1, h, z1), new Vector3(x0, h, z1), new Vector3(x0, 0, z1), SideZShade);
                    if (!solid[ia, ib - 1]) Face(new Vector3(x0, 0, z0), new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, 0, z0), SideZShade);
                    if (!solid[ia + 1, ib]) Face(new Vector3(x1, 0, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x1, 0, z1), SideXShade);
                    if (!solid[ia - 1, ib]) Face(new Vector3(x0, 0, z1), new Vector3(x0, h, z1), new Vector3(x0, h, z0), new Vector3(x0, 0, z0), SideXShade);
                }

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        private void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float shade)
        {
            int i = _v.Count;
            byte s = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
            Color32 col = new Color32(s, s, s, 255);
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _c.Add(col); _c.Add(col); _c.Add(col); _c.Add(col);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        private void OnDestroy()
        {
            if (_go != null) Destroy(_go);
            if (_mesh != null) Destroy(_mesh);
            if (_mat != null) Destroy(_mat);
        }
    }
}