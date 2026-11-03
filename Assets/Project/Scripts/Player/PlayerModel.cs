using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MinecraftClone.Networking;
using MinecraftClone.Management;
using MinecraftClone.Audio;
using MinecraftClone.Core;

namespace MinecraftClone.Player
{
    public class PlayerModel : MonoBehaviour
    {
        [SerializeField] private Texture2D skin;            // пусто = Resources/Skins/steve
        [SerializeField] private Material baseMaterial;     // необязательно: свой материал
        [SerializeField] private float yOffset = 0f;        // если ноги висят над землёй или проваливаются

        private const float PX = 1.8f / 32f;                // размер одного пикселя скина в метрах

        private Transform _head, _rightArm, _leftArm, _rightLeg, _leftLeg;
        private Renderer[] _renderers;
        private Material _material;
        private NetworkIdentity _identity;
        private NetworkPlayer _netPlayer;
        private Camera _playerCamera;

        private float _stepDist;
        private Vector3 _lastPos;
        private float _phase, _amount, _pitch;
        private bool _visible = true;

        private float _swingTimer = -1f;
        private const float SwingDuration = 0.3f;

        public void Swing() { _swingTimer = 0f; }

        public void SetSkin(Texture2D tex)
        {

            Debug.Log("[Skin] SetSkin, tex=" + (tex != null) + ", modelBuilt=" + (_head != null));
            if (tex == null) return;
            skin = tex;
            if (_head == null) return;          // модель ещё не построена, Start возьмёт skin сам

            if (_material != null) Destroy(_material);
            Transform old = transform.Find("Model");
            if (old != null) { old.name = "Model_old"; old.gameObject.SetActive(false); Destroy(old.gameObject); }

            BuildMaterial();
            BuildModel();
            _visible = true;                    // чтобы UpdateVisibility заново применил видимость
        }

        private void Start()
        {
            _identity = GetComponent<NetworkIdentity>();
            _netPlayer = GetComponent<NetworkPlayer>();
            _playerCamera = GetComponentInChildren<Camera>(true);

            if (skin == null) skin = Resources.Load<Texture2D>("Skins/steve");

            BuildMaterial();
            Debug.Log("[PM] shader=" + _material.shader.name);
            BuildModel();
            _lastPos = transform.position;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        // ---------- Материал ----------

        private void BuildMaterial()
        {
            if (baseMaterial != null)
            {
                _material = new Material(baseMaterial);
            }
            else
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Texture");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                _material = new Material(sh);
            }

            if (skin != null)
            {
                skin.filterMode = FilterMode.Point;
                _material.mainTexture = skin;
            }
            else
            {
                _material.color = new Color(0.3f, 0.5f, 0.8f);
            }
        }

        // ---------- Сборка модели ----------

        private void BuildModel()
        {
            Transform root = new GameObject("Model").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(0f, yOffset, 0f);

            bool oldFormat = skin != null && skin.height == 32;

            //                          имя        pivot (px)            u0  v0   w  h  d   min (px от pivot)
            _head = MakePart(root, "Head", new Vector3(0, 24, 0), 0, 0, 8, 8, 8, new Vector3(-4, 0, -4), false);
            MakePart(root, "Body", new Vector3(0, 24, 0), 16, 16, 8, 12, 4, new Vector3(-4, -12, -2), false);
            _rightArm = MakePart(root, "RightArm", new Vector3(6, 22, 0), 40, 16, 4, 12, 4, new Vector3(-2, -10, -2), false);
            _leftArm = oldFormat
                ? MakePart(root, "LeftArm", new Vector3(-6, 22, 0), 40, 16, 4, 12, 4, new Vector3(-2, -10, -2), true)
                : MakePart(root, "LeftArm", new Vector3(-6, 22, 0), 32, 48, 4, 12, 4, new Vector3(-2, -10, -2), false);
            _rightLeg = MakePart(root, "RightLeg", new Vector3(2, 12, 0), 0, 16, 4, 12, 4, new Vector3(-2, -12, -2), false);
            _leftLeg = oldFormat
                ? MakePart(root, "LeftLeg", new Vector3(-2, 12, 0), 0, 16, 4, 12, 4, new Vector3(-2, -12, -2), true)
                : MakePart(root, "LeftLeg", new Vector3(-2, 12, 0), 16, 48, 4, 12, 4, new Vector3(-2, -12, -2), false);

            _renderers = root.GetComponentsInChildren<Renderer>();
        }

        private Rect UV(int px, int py, int w, int h)
        {
            float W = skin != null ? skin.width : 64f;
            float H = skin != null ? skin.height : 64f;
            return new Rect(px / W, 1f - (py + h) / H, w / W, h / H);
        }

