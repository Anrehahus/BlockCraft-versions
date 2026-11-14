#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;

public static class NewBlocksCreator
{
    private static readonly (BlockType type, string tex, string name, float hardness)[] Items =
    {
        (BlockType.GoldBlock,      "gold_block",       "Block of Gold",    3f),
        (BlockType.WhiteWool,      "white_wool",       "White Wool",       0.8f),
        (BlockType.OrangeWool,     "orange_wool",      "Orange Wool",      0.8f),
        (BlockType.MagentaWool,    "magenta_wool",     "Magenta Wool",     0.8f),
        (BlockType.LightBlueWool,  "light_blue_wool",  "Light Blue Wool",  0.8f),
        (BlockType.YellowWool,     "yellow_wool",      "Yellow Wool",      0.8f),
        (BlockType.LimeWool,       "lime_wool",        "Lime Wool",        0.8f),
        (BlockType.PinkWool,       "pink_wool",        "Pink Wool",        0.8f),
        (BlockType.GrayWool,       "gray_wool",        "Gray Wool",        0.8f),
        (BlockType.LightGrayWool,  "light_gray_wool",  "Light Gray Wool",  0.8f),
        (BlockType.CyanWool,       "cyan_wool",        "Cyan Wool",        0.8f),
        (BlockType.PurpleWool,     "purple_wool",      "Purple Wool",      0.8f),
        (BlockType.BlueWool,       "blue_wool",        "Blue Wool",        0.8f),
        (BlockType.BrownWool,      "brown_wool",       "Brown Wool",       0.8f),
        (BlockType.GreenWool,      "green_wool",       "Green Wool",       0.8f),
        (BlockType.RedWool,        "red_wool",         "Red Wool",         0.8f),
        (BlockType.BlackWool,      "black_wool",       "Black Wool",       0.8f),
    };

    private static readonly (BlockType type, string top, string bottom, string side, string name, float hardness)[] Items2 =
    {
        (BlockType.StoneSlab,       "smooth_stone",        "smooth_stone",        "smooth_stone",            "Stone Slab",       2f),
        (BlockType.StoneSlabDouble, "smooth_stone",        "smooth_stone",        "smooth_stone_slab_side",  "Smooth Stone",     2f),
        (BlockType.IronBlock,       "iron_block",          "iron_block",          "iron_block",              "Block of Iron",    5f),
        (BlockType.Tnt,             "tnt_top",             "tnt_bottom",          "tnt_side",                "TNT",              0f),
        (BlockType.MossyCobblestone,"mossy_cobblestone",   "mossy_cobblestone",   "mossy_cobblestone",       "Mossy Cobblestone",2f),
        (BlockType.Bricks,          "bricks",              "bricks",              "bricks",                  "Bricks",           2f),
        (BlockType.Bookshelf,       "oak_planks",          "oak_planks",          "bookshelf",               "Bookshelf",        1.5f),
    };

    [MenuItem("Minecraft/Create Gold Block And Wool")]
    public static void Create()
    {
        const string folder = "Assets/Project/ScriptableObjects";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError("Папка не найдена: " + folder + ". Поправь путь в скрипте.");
            return;
        }

        SerializedObject wmSo = null;
        SerializedProperty list = null;
        var wm = Object.FindFirstObjectByType<WorldManager>();
        if (wm != null)
        {
            wmSo = new SerializedObject(wm);
            list = wmSo.FindProperty("blockTypes");
        }

        int created = 0;
        foreach (var it in Items)
        {
            Texture2D tex = FindTexture(it.tex);
            if (tex == null) { Debug.LogWarning("Текстура не найдена: " + it.tex); continue; }

            string texPath = AssetDatabase.GetAssetPath(tex);
            var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (imp != null)
            {
                imp.isReadable = true;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }

            string soPath = folder + "/" + it.tex + ".asset";
            var so = AssetDatabase.LoadAssetAtPath<BlockDataSO>(soPath);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<BlockDataSO>();
                AssetDatabase.CreateAsset(so, soPath);
            }

            so.Type = it.type;
            so.BlockName = it.name;
            so.IsBreakable = true;
            so.Hardness = it.hardness;
            so.TopTexture = so.BottomTexture = so.SideTexture = tex;
            EditorUtility.SetDirty(so);

            if (list != null && !Contains(list, so))
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = so;
            }
            created++;
        }

        if (wmSo != null)
        {
            wmSo.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wm.gameObject.scene);
        }
        else Debug.LogWarning("WorldManager не найден в открытой сцене: добавь ассеты в Block Types вручную.");

        AssetDatabase.SaveAssets();
        Debug.Log($"Готово: обработано блоков {created} из {Items.Length}.");
    }

    [MenuItem("Minecraft/Create Slab And Misc Blocks")]
    public static void CreateMore()
    {
        const string folder = "Assets/Project/ScriptableObjects";
        if (!AssetDatabase.IsValidFolder(folder)) { Debug.LogError("Папка не найдена: " + folder); return; }

        SerializedObject wmSo = null; SerializedProperty list = null;
        var wm = Object.FindFirstObjectByType<WorldManager>();
        if (wm != null) { wmSo = new SerializedObject(wm); list = wmSo.FindProperty("blockTypes"); }

        foreach (var it in Items2)
        {
            Texture2D top = Prep(it.top), bottom = Prep(it.bottom), side = Prep(it.side);
            if (top == null || bottom == null || side == null) { Debug.LogWarning("Нет текстуры для " + it.type); continue; }

            string soPath = folder + "/" + it.type + ".asset";
            var so = AssetDatabase.LoadAssetAtPath<BlockDataSO>(soPath);
            if (so == null) { so = ScriptableObject.CreateInstance<BlockDataSO>(); AssetDatabase.CreateAsset(so, soPath); }

            so.Type = it.type; so.BlockName = it.name;
            so.IsSolid = true; so.IsOpaque = it.type != BlockType.StoneSlab;
            so.IsBreakable = true; so.Hardness = it.hardness;
            so.TopTexture = top; so.BottomTexture = bottom; so.SideTexture = side;
            EditorUtility.SetDirty(so);

            if (list != null && !Contains(list, so))
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = so;
            }
        }

        if (wmSo != null)
        {
            wmSo.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wm.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Готово: плита и блоки добавлены.");
    }

    private static Texture2D FindTexture(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Texture2D"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == name)
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        return null;
    }

    private static Texture2D Prep(string name)
    {
        Texture2D tex = FindTexture(name);
        if (tex == null) return null;
        var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        if (imp != null)
        {
            imp.isReadable = true; imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        return tex;
    }

    private static bool Contains(SerializedProperty list, Object obj)
    {
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == obj) return true;
        return false;
    }
}
#endif