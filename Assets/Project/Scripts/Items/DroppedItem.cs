using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;

namespace MinecraftClone.Items
{
    // Один выпавший предмет: маленький вращающийся блок, который падает и лежит на земле.
    // Физика простая, без Rigidbody: проверка клеток мира, как у FallingBlockEntity.
    public class DroppedItem : MonoBehaviour
    {
        public const float Half = 0.125f;        // половина размера для столкновений
        private const float Gravity = 20f;
        private const float MaxFall = 40f;
        private const float Lifetime = 300f;     // 5 минут, потом исчезает
        public const float PickupDelay = 0.6f;   // сразу после выпадения подобрать нельзя

        public int Id { get; private set; }
        public BlockType Type { get; private set; }
        public bool Authoritative { get; private set; }   // true на сервере и в одиночной игре
        public Vector3 Position => _pos;
        public float Age => _age;

        // клиент не шлёт повторный запрос на подбор, пока ждёт ответ сервера
        public bool CanBePickedUp => _age >= PickupDelay && Time.time >= _claimedUntil;
        public void MarkClaimed() { _claimedUntil = Time.time + 1f; }

        public void GetState(out Vector3 pos, out Vector3 vel) { pos = _pos; vel = _vel; }
        public void RestoreAge(float age) { _age = Mathf.Max(0f, age); }

        private ItemDropSystem _sys;
        private Transform _visual;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Vector3 _pos, _vel;
        private float _age, _phase, _claimedUntil;
        private bool _onGround;

        public void Init(ItemDropSystem sys, int id, BlockType type, Vector3 pos, Vector3 vel, bool authoritative)
        {
            _sys = sys; Id = id; Type = type; Authoritative = authoritative;
            _pos = pos; _vel = vel;
            _phase = Random.value * Mathf.PI * 2f;
            transform.position = pos;

            var vgo = new GameObject("Visual");
            _visual = vgo.transform;
            _visual.SetParent(transform, false);

            _mf = vgo.AddComponent<MeshFilter>();
            _mr = vgo.AddComponent<MeshRenderer>();
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            TryBindVisual();

            _visual.localScale = Vector3.one * ((type.IsPlant() || type.IsSign()) ? 0.4f : 0.25f);
        }

        // Предмет мог прийти по сети раньше, чем мир подготовил атлас текстур: добираем меш позже
        private void TryBindVisual()
        {
            if (_mf.sharedMesh == null) _mf.sharedMesh = _sys.GetMesh(Type);
            if (_mr.sharedMaterial == null && WorldManager.Instance != null)
                _mr.sharedMaterial = WorldManager.Instance.BlocksMaterial;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _age += dt;

            if (_mf.sharedMesh == null || _mr.sharedMaterial == null) TryBindVisual();

            if (_age > Lifetime)
            {
                if (Authoritative) _sys.ServerRemove(Id);               // сервер убирает у всех
                else if (_age > Lifetime + 10f) Destroy(gameObject);    // страховка, если сообщение потерялось
                return;
            }

            // пока чанк под предметом не загружен, предмет замирает, иначе он провалится в пустоту
            if (_sys.IsChunkLoaded(Mathf.FloorToInt(_pos.x), Mathf.FloorToInt(_pos.z)))
                Simulate(dt);

            if (_pos.y < -20f)
            {
                if (Authoritative) _sys.ServerRemove(Id); else Destroy(gameObject);
                return;
            }

            transform.position = _pos;
            _visual.localPosition = new Vector3(0f, 0.12f + Mathf.Sin(_age * 2.5f + _phase) * 0.04f, 0f);
            _visual.localRotation = Quaternion.Euler(0f, _age * 60f + _phase * Mathf.Rad2Deg, 0f);
        }

        private bool Solid(float x, float y, float z)
        {
            return _sys.IsSolidCell(Mathf.FloorToInt(x), Mathf.FloorToInt(y), Mathf.FloorToInt(z));
        }

        private void Simulate(float dt)
        {
            // блок поставили прямо на предмет: выдавливаем вверх
            for (int guard = 0; guard < 4 && Solid(_pos.x, _pos.y, _pos.z); guard++)
            {
                _pos.y = Mathf.Floor(_pos.y) + 1f + Half;
                _vel.y = 0f;
            }

            // вертикаль
            _vel.y = Mathf.Max(_vel.y - Gravity * dt, -MaxFall);
            float ny = _pos.y + _vel.y * dt;

            if (_vel.y <= 0f)
            {
                float bottom = ny - Half;
                if (Solid(_pos.x, bottom, _pos.z))
                {
                    ny = Mathf.Floor(bottom) + 1f + Half;   // встали на поверхность
                    _vel.y = 0f;
                    _onGround = true;
                }
                else _onGround = false;
            }
            else
            {
                float top = ny + Half;
                if (Solid(_pos.x, top, _pos.z))
                {
                    ny = Mathf.Floor(top) - Half - 0.001f;   // упёрлись в потолок
                    _vel.y = 0f;
                }
                _onGround = false;
            }
            _pos.y = ny;

            // горизонталь: на земле быстро гасим скольжение, в воздухе почти нет
            float drag = Mathf.Exp((_onGround ? -10f : -0.6f) * dt);
            _vel.x *= drag;
            _vel.z *= drag;

            float nx = _pos.x + _vel.x * dt;
            if (Solid(nx + Mathf.Sign(_vel.x) * Half, _pos.y, _pos.z)) _vel.x = 0f;
            else _pos.x = nx;

            float nz = _pos.z + _vel.z * dt;
            if (Solid(_pos.x, _pos.y, nz + Mathf.Sign(_vel.z) * Half)) _vel.z = 0f;
            else _pos.z = nz;
        }

        private void OnDestroy()
        {
            if (_sys != null) _sys.Unregister(Id);
        }
    }
}
