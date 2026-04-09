#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class IslandUvIslandIdPickerWindow : EditorWindow
{
    private bool _enabled = true;
    private bool _copyToClipboard = true;
    private bool _autoFillSelectedOverride = false;

    [MenuItem("Tools/Island UV/IslandId Picker")]
    public static void Open()
    {
        var w = GetWindow<IslandUvIslandIdPickerWindow>();
        w.titleContent = new GUIContent("IslandId Picker");
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Click a mesh in Scene View to read its islandId encoded in vertex color (R/G).\n" +
            "Requires: IslandUvImportConfig.Settings.writeIslandIdToVertexColor = true.\n" +
            "Ignored islands use id=65535 (0xFFFF).",
            MessageType.Info);

        _enabled = EditorGUILayout.ToggleLeft("Enable picking", _enabled);
        _copyToClipboard = EditorGUILayout.ToggleLeft("Copy result to clipboard", _copyToClipboard);
        _autoFillSelectedOverride = EditorGUILayout.ToggleLeft("Auto-fill selected IslandUvPerRendererOverrides (first empty Id slot)", _autoFillSelectedOverride);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Tip", "Hold Ctrl while clicking to avoid moving Scene selection.");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += DuringSceneGui;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGui;
    }

    private void DuringSceneGui(SceneView sceneView)
    {
        if (!_enabled) return;

        var e = Event.current;
        if (e == null) return;

        // Left-click in scene view.
        if (e.type != EventType.MouseDown || e.button != 0) return;

        // Don’t interfere when user is navigating.
        if (e.alt) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!Physics.Raycast(ray, out var hit, Mathf.Infinity)) return;

        var go = hit.collider != null ? hit.collider.gameObject : null;
        if (go == null) return;

        // Try MeshCollider first (best: hit.triangleIndex is valid)
        var mc = hit.collider as MeshCollider;
        if (mc != null && mc.sharedMesh != null)
        {
            if (TryReadIslandIdFromMeshHit(mc.sharedMesh, hit.triangleIndex, out ushort islandId))
            {
                Report(go, islandId);
                if (_autoFillSelectedOverride) TryAutofillSelectedOverrides(islandId);
                if (_copyToClipboard) EditorGUIUtility.systemCopyBuffer = islandId.ToString();

                // Optional: keep selection stable unless user wants it.
                if (e.control) e.Use();
                return;
            }
        }

        // Fallback: try MeshFilter/SkinnedMeshRenderer mesh, but we can’t map triangleIndex reliably without MeshCollider.
        // Still, we can attempt if collider mesh equals filter mesh; otherwise we warn.
        var mf = go.GetComponent<MeshFilter>();
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        Mesh m = mf != null ? mf.sharedMesh : (smr != null ? smr.sharedMesh : null);
        if (m != null)
        {
            Debug.LogWarning(
                $"[IslandUV] Hit '{go.name}' but collider is not a MeshCollider with a readable mesh. Add a MeshCollider to enable islandId picking.",
                go);
        }
    }

    private static bool TryReadIslandIdFromMeshHit(Mesh mesh, int triangleIndex, out ushort islandId)
    {
        islandId = 0;
        if (mesh == null) return false;
        if (triangleIndex < 0) return false;

        var colors = mesh.colors32;
        if (colors == null || colors.Length != mesh.vertexCount) return false;

        // triangleIndex is into the combined triangle list across submeshes when using MeshCollider.
        // Unity's MeshCollider uses the Mesh triangles; Mesh.GetTriangles(0) alone isn't enough.
        // We can get the full triangle array via mesh.triangles (combined).
        var tris = mesh.triangles;
        int triStart = triangleIndex * 3;
        if (tris == null || triStart + 2 >= tris.Length) return false;

        int i0 = tris[triStart];
        int i1 = tris[triStart + 1];
        int i2 = tris[triStart + 2];

        // Decode packed id = r + g*256.
        ushort id0 = (ushort)(colors[i0].r | (colors[i0].g << 8));
        ushort id1 = (ushort)(colors[i1].r | (colors[i1].g << 8));
        ushort id2 = (ushort)(colors[i2].r | (colors[i2].g << 8));

        // Usually all three match; if not, pick the majority.
        islandId = Majority(id0, id1, id2);
        return true;
    }

    private static ushort Majority(ushort a, ushort b, ushort c)
    {
        if (a == b || a == c) return a;
        if (b == c) return b;
        return a; // arbitrary fallback
    }

    private static void Report(GameObject go, ushort islandId)
    {
        string hex = $"0x{islandId:X4}";
        Debug.Log($"[IslandUV] {go.name}: islandId = {islandId} ({hex})", go);
    }

    private static void TryAutofillSelectedOverrides(ushort islandId)
    {
        // Very small convenience: if user selected an object with IslandUvPerRendererOverrides,
        // fill the first empty Id slot (== 0xFFFF) of the first enabled override.
        var active = Selection.activeGameObject;
        if (active == null) return;

        var comp = active.GetComponent<IslandUvPerRendererOverrides>();
        if (comp == null) return;

        bool changed = false;
        for (int s = 0; s < comp.overrides.Length; s++)
        {
            if (!comp.overrides[s].enabled) continue;
            var ids = comp.overrides[s].ids;
            if (ids == null) continue;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == 0xFFFF)
                {
                    ids[i] = islandId;
                    comp.overrides[s].ids = ids;
                    changed = true;
                    break;
                }
            }
            if (changed) break;
        }

        if (changed)
        {
            EditorUtility.SetDirty(comp);
            comp.Apply();
            Debug.Log($"[IslandUV] Auto-filled islandId {islandId} into selected '{active.name}'.", active);
        }
    }
}
#endif
