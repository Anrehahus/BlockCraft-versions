using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Rendering
{
    // Размеры таблички (в блоках), общие для меша и текста
    public static class SignGeometry
    {
        public const float HalfW = 0.5f, HalfH = 0.25f, HalfD = 0.0417f;   // доска 1 x 0.5 x 0.083
        public const float StickHalf = 0.0417f, StickHalfH = 0.25f;        // палка

        // центр доски относительно угла ячейки блока
        public static Vector3 BoardCenter(BlockType t)
        {
            if (t.IsStandingSign()) return new Vector3(0.5f, 0.75f, 0.5f);
            // на стене: доска вплотную к опоре (чуть заглублена, чтобы не мерцало)
            return new Vector3(0.5f, 0.5f, 0.5f) - t.SignFacing() * (0.5f - HalfD + 0.002f);
        }
    }

    // Надпись на табличке. Пока текст фиксированный, позже сюда добавится ввод.
    public class SignLabel : MonoBehaviour
    {
        public const string DefaultText = "Скоро здесь\nможно будет\nписать!";

        public BlockType Type { get; private set; }

        private static Font _font;
        private static Material _mat;
        private MeshRenderer _mr;
        private bool _fitted;
        private int _tries;

        public static SignLabel Create(Transform chunk, Vector3Int local, BlockType type)
        {
            GameObject go = new GameObject("SignText");
            go.transform.SetParent(chunk, false);
            SignLabel label = go.AddComponent<SignLabel>();
            label.Setup(local, type);
            return label;
        }

        private static Font GetFont()
        {
            if (_font != null) return _font;

            // тот же пиксельный шрифт, что и в чате и инвентаре
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.ToLower().Contains("minecraft")) { _font = f; break; }

            // запасные варианты, если он не найден
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { } }
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 64);

            return _font;
        }

        private void Setup(Vector3Int local, BlockType type)
        {
            Type = type;
            Vector3 f = type.SignFacing();

            transform.localPosition = (Vector3)local + SignGeometry.BoardCenter(type) + f * (SignGeometry.HalfD + 0.004f);
            transform.rotation = Quaternion.LookRotation(-f, Vector3.up);   // текст читается с той стороны, куда смотрит табличка
            transform.localScale = Vector3.one * 0.01f;                      // пока не подогнали размер

            Font font = GetFont();
            TextMesh tm = gameObject.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = DefaultText;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 32;
            tm.characterSize = 0.1f;
            tm.color = Color.black;

            _mr = GetComponent<MeshRenderer>();
            _mr.sharedMaterial = GetMaterial();
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
        }

        // Подгоняем размер текста под доску, как только Unity построит меш надписи
        private void LateUpdate()
        {
            if (_mat != null)
            {
                Texture t = GetFont().material.mainTexture;
                if (t != null)
                {
                    t.filterMode = FilterMode.Point;
                    _mat.mainTexture = t;
                }
            }
            if (_fitted || _mr == null) return;

            Vector3 s = _mr.localBounds.size;
            if (s.x < 0.0001f || s.y < 0.0001f)
            {
                if (++_tries > 60) _fitted = true;
                return;
            }

            float k = Mathf.Min(0.9f / s.x, 0.4f / s.y);   // область текста: 0.9 x 0.4 блока
            transform.localScale = Vector3.one * k;
            _fitted = true;
        }

        private static Material GetMaterial()
        {
            if (_mat == null)
            {
                Shader sh = Resources.Load<Shader>("Shaders/SignText");
                if (sh == null) sh = Shader.Find("Minecraft/SignText");
                if (sh == null)
                {
                    Debug.LogWarning("[SignLabel] Не найден шейдер Minecraft/SignText (Resources/Shaders/SignText.shader)");
                    return GetFont().material;
                }

                _mat = new Material(sh);
                _mat.color = Color.black;
            }
            return _mat;
        }
    }
}