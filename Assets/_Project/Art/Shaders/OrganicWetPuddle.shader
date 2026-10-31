Shader "Backrooms/OrganicWetPuddle"
{
    Properties
    {
        _BaseMap ("Wet Carpet Albedo", 2D) = "white" {}
        _BumpMap ("Carpet Normal Map", 2D) = "bump" {}
        _FloorTextureMeters ("Floor Texture Meters", Float) = 2.0
        _Smoothness ("Wet Carpet Smoothness", Range(0.1, 0.9)) = 0.46
        _DarkenFactor ("Wet Moisture Darkening", Range(0.3, 1.0)) = 0.78
        _BumpScale ("Normal Map Bump Scale", Range(0.0, 2.0)) = 1.0
        _EdgeFeather ("Edge Feather Width", Range(0.05, 0.50)) = 0.30
        _WobbleAmount ("Edge Wobble Amount", Range(0.0, 0.20)) = 0.07
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent+10" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float3 bitangentWS: TEXCOORD3;
                float2 uv         : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                float _FloorTextureMeters;
                float _Smoothness;
                float _DarkenFactor;
                float _BumpScale;
                float _EdgeFeather;
                float _WobbleAmount;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInputs.positionCS;
                output.positionWS = vertexInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. Organic puddle edge mask from mesh UV (0 to 1 across room)
                float2 p = input.uv * 2.0 - 1.0; // (-1..1)
                
                // Chebyshev-rounded-box distance to naturally fit Backrooms rooms
                float r = pow(pow(abs(p.x), 4.0) + pow(abs(p.y), 4.0), 0.25);

                // Add harmonic sinusoidal wobble for natural water seepage contours
                float angle = atan2(p.y, p.x);
                float wobble = sin(angle * 5.0) * _WobbleAmount + sin(angle * 11.0 + 1.2) * (_WobbleAmount * 0.55);
                float dist = r + wobble;

                // Smooth feather from interior (1.0) to perimeter (0.0)
                float alpha = smoothstep(0.96, max(0.01, 0.96 - _EdgeFeather), dist);

                // Discard early if fully transparent outside puddle boundaries
                if (alpha <= 0.001)
                    discard;

                // 2. World-space aligned carpet texture & normal map
                float meters = max(0.1, _FloorTextureMeters);
                float2 carpetUV = float2(input.positionWS.x / meters, input.positionWS.z / meters);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, carpetUV);
                albedo.rgb *= _DarkenFactor; // Rich dark damp moisture saturation

                // Normal mapping
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, carpetUV);
                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);
                half3x3 tangentToWorld = half3x3(input.tangentWS, input.bitangentWS, input.normalWS);
                half3 worldNormal = normalize(mul(normalTS, tangentToWorld));

                // 3. Lighting calculations
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);

                // Diffuse & ambient
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(worldNormal, mainLight.direction));
                half3 diffuse = mainLight.color * (mainLight.distanceAttenuation * NdotL);

                // Specular reflection (damp sheen, NOT blinding mirror white!)
                half glossExp = exp2(9.0 * _Smoothness + 1.0);
                half3 halfDir = normalize(mainLight.direction + viewDirWS);
                half NdotH = saturate(dot(worldNormal, halfDir));
                half specIntensity = pow(NdotH, glossExp) * (_Smoothness * 0.38);
                half3 specular = mainLight.color * specIntensity;

                // Additional lights (fluorescent tubes in the Backrooms ceiling)
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
                {
                    Light addLight = GetAdditionalLight(lightIndex, input.positionWS);
                    half addNdotL = saturate(dot(worldNormal, addLight.direction));
                    half3 addIllum = addLight.color * (addLight.distanceAttenuation * addNdotL);
                    diffuse += addIllum;

                    half3 addHalfDir = normalize(addLight.direction + viewDirWS);
                    half addNdotH = saturate(dot(worldNormal, addHalfDir));
                    specular += addLight.color * (pow(addNdotH, glossExp) * (_Smoothness * 0.38) * addLight.distanceAttenuation);
                }
                #endif

                half3 ambient = SampleSH(worldNormal);
                half3 finalColor = (diffuse + ambient) * albedo.rgb + specular;

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
