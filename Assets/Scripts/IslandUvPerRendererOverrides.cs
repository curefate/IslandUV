using System;
using UnityEngine;

/// <summary>
/// Per-renderer overrides for IslandUV final shader.
/// This component stores override data (serialized) and applies it to the attached Renderer via MaterialPropertyBlock.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class IslandUvPerRendererOverrides : MonoBehaviour
{
    public const string ExpectedShaderName = "IslandUV/IslandUV_Unlit";
    public const int OverrideSlotCount = 4;
    public const int IdsPerSlot = 4;

    [Serializable]
    public struct OverrideSlot
    {
        public bool enabled;

        [Tooltip("If islandId matches any of these, the override slot is applied.")]
        public ushort[] ids;

        [Header("UV Transform")]
        public Vector2 tiling;
        public Vector2 offset;

        [Header("UV Flags")]
        public bool flipU;
        public bool flipV;
        public bool swapUV;
    }

    [Tooltip("Target renderer")]
    public Renderer targetRenderer;

    [Header("Override slots (X=4, N=4)")]
    public OverrideSlot[] overrides = Array.Empty<OverrideSlot>();

    [Tooltip("If enabled, clears the property block when the component is disabled.")]
    public bool clearOnDisable = false;

    private static OverrideSlot NewDefaultSlot()
    {
        return new OverrideSlot
        {
            enabled = false,
            ids = new ushort[IdsPerSlot] { 0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF },
            tiling = Vector2.one,
            offset = Vector2.zero,
            flipU = false,
            flipV = false,
            swapUV = false,
        };
    }

    private MaterialPropertyBlock _mpb;

    private void Reset()
    {
        EnsureArrayInitialized();
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        Apply();
    }

    private void OnEnable()
    {
        EnsureArrayInitialized();
        Apply();
    }

    private void OnDisable()
    {
        if (!clearOnDisable) return;
        var r = ResolveRenderer();
        if (r == null) return;
        r.SetPropertyBlock(null);
    }

    private void OnValidate()
    {
        EnsureArrayInitialized();
        Apply();
    }

    public void Apply()
    {
        var r = ResolveRenderer();
        if (r == null) return;

        WarnIfShaderMismatch(r);

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);

        int count = overrides != null ? overrides.Length : 0;
        for (int i = 0; i < OverrideSlotCount; i++)
        {
            var slot = (i < count) ? overrides[i] : NewDefaultSlot();
            SetSlot(_mpb, i, slot);
        }

        r.SetPropertyBlock(_mpb);
    }

    private Renderer ResolveRenderer()
    {
        if (targetRenderer != null) return targetRenderer;
        return GetComponent<Renderer>();
    }

    private void EnsureArrayInitialized()
    {
        if (overrides == null || overrides.Length != OverrideSlotCount)
        {
            overrides = new OverrideSlot[OverrideSlotCount];
            for (int i = 0; i < OverrideSlotCount; i++) overrides[i] = NewDefaultSlot();
        }

        // Ensure each slot has an ids array.
        for (int i = 0; i < overrides.Length; i++)
        {
            if (overrides[i].ids == null || overrides[i].ids.Length != IdsPerSlot)
                overrides[i].ids = new ushort[IdsPerSlot] { 0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF };
        }
    }

    private static void WarnIfShaderMismatch(Renderer r)
    {
        // Note: even if mismatch, we still apply MPB (harmless), but warn because it won't affect the intended shader.
        var mat = r.sharedMaterial;
        if (mat == null || mat.shader == null) return;
        if (mat.shader.name != ExpectedShaderName)
        {
            Debug.LogWarning(
                $"[{nameof(IslandUvPerRendererOverrides)}] Renderer '{r.name}' uses shader '{mat.shader.name}', expected '{ExpectedShaderName}'. MPB overrides may have no effect.",
                r);
        }
    }

    private static void SetSlot(MaterialPropertyBlock mpb, int index, OverrideSlot slot)
    {
        string p = $"_Ov{index}_";

        mpb.SetFloat(p + "Enabled", slot.enabled ? 1f : 0f);
        mpb.SetVector(p + "ST", new Vector4(slot.tiling.x, slot.tiling.y, slot.offset.x, slot.offset.y));

        int flags = 0;
        if (slot.flipU) flags |= 1;
        if (slot.flipV) flags |= 2;
        if (slot.swapUV) flags |= 4;
        mpb.SetFloat(p + "Flags", flags);

        // ids[] is fixed-length IdsPerSlot.
        ushort id0 = (slot.ids != null && slot.ids.Length > 0) ? slot.ids[0] : (ushort)0xFFFF;
        ushort id1 = (slot.ids != null && slot.ids.Length > 1) ? slot.ids[1] : (ushort)0xFFFF;
        ushort id2 = (slot.ids != null && slot.ids.Length > 2) ? slot.ids[2] : (ushort)0xFFFF;
        ushort id3 = (slot.ids != null && slot.ids.Length > 3) ? slot.ids[3] : (ushort)0xFFFF;
        mpb.SetFloat(p + "Id0", id0);
        mpb.SetFloat(p + "Id1", id1);
        mpb.SetFloat(p + "Id2", id2);
        mpb.SetFloat(p + "Id3", id3);
    }
}
