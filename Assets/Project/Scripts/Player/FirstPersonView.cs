using UnityEngine;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Rendering;
using MinecraftClone.Items;

namespace MinecraftClone.Player
{
    // Вид от первого лица: рука справа внизу, покачивание при ходьбе и взмах при ударе.
    // Добавляется автоматически вместе с PlayerModel.
    public class FirstPersonView : MonoBehaviour
    {
        [Header("Рука (подгоните под вкус)")]
        [SerializeField] private Vector3 armPosition = new Vector3(0.22f, -0.48f, 0.28f);
        [SerializeField] private Vector3 armRotation = new Vector3(-150f, -15f, 0f);
        [SerializeField] private float armScale = 1f;

        [Header("Покачивание при ходьбе")]
        [SerializeField] private float cameraBobX = 0.02f;
        [SerializeField] private float cameraBobY = 0.025f;
        [SerializeField] private float cameraRoll = 0.5f;     // градусы
        [SerializeField] private float armBobX = 0.02f;
        [SerializeField] private float armBobY = 0.025f;

        [Header("Предмет в руке (подгоните под вкус)")]
        [SerializeField] private Vector3 heldArmOffset = new Vector3(0.56f, -0.52f, 0.72f);   // позиция предмета, как в оригинале

        [SerializeField] private float heldReferenceFov = 70f;   // FOV, для которого рассчитана поза (как в оригинале)
        private Transform _itemRoot;

        private float _swingP = -1f;   // прогресс взмаха 0..1, -1 = нет

        private PlayerBlockInteraction _interaction;
        private Transform _itemHolder;
        private Material _itemMat;
        private Mesh _itemMesh;
        private Color32[] _itemBase, _itemTmp;
        private int _itemId;
        private bool _itemFlat;
        private float _itemLightApplied = -1f;

        private const float SwingDuration = 0.3f;

        private bool _itemShaderMissing;
        private PlayerModel _model;
        private PlayerHealth _health;
        private NetworkIdentity _identity;
        private CharacterController _controller;
        private Camera _cam;

        private Vector3 _camBasePos;
        private bool _hasBase;

        private Transform _holder;
        private Mesh _srcMesh;
        private Material _mat;
        private bool _shaderMissing;

        private Vector3 _lastPos;
        private float _phase, _amount;
        private float _swing = -1f;

        private float _handLight = -1f, _handApplied = -1f;

        public void Swing() { _swing = 0f; }

        private bool IsOwn =>
            _identity == null || _identity.netId == 0 || _identity.isLocalPlayer
            || (NetworkServer.active && _identity.connectionToClient == NetworkServer.localConnection);

        private void Start()
        {
            _model = GetComponent<PlayerModel>();
            _health = GetComponent<PlayerHealth>();
            _identity = GetComponent<NetworkIdentity>();
            _controller = GetComponent<CharacterController>();
            _cam = GetComponentInChildren<Camera>(true);
            _lastPos = transform.position;
            _interaction = GetComponent<PlayerBlockInteraction>();
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            ClearHeldItem();
        }

        private void LateUpdate()
        {
            if (!IsOwn || _cam == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!_hasBase) { _camBasePos = _cam.transform.localPosition; _hasBase = true; }

            bool dead = _health != null && _health.IsDead;

            // ходьба
            Vector3 delta = transform.position - _lastPos;
            _lastPos = transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            float speed = dist / dt;
            if (speed > 20f) { dist = 0f; speed = 0f; }   // телепорт, а не ходьба

            bool walking = !dead && _controller != null && _controller.enabled && _controller.isGrounded && speed > 0.3f;
            float target = walking ? Mathf.Clamp01(speed / 4.3f) : 0f;
            _amount = Mathf.Lerp(_amount, target, 10f * dt);
            if (walking) _phase += dist * 2.8f;

            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);

            // камера
            if (!dead)
            {
                float camAmount = GameSettings.ViewBobbing ? _amount : 0f;   // настройка влияет только на камеру
                _cam.transform.localPosition = _camBasePos + new Vector3(s * cameraBobX, -Mathf.Abs(c) * cameraBobY, 0f) * camAmount;
                float px = Mathf.DeltaAngle(0f, _cam.transform.localEulerAngles.x);
                _cam.transform.localRotation = Quaternion.Euler(px, 0f, s * cameraRoll * camAmount);
            }

