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

    [MenuItem("Tools/Island UV/Importer Settings")]
    public static void Open()
    {
        GetWindow<IslandUvImporterSettingsWindow>(false, "IslandUV Importer", true);
    }

    private void OnEnable()
    {
        RefreshFromSelection();
        Selection.selectionChanged += OnSelectionChanged;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
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
}
#endif
