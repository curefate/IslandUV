using System;
using UnityEditor;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;

namespace IslandUV.Editor
{

/// <summary>
/// Shared IslandUV settings used by importer.userData, postprocessor, and mesh processor.
/// Editor-only by design (lives under Assets/Editor).
/// </summary>
public static class IslandUvSettings
{
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
        [Tooltip("Allow UVs to span across multiple sub-meshes.")]
        public bool allowAcrossSubMeshes = true;

        [Tooltip("How UVs are propagated across the mesh. Local = allow chaining, Island = derive from normal of island.")]
        public Propagation propagation = Propagation.Local;

        public bool ignoreSmall = false;
        public SmallIsland smallIsland = SmallIsland.TriCount;
        [Min(0)] public int minIslandTris = 4;
        [Range(0f, 1f)] public float minIslandAreaRatio = 0.001f;
    }
}

/// <summary>
/// Read/write IslandUV settings stored in AssetImporter.userData as JSON.
/// Format: { "IslandUV": { "version": 1, ...settings... } }
/// The root object may contain other keys; we only touch the "IslandUV" object.
/// </summary>
public static class IslandUvImporterSettings
{
    public const string RootKey = "IslandUV";
    public const int CurrentVersion = 1;

    public static IslandUvSettings.Settings DefaultSettings => new IslandUvSettings.Settings();

    public static bool TryGetSettings(AssetImporter importer, out IslandUvSettings.Settings settings, out bool usedDefault)
    {
        settings = DefaultSettings;
        usedDefault = true;
        if (importer == null) return false;

        string userData = importer.userData;
        if (string.IsNullOrWhiteSpace(userData)) return true;

        if (!TryGetRootObject(userData, out var root))
            return true; // parse failure -> default

        var node = root[RootKey];
        if (node == null || node.Type == JTokenType.Null)
            return true; // missing -> default

        try
        {
            if (node is not JObject islandRoot)
                return true; // unexpected -> default

            // Schema A:
            // { "IslandUV": { "version": 1, "settings": { ... } } }
            // No old-format compatibility by request.

            int version = islandRoot["version"]?.Value<int>() ?? 0;
            if (version != CurrentVersion)
                return true; // unsupported version -> default

            if (islandRoot["settings"] is not JObject settingsObj)
                return true;

            settings = settingsObj.ToObject<IslandUvSettings.Settings>();
            if (settings == null)
                return true;

            usedDefault = false;
            return true;
        }
        catch
        {
            return true; // any failure -> default
        }
    }

    public static void SetSettings(AssetImporter importer, IslandUvSettings.Settings settings)
    {
        if (importer == null) throw new ArgumentNullException(nameof(importer));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        // Preserve other root keys.
        if (!TryGetRootObject(importer.userData, out var root))
        {
            // If userData is not valid JSON object, we don't touch it.
            // This is consistent with previous behavior: parse failure -> defaults and no overwrite.
            return;
        }

        root[RootKey] = new JObject
        {
            ["version"] = CurrentVersion,
            ["settings"] = JObject.FromObject(settings),
        };
        importer.userData = root.ToString(Formatting.None);
    }

    public static void ClearSettings(AssetImporter importer)
    {
        if (importer == null) throw new ArgumentNullException(nameof(importer));

        if (string.IsNullOrWhiteSpace(importer.userData)) return;

        if (!TryGetRootObject(importer.userData, out var root))
            return;

        if (root.Property(RootKey) == null) return;
        root.Remove(RootKey);

        importer.userData = root.HasValues ? root.ToString(Formatting.None) : string.Empty;
    }

    public static bool SettingsEqual(IslandUvSettings.Settings a, IslandUvSettings.Settings b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;

        // Float comparisons: small epsilon.
        const float eps = 1e-5f;
        bool feq(float x, float y) => Mathf.Abs(x - y) <= eps;

        return a.enabled == b.enabled
            && feq(a.thresholdDeg, b.thresholdDeg)
            && a.targetUvChannel == b.targetUvChannel
            && a.allowAcrossSubMeshes == b.allowAcrossSubMeshes
            && a.propagation == b.propagation
            && a.ignoreSmall == b.ignoreSmall
            && a.smallIsland == b.smallIsland
            && a.minIslandTris == b.minIslandTris
            && feq(a.minIslandAreaRatio, b.minIslandAreaRatio);
    }

    private static bool TryGetRootObject(string userData, out JObject root)
    {
        root = null;

        // If empty, treat as a new object we can write into.
        if (string.IsNullOrWhiteSpace(userData))
        {
            root = new JObject();
            return true;
        }

        try
        {
            var token = JToken.Parse(userData);
            root = token as JObject;
            return root != null;
        }
        catch
        {
            // Don't touch userData we can't parse.
            root = null;
            return false;
        }
    }
}

}