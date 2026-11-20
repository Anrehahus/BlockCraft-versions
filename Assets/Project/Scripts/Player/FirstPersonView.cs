using UnityEngine;
using Mirror;
using MinecraftClone.Management;
using MinecraftClone.Rendering;
using MinecraftClone.Items;
using MinecraftClone.Networking;

namespace MinecraftClone.Player
{
    // Вид от первого лица: рука справа внизу, покачивание при ходьбе и взмах при ударе.
    // Добавляется автоматически вместе с PlayerModel.
    public class FirstPersonView : MonoBehaviour
    {
        [Header("Покачивание при ходьбе")]
        [SerializeField] private float cameraBobX = 0.02f;
        [SerializeField] private float cameraBobY = 0.025f;
        [SerializeField] private float cameraRoll = 0.5f;     // градусы
        [SerializeField] private float armBobX = 0.02f;
        [SerializeField] private float armBobY = 0.025f;

        [Header("Предмет в руке (подгоните под вкус)")]
        [SerializeField] private Vector3 heldArmOffset = new Vector3(0.56f, -0.52f, 0.72f);   // позиция предмета, как в оригинале

        [Header("Рука (смещение относительно оригинала)")]
        [SerializeField] private Vector3 armOffset = Vector3.zero;

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
        private NetworkPlayer _netPlayer;
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
            _netPlayer = GetComponent<NetworkPlayer>();
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

        private void EnsureViewRoot()
        {
            if (_itemRoot == null)
            {
                _itemRoot = new GameObject("ViewRoot").transform;
                _itemRoot.SetParent(_cam.transform, false);
            }
            float k = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(heldReferenceFov * 0.5f * Mathf.Deg2Rad);
            _itemRoot.localScale = new Vector3(k, k, 1f);
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

            // рука или предмет в руке
            EnsureViewRoot();
            EnsureArm();
            if (_holder == null) return;

            int heldId = _interaction != null ? PlayerInventory.HotbarItem(_interaction.SelectedSlot) : 0;
            if (_netPlayer != null) _netPlayer.SetHeldItem(heldId);   // другие игроки увидят
            bool hasItem = BindHeldItem(heldId);
            _holder.gameObject.SetActive(!dead && !hasItem);

            float lightTarget = LightTint.Sample(_cam.transform.position);
            _handLight = _handLight < 0f ? lightTarget : Mathf.MoveTowards(_handLight, lightTarget, 4f * dt);
            if (_mat != null && Mathf.Abs(_handLight - _handApplied) > 0.005f)
            {
                _handApplied = _handLight;
                LightTint.SetColor(_mat, new Color(_handLight, _handLight, _handLight, 1f));
            }
            if (_itemMesh != null && Mathf.Abs(_handLight - _itemLightApplied) > 0.005f) ApplyItemLight();

            // прогресс взмаха
            _swingP = -1f;
            if (_swing >= 0f)
            {
                _swing += dt;
                float p = _swing / SwingDuration;
                if (p >= 1f) _swing = -1f; else _swingP = p;
            }
            float sp = Mathf.Max(_swingP, 0f);
            float sq = Mathf.Sqrt(sp);
            float sinSq = Mathf.Sin(sp * sp * Mathf.PI);        // sin(p^2 * pi)
            float sinR = Mathf.Sin(sq * Mathf.PI);              // sin(sqrt(p) * pi)
            float sin2 = Mathf.Sin(sq * Mathf.PI * 2f);         // sin(sqrt(p) * 2pi)
            float sinP = Mathf.Sin(sp * Mathf.PI);              // sin(p * pi)

            Vector3 bob = new Vector3(s * armBobX, -Mathf.Abs(c) * armBobY, 0f) * _amount;

            // рука: цепочка трансформаций оригинала
            Vector3 armBase = new Vector3(0.64f - 0.3f * sinR, 0.4f * sin2 - 0.6f, 0.72f + 0.4f * sinP);
            Quaternion aq = Quaternion.AngleAxis(-(45f + 70f * sinR), Vector3.up) * Quaternion.AngleAxis(-20f * sinSq, Vector3.forward);
            Quaternion ard = Quaternion.AngleAxis(120f, Vector3.forward) * Quaternion.AngleAxis(-200f, Vector3.right) * Quaternion.AngleAxis(135f, Vector3.up);
            Vector3 ainner = new Vector3(-1f, 3.6f, -3.5f) + ard * new Vector3(5.225f, 0.125f, 0f);
            _holder.localPosition = armBase + armOffset + bob + aq * ainner;
            _holder.localRotation = aq * ard * Quaternion.AngleAxis(180f, Vector3.forward);
            _holder.localScale = Vector3.one * 1.1111f;   // наш пиксель скина 1.8/32 м, в оригинале 1/16 м

            // предмет: поза и взмах оригинала
            if (_itemHolder != null)
            {
                Vector3 swingPos = new Vector3(-0.4f * sinR, 0.2f * sin2, 0.2f * sinP);
                Quaternion swingRot = Quaternion.AngleAxis(-(45f - 20f * sinSq), Vector3.up)
                                    * Quaternion.AngleAxis(-20f * sinR, Vector3.forward)
                                    * Quaternion.AngleAxis(80f * sinR, Vector3.right)
                                    * Quaternion.AngleAxis(45f, Vector3.up);

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
            _holder.SetParent(_itemRoot, false);

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

            var go = new GameObject("ViewItem");
            _itemHolder = go.transform;
            _itemHolder.SetParent(_itemRoot, false);
            _itemHolder.localScale = Vector3.one * (_itemFlat ? 0.68f : 0.4f);

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
            if (_itemHolder != null) Destroy(_itemHolder.gameObject);
            if (_itemMat != null) Destroy(_itemMat);
            if (_itemMesh != null) Destroy(_itemMesh);
            _itemHolder = null; _itemMat = null; _itemMesh = null;
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