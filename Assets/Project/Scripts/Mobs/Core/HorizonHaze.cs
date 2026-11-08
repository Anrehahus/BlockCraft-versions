using UnityEngine;
using MinecraftClone.Management;

namespace MinecraftClone.Effects
{
    // Дымка у горизонта: цилиндр вокруг камеры. Внизу цвета тумана, выше плавно становится прозрачным.
    // Закрывает пустоту за краем прогруженного мира и убирает резкую полосу между туманом и небом.
    public class HorizonHaze : MonoBehaviour
    {
        [SerializeField] private float radiusFactor = 1.8f;   // радиус относительно дальности тумана (больше 1.42, чтобы закрыть углы)
        [SerializeField] private float hazeHeight = 0.3f;     // высота дымки относительно радиуса (0.3 примерно 17° над горизонтом)

        private Transform _dome;
        private Material _mat;

        private void Start()
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) { enabled = false; return; }

            _mat = new Material(sh) { renderQueue = 2900 };   // после непрозрачного, до воды и облаков

            var go = new GameObject("HorizonHaze");
            _dome = go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = BuildMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void LateUpdate()
        {
            if (_dome == null) return;

            WorldManager wm = WorldManager.Instance;
            if (wm == null || wm.Config == null) { _dome.gameObject.SetActive(false); return; }
            _dome.gameObject.SetActive(true);

            float radius = wm.FogEnd * radiusFactor;
            _dome.position = transform.position;
            _dome.localScale = Vector3.one * radius;

            // цвет следует за туманом (под водой и в лаве тоже)
            Color fog = RenderSettings.fogColor;
            _mat.color = new Color(fog.r, fog.g, fog.b, 1f);
        }

        private Mesh BuildMesh()
        {
            const int seg = 48;
            // кольца: высота (в радиусах) и прозрачность
            float[] ys = { -2f, 0f, hazeHeight * 0.4f, hazeHeight };
            float[] al = { 1f, 1f, 0.55f, 0f };

            var v = new Vector3[ys.Length * (seg + 1)];
            var c = new Color32[v.Length];
            for (int r = 0; r < ys.Length; r++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    int idx = r * (seg + 1) + i;
                    v[idx] = new Vector3(Mathf.Cos(a), ys[r], Mathf.Sin(a));
                    c[idx] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(al[r] * 255f));
                }

            var t = new System.Collections.Generic.List<int>();
            for (int r = 0; r < ys.Length - 1; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = r * (seg + 1) + i, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(b1);
                    t.Add(a0); t.Add(b1); t.Add(a1);
                }

            var mesh = new Mesh { name = "HorizonHaze" };
            mesh.vertices = v;
            mesh.colors32 = c;
            mesh.SetTriangles(t, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(4f, 8f, 4f));
            return mesh;
        }

        private void OnDestroy()
        {
            if (_dome != null) Destroy(_dome.gameObject);
            if (_mat != null) Destroy(_mat);
        }
    }
}