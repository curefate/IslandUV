#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "IslandUvImportConfig", menuName = "Island UV/Import Config")]
public class IslandUvImportConfig : ScriptableObject
{
    public enum NormalSource
    {
        Face = 0,
        Vertex = 1,
    }

    public enum Propagation
    {
        Local = 0,
        Island = 1,
    }

    public enum SmallIsland
    {
        TriCount = 0,
        AreaRatio = 1,
    }

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

        [Range(0f, 90f)]
        public float normalAngleThresholdDeg = 25f;

        [Range(0, 7)]
        public int targetUvChannel = 2;

        [Tooltip("是否把 IslandId 写入顶点色（Color32）。启用后：R/G 编码 16-bit islandId（id = R + G*256），B=0，A=255。便于 shader 进行 per-island 配置。")]
        public bool writeIslandIdToVertexColor = true;

        [Tooltip("是否允许跨 SubMesh 进行相邻三角形连通（影响 Island 聚类）。默认开启：忽略材质分组，仅按几何相邻+法线阈值聚类。")]
        public bool allowAcrossSubMeshes = true;

        /* [Tooltip("是否进行按 Island 的顶点拆分。")]
        [HideInInspector]
        public bool splitVertices = true; */

        /* [Header("TextUV Mapping")]
        [Tooltip("每个 Island 的平面投影 TextUV 缩放：数值越大 => 文字 tile 越大（UV 变化越慢）。")]
        public float tileWorldSize = 1.0f; */

        /* [Tooltip("是否把投影坐标归一化到 [0,1] 范围。")]
        public bool normalizeUv = false; */

        [Header("Island")]
        [Tooltip("相邻三角形夹角判断使用的法线来源：Face=几何面法线；Vertex=三角形三个顶点法线平均后的方向（更贴近渲染的平滑/硬边）。若 Mesh 无顶点法线，将自动回退到 Face 并给出警告。")]
        public NormalSource normalSource = NormalSource.Vertex;

        [Tooltip("岛的生长方式：Local=允许链式传递（每条边满足阈值即可连通）；Island=对比岛参考法线（抑制链式漂移，更不容易把弯曲面吞成一个大岛）。")]
        public Propagation propagation = Propagation.Local;

        [Header("Small Island")]
        [Tooltip("是否忽略过小的 Island。启用后，小岛会写入固定 UV（建议对应贴图的空白区）。")]
        public bool ignoreSmall = false;

        [Tooltip("忽略小岛的阈值类型：TriCount=按岛三角形数量；AreaRatio=按岛几何面积占全模型几何面积的比例。")]
        public SmallIsland smallIsland = SmallIsland.TriCount;

        [Min(0)]
        [Tooltip("当阈值类型为 TriCount 时：岛的三角形数小于该值将被忽略。")]
        public int minIslandTris = 4;

        [Range(0f, 1f)]
        [Tooltip("当阈值类型为 AreaRatio 时：岛面积/总面积小于该值将被忽略。例如 0.01 表示 1%。")]
        public float minIslandAreaRatio = 0.001f;

        [Tooltip("被忽略的小岛写入的固定 UV。默认 (0,0)。")]
        public Vector2 ignoredUv = Vector2.zero;
    }

    public Settings defaultSettings = new();
    public List<Entry> entries = new();

    public Settings LoadSettings(string guid)
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
        return defaultSettings;
    }
}
#endif