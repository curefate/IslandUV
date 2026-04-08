Shader "Debug/UV"
{
    Properties
    {
        _UVChannel ("UV Channel (0=UV0, 1=UV1, 2=UV2, 3=UV3)", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float2 uv3 : TEXCOORD3;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            int _UVChannel;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                
                // 根据UV通道参数选择相应的UV
                if (_UVChannel == 0)
                    o.uv = v.uv0;
                else if (_UVChannel == 1)
                    o.uv = v.uv1;
                else if (_UVChannel == 2)
                    o.uv = v.uv2;
                else
                    o.uv = v.uv3;
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 归一化UV（通过frac获取0-1范围内的小数部分）
                float2 normalizedUV = frac(i.uv);
                
                // 计算UV与原点(0,0)的距离
                float distanceFromOrigin = length(normalizedUV);
                
                // 距离范围是0到sqrt(2)（约1.414），标准化到0-1
                float normalizedDistance = distanceFromOrigin / sqrt(2.0);
                
                // 反转距离值，使原点为白色(1.0)，远处为暗色
                float intensity = 1.0 - normalizedDistance;
                
                // 返回颜色
                return fixed4(intensity, intensity, intensity, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
