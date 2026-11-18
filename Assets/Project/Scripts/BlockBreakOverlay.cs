using UnityEngine;

namespace MinecraftClone.Core
{
    public class BlockBreakOverlay : MonoBehaviour
    {
        private const int Stages = 10;
        private static Texture2D[] _tex;
        private MeshRenderer _mr;
        private Material _mat;

        private void Awake()
        {
            if (_tex == null)
            {
                _tex = new Texture2D[Stages];
                for (int i = 0; i < Stages; i++)
                {
                    _tex[i] = Resources.Load<Texture2D>("BreakStages/destroy_stage_" + i);
                    if (_tex[i] != null) { _tex[i].filterMode = FilterMode.Point; _tex[i].wrapMode = TextureWrapMode.Clamp; }
                }
            }

            var prim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.AddComponent<MeshFilter>().sharedMesh = prim.GetComponent<MeshFilter>().sharedMesh;
            Destroy(prim);

            _mat = new Material(Shader.Find("Sprites/Default"));
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = _mat;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mr.enabled = false;
        }

        public void Show(Vector3Int pos, Vector3 min, Vector3 max, float progress)
        {
            int s = Mathf.Clamp((int)(progress * Stages), 0, Stages - 1);
            if (_tex[s] == null) { Hide(); return; }
            _mat.mainTexture = _tex[s];
            transform.position = (Vector3)pos + (min + max) * 0.5f;
            transform.localScale = (max - min) + Vector3.one * 0.004f;   // чуть больше блока, без мерцания
            _mr.enabled = true;
        }

        public void Hide() { if (_mr != null) _mr.enabled = false; }

        private void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}