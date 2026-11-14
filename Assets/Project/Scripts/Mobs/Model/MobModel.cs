using System.Collections.Generic;
using UnityEngine;

namespace MinecraftClone.Mobs
{
    // Строит модель моба из MobDataSO (части, текстура) и анимирует её. Только картинка, логики нет.
    // Добавляется на моба автоматически (Mob.OnStartClient).
    public class MobModel : MonoBehaviour
    {
        private const float FlashAmount = 0.7f;   // насколько белая вспышка (0..1)

        private struct Swing { public Transform T; public float Amp; public bool Wave; public float Rest; public bool Pose; public float AttackPitch; }

        private Mob _mob;
        private MobDataSO _data;
        private Transform _root;
        private Renderer[] _renderers;
        private Material _normalMat, _flashMat;
        private Texture2D _flashTex;
        private bool _flashing;
        private Material _hurtMat, _shown;
        private int _lastHurtCount;
        private float _hurtUntil;
        private const float HurtFlashTime = 0.25f;
        private float _deathTimer;
        private const float DeathFallTime = 0.6f;
        private readonly List<Swing> _swingA = new List<Swing>();
        private readonly List<Swing> _swingB = new List<Swing>();

        private struct Leg { public Transform T; public int Index; public float Side; }
        private readonly List<Leg> _legs = new List<Leg>();
        private static readonly float[] LegRestYaw = { 45f, 22.5f, -22.5f, -45f };
        private static readonly float[] LegRestRoll = { -45f, -33.3f, -33.3f, -45f };
        private static readonly float[] LegPhase = { 0f, Mathf.PI, Mathf.PI * 0.5f, Mathf.PI * 1.5f };

        private Vector3 _lastPos;
        private float _phase, _amount;

        private const float HitSwingDuration = 0.4f;   // длительность одного взмаха, секунды
        private float _swingTimer = -1f;
        private int _lastHitCount;
        private float _hitSwing;                       // добавка к углу рук во время удара, градусы
        private float _attackBlend;   // плавный переход в боевую позу (0..1)
        private bool _mirror;                      // зеркалить текстуру текущей части

        private Material _underMat, _woolMat, _underHurtMat, _woolHurtMat;
        private readonly List<Renderer> _underR = new List<Renderer>();
        private readonly List<Renderer> _woolR = new List<Renderer>();

        public void Build(Mob mob)
        {
            _mob = mob;
            _data = mob.Data;

            BuildMaterials();

            _root = new GameObject("Model").transform;
            _root.SetParent(transform, false);

            foreach (var p in _data.ModelParts)
            {
                Transform t = MakePart(_root, p);
                Renderer pr = t.GetComponent<Renderer>();
                if (p.layer == 1) _underR.Add(pr);
                else if (p.layer == 2) _woolR.Add(pr);

                if (p.anim == PartAnim.SwingA) _swingA.Add(new Swing { T = t, Amp = p.swingAmplitude, Wave = p.waveWhenAttacking, Rest = p.restPitch, Pose = p.poseWhenAttacking, AttackPitch = p.attackPitch });
                else if (p.anim == PartAnim.SwingB) _swingB.Add(new Swing { T = t, Amp = p.swingAmplitude, Wave = p.waveWhenAttacking, Rest = p.restPitch, Pose = p.poseWhenAttacking, AttackPitch = p.attackPitch });
                else if (p.anim == PartAnim.SpiderLeg)
                    _legs.Add(new Leg { T = t, Index = p.legIndex, Side = p.pivotPx.x >= 0f ? 1f : -1f });
            }

            var baseR = new List<Renderer>();
            foreach (var r in _root.GetComponentsInChildren<Renderer>())
                if (!_underR.Contains(r) && !_woolR.Contains(r)) baseR.Add(r);
            _renderers = baseR.ToArray();

            _lastPos = transform.position;
            _lastHitCount = mob.HitCount;
            _lastHurtCount = mob.HurtCount;
        }

        private void OnDestroy()
        {
            if (_hurtMat != null) Destroy(_hurtMat);
            if (_normalMat != null) Destroy(_normalMat);
            if (_flashMat != null) Destroy(_flashMat);
            if (_flashTex != null) Destroy(_flashTex);
            if (_underMat != null) Destroy(_underMat);
            if (_woolMat != null) Destroy(_woolMat);
            if (_underHurtMat != null) Destroy(_underHurtMat);
            if (_woolHurtMat != null) Destroy(_woolHurtMat);
        }

        // ---------- Материалы ----------

