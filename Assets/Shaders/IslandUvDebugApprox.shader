Shader "IslandUV/Debug/ApproxIslandGradient"
{
    Properties
    {
        [Enum(UV0,0, UV1,1, UV2,2, UV3,3, UV4,4, UV5,5, UV6,6, UV7,7)] _UvChannel("UV Channel", Float) = 2
        _CellScale("Pseudo Island Cell Scale", Float) = 8
        _GradAxis("Gradient Axis (0=U,1=V,2=Radial)", Float) = 2
        _Light("Light", Color) = (1,1,1,1)
        _Dark("Dark", Color) = (0.1,0.1,0.1,1)
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
                float _CellScale;
                float _GradAxis;
                half4 _Light;
                half4 _Dark;
                half _Sat;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
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

            // Small hash helpers
            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float3 Hash33(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yzz) * p3.zyx);
            }

            float3 HsvToRgb(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = PickUv(IN);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Pseudo "island id": hash a coarse cell of UV space.
                // NOTE: This is an approximation; true islands require CPU-provided IDs.
                float scale = max(_CellScale, 1e-3);
                float2 cell = floor(uv * scale);
                float3 rnd = Hash33(float3(cell, cell.x + cell.y * 17.0));

                // Base color from HSV for good variety.
                float hue = rnd.x;
                float sat = saturate(rnd.y * _Sat);
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
