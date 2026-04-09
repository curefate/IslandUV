Shader "IslandUV/Final/IslandOverrides"
{
    Properties
    {
        [Enum(UV0,0, UV1,1, UV2,2, UV3,3, UV4,4, UV5,5, UV6,6, UV7,7)] _UvChannel("UV Channel", Float) = 0

        // Default params (material-level)
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _Default_ST("Default Tiling/Offset (xy=tiling, zw=offset)", Vector) = (1,1,0,0)
        _Default_Flags("Default Flags (bit0 flipU, bit1 flipV, bit2 swapUV)", Float) = 0

        // Ignored islands (id == 0xFFFF)
        _IgnoredColor("Ignored Color", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float _UvChannel;
                float4 _BaseColor;
                float4 _Default_ST;
                float _Default_Flags;
                float4 _IgnoredColor;

                float _Ov0_Enabled; float4 _Ov0_ST; float _Ov0_Flags; float _Ov0_Id0; float _Ov0_Id1; float _Ov0_Id2; float _Ov0_Id3;
                float _Ov1_Enabled; float4 _Ov1_ST; float _Ov1_Flags; float _Ov1_Id0; float _Ov1_Id1; float _Ov1_Id2; float _Ov1_Id3;
                float _Ov2_Enabled; float4 _Ov2_ST; float _Ov2_Flags; float _Ov2_Id0; float _Ov2_Id1; float _Ov2_Id2; float _Ov2_Id3;
                float _Ov3_Enabled; float4 _Ov3_ST; float _Ov3_Flags; float _Ov3_Id0; float _Ov3_Id1; float _Ov3_Id2; float _Ov3_Id3;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float2 uv3 : TEXCOORD3;
                float2 uv4 : TEXCOORD4;
                float2 uv5 : TEXCOORD5;
                float2 uv6 : TEXCOORD6;
                float2 uv7 : TEXCOORD7;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            float2 PickUv(Attributes IN)
            {
                if (_UvChannel < 0.5) return IN.uv0;
                if (_UvChannel < 1.5) return IN.uv1;
                if (_UvChannel < 2.5) return IN.uv2;
                if (_UvChannel < 3.5) return IN.uv3;
                if (_UvChannel < 4.5) return IN.uv4;
                if (_UvChannel < 5.5) return IN.uv5;
                if (_UvChannel < 6.5) return IN.uv6;
                return IN.uv7;
            }

            uint DecodeIslandIdFromColor(half4 c)
            {
                uint r = (uint)round(saturate(c.r) * 255.0);
                uint g = (uint)round(saturate(c.g) * 255.0);
                return r + (g << 8);
            }

            uint FlagsToUInt(float f) { return (uint)round(f); }

            float2 ApplyFlags(float2 uv, uint flags)
            {
                // bit2 swapUV
                if ((flags & 4u) != 0u) uv = uv.yx;
                // bit0 flipU
                if ((flags & 1u) != 0u) uv.x = 1.0 - uv.x;
                // bit1 flipV
                if ((flags & 2u) != 0u) uv.y = 1.0 - uv.y;
                return uv;
            }

            float2 ApplyST(float2 uv, float4 st)
            {
                return uv * st.xy + st.zw;
            }

            bool MatchId(uint islandId, float id0, float id1, float id2, float id3)
            {
                uint a = (uint)round(id0);
                uint b = (uint)round(id1);
                uint c = (uint)round(id2);
                uint d = (uint)round(id3);
                return (islandId == a) || (islandId == b) || (islandId == c) || (islandId == d);
            }

            void ApplyOverrideIfMatch(
                in uint islandId,
                in float enabled,
                in float4 ovST,
                in float ovFlags,
                in float id0, in float id1, in float id2, in float id3,
                inout float4 st,
                inout uint flags,
                inout bool matched)
            {
                if (matched) return;
                if (enabled <= 0.5) return;
                if (!MatchId(islandId, id0, id1, id2, id3)) return;
                st = ovST;
                flags = FlagsToUInt(ovFlags);
                matched = true;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = PickUv(IN);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                uint islandId = DecodeIslandIdFromColor(IN.color);

                // Ignored island short-circuit.
                if (islandId == 0xFFFFu)
                {
                    return half4(_IgnoredColor.rgb, _IgnoredColor.a);
                }

                float4 st = _Default_ST;
                uint flags = FlagsToUInt(_Default_Flags);

                bool matched = false;
                ApplyOverrideIfMatch(islandId, _Ov0_Enabled, _Ov0_ST, _Ov0_Flags, _Ov0_Id0, _Ov0_Id1, _Ov0_Id2, _Ov0_Id3, st, flags, matched);
                ApplyOverrideIfMatch(islandId, _Ov1_Enabled, _Ov1_ST, _Ov1_Flags, _Ov1_Id0, _Ov1_Id1, _Ov1_Id2, _Ov1_Id3, st, flags, matched);
                ApplyOverrideIfMatch(islandId, _Ov2_Enabled, _Ov2_ST, _Ov2_Flags, _Ov2_Id0, _Ov2_Id1, _Ov2_Id2, _Ov2_Id3, st, flags, matched);
                ApplyOverrideIfMatch(islandId, _Ov3_Enabled, _Ov3_ST, _Ov3_Flags, _Ov3_Id0, _Ov3_Id1, _Ov3_Id2, _Ov3_Id3, st, flags, matched);

                float2 uv = IN.uv;
                uv = ApplyFlags(uv, flags);
                uv = ApplyST(uv, st);

                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                return half4(albedo.rgb, albedo.a);
            }
            ENDHLSL
        }
    }
}