        private void BuildMaterials()
        {
            Shader sh = Resources.Load<Shader>("Shaders/MobUnlit");
            if (sh == null)
            {
                Debug.LogError("[MobModel] Не найден Resources/MobUnlit.shader");
                sh = Shader.Find("Unlit/Texture");
            }
            _normalMat = new Material(sh);
            _normalMat.SetFloat("_Cutoff", 0.5f);

            Texture2D skin = _data.Texture;
            if (skin != null)
            {
                skin.filterMode = FilterMode.Point;
                _normalMat.mainTexture = skin;
            }
            else
            {
                Debug.LogError($"[MobModel] У моба '{_data.Id}' не задана текстура");
                _normalMat.color = new Color(0.7f, 0.3f, 0.7f);
            }

            _hurtMat = new Material(_normalMat);
            _hurtMat.color = new Color(1f, 0.3f, 0.3f);

            _underMat = MakeOverlayMat(sh, _data.UndercoatTexture, out _underHurtMat);
            _woolMat = MakeOverlayMat(sh, _data.WoolTexture, out _woolHurtMat);

            if (_data.ChargeEffect == ChargeEffect.None) return;

            // Белая копия текстуры для вспышки. Умножением цвета не обойтись:
            // чёрное лицо осталось бы чёрным, поэтому осветляем сами пиксели.
            _flashMat = new Material(_normalMat);
            if (skin != null && skin.isReadable)
            {
                Color32[] px = skin.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    px[i].r = (byte)(px[i].r + (255 - px[i].r) * FlashAmount);
                    px[i].g = (byte)(px[i].g + (255 - px[i].g) * FlashAmount);
                    px[i].b = (byte)(px[i].b + (255 - px[i].b) * FlashAmount);
                }
                _flashTex = new Texture2D(skin.width, skin.height, TextureFormat.RGBA32, false);
                _flashTex.filterMode = FilterMode.Point;
                _flashTex.wrapMode = TextureWrapMode.Clamp;
                _flashTex.SetPixels32(px);
                _flashTex.Apply();
                _flashMat.mainTexture = _flashTex;
            }
            else
            {
                Debug.LogWarning($"[MobModel] У текстуры моба '{_data.Id}' выключен Read/Write, вспышка будет упрощённой.");
                _flashMat.color = new Color(3f, 3f, 3f, 1f);
            }
        }

        private static Material MakeOverlayMat(Shader sh, Texture2D tex, out Material hurt)
        {
            hurt = null;
            if (tex == null) return null;
            tex.filterMode = FilterMode.Point;
            var m = new Material(sh);
            m.SetFloat("_Cutoff", 0.5f);
            m.mainTexture = tex;
            hurt = new Material(m) { color = new Color(1f, 0.3f, 0.3f) };
            return m;
        }

        // ---------- Сборка части ----------

        private Rect UV(int px, int py, int w, int h)
        {
            float W = _data.Texture != null ? _data.Texture.width : 64f;
            float H = _data.Texture != null ? _data.Texture.height : 32f;
            return new Rect(px / W, 1f - (py + h) / H, w / W, h / H);
        }