            // рука
            // рука или предмет в руке
            EnsureArm();
            if (_holder == null) return;

            int heldId = _interaction != null ? PlayerInventory.HotbarItem(_interaction.SelectedSlot) : 0;
            bool hasItem = BindHeldItem(heldId);
            _holder.gameObject.SetActive(!dead && !hasItem);   // есть предмет: руки нет, пусто: опять рука

            float lightTarget = LightTint.Sample(_cam.transform.position);
            _handLight = _handLight < 0f ? lightTarget : Mathf.MoveTowards(_handLight, lightTarget, 4f * dt);
            if (_mat != null && Mathf.Abs(_handLight - _handApplied) > 0.005f)
            {
                _handApplied = _handLight;
                LightTint.SetColor(_mat, new Color(_handLight, _handLight, _handLight, 1f));
            }
            if (_itemMesh != null && Mathf.Abs(_handLight - _itemLightApplied) > 0.005f) ApplyItemLight();

            Vector3 bob = new Vector3(s * armBobX, -Mathf.Abs(c) * armBobY, 0f) * _amount;
            Vector3 pos = armPosition + bob;
            Vector3 rot = armRotation;
            _swingP = -1f;

            if (_swing >= 0f)
            {
                _swing += dt;
                float p = _swing / SwingDuration;
                if (p >= 1f) _swing = -1f;
                else
                {
                    _swingP = p;
                    float k = Mathf.Sin(p * Mathf.PI);
                    rot.x += k * 50f;                                         // взмах руки
                    pos += new Vector3(-0.06f * k, -0.04f * k, 0.12f * k);
                }
            }

            _holder.localPosition = pos;
            _holder.localRotation = Quaternion.Euler(rot);

            // предмет: взмах как в оригинале (сдвиг влево и вперёд + наклон верхом вперёд)
            if (_itemHolder != null)
            {
                // проекция как у оригинала (FOV 70): растягиваем x и y пропорционально FOV камеры
                float k = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(heldReferenceFov * 0.5f * Mathf.Deg2Rad);
                _itemRoot.localScale = new Vector3(k, k, 1f);

                float sp = Mathf.Max(_swingP, 0f);
                float f = Mathf.Sin(sp * sp * Mathf.PI);
                float f1 = Mathf.Sin(Mathf.Sqrt(sp) * Mathf.PI);

                // взмах: сдвиг влево, вверх-вниз и вперёд
                Vector3 swingPos = new Vector3(-0.4f * f1, 0.2f * Mathf.Sin(Mathf.Sqrt(sp) * Mathf.PI * 2f), 0.2f * Mathf.Sin(sp * Mathf.PI));
                // взмах: наклон вокруг предмета (как в оригинале, 45° - 20° * f, -20° по Z, -80° по X)
                Quaternion swingRot = Quaternion.AngleAxis(-(45f - 20f * f), Vector3.up)
                                    * Quaternion.AngleAxis(-20f * f1, Vector3.forward)
                                    * Quaternion.AngleAxis(80f * f1, Vector3.right)
                                    * Quaternion.AngleAxis(45f, Vector3.up);

                // поза предмета в руке: инструменты и еда боком (Y 90, Z 25), блоки с поворотом 45°
                Vector3 dispPos;
                Quaternion dispRot;
                if (_itemFlat)
                {
                    dispPos = new Vector3(0.0706f, 0.2f, -0.0706f);
                    dispRot = Quaternion.AngleAxis(90f, Vector3.up) * Quaternion.AngleAxis(25f, Vector3.forward);
                }
                else
                {
                    dispPos = Vector3.zero;
                    dispRot = Quaternion.AngleAxis(-45f, Vector3.up);
                }

                _itemHolder.localPosition = heldArmOffset + bob + swingPos + swingRot * dispPos;
                _itemHolder.localRotation = swingRot * dispRot;
                _itemHolder.gameObject.SetActive(!dead);
            }
        }

