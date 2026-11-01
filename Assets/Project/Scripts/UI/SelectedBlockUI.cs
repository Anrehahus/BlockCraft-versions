using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MinecraftClone.Player;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Core;

namespace MinecraftClone.UI
{
    [RequireComponent(typeof(Image))]
    public class SelectedBlockUI : MonoBehaviour
    {
        private PlayerBlockInteraction _playerInteraction;
        private WorldManager _worldManager;
        private Image _slotImage;
        private BlockType _lastType = (BlockType)(-1);

        // Кеш созданных спрайтов во избежание утечки памяти и ошибок чтения в билд-версии
        private readonly Dictionary<BlockType, Sprite> _spriteCache = new Dictionary<BlockType, Sprite>();

        private void Start()
        {
            _worldManager = FindFirstObjectByType<WorldManager>();
            _slotImage = GetComponent<Image>();
        }

        private void Update()
        {
            if (_playerInteraction == null)
            {
                _playerInteraction = FindFirstObjectByType<PlayerBlockInteraction>();
                return;
            }

            if (_worldManager == null || _slotImage == null) return;

            BlockType currentType = _playerInteraction.CurrentSelectedBlock;

            if (currentType == _lastType) return;
            _lastType = currentType;

            // 1. Проверяем кеш
            if (_spriteCache.TryGetValue(currentType, out Sprite cachedSprite))
            {
                SetImageSprite(cachedSprite);
                return;
            }

            if (_worldManager.BlockDatabase != null && _worldManager.BlockDatabase.TryGetValue(currentType, out BlockDataSO blockData) && blockData != null)
            {
                // 2. Блочная иконка из ScriptableObject
                if (blockData.BlockIcon != null)
                {
                    _spriteCache[currentType] = blockData.BlockIcon;
                    SetImageSprite(blockData.BlockIcon);
                    return;
                }

                // 3. Динамическая сборка из текстур
                Texture2D tex = blockData.SideTexture ?? blockData.TopTexture ?? blockData.BottomTexture;

                if (tex != null)
                {
                    try
                    {
                        Sprite dynamicSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        _spriteCache[currentType] = dynamicSprite;
                        SetImageSprite(dynamicSprite);
                        return;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[UI SelectedBlock] Не удалось создать спрайт для {currentType}. Включите Read/Write в настройках текстуры! Ошибка: {e.Message}");
                    }
                }
            }

            SetImageSprite(null);
        }

        private void SetImageSprite(Sprite sprite)
        {
            if (sprite != null)
            {
                _slotImage.sprite = sprite;
                _slotImage.color = Color.white;
            }
            else
            {
                _slotImage.sprite = null;
                _slotImage.color = Color.clear;
            }
        }
    }
}