        private Transform MakePart(Transform parent, ModelPartDef p)
        {
            float PX = _data.PixelSize;
            int u0 = p.uv.x, v0 = p.uv.y, w = p.size.x, h = p.size.y, d = p.size.z;

            GameObject go = new GameObject(p.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p.pivotPx * PX;

            float inf = p.inflate;
            Vector3 a = (p.minPx - Vector3.one * inf) * PX;
            Vector3 b = (p.minPx + new Vector3(w, h, d) + Vector3.one * inf) * PX;
            float x0 = a.x, y0 = a.y, z0 = a.z, x1 = b.x, y1 = b.y, z1 = b.z;

            // раскладка граней как в Minecraft
            Rect top = UV(u0 + d, v0, w, d);
            Rect bottom = UV(u0 + d + w, v0, w, d);
            Rect right = UV(u0, v0 + d, d, h);
            Rect front = UV(u0 + d, v0 + d, w, h);
            Rect left = UV(u0 + d + w, v0 + d, d, h);
            Rect back = UV(u0 + d + w + d, v0 + d, w, h);
            _mirror = p.mirror;
            if (_mirror) { Rect tmp = right; right = left; left = tmp; }

            var v = new List<Vector3>(24);
            var t = new List<Vector2>(24);
            var tr = new List<int>(36);

            Quad(v, t, tr, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), front);  // +Z перед
            Quad(v, t, tr, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), back);   // -Z зад
            Quad(v, t, tr, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), right);  // +X
            Quad(v, t, tr, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), left);   // -X
            Quad(v, t, tr, new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), top);    // верх
            Quad(v, t, tr, new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), bottom); // низ

            Mesh mesh = new Mesh { name = p.name };
            mesh.SetVertices(v);
            mesh.SetUVs(0, t);
            mesh.SetTriangles(tr, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _normalMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go.transform;
        }

        private void Quad(List<Vector3> v, List<Vector2> t, List<int> tr,
                            Vector3 bl, Vector3 br, Vector3 trg, Vector3 tl, Rect uv)
        {
            int i = v.Count;
            v.Add(bl); v.Add(tl); v.Add(trg); v.Add(br);

            float l = _mirror ? uv.xMax : uv.xMin;
            float r = _mirror ? uv.xMin : uv.xMax;
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
            if (_root == null) return;

            if (_mob.IsDying)
            {
                _deathTimer += Time.deltaTime;
                float f = Mathf.Min(1f, Mathf.Sqrt(_deathTimer / DeathFallTime));
                _root.localRotation = Quaternion.Euler(0f, 0f, -90f * f);   // ложится на бок
                UpdateMaterial();
                return;                                                     // ходьбу больше не анимируем
            }

            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                // ходьба: части качаются по пройденному пути (позиция приходит по сети)
                Vector3 delta = transform.position - _lastPos;
                _lastPos = transform.position;
                delta.y = 0f;

                float dist = delta.magnitude;
                float speed = dist / dt;
                if (speed > 20f) { dist = 0f; speed = 0f; }   // телепорт, а не ходьба

                _amount = Mathf.Lerp(_amount, Mathf.Clamp01(speed / 4f), 10f * dt);
                _phase += dist * 4f;
            }

            float s = Mathf.Cos(_phase * 0.6662f) * _amount * Mathf.Rad2Deg;

            foreach (var sw in _swingA) sw.T.localRotation = Quaternion.Euler(PartPitch(sw, s), 0f, 0f);

            _attackBlend = Mathf.MoveTowards(_attackBlend, _mob.IsAttacking ? 1f : 0f, 6f * dt);
            // на каждый новый удар моба запускаем один взмах рук
            int hits = _mob.HitCount;
            if (hits != _lastHitCount) { _lastHitCount = hits; _swingTimer = 0f; }

            _hitSwing = 0f;
            if (_swingTimer >= 0f)
            {
                _swingTimer += dt;
                float p = _swingTimer / HitSwingDuration;
                if (p >= 1f) _swingTimer = -1f;
                else _hitSwing = -Mathf.Sin(p * Mathf.PI) * 50f;   // руки вверх и обратно
            }

            foreach (var sw in _swingB) sw.T.localRotation = Quaternion.Euler(PartPitch(sw, -s), 0f, 0f);

            foreach (var lg in _legs)
            {
                float yawAnim = -(Mathf.Cos(_phase * 0.6662f * 2f + LegPhase[lg.Index]) * 0.4f) * _amount * Mathf.Rad2Deg;
                float rollAnim = Mathf.Abs(Mathf.Sin(_phase * 0.6662f + LegPhase[lg.Index]) * 0.4f) * _amount * Mathf.Rad2Deg;
                float yaw = lg.Side * (LegRestYaw[lg.Index] + yawAnim);
                float roll = lg.Side * (LegRestRoll[lg.Index] + rollAnim);
                lg.T.localRotation = Quaternion.AngleAxis(roll, Vector3.forward) * Quaternion.AngleAxis(yaw, Vector3.up);
            }

            if (_data.ChargeEffect == ChargeEffect.SwellAndFlash) UpdateSwell();

            UpdateMaterial();
        }

        private void UpdateMaterial()
        {
            int hurts = _mob.HurtCount;
            if (hurts != _lastHurtCount) { _lastHurtCount = hurts; _hurtUntil = Time.time + HurtFlashTime; }

            bool hurtNow = _mob.IsDying || Time.time < _hurtUntil;
            SetLayer(_underR, hurtNow ? _underHurtMat : _underMat, true);
            SetLayer(_woolR, hurtNow ? _woolHurtMat : _woolMat, !_mob.IsSheared);

            Material want = hurtNow ? _hurtMat
                    : (_flashing && _flashMat != null ? _flashMat : _normalMat);
            if (want == _shown) return;

            _shown = want;
            foreach (var r in _renderers)
                if (r != null) r.sharedMaterial = want;
        }

        private static void SetLayer(List<Renderer> list, Material m, bool on)
        {
            foreach (var r in list)
            {
                if (r == null) continue;
                r.enabled = on && m != null;
                if (m != null) r.sharedMaterial = m;
            }
        }

        private float PartPitch(Swing sw, float swing)
        {
            float pitch = sw.Rest + swing * sw.Amp;                     // поза покоя + ходьба
            if (sw.Pose) pitch = Mathf.Lerp(pitch, sw.AttackPitch, _attackBlend);        // стойка прицеливания
            if (sw.Wave) pitch += _hitSwing;                                            // взмах при ударе
            return pitch;
        }

        // надувание и вспышки, формулы из Minecraft
        private void UpdateSwell()
        {
            float sw = _mob.Charge01;
            float wobble = 1f + Mathf.Sin(sw * 100f) * sw * 0.01f;
            float f = sw * sw;
            f *= f;
            float xz = (1f + f * 0.4f) * wobble;
            float y = (1f + f * 0.1f) / wobble;
            _root.localScale = new Vector3(xz, y, xz);

            _flashing = sw > 0f && ((int)(sw * 10f) % 2) == 1;
        }
    }
}