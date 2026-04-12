#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class IslandUvImporterSettingsWindow : EditorWindow
{
    private Object _selected;
    private AssetImporter _importer;

    private IslandUvSettings.Settings _applied;
    private IslandUvSettings.Settings _editing;
    private bool _usedDefault;

    private string _status;

    // --- IslandId Picker (SceneView) ---
    private bool _pickerEnabled = true;
    private bool _pickerCopyToClipboard = true;

    [MenuItem("Tools/Island UV/Importer Settings")]
    public static void Open()
    {
        GetWindow<IslandUvImporterSettingsWindow>(false, "IslandUV Importer", true);
    }

    private void OnEnable()
    {
        RefreshFromSelection();
        Selection.selectionChanged += OnSelectionChanged;

        SceneView.duringSceneGui += DuringSceneGui;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;

        SceneView.duringSceneGui -= DuringSceneGui;
    }

    private void OnSelectionChanged()
    {
        RefreshFromSelection();
        Repaint();
    }

    private void RefreshFromSelection()
    {
        _selected = Selection.activeObject;
        _importer = null;
        _status = null;

        if (_selected == null)
        {
            _applied = null;
            _editing = null;
            return;
        }

        string path = AssetDatabase.GetAssetPath(_selected);
        if (string.IsNullOrEmpty(path))
        {
            _applied = null;
            _editing = null;
            return;
        }

        _importer = AssetImporter.GetAtPath(path);
        if (_importer == null)
        {
            _applied = null;
            _editing = null;
            return;
        }

        // Only show for model assets (simple extension check keeps it lightweight).
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".fbx" && ext != ".obj" && ext != ".dae" && ext != ".blend")
        {
            _status = "Selected asset doesn't look like a model file.";
        }

        IslandUvImporterSettings.TryGetSettings(_importer, out _applied, out _usedDefault);
        _editing = Clone(_applied);
    }

    private static IslandUvSettings.Settings Clone(IslandUvSettings.Settings s)
    {
        if (s == null) return null;
        return new IslandUvSettings.Settings
        {
            enabled = s.enabled,
            thresholdDeg = s.thresholdDeg,
            targetUvChannel = s.targetUvChannel,
            allowAcrossSubMeshes = s.allowAcrossSubMeshes,
            normalSource = s.normalSource,
            propagation = s.propagation,
            ignoreSmall = s.ignoreSmall,
            smallIsland = s.smallIsland,
            minIslandTris = s.minIslandTris,
            minIslandAreaRatio = s.minIslandAreaRatio,
        };
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("IslandUV Importer Settings", EditorStyles.boldLabel);

        if (_selected == null)
        {
            EditorGUILayout.HelpBox("Select a model asset in the Project window.", MessageType.Info);
            return;
        }

        string path = AssetDatabase.GetAssetPath(_selected);
        EditorGUILayout.LabelField("Asset", path);

        if (_importer == null)
        {
            EditorGUILayout.HelpBox("No AssetImporter found for the selected asset.", MessageType.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.Info);

        if (_usedDefault)
            EditorGUILayout.HelpBox("No IslandUV data found (or parse failed). Using defaults.", MessageType.Info);

        if (_editing == null)
        {
            EditorGUILayout.HelpBox("Internal: settings missing.", MessageType.Error);
            return;
        }

        DrawSettings(_editing);

        DrawIslandIdPicker();

        bool isDirty = !IslandUvImporterSettings.SettingsEqual(_editing, _applied);

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear", GUILayout.Width(100)))
            {
                Clear();
            }

            GUI.enabled = isDirty;
            if (GUILayout.Button("Revert", GUILayout.Width(100)))
            {
                _editing = Clone(_applied);
                _status = "Reverted.";
            }
            if (GUILayout.Button("Apply", GUILayout.Width(100)))
            {
                Apply();
            }
            GUI.enabled = true;
        }

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.None);
    }

    private static void DrawSettings(IslandUvSettings.Settings s)
    {
        EditorGUILayout.Space(6);
        s.enabled = EditorGUILayout.ToggleLeft("Enabled", s.enabled);

        using (new EditorGUI.DisabledScope(!s.enabled))
        {
            s.thresholdDeg = EditorGUILayout.Slider("Threshold (deg)", s.thresholdDeg, 0f, 90f);
            s.targetUvChannel = EditorGUILayout.IntSlider("Target UV Channel", s.targetUvChannel, 0, 7);
            s.allowAcrossSubMeshes = EditorGUILayout.Toggle("Allow Across SubMeshes", s.allowAcrossSubMeshes);

            EditorGUILayout.Space(6);
            s.normalSource = (IslandUvSettings.NormalSource)EditorGUILayout.EnumPopup("Normal Source", s.normalSource);
            s.propagation = (IslandUvSettings.Propagation)EditorGUILayout.EnumPopup("Propagation", s.propagation);

            EditorGUILayout.Space(6);
            s.ignoreSmall = EditorGUILayout.Toggle("Ignore Small Islands", s.ignoreSmall);
            using (new EditorGUI.DisabledScope(!s.ignoreSmall))
            {
                s.smallIsland = (IslandUvSettings.SmallIsland)EditorGUILayout.EnumPopup("Small Island Mode", s.smallIsland);
                if (s.smallIsland == IslandUvSettings.SmallIsland.TriCount)
                {
                    s.minIslandTris = Mathf.Max(0, EditorGUILayout.IntField("Min Island Tris", s.minIslandTris));
                }
                else
                {
                    s.minIslandAreaRatio = EditorGUILayout.Slider("Min Area Ratio", s.minIslandAreaRatio, 0f, 1f);
                }
            }
        }
    }

    private void Apply()
    {
        if (_importer == null || _editing == null)
            return;

        try
        {
            IslandUvImporterSettings.SetSettings(_importer, _editing);

            // Reimport only on Apply.
            _importer.SaveAndReimport();

            IslandUvImporterSettings.TryGetSettings(_importer, out _applied, out _usedDefault);
            _editing = Clone(_applied);
            _status = "Applied and reimported.";
        }
        catch (System.Exception ex)
        {
            _status = "Apply failed: " + ex.Message;
        }
    }

    private void Clear()
    {
        if (_importer == null) return;

        try
        {
            IslandUvImporterSettings.ClearSettings(_importer);
            _importer.SaveAndReimport();

            // Reset to defaults (no userData IslandUV key)
            IslandUvImporterSettings.TryGetSettings(_importer, out _applied, out _usedDefault);
            _editing = Clone(_applied);
            _status = "Cleared IslandUV userData and reimported.";
        }
        catch (System.Exception ex)
        {
            _status = "Clear failed: " + ex.Message;
        }
    }

    private void DrawIslandIdPicker()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("IslandId Picker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "In Scene View, left-click a MeshCollider to read islandId from vertex color (R/G).\n" +
            "Ignored islands use id=65535 (0xFFFF).",
            MessageType.Info);

        _pickerEnabled = EditorGUILayout.ToggleLeft("Enable picking", _pickerEnabled);
        _pickerCopyToClipboard = EditorGUILayout.ToggleLeft("Copy result to clipboard", _pickerCopyToClipboard);
    }

    private void DuringSceneGui(SceneView sceneView)
    {
        if (!_pickerEnabled) return;

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

        // Require MeshCollider for stable triangleIndex.
        var mc = hit.collider as MeshCollider;
        if (mc != null && mc.sharedMesh != null)
        {
            if (TryReadIslandIdFromMeshHit(mc.sharedMesh, hit.triangleIndex, out ushort islandId))
            {
                Report(go, islandId);
                if (_pickerCopyToClipboard) EditorGUIUtility.systemCopyBuffer = islandId.ToString();
                if (e.control) e.Use();
                return;
            }
        }

        var mf = go.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
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

        islandId = Majority(id0, id1, id2);
        return true;
    }

    private static ushort Majority(ushort a, ushort b, ushort c)
    {
        if (a == b || a == c) return a;
        if (b == c) return b;
        return a;
    }

    private static void Report(GameObject go, ushort islandId)
    {
        string hex = $"0x{islandId:X4}";
        Debug.Log($"[IslandUV] {go.name}: islandId = {islandId} ({hex})", go);
    }
}
#endif
