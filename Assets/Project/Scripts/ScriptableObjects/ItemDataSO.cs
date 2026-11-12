using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Items
{
    // Описание предмета, который не является блоком (еда, инструменты, материалы).
    // Ассеты кладутся в папку Resources/Items: реестр находит их сам, ничего никуда добавлять не нужно.
    // Предметы-блоки отдельно создавать не надо: их реестр делает из BlockDataSO автоматически.
    [CreateAssetMenu(fileName = "NewItem", menuName = "Minecraft/Item Data")]
    public class ItemDataSO : ScriptableObject
    {
        [Header("Основное")]
        [Tooltip("Уникальное имя для сохранений, например raw_pork. Если мир уже сохраняли, менять нельзя.")]
        public string Key;
        public string DisplayName;
        [Tooltip("Картинка предмета 16x16 (Read/Write и Point включаются сами)")]
        public Texture2D Texture;
        [Min(1)] public int MaxStack = 99;

        [Header("Свойства")]
        [Tooltip("Сколько здоровья восстанавливает по ПКМ. 0 = не еда. 6 = три сердца.")]
        public float FoodHeal;
        [Tooltip("Какой блок ставит по ПКМ. Air = в мир не ставится.")]
        public BlockType PlaceBlock = BlockType.Air;

#if UNITY_EDITOR
        // Текстура должна быть читаемой (иконка и атлас), без сжатия и с резкими пикселями
        private void OnValidate()
        {
            if (Texture == null) return;

            string path = UnityEditor.AssetDatabase.GetAssetPath(Texture);
            var imp = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
            if (imp == null) return;

            bool ok = imp.isReadable && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled
                      && imp.textureCompression == UnityEditor.TextureImporterCompression.Uncompressed;
            if (ok) return;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (imp == null) return;
                imp.isReadable = true;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            };
        }
#endif
    }
}