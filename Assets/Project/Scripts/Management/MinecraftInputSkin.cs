using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MinecraftClone.UI
{
    /// <summary>
    /// Спрайтовое оформление TMP_InputField (поле IP, ник): UI/textfield, в фокусе UI/textfield_focused.
    /// Вешается автоматически на все поля ввода при загрузке сцены.
    /// </summary>
    [DisallowMultipleComponent]
    public class MinecraftInputSkin : MonoBehaviour
    {
        private TMP_InputField _field;
        private Image _img;
        private Sprite _normal, _focused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded += (s, m) => Install(s);
            Install(SceneManager.GetActiveScene());
        }

        private static void Install(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (TMP_InputField f in root.GetComponentsInChildren<TMP_InputField>(true))
                    if (f.GetComponent<MinecraftInputSkin>() == null)
                        f.gameObject.AddComponent<MinecraftInputSkin>();
        }

        private void Awake()
        {
            _field = GetComponent<TMP_InputField>();
            if (_field == null) { Destroy(this); return; }

            _normal = Resources.Load<Sprite>("UI/textfield");
            _focused = Resources.Load<Sprite>("UI/textfield_focused");
            if (_normal == null || _focused == null)
            {
                Debug.LogWarning("[InputSkin] Не найдены спрайты UI/textfield и UI/textfield_focused " +
                                 "(проверьте имена и Texture Type = Sprite (2D and UI)). Поле ввода оставлено как есть.", this);
                Destroy(this);
                return;
            }

            // Рамку рисует одна картинка: targetGraphic поля или Image на самом объекте (если нет, создаём)
            _img = _field.targetGraphic as Image;
            if (_img == null) _img = GetComponent<Image>();
            if (_img == null) _img = gameObject.AddComponent<Image>();
            _field.targetGraphic = _img;

            // Остальные картинки внутри поля (старый фон, обводка) перекрывали наш спрайт: отключаем
            foreach (Image other in GetComponentsInChildren<Image>(true))
                if (other != _img) other.enabled = false;

            _img.enabled = true;
            _img.raycastTarget = true;
            _img.type = Image.Type.Sliced;
            _img.color = Color.white;
            _field.transition = Selectable.Transition.None;   // свои спрайты вместо стандартного тинта
        }

        private void Update()
        {
            Sprite s = _field.isFocused ? _focused : _normal;
            if (_img.sprite != s) _img.sprite = s;

            // углы 9-slice масштабируем под высоту поля, чтобы рамка была заметной
            float h = ((RectTransform)transform).rect.height;
            if (h >= 1f)
            {
                float m = _normal.rect.height / (h * _img.pixelsPerUnit);
                if (!Mathf.Approximately(_img.pixelsPerUnitMultiplier, m)) _img.pixelsPerUnitMultiplier = m;
            }
        }
    }
}