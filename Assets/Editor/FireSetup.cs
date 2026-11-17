#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;

public static class FireSetup
{
    private const string SoFolder = "Assets/Project/ScriptableObjects";

    [MenuItem("Minecraft/Setup Fire")]
    public static void Setup()
    {
        string p0 = FindPath("fire_0"), p1 = FindPath("fire_1");
        if (p0 == null || p1 == null) { Debug.LogError("Не найдены fire_0.png / fire_1.png в проекте."); return; }
        if (!AssetDatabase.IsValidFolder(SoFolder)) { Debug.LogError("Папка не найдена: " + SoFolder); return; }

        Prepare(p0); Prepare(p1);
        var s0 = AssetDatabase.LoadAssetAtPath<Texture2D>(p0);
        var s1 = AssetDatabase.LoadAssetAtPath<Texture2D>(p1);
        var l0 = FirstFrame(s0, p0, "fire_layer_0");
        var l1 = FirstFrame(s1, p1, "fire_layer_1");

        string soPath = SoFolder + "/Fire.asset";
        var so = AssetDatabase.LoadAssetAtPath<BlockDataSO>(soPath);
        if (so == null) { so = ScriptableObject.CreateInstance<BlockDataSO>(); AssetDatabase.CreateAsset(so, soPath); }
        so.Type = BlockType.Fire;
        so.BlockName = "Fire";
        so.IsSolid = false;
        so.IsOpaque = false;
        so.IsBreakable = true;
        so.Hardness = 0f;
        so.TopTexture = l0;      // слой 0
        so.SideTexture = l1;     // слой 1
        so.BottomTexture = null;
        EditorUtility.SetDirty(so);

        var wm = Object.FindFirstObjectByType<WorldManager>();
        if (wm != null)
        {
            var wmSo = new SerializedObject(wm);
            var list = wmSo.FindProperty("blockTypes");
            bool has = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == so) { has = true; break; }
            if (!has) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = so; }
            wmSo.FindProperty("fireAnimationStrip0").objectReferenceValue = s0;
            wmSo.FindProperty("fireAnimationStrip1").objectReferenceValue = s1;
            wmSo.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wm.gameObject.scene);
        }
        else Debug.LogWarning("WorldManager не найден в открытой сцене: добавьте Fire.asset в Block Types и ленты в поля вручную.");

        AssetDatabase.SaveAssets();
        Debug.Log("Огонь настроен. Сохраните сцену (Ctrl+S).");
    }

    private static string FindPath(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Texture2D"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == name) return p;
        }
        return null;
    }

    private static void Prepare(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        imp.isReadable = true;
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
    }

    private static Texture2D FirstFrame(Texture2D strip, string stripPath, string name)
    {
        string path = Path.GetDirectoryName(stripPath).Replace('\\', '/') + "/" + name + ".png";
        var f = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        f.SetPixels(strip.GetPixels(0, strip.height - 16, 16, 16));   // верхний кадр ленты
        File.WriteAllBytes(path, f.EncodeToPNG());
        Object.DestroyImmediate(f);
        AssetDatabase.ImportAsset(path);
        Prepare(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif