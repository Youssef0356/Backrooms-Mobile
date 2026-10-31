Shader "Custom/LiminalSkybox"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.20, 0.55, 0.95, 1.0)
        _HorizonColor ("Horizon Color", Color) = (0.68, 0.82, 0.98, 1.0)
        _BottomColor ("Bottom Color", Color) = (0.60, 0.75, 0.92, 1.0)
        _HorizonHeight ("Horizon Height", Range(-1.0, 1.0)) = 0.0
        _Exponent ("Gradient Exponent", Range(0.2, 5.0)) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            fixed4 _TopColor;
            fixed4 _HorizonColor;
            fixed4 _BottomColor;
            float _HorizonHeight;
            float _Exponent;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.texcoord);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float y = i.dir.y - _HorizonHeight;
                fixed4 col;
                if (y > 0.0)
                {
                    float t = saturate(pow(y, _Exponent));
                    col = lerp(_HorizonColor, _TopColor, t);
                }
                else
                {
                    float t = saturate(pow(-y, _Exponent * 1.2));
                    col = lerp(_HorizonColor, _BottomColor, t);
                }
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
