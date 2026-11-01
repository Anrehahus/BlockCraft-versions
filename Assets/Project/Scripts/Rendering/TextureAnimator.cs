using UnityEngine;

namespace MinecraftClone.Rendering
{
    /// <summary>Анимирует область атласа, подменяя в ней кадры из вертикальной ленты.</summary>
    public class TextureAnimator : MonoBehaviour
    {
        [SerializeField] private float frameTime = 0.1f;

        private Texture2D _atlas;
        private Color[][] _frames;
        private int _x, _y, _size;
        private int _index;
        private float _timer;

        public void Init(Texture2D atlas, Rect uvRect, Texture2D strip, float frameTime)
        {
            this.frameTime = frameTime;
            _atlas = atlas;
            _x = Mathf.RoundToInt(uvRect.x * atlas.width);
            _y = Mathf.RoundToInt(uvRect.y * atlas.height);
            _size = Mathf.RoundToInt(uvRect.width * atlas.width);

            int count = strip.height / strip.width;
            if (count < 2 || strip.width != _size)
            {
                Debug.LogWarning($"[TextureAnimator] Лента {strip.width}x{strip.height} не подходит к области {_size}px. Анимация отключена.");
                enabled = false;
                return;
            }

            _frames = new Color[count][];
            for (int i = 0; i < count; i++)
                _frames[i] = strip.GetPixels(0, strip.height - (i + 1) * _size, _size, _size); // первый кадр сверху
        }

        private void Update()
        {
            if (_frames == null || _atlas == null) return;

            _timer += Time.deltaTime;
            if (_timer < frameTime) return;
            _timer -= frameTime;

            _index = (_index + 1) % _frames.Length;
            _atlas.SetPixels(_x, _y, _size, _size, _frames[_index]);
            _atlas.Apply(false);
        }
    }
}