        // Рука = копия правой руки модели с отдельным материалом, который рисуется поверх мира
        private void EnsureArm()
        {
            if (_model == null || _shaderMissing) return;

            Transform src = _model.RightArm;
            if (src == null) return;
            MeshFilter smf = src.GetComponent<MeshFilter>();
            if (smf == null || smf.sharedMesh == null) return;
            if (_holder != null && smf.sharedMesh == _srcMesh) return;   // уже построена и скин не менялся

            if (_holder != null) Destroy(_holder.gameObject);
            if (_mat != null) Destroy(_mat);

            Shader sh = Resources.Load<Shader>("Shaders/ViewModel");
            if (sh == null) sh = Shader.Find("Minecraft/ViewModel");
            if (sh == null)
            {
                _shaderMissing = true;
                Debug.LogWarning("[FirstPersonView] Не найден шейдер Resources/Shaders/ViewModel.shader");
                return;
            }

            _mat = new Material(sh);
            _handApplied = -1f;
            Texture2D skin = _model.Skin;
            if (skin != null) { skin.filterMode = FilterMode.Point; _mat.mainTexture = skin; }

            _srcMesh = smf.sharedMesh;

            GameObject go = new GameObject("ViewArm");
            _holder = go.transform;
            _holder.SetParent(_cam.transform, false);
            _holder.localScale = Vector3.one * armScale;

            go.AddComponent<MeshFilter>().sharedMesh = _srcMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // true, если предмет показан (тогда руку прячем). Атлас ещё не готов: остаётся рука.
        private bool BindHeldItem(int id)
        {
            if (id <= 0) { ClearHeldItem(); return false; }
            if (id == _itemId && _itemHolder != null) return true;

            var sys = ItemDropSystem.Instance;
            var wm = WorldManager.Instance;
            Mesh src = sys != null ? sys.GetMesh(id) : null;
            if (src == null || wm == null || wm.BlocksMaterial == null) { ClearHeldItem(); return false; }

            if (_itemShaderMissing) return false;
            Shader sh = Resources.Load<Shader>("Shaders/HeldItem");
            if (sh == null) sh = Shader.Find("Minecraft/HeldItem");
            if (sh == null)
            {
                _itemShaderMissing = true;
                Debug.LogWarning("[FirstPersonView] Не найден шейдер Resources/Shaders/HeldItem.shader");
                return false;
            }
            Texture atlas = wm.BlocksMaterial.mainTexture;
            if (atlas == null && wm.BlocksMaterial.HasProperty("_BaseMap")) atlas = wm.BlocksMaterial.GetTexture("_BaseMap");
            if (atlas == null) return false;

            ClearHeldItem();
            _itemId = id;
            _itemFlat = sys.IsFlat(id);

            var root = new GameObject("ViewItemRoot");
            _itemRoot = root.transform;
            _itemRoot.SetParent(_cam.transform, false);

            var go = new GameObject("ViewItem");
            _itemHolder = go.transform;
            _itemHolder.SetParent(_itemRoot, false);
            _itemHolder.localScale = Vector3.one * (_itemFlat ? 0.68f : 0.4f);   // масштабы оригинала

            _itemMesh = Instantiate(src);
            _itemBase = src.colors32;
            _itemTmp = new Color32[_itemBase.Length];
            _itemLightApplied = -1f;

            _itemMat = new Material(sh);
            _itemMat.mainTexture = atlas;

            go.AddComponent<MeshFilter>().sharedMesh = _itemMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _itemMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return true;
        }

        private void ClearHeldItem()
        {
            if (_itemRoot != null) Destroy(_itemRoot.gameObject);   // вместе с предметом
            if (_itemMat != null) Destroy(_itemMat);
            if (_itemMesh != null) Destroy(_itemMesh);
            _itemRoot = null; _itemHolder = null; _itemMat = null; _itemMesh = null;
            _itemId = 0;
        }

        // освещённость как у руки: затемняем цвета вершин
        private void ApplyItemLight()
        {
            _itemLightApplied = _handLight;
            for (int i = 0; i < _itemBase.Length; i++)
            {
                Color32 b = _itemBase[i];
                _itemTmp[i] = new Color32((byte)(b.r * _handLight + 0.5f), (byte)(b.g * _handLight + 0.5f), (byte)(b.b * _handLight + 0.5f), 255);
            }
            _itemMesh.colors32 = _itemTmp;
        }
    }
}