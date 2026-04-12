using System;
using UnityEngine;

/// <summary>
/// Shared IslandUV settings used by importer.userData, postprocessor, and mesh processor.
/// Editor-only by design (lives under Assets/Editor).
/// </summary>
public static class IslandUvSettings
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
    public class Settings
    {
        public bool enabled = false;
        [Range(0f, 90f)] public float thresholdDeg = 25f;
        [Range(0, 7)] public int targetUvChannel = 2;
        public bool allowAcrossSubMeshes = true;

        public NormalSource normalSource = NormalSource.Vertex;
        public Propagation propagation = Propagation.Local;

        public bool ignoreSmall = false;
        public SmallIsland smallIsland = SmallIsland.TriCount;
        [Min(0)] public int minIslandTris = 4;
        [Range(0f, 1f)] public float minIslandAreaRatio = 0.001f;
    }
}
