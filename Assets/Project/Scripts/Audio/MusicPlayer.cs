using UnityEngine;
using MinecraftClone.Management;
using MinecraftClone.UI;

namespace MinecraftClone.Audio
{
    public class MusicPlayer : MonoBehaviour
    {
        // Задержки в секундах (как в оригинале: музыка включается редко и не сразу)
        private const float FirstDelayMin = 60f;
        private const float FirstDelayMax = 240f;
        private const float PauseMin = 300f;
        private const float PauseMax = 600f;
        private const float Volume = 0.5f;

        private AudioClip[] _tracks;
        private AudioSource _src;
        private float _timer;
        private int _last = -1;
        private bool _wasPlaying;

        private void Awake()
        {
            _tracks = Resources.LoadAll<AudioClip>("Music");
            if (_tracks.Length == 0)
                Debug.LogWarning("[Music] В Resources/Music нет треков.");

            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = false;
            _src.spatialBlend = 0f;   // 2D: слышно одинаково везде
            _src.volume = Volume;

            _timer = Random.Range(FirstDelayMin, FirstDelayMax);
        }

        private void Update()
        {
            if (_tracks == null || _tracks.Length == 0) return;

            if (_src.isPlaying)
            {
                _wasPlaying = true;
                return;
            }

            // трек закончился: ждём до следующего
            if (_wasPlaying)
            {
                _wasPlaying = false;
                _timer = Random.Range(PauseMin, PauseMax);
            }

            if (WorldManager.Instance == null || !WorldManager.Instance.IsWorldReady) return;
            if (RuntimeLoadingScreen.IsShown) return;

            // unscaled: в одиночной игре на паузе таймер тоже идёт, как в оригинале
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;

            int i = Random.Range(0, _tracks.Length);
            if (_tracks.Length > 1 && i == _last) i = (i + 1) % _tracks.Length; // не два раза подряд
            _last = i;

            _src.clip = _tracks[i];
            _src.Play();
        }
    }
}