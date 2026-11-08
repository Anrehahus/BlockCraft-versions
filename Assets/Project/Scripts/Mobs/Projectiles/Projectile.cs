using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Networking;
using MinecraftClone.Player;

namespace MinecraftClone.Mobs
{
    // Летящая стрела. Работает и в одиночной игре, и в сетевой:
    //  - сервер (или одиночная игра) создаёт "настоящую" стрелу (authoritative = true), только она наносит урон;
    //  - клиенты создают визуальную копию по RPC и сами считают траекторию (блоки у всех одинаковые).
    // Префаб не нужен: объект и модель строятся из кода.
    public class Projectile : MonoBehaviour
    {
        private const float Unit = 0.05625f;   // как в Minecraft: 1 единица модели стрелы = 0.05625 блока

        private ProjectileDataSO _data;
        private Vector3 _vel;
        private float _damage;
        private bool _authoritative;

        private float _age, _stuckTime;
        private bool _stuck;
        private bool _falling;          // стрела потеряла опору и падает вертикально
        private float _fallSpeed;       // скорость падения (отрицательная)
        private float _supportTimer;    // проверка опоры раз в 0.1 с

        private Material _mat;
        private Mesh _mesh;

        public static Projectile Spawn(ProjectileDataSO data, Vector3 pos, Vector3 velocity, float damage, bool authoritative)
        {
            GameObject go = new GameObject("Projectile_" + data.Id);
            go.transform.position = pos;
            if (velocity.sqrMagnitude > 0.0001f) go.transform.rotation = Quaternion.LookRotation(velocity);

            Projectile p = go.AddComponent<Projectile>();
            p._data = data;
            p._vel = velocity;
            p._damage = damage;
            p._authoritative = authoritative;
            p.BuildModel();
            return p;
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_mesh != null) Destroy(_mesh);
        }

