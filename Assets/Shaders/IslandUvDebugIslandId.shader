Shader "IslandUV/Debug/IslandIdGradient"
{
    Properties
    {
        [Enum(UV0,0, UV1,1, UV2,2, UV3,3, UV4,4, UV5,5, UV6,6, UV7,7)] _UvChannel("UV Channel", Float) = 2
        _GradAxis("Gradient Axis (0=U,1=V,2=Radial)", Float) = 1
        _Light("Light", Color) = (1,1,1,1)
        _Dark("Dark", Color) = (0.2,0.2,0.2,1)
        _Sat("Saturation", Range(0,2)) = 1
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
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _UvChannel;
                float _GradAxis;
                half4 _Light;
                half4 _Dark;
                half _Sat;
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
                // Branching is fine for debug.
                if (_UvChannel < 0.5) return IN.uv0;
                if (_UvChannel < 1.5) return IN.uv1;
                if (_UvChannel < 2.5) return IN.uv2;
                if (_UvChannel < 3.5) return IN.uv3;
                if (_UvChannel < 4.5) return IN.uv4;
                if (_UvChannel < 5.5) return IN.uv5;
                if (_UvChannel < 6.5) return IN.uv6;
                return IN.uv7;
            }

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float3 HsvToRgb(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            uint DecodeIslandIdFromColor(half4 c)
            {
                // CPU packs: id = r + g*256, with r/g as bytes.
                // In shader, COLOR is typically normalized 0..1.
                uint r = (uint)round(saturate(c.r) * 255.0);
                uint g = (uint)round(saturate(c.g) * 255.0);
                return r + (g << 8);
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
                float2 uv = IN.uv;

                uint islandId = DecodeIslandIdFromColor(IN.color);
                float idf = (float)islandId;

                // Generate a stable per-island base color.
                float hue = Hash11(idf + 0.123);
                float sat = saturate(Hash11(idf * 1.37 + 1.7) * _Sat);
                float val = 1.0;
                float3 baseCol = HsvToRgb(float3(hue, sat, val));

                // Gradient factor from UV.
                float t;
                if (_GradAxis < 0.5)
                {
                    t = frac(uv.x);
                }
                else if (_GradAxis < 1.5)
                {
                    t = frac(uv.y);
                }
                else
                {
                    float2 d = frac(uv) - 0.5;
                    t = saturate(length(d) * 2.0);
                }

                float3 shade = lerp(_Light.rgb, _Dark.rgb, t);
                float3 col = baseCol * shade;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
