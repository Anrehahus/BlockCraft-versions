#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using MinecraftClone.Items;

public static class ArmorItemsCreator
{
    private const string AssetDir = "Assets/Resources/Items";
    private const string IconDir = "Assets/Resources/Items/Icons";

    private static readonly (string key, string name, ArmorSlot slot)[] Defs =
    {
        ("leather_helmet",     "Кожаный шлем",        ArmorSlot.Helmet),
        ("leather_chestplate", "Кожаная куртка",      ArmorSlot.Chestplate),
        ("leather_leggings",   "Кожаные поножи",      ArmorSlot.Leggings),
        ("leather_boots",      "Кожаные ботинки",     ArmorSlot.Boots),
        ("iron_helmet",        "Железный шлем",       ArmorSlot.Helmet),
        ("iron_chestplate",    "Железный нагрудник",  ArmorSlot.Chestplate),
        ("iron_leggings",      "Железные поножи",     ArmorSlot.Leggings),
        ("iron_boots",         "Железные ботинки",    ArmorSlot.Boots),
        ("chainmail_helmet",     "Кольчужный шлем",   ArmorSlot.Helmet),
        ("chainmail_chestplate", "Кольчужная кираса", ArmorSlot.Chestplate),
        ("chainmail_leggings",   "Кольчужные поножи", ArmorSlot.Leggings),
        ("chainmail_boots",      "Кольчужные ботинки", ArmorSlot.Boots),
    };

    [MenuItem("Minecraft/Create Armor Items")]
    public static void Create()
    {
        Directory.CreateDirectory(AssetDir);
        foreach (var d in Defs)
        {
            string iconPath = $"{IconDir}/{d.key}.png";
            PrepareIcon(iconPath);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (tex == null) Debug.LogWarning($"[Armor] Нет иконки {iconPath}");

            string assetPath = $"{AssetDir}/{d.key}.asset";
            var so = AssetDatabase.LoadAssetAtPath<ItemDataSO>(assetPath);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<ItemDataSO>();
                AssetDatabase.CreateAsset(so, assetPath);
            }

            so.Key = d.key;
            so.DisplayName = d.name;
            so.Texture = tex;
            so.MaxStack = 1;
            so.Armor = d.slot;
            EditorUtility.SetDirty(so);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Armor] Создано/обновлено предметов брони: " + Defs.Length);
    }

    private static readonly (string key, string name, int stack)[] Misc =
{
    ("apple",       "Яблоко",          99),
    ("iron_sword",  "Железный меч",    1),
    ("iron_shovel", "Железная лопата", 1),
};

    [MenuItem("Minecraft/Create Misc Items")]
    public static void CreateMisc()
    {
        Directory.CreateDirectory(AssetDir);
        foreach (var d in Misc)
        {
            string iconPath = $"{IconDir}/{d.key}.png";
            PrepareIcon(iconPath);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (tex == null) Debug.LogWarning($"[Items] Нет иконки {iconPath}");

            string assetPath = $"{AssetDir}/{d.key}.asset";
            var so = AssetDatabase.LoadAssetAtPath<ItemDataSO>(assetPath);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<ItemDataSO>();
                AssetDatabase.CreateAsset(so, assetPath);
            }

            so.Key = d.key;
            so.DisplayName = d.name;
            so.Texture = tex;
            so.MaxStack = d.stack;
            so.Armor = ArmorSlot.None;
            EditorUtility.SetDirty(so);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Items] Создано/обновлено предметов: " + Misc.Length);
    }

    private static void PrepareIcon(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        bool ok = imp.isReadable && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled
                  && imp.textureCompression == TextureImporterCompression.Uncompressed;
        if (ok) return;
        imp.isReadable = true;
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
    }
}
#endif