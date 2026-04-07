#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "IslandUvImportConfig", menuName = "Island UV/Import Config")]
public class IslandUvImportConfig : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public UnityEngine.Object modelAsset;

        public Settings settings = new();
    }


    [Serializable]
    public class Settings
    {
        [Header("General")]
        public bool enabled = true;

        [Tooltip("法线夹角阈值（degrees）。相邻三角面法线夹角 <= 阈值则归为同一Island。")]
        [Range(0f, 90f)]
        public float normalAngleThresholdDeg = 25f;

        [Tooltip("写入 TextUV 的 UV 通道（0..7 => UV0..UV7 / TEXCOORD0..7）。建议用 2（UV2）。")]
        [Range(0, 7)]
        public int targetUvChannel = 2;

        [Tooltip("是否进行按 Island 的顶点拆分。")]
        public bool splitVertices = true;

        /* [Header("TextUV Mapping")]
        [Tooltip("每个 Island 的平面投影 TextUV 缩放：数值越大 => 文字 tile 越大（UV 变化越慢）。")]
        public float tileWorldSize = 1.0f; */

        [Tooltip("是否把投影坐标归一化到 [0,1] 范围。")]
        public bool normalizePerIsland = false;
    }

    public List<Entry> entries = new();

    public Settings? LoadSettings(string guid)
    {
        foreach (var entry in entries)
        {
            if (entry.modelAsset != null)
            {
                string entryGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.modelAsset));
                if (entryGuid == guid)
                {
                    return entry.settings;
                }
            }
        }

        return null;
    }
}
#endif