        private Transform MakePart(Transform parent, string partName, Vector3 pivotPx,
                                   int u0, int v0, int w, int h, int d, Vector3 minPx, bool mirror)
        {
            GameObject go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pivotPx * PX;

            Vector3 a = minPx * PX;
            Vector3 b = (minPx + new Vector3(w, h, d)) * PX;
            float x0 = a.x, y0 = a.y, z0 = a.z, x1 = b.x, y1 = b.y, z1 = b.z;

            Rect top = UV(u0 + d, v0, w, d);
            Rect bottom = UV(u0 + d + w, v0, w, d);
            Rect right = UV(u0, v0 + d, d, h);
            Rect front = UV(u0 + d, v0 + d, w, h);
            Rect left = UV(u0 + d + w, v0 + d, d, h);
            Rect back = UV(u0 + d + w + d, v0 + d, w, h);

            if (mirror) { Rect tmp = right; right = left; left = tmp; }

            var v = new List<Vector3>(24);
            var t = new List<Vector2>(24);
            var tr = new List<int>(36);

            // порядок углов: низ-лево, низ-право, верх-право, верх-лево (если смотреть на грань снаружи)
            Quad(v, t, tr, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), front, mirror); // +Z перед
            Quad(v, t, tr, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), back, mirror); // -Z зад
            Quad(v, t, tr, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), right, mirror); // +X правая сторона
            Quad(v, t, tr, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), left, mirror); // -X левая сторона
            Quad(v, t, tr, new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), top, mirror); // верх
            Quad(v, t, tr, new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), bottom, mirror); // низ

            Mesh mesh = new Mesh { name = partName };
            mesh.SetVertices(v);
            mesh.SetUVs(0, t);
            mesh.SetTriangles(tr, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go.transform;
        }

        private static void Quad(List<Vector3> v, List<Vector2> t, List<int> tr,
                                 Vector3 bl, Vector3 br, Vector3 trg, Vector3 tl, Rect uv, bool flip)
        {
            int i = v.Count;
            v.Add(bl); v.Add(tl); v.Add(trg); v.Add(br);

            float l = flip ? uv.xMax : uv.xMin;
            float r = flip ? uv.xMin : uv.xMax;
            t.Add(new Vector2(l, uv.yMin));
            t.Add(new Vector2(l, uv.yMax));
            t.Add(new Vector2(r, uv.yMax));
            t.Add(new Vector2(r, uv.yMin));

            tr.Add(i); tr.Add(i + 1); tr.Add(i + 2);
            tr.Add(i); tr.Add(i + 2); tr.Add(i + 3);
        }

        // ---------- Анимация ----------

        private void LateUpdate()
        {
            if (_head == null) return;

            UpdateVisibility();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - _lastPos;
            _lastPos = transform.position;
            delta.y = 0f;

            float dist = delta.magnitude;
            float speed = dist / dt;
            if (speed > 20f) { dist = 0f; speed = 0f; } // телепорт, а не ходьба

            _amount = Mathf.Lerp(_amount, Mathf.Clamp01(speed / 4.3f), 10f * dt);
            _phase += dist * 4f;
            UpdateFootsteps(dist);

            float swing = Mathf.Cos(_phase * 0.6662f);
            float leg = swing * 1.4f * _amount * Mathf.Rad2Deg;
            float arm = swing * 1.0f * _amount * Mathf.Rad2Deg;

            _rightLeg.localRotation = Quaternion.Euler(leg, 0f, 0f);
            _leftLeg.localRotation = Quaternion.Euler(-leg, 0f, 0f);
            _leftArm.localRotation = Quaternion.Euler(arm, 0f, 0f);

            float armX = -arm, armY = 0f;
            if (_swingTimer >= 0f)
            {
                _swingTimer += dt;
                float p = _swingTimer / SwingDuration;
                if (p >= 1f) _swingTimer = -1f;
                else
                {
                    float f = 1f - p;
                    f = 1f - f * f * f * f;
                    armX -= (Mathf.Sin(f * Mathf.PI) * 1.2f + Mathf.Sin(p * Mathf.PI) * 0.4f) * Mathf.Rad2Deg;
                    armY = -Mathf.Sin(Mathf.Sqrt(p) * Mathf.PI) * 0.2f * Mathf.Rad2Deg;
                }
            }
            _rightArm.localRotation = Quaternion.Euler(armX, armY, 0f);

            float targetPitch = 0f;
            if (_playerCamera != null)
            {
                targetPitch = _playerCamera.transform.localEulerAngles.x;
                if (targetPitch > 180f) targetPitch -= 360f;
            }

            _pitch = Mathf.Lerp(_pitch, targetPitch, 15f * dt);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Свою модель от первого лица не показываем, чужие показываем
        private void UpdateVisibility()
        {
            bool own = _identity == null || _identity.netId == 0 || _identity.isLocalPlayer;
            bool show = !own;
            if (show == _visible) return;

            _visible = show;
            foreach (var r in _renderers)
                if (r != null) r.enabled = show;
        }

        private void UpdateFootsteps(float dist)
        {
            if (dist <= 0f || WorldManager.Instance == null) return;

            Vector3 p = transform.position;
            float fy = p.y - Mathf.Floor(p.y);
            bool grounded = fy < 0.2f || fy > 0.97f;   // ноги на уровне верха блока
            if (!grounded) return;

            _stepDist += dist;
            if (_stepDist < 1.8f) return;
            _stepDist = 0f;

            BlockType below = WorldManager.Instance.GetBlockAtWorldPosition(
                Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y - 0.25f), Mathf.FloorToInt(p.z));

            BlockSounds.Play(below, p, 0.3f, 1.0f);
        }
    }
}