        // ---------- Полёт ----------

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _data == null) return;

            if (_stuck)
            {
                _stuckTime += dt;
                if (_stuckTime >= _data.StuckLifetime) { Destroy(gameObject); return; }

                _supportTimer -= dt;
                if (_supportTimer <= 0f)
                {
                    _supportTimer = 0.1f;
                    if (!IsSupported(transform.position))
                    {
                        // блок, в котором торчала стрела, исчез: начинаем падать строго вниз
                        _stuck = false;
                        _falling = true;
                        _fallSpeed = 0f;
                    }
                }
                if (_stuck) return;
            }

            if (_falling)
            {
                FallStraight(dt);
                return;
            }

            _age += dt;
            if (_age >= _data.MaxLifetime) { Destroy(gameObject); return; }

            dt = Mathf.Min(dt, 0.05f);
            _vel.y += _data.Gravity * dt;

            Vector3 step = _vel * dt;
            int n = Mathf.Max(1, Mathf.CeilToInt(step.magnitude / 0.25f));   // мелкие шаги, чтобы не пролетать сквозь блоки
            Vector3 s = step / n;
            Vector3 p = transform.position;

            for (int i = 0; i < n; i++)
            {
                Vector3 next = p + s;

                if (SolidAt(next) || VoxelSeam.IsSeam(next, SolidCell))
                {
                    // втыкаемся: чуть заглубляем в блок и замираем
                    transform.position = p + s.normalized * 0.15f;
                    _stuck = true;
                    return;
                }

                if (HitsPlayer(next, out NetworkPlayer hitPlayer))
                {
                    if (_authoritative)
                    {
                        PlayerHealth health = hitPlayer.GetComponentInParent<PlayerHealth>();
                        if (health != null) health.TakeDamage(_damage, transform.position);
                    }
                    Destroy(gameObject);
                    return;
                }

                p = next;
            }

            transform.position = p;
            if (_vel.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(_vel);
        }

        private static bool SolidAt(Vector3 pos)
        {
            WorldManager wm = WorldManager.Instance;
            if (wm == null) return false;

            int x = Mathf.FloorToInt(pos.x), y = Mathf.FloorToInt(pos.y), z = Mathf.FloorToInt(pos.z);
            if (!wm.IsChunkLoadedAtWorldPos(x, z)) return true;   // чанк не загружен: останавливаемся

            BlockType b = wm.GetBlockAtWorldPosition(x, y, z);
            if (b == BlockType.Air || b == BlockType.Water || b == BlockType.Lava) return false;
            if (wm.BlockDatabase != null && wm.BlockDatabase.TryGetValue(b, out BlockDataSO d) && d != null)
                return d.IsSolid;   // цветы и саженцы стрела пролетает
            return true;
        }
        private static readonly System.Func<int, int, int, bool> SolidCell =
            (x, y, z) => SolidAt(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));

        // есть ли рядом с остриём хоть один твёрдый блок (в радиусе 0.2)
        private static bool IsSupported(Vector3 p)
        {
            const float r = 0.2f;
            if (SolidAt(p)) return true;
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iy = -1; iy <= 1; iy += 2)
                    for (int iz = -1; iz <= 1; iz += 2)
                        if (SolidAt(p + new Vector3(ix * r, iy * r, iz * r))) return true;
            return false;
        }

        private void FallStraight(float dt)
        {
            _stuckTime += dt;   // время жизни стрелы продолжает идти
            if (_stuckTime >= _data.StuckLifetime) { Destroy(gameObject); return; }

            dt = Mathf.Min(dt, 0.05f);
            // разворачиваем стрелу остриём вниз (плавно, минимальным поворотом)
            Quaternion pointDown = Quaternion.FromToRotation(transform.forward, Vector3.down) * transform.rotation;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, pointDown, 540f * dt);
            float g = _data.Gravity < 0f ? _data.Gravity : -20f;
            _fallSpeed = Mathf.Max(_fallSpeed + g * dt, -78f);

            float dy = _fallSpeed * dt;
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(dy) / 0.25f));
            Vector3 s = Vector3.up * (dy / n);
            Vector3 p = transform.position;

            for (int i = 0; i < n; i++)
            {
                Vector3 next = p + s;
                if (SolidAt(next))
                {
                    // упёрлись в следующий блок: втыкаемся и снова ждём, пока его не уберут
                    transform.position = p + Vector3.down * 0.15f;
                    _falling = false;
                    _stuck = true;
                    transform.rotation = Quaternion.LookRotation(Vector3.down, transform.up);
                    _supportTimer = 0.1f;
                    return;
                }
                p = next;
            }

            transform.position = p;
            if (p.y < -20f) Destroy(gameObject);   // упала за пределы мира
        }

        // коробка игрока: ширина 0.8 (с запасом), высота 1.8, точка у ног
        private static bool HitsPlayer(Vector3 pos, out NetworkPlayer hit)
        {
            hit = null;
            foreach (var pl in Mob.GetPlayers())
            {
                if (pl == null) continue;
                Vector3 c = pl.transform.position;
                if (Mathf.Abs(pos.x - c.x) < 0.3f && Mathf.Abs(pos.z - c.z) < 0.3f
                    && pos.y > c.y && pos.y < c.y + 1.8f)
        {
                    hit = pl;
                    return true;
                }
            }
            return false;
        }

        // ---------- Модель ----------

        private void BuildModel()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _mat = new Material(sh);

            Texture2D tex = _data.Texture;
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point;
                _mat.mainTexture = tex;
            }
            else
            {
                _mat.color = new Color(0.8f, 0.7f, 0.5f);
            }

            // в текстуре стрелы много прозрачных пикселей: вырезаем их и рисуем обе стороны
            _mat.SetFloat("_AlphaClip", 1f);
            _mat.SetFloat("_Cutoff", 0.5f);
            _mat.EnableKeyword("_ALPHATEST_ON");
            _mat.renderQueue = 2450;
            _mat.SetFloat("_Cull", 0f);

            float W = tex != null ? tex.width : 32f;
            float H = tex != null ? tex.height : 32f;
            Rect shaft = new Rect(0f, 1f - 5f / H, 16f / W, 5f / H);    // древко: 16x5 пикселей в левом верхнем углу
            Rect tail = new Rect(0f, 1f - 10f / H, 5f / W, 5f / H);     // оперение: 5x5 пикселей под ним

            var v = new List<Vector3>(12);
            var t = new List<Vector2>(12);
            var tr = new List<int>(18);

            Vector3 f = Vector3.forward;
            float c = 0.70710678f;
            Vector3 w1 = new Vector3(c, c, 0f);
            Vector3 w2 = new Vector3(-c, c, 0f);

            // два скрещённых древка (16 x 4 единицы), у стрелы Minecraft они повёрнуты на 45 градусов
            AddQuad(v, t, tr, f * (-12f * Unit) - w1 * (2f * Unit), f * (16f * Unit), w1 * (4f * Unit), shaft);
            AddQuad(v, t, tr, f * (-12f * Unit) - w2 * (2f * Unit), f * (16f * Unit), w2 * (4f * Unit), shaft);

            // оперение: квадрат 4x4, повёрнутый на 45 градусов, у задней части
            Vector3 tc = f * (-11f * Unit);
            AddQuad(v, t, tr, tc - w1 * (2f * Unit) - w2 * (2f * Unit), w1 * (4f * Unit), w2 * (4f * Unit), tail);

            _mesh = new Mesh { name = "Arrow" };
            _mesh.SetVertices(v);
            _mesh.SetUVs(0, t);
            _mesh.SetTriangles(tr, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // origin = левый нижний угол, axisU = вектор вправо, axisV = вектор вверх
        private static void AddQuad(List<Vector3> v, List<Vector2> t, List<int> tr,
                                    Vector3 origin, Vector3 axisU, Vector3 axisV, Rect uv)
        {
            int i = v.Count;
            v.Add(origin);
            v.Add(origin + axisU);
            v.Add(origin + axisU + axisV);
            v.Add(origin + axisV);

            t.Add(new Vector2(uv.xMin, uv.yMin));
            t.Add(new Vector2(uv.xMax, uv.yMin));
            t.Add(new Vector2(uv.xMax, uv.yMax));
            t.Add(new Vector2(uv.xMin, uv.yMax));

            tr.Add(i); tr.Add(i + 1); tr.Add(i + 2);
            tr.Add(i); tr.Add(i + 2); tr.Add(i + 3);
        }
    }
}