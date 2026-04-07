#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class IslandUvPostProcessor : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        var importer = assetImporter as ModelImporter;
        if (importer != null)
        {
            importer.isReadable = true;  // 确保模型可读
        }
    }

    void OnPostprocessModel(GameObject model)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var settings = IslandUvImportConfigAsset.Instance.LoadSettings(guid);
        if (settings == null || !settings.enabled) return;

        var mfs = model.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in mfs)
        {
            var mesh = mf.sharedMesh;
            if (mesh != null)
            {
                IslandUvMeshProcessor.ProcessMesh(mesh, settings);
            }
        }

        Debug.Log($"Processed IslandUV for model: {assetPath}");
    }
}
#endif