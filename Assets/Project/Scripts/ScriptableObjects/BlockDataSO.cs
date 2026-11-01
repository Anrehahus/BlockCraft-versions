using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.ScriptableObjects
{
    [CreateAssetMenu(fileName = "NewBlockData", menuName = "Minecraft/Block Data")]
    public class BlockDataSO : ScriptableObject
    {
        [Header("Основные настройки")]
        public BlockType Type;
        public string BlockName;
        public bool IsSolid = true;
        public bool IsOpaque = true;

        [Header("Физика и Гравитация")]
        [Tooltip("Падает ли блок, если под ним нет опоры (Песок, Гравий)")]
        public bool IsGravityAffected = false;

        [Header("Свойства жидкости")]
        public bool IsLiquid = false;

        [Header("Прочность и разрушение")]
        [Tooltip("Можно ли сломать этот блок (для Bedrock поставьте false)")]
        public bool IsBreakable = true;

        [Tooltip("Время/сложность разрушения блока")]
        public float Hardness = 1.0f;

        [Header("Текстуры граней")]
        public Texture2D TopTexture;
        public Texture2D BottomTexture;
        public Texture2D SideTexture;

        [Header("UI Иконка")]
        public Sprite BlockIcon;

        public Texture2D GetTextureForFace(int faceIndex)
        {
            switch (faceIndex)
            {
                case 2: return TopTexture;
                case 3: return BottomTexture;
                case 4: return SideTexture;
                default: return SideTexture;
            }
        }
    }
}