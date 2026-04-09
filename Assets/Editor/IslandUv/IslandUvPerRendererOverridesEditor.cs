#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IslandUvPerRendererOverrides))]
public class IslandUvPerRendererOverridesEditor : Editor
{
    private SerializedProperty _targetRenderer;
    private SerializedProperty _overrides;
    private SerializedProperty _clearOnDisable;

    private bool[] _foldouts = new bool[IslandUvPerRendererOverrides.OverrideSlotCount];

    private void OnEnable()
    {
        _targetRenderer = serializedObject.FindProperty("targetRenderer");
        _overrides = serializedObject.FindProperty("overrides");
        _clearOnDisable = serializedObject.FindProperty("clearOnDisable");

        if (_foldouts == null || _foldouts.Length != IslandUvPerRendererOverrides.OverrideSlotCount)
            _foldouts = new bool[IslandUvPerRendererOverrides.OverrideSlotCount];
        if (_foldouts.Length > 0) _foldouts[0] = true;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_targetRenderer);
        EditorGUILayout.PropertyField(_clearOnDisable);

        var comp = (IslandUvPerRendererOverrides)target;
        var r = comp != null ? (comp.targetRenderer != null ? comp.targetRenderer : comp.GetComponent<Renderer>()) : null;
        var mat = (r != null) ? r.sharedMaterial : null;
        if (mat != null && mat.shader != null && mat.shader.name != IslandUvPerRendererOverrides.ExpectedShaderName)
        {
            EditorGUILayout.HelpBox(
                $"Renderer shader is '{mat.shader.name}', expected '{IslandUvPerRendererOverrides.ExpectedShaderName}'. Overrides may have no effect.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "This component stores overrides (serialized) and applies them via MaterialPropertyBlock.\n" +
            "- Fill island IDs (ushort). 65535 (0xFFFF) means empty slot.\n" +
            "- Flags: bit0 flipU, bit1 flipV, bit2 swapUV.\n" +
            "- Overrides apply in order: Ov0 -> Ov1 -> Ov2 -> Ov3.",
            MessageType.Info);

        if (_overrides != null)
        {
            // Guard: ensure fixed array size so slots are always visible in inspector.
            if (_overrides.arraySize != IslandUvPerRendererOverrides.OverrideSlotCount)
                _overrides.arraySize = IslandUvPerRendererOverrides.OverrideSlotCount;

            for (int i = 0; i < Mathf.Min(_overrides.arraySize, IslandUvPerRendererOverrides.OverrideSlotCount); i++)
            {
                var slot = _overrides.GetArrayElementAtIndex(i);
                string title = $"Override {i}";
                DrawSlot(slot, ref _foldouts[i], title);
            }
        }

        EditorGUILayout.Space(8);
        if (GUILayout.Button("Apply Now"))
        {
            foreach (var t in targets)
            {
                ((IslandUvPerRendererOverrides)t).Apply();
                EditorUtility.SetDirty(t);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawSlot(SerializedProperty slotProp, ref bool foldout, string title)
    {
        foldout = EditorGUILayout.Foldout(foldout, title, true);
        if (!foldout) return;

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("enabled"));

            EditorGUILayout.LabelField("Island IDs (any match applies)");
            using (new EditorGUI.IndentLevelScope())
            {
                var ids = slotProp.FindPropertyRelative("ids");
                if (ids != null && ids.isArray)
                {
                    for (int i = 0; i < Mathf.Min(ids.arraySize, IslandUvPerRendererOverrides.IdsPerSlot); i++)
                        EditorGUILayout.PropertyField(ids.GetArrayElementAtIndex(i), new GUIContent($"Id {i}"));
                }
            }

            EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("tiling"));
            EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("offset"));

            EditorGUILayout.LabelField("Flags");
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("flipU"));
                EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("flipV"));
                EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("swapUV"));
            }
        }

        EditorGUILayout.Space(4);
    }
}
#endif
