#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace IslandUV.Editor
{

public class IslandUvPostProcessor : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        var importer = assetImporter as ModelImporter;
    if (importer == null) return;

    // Only require readable meshes when IslandUV is enabled.
    // This avoids forcing Read/Write on unrelated models.
    IslandUvImporterSettings.TryGetSettings(importer, out var settings, out _);
    if (settings != null && settings.enabled)
        importer.isReadable = true;
    }

    void OnPostprocessModel(GameObject model)
    {
    var importer = assetImporter;
    IslandUvImporterSettings.TryGetSettings(importer, out var settings, out var usedDefault);
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
    }
}

}
#endif
