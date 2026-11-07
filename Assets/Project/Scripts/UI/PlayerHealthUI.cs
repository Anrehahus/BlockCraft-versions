using UnityEngine;
using MinecraftClone.Player;

namespace MinecraftClone.UI
{
    // Ряд из 10 сердец над хотбаром. Добавляется автоматически вместе с PlayerHotbarUI.
    // Текстуры из Resources/UI (все 9x9):
    //   heart_container        - пустое сердце: чёрное с чёрной обводкой
    //   heart_container_blink  - пустое сердце: чёрное с белой обводкой (вспышка урона)
    //   heart_full / heart_half            - красное целое / половинка
    //   heart_blink_full / heart_blink_half - розовое целое / половинка (вспышка лечения)
    public class PlayerHealthUI : MonoBehaviour
    {
        private const int HeartCount = 10;
        private const float BlinkTime = 0.5f;   // сколько длится вспышка

        private Texture2D _container, _containerBlink, _full, _half, _blinkFull, _blinkHalf;
        private PlayerHealth _health;
        private bool _warned;

        private int _shownHp = -1;   // здоровье в половинках сердец, которое мы видели в прошлый раз
        private int _fromHp;         // здоровье до последнего изменения
        private bool _blinkHeal;     // последнее изменение было лечением
        private float _blinkUntil;

        private void Awake()
        {
            _container = McGui.Load("heart_container");
            _containerBlink = McGui.Load("heart_container_blink");
            _full = McGui.Load("heart_full");
            _half = McGui.Load("heart_half");
            _blinkFull = McGui.Load("heart_blink_full");
            _blinkHalf = McGui.Load("heart_blink_half");
        }

        private void Update()
        {
            if (_health == null)
            {
                PlayerHotbarUI hb = PlayerHotbarUI.Instance;
                if (hb == null || hb.Player == null) return;

                _health = hb.Player.GetComponent<PlayerHealth>();
                _shownHp = -1;
                if (_health == null && !_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[Hearts] На префабе игрока нет компонента PlayerHealth, сердечки не показываются.");
                }
                return;
            }

            int hp = Mathf.CeilToInt(_health.Health);
            if (_shownHp < 0) _shownHp = hp;

            if (hp != _shownHp)
            {
                _fromHp = _shownHp;
                _blinkHeal = hp > _shownHp;
                _blinkUntil = Time.unscaledTime + BlinkTime;
                _shownHp = hp;
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_health == null || _shownHp < 0) return;
            if (InventoryUI.IsOpen || RuntimeLoadingScreen.IsShown) return;
            if (_container == null || _full == null || _half == null) return;

            GUI.depth = 10;   // под меню паузы и чатом

            float u = PlayerHotbarUI.Scale;                                  // один "пиксель" оригинала на экране
            float x0 = Mathf.Round((Screen.width - 182f * u) / 2f);          // левый край хотбара
            float y0 = Mathf.Round(Screen.height - 39f * u);                 // над хотбаром

            int hp = _shownHp;
            bool blinking = Time.unscaledTime < _blinkUntil;
            bool dmgBlink = blinking && !_blinkHeal;
            bool healBlink = blinking && _blinkHeal;

            int tick = (int)(Time.unscaledTime * 20f);

            for (int i = 0; i < HeartCount; i++)
            {
                float y = y0;
                if (hp <= 4 && (((tick * 73856093) ^ (i * 19349663)) & 1) == 1) y += u;   // дрожь при низком здоровье

                Rect r = new Rect(x0 + 8f * i * u, y, 9f * u, 9f * u);

                // 1. фон: при уроне у сердец, из которых что-то потеряно, белая обводка
                bool lostHere = dmgBlink && _fromHp > 2 * i && hp < 2 * i + 2;
                Texture2D bg = (lostHere && _containerBlink != null) ? _containerBlink : _container;
                GUI.DrawTexture(r, bg, ScaleMode.StretchToFill, true);

                // 2. при лечении: вылеченная часть розовая
                if (healBlink) DrawAmount(r, i, hp, _blinkFull, _blinkHalf);

                // 3. красная часть: при лечении только то, что было до лечения
                DrawAmount(r, i, healBlink ? _fromHp : hp, _full, _half);
            }
        }

        // amount в половинках сердец: 2i+2 и больше = целое сердце, 2i+1 = половинка
        private static void DrawAmount(Rect r, int i, int amount, Texture2D full, Texture2D half)
        {
            Texture2D t = null;
            if (amount >= 2 * i + 2) t = full;
            else if (amount == 2 * i + 1) t = half;

            if (t != null) GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
        }
    }
}