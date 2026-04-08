Shader "Debug/IslandDebug"
{
    Properties
    {
        _UVChannel ("UV Channel (0=UV0, 1=UV1, 2=UV2, 3=UV3)", Int) = 0
        _IslandSize ("Island Size", Float) = 1.0
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
            float _IslandSize;

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

            // 简单的伪随机函数
            float random(float2 st)
            {
                return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453123);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 获取UV坐标
                float2 uv = i.uv;
                
                // 根据UV所在的岛屿确定格子位置
                // IslandSize 用于控制岛屿的大小（同一岛屿内的UV范围）
                float2 islandGrid = floor(uv / _IslandSize);
                
                // 基于岛屿位置生成伪随机颜色
                float r = random(islandGrid + float2(0.0, 0.0));
                float g = random(islandGrid + float2(1.0, 0.0));
                float b = random(islandGrid + float2(0.0, 1.0));
                
                // 为相邻岛屿确保颜色对比度
                // 增加饱和度
                float maxComponent = max(r, max(g, b));
                float minComponent = min(r, min(g, b));
                
                // 如果颜色太暗，进行亮度补偿
                if (maxComponent < 0.3)
                {
                    r += 0.4;
                    g += 0.4;
                    b += 0.4;
                }
                
                // 添加岛屿边界线（增强对比）
                float2 localUv = frac(uv / _IslandSize);
                float edgeThreshold = 0.02;
                bool nearEdge = (localUv.x < edgeThreshold || localUv.x > (1.0 - edgeThreshold)) ||
                                (localUv.y < edgeThreshold || localUv.y > (1.0 - edgeThreshold));
                
                if (nearEdge)
                {
                    // 边界处渲染为黑色以增强可视性
                    r *= 0.5;
                    g *= 0.5;
                    b *= 0.5;
                }
                
                return fixed4(r, g, b, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
