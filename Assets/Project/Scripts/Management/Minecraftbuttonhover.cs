using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MinecraftClone.UI
{
    /// <summary>
    /// Оформление любой uGUI-кнопки: спрайт при наведении (UI/button_hover), жёлтый основной текст
    /// и тень текста, которую скрипт создаёт сам. Вешается автоматически на все Button при загрузке сцены.
    /// Тень рисуется отдельным текстом позади основного и никогда не подсвечивается.
    /// </summary>
    [DisallowMultipleComponent]
    public class MinecraftButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const string ShadowName = "TextShadow(auto)";
        private static readonly Color HoverText = new Color(1f, 1f, 0.6f);
        private static readonly Color DisabledText = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color ShadowColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        private Button _btn;
        private Image _img;
        private Graphic _label, _shadow;
        private Color _labelColor = Color.white;
        private Sprite _normal, _hover, _disabled;
        private Color _baseColor = Color.white;
        private bool _useSprites;
        private bool _over;
        private string _lastText = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded += (s, m) => Install(s);
            Install(SceneManager.GetActiveScene());
        }

        private static void Install(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Button b in root.GetComponentsInChildren<Button>(true))
                    if (b.GetComponent<MinecraftButtonHover>() == null)
                        b.gameObject.AddComponent<MinecraftButtonHover>();
        }

        private void Awake()
        {
            _btn = GetComponent<Button>();
            _img = _btn != null ? (_btn.targetGraphic as Image) : null;
            if (_img == null) _img = GetComponent<Image>();
            if (_btn == null) { Destroy(this); return; }
            if (_img == null) return;

            _hover = Resources.Load<Sprite>("UI/button_hover");
            _disabled = Resources.Load<Sprite>("UI/button_disabled");
            _normal = Resources.Load<Sprite>("UI/button");
            if (_normal == null) _normal = _img.sprite;
            _baseColor = _img.color;

            // Свой hover вместо стандартного перехода Unity, но только если спрайты реально загрузились
            if (_normal != null && _hover != null)
            {
                _useSprites = true;
                _baseColor = Color.white;
                _img.type = Image.Type.Sliced;
                _btn.transition = Selectable.Transition.None;
            }
        }

        // Тексты ищем в Start: к этому моменту дочерние объекты кнопки уже созданы
        private void Start()
        {
            var labels = new List<Graphic>();
            foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true))
                if (t.gameObject.name != ShadowName) labels.Add(t);
            foreach (Text t in GetComponentsInChildren<Text>(true))
                if (t.gameObject.name != ShadowName) labels.Add(t);

            // Тексты не должны принимать клики: иначе увеличенный Rect текста даёт нажатие "по воздуху" рядом с кнопкой
            foreach (Graphic g in labels) g.raycastTarget = false;

            // основной текст: самый светлый (если вручную сделана тень, она темнее)
            float best = -1f;
            foreach (Graphic g in labels)
            {
                float lum = g.color.grayscale;
                if (lum > best) { best = lum; _label = g; }
            }
            if (_label == null) return;
            _labelColor = _label.color;

            // Тень создаём только если на кнопке один текст (вручную сделанную не дублируем)
            if (labels.Count == 1 && _label.gameObject != gameObject)
                CreateShadow();
        }

        private void CreateShadow()
        {
            GameObject go = Instantiate(_label.gameObject, _label.transform.parent, false);
            go.name = ShadowName;
            go.transform.SetSiblingIndex(_label.transform.GetSiblingIndex()); // позади основного текста

            _shadow = go.GetComponent<Graphic>();
            _shadow.raycastTarget = false;

            float size = 18f;
            if (_label is TMP_Text tmp) size = tmp.fontSize;
            else if (_label is Text txt) size = txt.fontSize;
            float o = Mathf.Max(1.5f, size / 10f);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchoredPosition += new Vector2(o, -o);

            _lastText = GetText(_label);
            _shadow.color = ShadowColor;
        }

        private void OnEnable() { _over = false; Apply(); }
        private void OnDisable() { _over = false; }
        private void Update() { Apply(); }

        private void LateUpdate()
        {
            if (_shadow == null || _label == null) return;

            // Другой код может записать текст в первый найденный TMP (это тень): переносим на основной
            string m = GetText(_label), s = GetText(_shadow);
            if (m != s)
            {
                if (s != _lastText) SetText(_label, s);
                else SetText(_shadow, m);
            }
            _lastText = GetText(_label);

            Color c = ShadowColor;
            c.a = _label.color.a;
            if (_shadow.color != c) _shadow.color = c;

            bool on = _label.gameObject.activeSelf;
            if (_shadow.gameObject.activeSelf != on) _shadow.gameObject.SetActive(on);
        }

        public void OnPointerEnter(PointerEventData e) { _over = true; }
        public void OnPointerExit(PointerEventData e) { _over = false; }

        // Масштабируем углы 9-slice под высоту кнопки, чтобы чёрная обводка не оставалась тонкой в 1 пиксель
        private void FitBorder()
        {
            RectTransform rt = transform as RectTransform;
            if (rt == null || _normal == null) return;
            float h = rt.rect.height;
            if (h < 1f) return;
            float m = _normal.rect.height / (h * _img.pixelsPerUnit);
            if (!Mathf.Approximately(_img.pixelsPerUnitMultiplier, m)) _img.pixelsPerUnitMultiplier = m;
        }

        private void Apply()
        {
            if (_btn == null) return;
            bool ok = _btn.interactable;
            if (_useSprites && _img != null) FitBorder();

            if (_useSprites && _img != null)
            {
                Sprite s = _normal;
                if (!ok && _disabled != null) s = _disabled;
                else if (ok && _over) s = _hover;
                if (_img.sprite != s) _img.sprite = s;

                // нет спрайта для disabled: затемняем цветом, а не оставляем белой
                Color c = (!ok && _disabled == null) ? _baseColor * 0.55f : _baseColor;
                c.a = _baseColor.a;
                if (_img.color != c) _img.color = c;
            }

            // подсвечивается только основной текст, тень в LateUpdate держит свой цвет
            if (_label != null)
                _label.color = !ok ? DisabledText : (_over ? HoverText : _labelColor);
        }

        private static string GetText(Graphic g)
        {
            if (g is TMP_Text t) return t.text;
            if (g is Text u) return u.text;
            return "";
        }

        private static void SetText(Graphic g, string v)
        {
            if (g is TMP_Text t) t.text = v;
            else if (g is Text u) u.text = v;
        }
    }
}