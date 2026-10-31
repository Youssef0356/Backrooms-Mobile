Shader "Backrooms/ChalkDecal"
{
    Properties
    {
        _BaseMap ("Chalk Texture (URP)", 2D) = "white" {}
        _MainTex ("Chalk Texture (Fallback)", 2D) = "white" {}
        _BaseColor ("Color Tint", Color) = (0.96, 0.95, 0.90, 0.95)
    }
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent+150" 
            "RenderPipeline"="UniversalPipeline" 
            "IgnoreProjector"="True"
        }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _MainTex_ST;
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                // Fallback to _MainTex if _BaseMap has zero alpha
                if (col.a < 0.01)
                {
                    half4 colAlt = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    if (colAlt.a > col.a) col = colAlt;
                }
                col.rgb *= _BaseColor.rgb;
                col.a *= _BaseColor.a;
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Sprites/Default"
}