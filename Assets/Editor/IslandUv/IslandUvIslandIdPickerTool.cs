#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SceneView tool that picks an islandId by clicking a MeshCollider and reading the islandId encoded in UV.zw.
/// </summary>
public sealed class IslandUvIslandIdPickerTool
{
    public delegate void PickedHandler(GameObject go, ushort islandId);

    private readonly PickedHandler _onPicked;

    public bool Enabled { get; set; } = true;
    public bool CopyToClipboard { get; set; } = true;

    public IslandUvIslandIdPickerTool(PickedHandler onPicked)
    {
        _onPicked = onPicked;
    }

    public void Attach()
    {
        SceneView.duringSceneGui += DuringSceneGui;
    }

    public void Detach()
    {
        SceneView.duringSceneGui -= DuringSceneGui;
    }

    private readonly List<Vector4> _uv4 = new List<Vector4>(1024);

    private void DuringSceneGui(SceneView sceneView)
    {
        if (!Enabled) return;

        var e = Event.current;
        if (e == null) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;
        if (e.alt) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!Physics.Raycast(ray, out var hit, Mathf.Infinity)) return;

        var go = hit.collider != null ? hit.collider.gameObject : null;
        if (go == null) return;

        var mc = hit.collider as MeshCollider;
        if (mc == null || mc.sharedMesh == null) return;

    if (!TryReadIslandIdFromMeshHit(mc.sharedMesh, hit.triangleIndex, out ushort islandId))
            return;

        _onPicked?.Invoke(go, islandId);
        if (CopyToClipboard) EditorGUIUtility.systemCopyBuffer = islandId.ToString();

        // Optional: swallow the click (helps prevent accidental scene interaction). Keep it behind Ctrl to avoid surprising behavior.
        if (e.control) e.Use();
    }

    private bool TryReadIslandIdFromMeshHit(Mesh mesh, int triangleIndex, out ushort islandId)
    {
        islandId = 0;
        if (mesh == null) return false;
        if (triangleIndex < 0) return false;

        if (!TryGetImporterUvChannel(mesh, out int uvChannel))
            return false;

        _uv4.Clear();
        mesh.GetUVs(uvChannel, _uv4);
        if (_uv4 == null || _uv4.Count != mesh.vertexCount)
            return false;

        var tris = mesh.triangles;
        int triStart = triangleIndex * 3;
        if (tris == null || triStart + 2 >= tris.Length) return false;

        int i0 = tris[triStart];
        int i1 = tris[triStart + 1];
        int i2 = tris[triStart + 2];

    ushort id0 = DecodeIdFromZW(_uv4[i0].z, _uv4[i0].w);
    ushort id1 = DecodeIdFromZW(_uv4[i1].z, _uv4[i1].w);
    ushort id2 = DecodeIdFromZW(_uv4[i2].z, _uv4[i2].w);

        islandId = Majority(id0, id1, id2);
        return true;
    }

    private static ushort DecodeIdFromZW(float z, float w)
    {
        // Encoded as: z=lo/255, w=hi/255.
        int lo = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(z) * 255f), 0, 255);
        int hi = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(w) * 255f), 0, 255);
        return (ushort)(lo | (hi << 8));
    }

    private static bool TryGetImporterUvChannel(Mesh mesh, out int uvChannel)
    {
        uvChannel = 2;
        if (mesh == null) return false;

        // Try to resolve the mesh asset path -> importer -> IslandUV settings.
        string path = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(path))
            return false;

        var importer = AssetImporter.GetAtPath(path);
        if (importer == null)
            return false;

        IslandUvImporterSettings.TryGetSettings(importer, out var settings, out _);
        if (settings == null)
            return false;

        uvChannel = Mathf.Clamp(settings.targetUvChannel, 0, 7);
        return true;
    }

    private static ushort Majority(ushort a, ushort b, ushort c)
    {
        if (a == b || a == c) return a;
        if (b == c) return b;
        return a;
    }
}
#endif
