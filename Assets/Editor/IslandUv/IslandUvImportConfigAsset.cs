#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class IslandUvImportConfigAsset
{
    private static IslandUvImportConfig _instance;

    public static IslandUvImportConfig Instance
    {
        get
        {
            if (_instance == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:IslandUvImportConfig");
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _instance = AssetDatabase.LoadAssetAtPath<IslandUvImportConfig>(path);
                }
                else
                {
                    Debug.LogError("IslandUvImportConfig asset not found. Please create one via Assets > Create > Island UV > Import Config.");
                }
            }
            return _instance;
        }
    }
}
#endif