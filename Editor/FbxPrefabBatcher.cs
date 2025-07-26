using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class FbxPrefabBatcher : EditorWindow
{
    #region === CONFIGURATION ===

    private const string MATERIALS_DIR = "Materials";
    private const string TEXTURES_DIR = "Textures";
    private const string SHARED_MATERIALS_DIR = "SharedMaterials";

    #endregion

    #region === UI PROPERTIES ===

    [Header("Input / Output")]
    private Object inputAsset;
    private DefaultAsset outputFolder;

    [Header("Prefab Settings")]
    private string objectTag = "Untagged";
    private enum ColliderType { None, Box, Sphere, Capsule, Mesh }
    private ColliderType selectedCollider = ColliderType.None;
    private MonoScript scriptToAdd;

    [Header("Processing Options")]
    private bool treatAsMultipleModels = false;
    private bool extractMaterials = false;

    #endregion

    #region === ENTRY POINT ===

    [MenuItem("Tools/FBX Prefab Batcher")]
    public static void ShowWindow() => GetWindow<FbxPrefabBatcher>("FBX Prefab Batcher");

    private void OnGUI()
    {
        GUILayout.Label("FBX to Prefab Converter", EditorStyles.boldLabel);

        inputAsset = EditorGUILayout.ObjectField("Input (Folder or FBX)", inputAsset, typeof(Object), false);
        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space();
        GUILayout.Label("Prefab Settings", EditorStyles.boldLabel);
        objectTag = EditorGUILayout.TagField("Tag", objectTag);
        selectedCollider = (ColliderType)EditorGUILayout.EnumPopup("Add Collider", selectedCollider);
        scriptToAdd = (MonoScript)EditorGUILayout.ObjectField("Add Script", scriptToAdd, typeof(MonoScript), false);

        EditorGUILayout.Space();
        GUILayout.Label("Processing Options", EditorStyles.boldLabel);
        treatAsMultipleModels = EditorGUILayout.Toggle("Multiple Models", treatAsMultipleModels);
        extractMaterials = EditorGUILayout.Toggle("Extract Materials", extractMaterials);

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Prefabs"))
            GeneratePrefabs();
    }

    #endregion

    #region === PREFAB PROCESSING ===

    private void GeneratePrefabs()
    {
        if (inputAsset == null || outputFolder == null)
        {
            Debug.LogWarning("Please set both input and output.");
            return;
        }

        string inputPath = AssetDatabase.GetAssetPath(inputAsset);
        string outputPath = AssetDatabase.GetAssetPath(outputFolder);

        string[] assetPaths = Directory.Exists(inputPath)
            ? System.Array.ConvertAll(AssetDatabase.FindAssets("t:GameObject", new[] { inputPath }), AssetDatabase.GUIDToAssetPath)
            : new[] { inputPath };

        Dictionary<string, int> materialUsageCount = new();
        foreach (string assetPath in assetPaths)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null) continue;
            var prefabSources = treatAsMultipleModels ? GetChildGameObjects(model) : new List<GameObject> { model };

            foreach (var prefabSource in prefabSources)
            {
                foreach (var renderer in prefabSource.GetComponentsInChildren<Renderer>())
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat == null) continue;
                        if (!materialUsageCount.ContainsKey(mat.name)) materialUsageCount[mat.name] = 0;
                        materialUsageCount[mat.name]++;
                    }
                }
            }
        }

        Dictionary<string, Material> sharedMaterialMap = new();
        string sharedMatFolder = Path.Combine(outputPath, SHARED_MATERIALS_DIR);
        EnsureFolderExists(sharedMatFolder);
        string sharedMatPath = ToRelativePath(sharedMatFolder);

        foreach (string assetPath in assetPaths)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null) continue;
            var prefabSources = treatAsMultipleModels ? GetChildGameObjects(model) : new List<GameObject> { model };

            foreach (var prefabSource in prefabSources)
            {
                if (prefabSource == null) continue;

                GameObject instance = Instantiate(prefabSource);
                ProcessObject(instance);

                if (extractMaterials)
                    ExtractAndReassignMaterials(instance, prefabSource.name, outputPath, sharedMatPath, sharedMaterialMap, materialUsageCount);

                string fileName = $"{prefabSource.name}.prefab";
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(outputPath, fileName));
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                DestroyImmediate(instance);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Prefabs generated successfully.");
    }

    private List<GameObject> GetChildGameObjects(GameObject root)
    {
        GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(root);
        var list = new List<GameObject>();
        foreach (Transform child in temp.transform)
        {
            GameObject childCopy = Instantiate(child.gameObject);
            list.Add(childCopy);
        }
        DestroyImmediate(temp);
        return list;
    }

    private void ProcessObject(GameObject obj)
    {
        obj.tag = objectTag;

        switch (selectedCollider)
        {
            case ColliderType.Box: obj.AddComponent<BoxCollider>(); break;
            case ColliderType.Sphere: obj.AddComponent<SphereCollider>(); break;
            case ColliderType.Capsule: obj.AddComponent<CapsuleCollider>(); break;
            case ColliderType.Mesh:
                var meshCollider = obj.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                break;
        }

        if (scriptToAdd != null)
        {
            System.Type scriptType = scriptToAdd.GetClass();
            if (scriptType != null && scriptType.IsSubclassOf(typeof(MonoBehaviour)))
                obj.AddComponent(scriptType);
        }
    }

    #endregion

    #region === MATERIAL HANDLING ===

    private void ExtractAndReassignMaterials(GameObject obj, string prefabName, string exportRoot, string sharedMatPath, Dictionary<string, Material> sharedMaterialMap, Dictionary<string, int> materialUsageCount)
    {
        string matPath = Path.Combine(exportRoot, MATERIALS_DIR, prefabName);
        string texPath = Path.Combine(exportRoot, TEXTURES_DIR, prefabName);

        string relativeMatPath = ToRelativePath(matPath);
        string relativeTexPath = ToRelativePath(texPath);

        if (!IsAssetPathValid(relativeMatPath) || !IsAssetPathValid(relativeTexPath)) return;

        var savedMaterials = new Dictionary<string, Material>();
        bool matUsed = false, texUsed = false;

        foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var original = mats[i];
                if (original == null) continue;

                bool isShared = materialUsageCount.TryGetValue(original.name, out int count) && count > 1;
                string matFolder = isShared ? sharedMatPath : relativeMatPath;

                if (isShared && sharedMaterialMap.TryGetValue(original.name, out var shared))
                {
                    mats[i] = shared;
                    continue;
                }

                if (!isShared && savedMaterials.TryGetValue(original.name, out var cached))
                {
                    mats[i] = cached;
                    continue;
                }

                if (isShared) EnsureFolderExists(sharedMatPath);
                else if (!matUsed) EnsureFolderExists(matPath);

                string newMatPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(matFolder, original.name + ".mat"));
                var matCopy = new Material(original);

                if (TryExtractMainTex(original, relativeTexPath, out var extractedTex))
                {
                    if (!texUsed) EnsureFolderExists(texPath);
                    matCopy.SetTexture("_MainTex", extractedTex);
                    texUsed = true;
                }

                AssetDatabase.CreateAsset(matCopy, newMatPath);
                mats[i] = matCopy;

                if (isShared)
                    sharedMaterialMap[original.name] = matCopy;
                else
                    savedMaterials[original.name] = matCopy;

                matUsed = true;
            }
            renderer.sharedMaterials = mats;
        }

        AssetDatabase.SaveAssets();
        TryDeleteFolderIfEmpty(matPath);
        TryDeleteFolderIfEmpty(texPath);
    }

    private bool TryExtractMainTex(Material mat, string texTargetFolder, out Texture2D newTex)
    {
        newTex = null;
        if (!mat.HasProperty("_MainTex")) return false;

        var tex = mat.GetTexture("_MainTex") as Texture2D;
        if (tex == null) return false;

        string assetPath = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(assetPath)) return false;

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return false;

        bool changed = false;
        if (!importer.isReadable) { importer.isReadable = true; changed = true; }

        if (importer is TextureImporter ti)
        {
            if (ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (ti.textureType != TextureImporterType.Default)
            { ti.textureType = TextureImporterType.Default; changed = true; }
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.mipmapEnabled = false;
        }

        if (changed) importer.SaveAndReimport();

        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (tex == null || !tex.isReadable) return false;

        byte[] pngData = tex.EncodeToPNG();
        if (pngData == null || pngData.Length == 0) return false;

        if (!AssetDatabase.IsValidFolder(texTargetFolder)) EnsureFolderExists(texTargetFolder);
        if (!Directory.Exists(texTargetFolder)) return false;

        string fileName = tex.name + ".png";
        string texPath = AssetDatabase.GenerateUniqueAssetPath(ToRelativePath(Path.Combine(texTargetFolder, fileName)));

        File.WriteAllBytes(texPath, pngData);
        AssetDatabase.ImportAsset(texPath);
        newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        return newTex != null;
    }

    #endregion

    #region === UTILITY ===

    private void TryDeleteFolderIfEmpty(string fullPath)
    {
        if (!Directory.Exists(fullPath)) return;
        if (Directory.GetFiles(fullPath).Length == 0 && Directory.GetDirectories(fullPath).Length == 0)
            AssetDatabase.DeleteAsset(ToRelativePath(fullPath));
    }

    private void EnsureFolderExists(string fullPath)
    {
        string[] parts = ToRelativePath(fullPath).Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private string ToRelativePath(string fullPath)
        => fullPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");

    private bool IsAssetPathValid(string relativePath)
    {
        if (!relativePath.StartsWith("Assets"))
        {
            Debug.LogError("Invalid asset path: " + relativePath);
            return false;
        }
        return true;
    }

    #endregion
}