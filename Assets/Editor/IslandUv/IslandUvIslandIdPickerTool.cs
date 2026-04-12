#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// SceneView tool that picks an islandId by clicking a MeshCollider and reading the vertex color (R/G) of the hit triangle.
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

    private static bool TryReadIslandIdFromMeshHit(Mesh mesh, int triangleIndex, out ushort islandId)
    {
        islandId = 0;
        if (mesh == null) return false;
        if (triangleIndex < 0) return false;

        var colors = mesh.colors32;
        if (colors == null || colors.Length != mesh.vertexCount) return false;

        var tris = mesh.triangles;
        int triStart = triangleIndex * 3;
        if (tris == null || triStart + 2 >= tris.Length) return false;

        int i0 = tris[triStart];
        int i1 = tris[triStart + 1];
        int i2 = tris[triStart + 2];

        ushort id0 = (ushort)(colors[i0].r | (colors[i0].g << 8));
        ushort id1 = (ushort)(colors[i1].r | (colors[i1].g << 8));
        ushort id2 = (ushort)(colors[i2].r | (colors[i2].g << 8));

        islandId = Majority(id0, id1, id2);
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
