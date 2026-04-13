#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;

public class IslandUvImporterSettingsWindow : EditorWindow
{
    private Object _selected;
    private AssetImporter _importer;

    private IslandUvSettings.Settings _applied;
    private IslandUvSettings.Settings _editing;
    private bool _usedDefault;

    private string _status;

    // --- IslandId Picker (SceneView) ---
    private IslandUvIslandIdPickerTool _picker;

    [MenuItem("Tools/Island UV/Importer Settings")]
    public static void Open()
    {
        GetWindow<IslandUvImporterSettingsWindow>(false, "IslandUV Importer", true);
    }

    private void OnEnable()
    {
        RefreshFromSelection();
        Selection.selectionChanged += OnSelectionChanged;

        _picker = new IslandUvIslandIdPickerTool(OnPickedIslandId);
        _picker.Attach();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;

        if (_picker != null)
        {
            _picker.Detach();
            _picker = null;
        }
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
        _editing = DeepCopy(_applied);
    }

    private static IslandUvSettings.Settings DeepCopy(IslandUvSettings.Settings s)
    {
        if (s == null) return null;
        // Keep it simple and future-proof: if fields change, we don't need to update this method.
        return JsonConvert.DeserializeObject<IslandUvSettings.Settings>(JsonConvert.SerializeObject(s));
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
                _editing = DeepCopy(_applied);
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
        s.enabled = EditorGUILayout.ToggleLeft(new GUIContent(
            "Enabled",
            "Enable IslandUV processing for this model importer."), s.enabled);

        using (new EditorGUI.DisabledScope(!s.enabled))
        {
            s.thresholdDeg = EditorGUILayout.Slider(new GUIContent(
                "Threshold (deg)",
                "Angle threshold (degrees) used to split triangles into islands."), s.thresholdDeg, 0f, 90f);
            s.targetUvChannel = EditorGUILayout.IntSlider(new GUIContent(
                "Target UV Channel",
                "UV channel index to write results to (0..7)."), s.targetUvChannel, 0, 7);
            s.allowAcrossSubMeshes = EditorGUILayout.Toggle(new GUIContent(
                "Allow Across SubMeshes",
                "Allow UVs to span across multiple sub-meshes."), s.allowAcrossSubMeshes);

            EditorGUILayout.Space(6);
            s.propagation = (IslandUvSettings.Propagation)EditorGUILayout.EnumPopup(new GUIContent(
                "Propagation",
                "How UVs are propagated across the mesh. Local = allow chaining, Island = derive from normal of island."), s.propagation);

            EditorGUILayout.Space(6);
            s.ignoreSmall = EditorGUILayout.Toggle(new GUIContent(
                "Ignore Small Islands",
                "If enabled, small islands are ignored and will use islandId=0xFFFF (65535) and UV=(0,0)."), s.ignoreSmall);
            using (new EditorGUI.DisabledScope(!s.ignoreSmall))
            {
                s.smallIsland = (IslandUvSettings.SmallIsland)EditorGUILayout.EnumPopup(new GUIContent(
                    "Small Island Mode",
                    "How 'small' is evaluated: by triangle count or by estimated area ratio."), s.smallIsland);
                if (s.smallIsland == IslandUvSettings.SmallIsland.TriCount)
                {
                    s.minIslandTris = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent(
                        "Min Island Tris",
                        "Islands with fewer triangles than this value will be ignored."), s.minIslandTris));
                }
                else
                {
                    s.minIslandAreaRatio = EditorGUILayout.Slider(new GUIContent(
                        "Min Area Ratio",
                        "Islands with area ratio below this value will be ignored."), s.minIslandAreaRatio, 0f, 1f);
                }
            }
        }
    }

    private void Apply()
    {
        if (_importer == null || _editing == null)
            return;

        RunImporterAction(
            action: () => IslandUvImporterSettings.SetSettings(_importer, _editing),
            successStatus: "Applied and reimported.",
            failurePrefix: "Apply failed: ");
    }

    private void Clear()
    {
        if (_importer == null) return;

        RunImporterAction(
            action: () => IslandUvImporterSettings.ClearSettings(_importer),
            successStatus: "Cleared IslandUV userData and reimported.",
            failurePrefix: "Clear failed: ");
    }

    private void RunImporterAction(System.Action action, string successStatus, string failurePrefix)
    {
        try
        {
            action?.Invoke();

            // Reimport only on Apply/Clear.
            _importer.SaveAndReimport();

            IslandUvImporterSettings.TryGetSettings(_importer, out _applied, out _usedDefault);
            _editing = DeepCopy(_applied);
            _status = successStatus;
        }
        catch (System.Exception ex)
        {
            _status = failurePrefix + ex.Message;
        }
    }

    private void DrawIslandIdPicker()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("IslandId Picker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "In Scene View, left-click a MeshCollider to read islandId from UV.zw (two-byte / 16-bit encoding).\n" +
            "The UV channel is read from the mesh asset's IslandUV importer settings (Target UV Channel).\n" +
            "Ignored islands use id=65535 (0xFFFF).",
            MessageType.Info);

        if (_picker != null)
        {
            _picker.Enabled = EditorGUILayout.ToggleLeft("Enable picking", _picker.Enabled);
            _picker.CopyToClipboard = EditorGUILayout.ToggleLeft("Copy result to clipboard", _picker.CopyToClipboard);
        }
    }

    private void OnPickedIslandId(GameObject go, ushort islandId)
    {
        string hex = $"0x{islandId:X4}";
        Debug.Log($"[IslandUV] {go.name}: islandId = {islandId} ({hex})", go);
    }
}
#endif
