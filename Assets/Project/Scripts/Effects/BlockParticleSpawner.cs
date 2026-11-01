using UnityEngine;

namespace MinecraftClone.Effects
{
    public static class BlockParticleSpawner
    {
        public static void SpawnBreakParticles(Vector3 worldPosition, Texture2D texture)
        {
            if (texture == null) return;

            int particleCount = 8;
            float texWidth = texture.width;
            float texHeight = texture.height;
            float sliceSize = Mathf.Min(texWidth, texHeight) * 0.5f;

            for (int i = 0; i < particleCount; i++)
            {
                GameObject particle = new GameObject("BlockDebris");

                particle.transform.position = worldPosition + new Vector3(
                    Random.Range(-0.2f, 0.2f),
                    Random.Range(0f, 0.3f),
                    Random.Range(-0.2f, 0.2f)
                );

                // Увеличили размер кусочков (было 0.35f)
                particle.transform.localScale = Vector3.one * 0.5f;

                SpriteRenderer sr = particle.AddComponent<SpriteRenderer>();

                float randomX = Random.Range(0f, texWidth - sliceSize);
                float randomY = Random.Range(0f, texHeight - sliceSize);
                Rect subRect = new Rect(randomX, randomY, sliceSize, sliceSize);

                Sprite sprite = Sprite.Create(texture, subRect, new Vector2(0.5f, 0.5f), 100f);
                sr.sprite = sprite;

                particle.AddComponent<DebrisMover>();
            }
        }
    }

    public class DebrisMover : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _lifetime = 0.6f;
        private float _timer;
        private float _spinSpeed;
        private float _currentAngle;
        private Transform _targetCameraTransform;

        private void Start()
        {
            _velocity = new Vector3(
                Random.Range(-1.8f, 1.8f),
                Random.Range(2.0f, 4.0f),
                Random.Range(-1.8f, 1.8f)
            );

            _spinSpeed = Random.Range(-250f, 250f);
            _currentAngle = Random.Range(0f, 360f);

            if (Camera.main != null)
            {
                _targetCameraTransform = Camera.main.transform;
            }
            else
            {
                Camera foundCam = Object.FindFirstObjectByType<Camera>();
                if (foundCam != null)
                {
                    _targetCameraTransform = foundCam.transform;
                }
            }
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            _velocity += Vector3.down * 14f * Time.deltaTime;
            transform.position += _velocity * Time.deltaTime;

            _currentAngle += _spinSpeed * Time.deltaTime;

            if (_targetCameraTransform != null)
            {
                transform.rotation = _targetCameraTransform.rotation * Quaternion.Euler(0, 180, _currentAngle);
            }
        }

        private void OnDestroy()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) Destroy(sr.sprite);
        }
    }
}