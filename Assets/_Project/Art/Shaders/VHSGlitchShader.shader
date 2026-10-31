Shader "Backrooms/VHSGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 0
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.25
        _ScanlineCount ("Scanline Count", Float) = 480
        _NoiseIntensity ("Static Noise Intensity", Range(0, 1)) = 0.12
        _TrackingIntensity ("Tracking Glitch Intensity", Range(0, 1)) = 0.20
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.20
        _MaxGlitchAlpha ("Max Glitch Alpha", Range(0, 0.5)) = 0.07
        _PursuitPulse ("Pursuit Heartbeat Pulse", Range(0, 1)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent+500" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _GlitchIntensity;
            float _ScanlineIntensity;
            float _ScanlineCount;
            float _NoiseIntensity;
            float _TrackingIntensity;
            float _VignetteIntensity;
            float _MaxGlitchAlpha;
            float _PursuitPulse;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.texcoord;
                float t = _Time.y;

                float2 centerCoord = (uv - 0.5) * 2.0;
                float centerDistSq = dot(centerCoord, centerCoord);

                // 1. Scanlines (subtle CRT horizontal lines)
                float scanline = sin(uv.y * _ScanlineCount + t * 4.0);
                float scanFactor = scanline * 0.5 + 0.5;
                scanFactor = pow(scanFactor, 1.8);
                float scanAlpha = (1.0 - scanFactor) * _ScanlineIntensity * (0.15 + 0.20 * _GlitchIntensity);

                // 2. High-frequency static noise (Feathered: center is 80% cleared for optimal visibility!)
                float2 noiseUV = uv * float2(420.0, 320.0) + float2(frac(t * 31.0), frac(t * 73.0));
                float noise = hash21(noiseUV);
                float centerNoiseFalloff = lerp(0.18, 1.0, smoothstep(0.10, 0.85, centerDistSq));
                float staticAlpha = noise * _NoiseIntensity * (0.02 + 0.35 * _GlitchIntensity) * centerNoiseFalloff;

                // 3. Tracking Glitch Bars (horizontal bands that roll vertically during glitch bursts)
                float barY1 = frac(t * 0.35);
                float bar1 = smoothstep(0.04 * (0.3 + _GlitchIntensity), 0.0, abs(uv.y - barY1));

                float barY2 = frac(t * 1.6 + 0.4);
                float bar2 = smoothstep(0.02 * (0.3 + _GlitchIntensity), 0.0, abs(uv.y - barY2)) * step(0.20, _GlitchIntensity);

                float trackingBars = saturate(bar1 * 0.65 + bar2 * 0.85);
                float barNoise = hash21(float2(floor(uv.y * 100.0), frac(t * 43.0)));
                float barGlitch = trackingBars * (0.2 + 0.8 * barNoise);
                float trackingAlpha = barGlitch * _TrackingIntensity * (0.05 + 0.55 * _GlitchIntensity);

                // 4. Subtle Dark Vignette
                float vignette = smoothstep(1.3, 0.45, centerDistSq);
                float vigAlpha = (1.0 - vignette) * _VignetteIntensity * (0.5 + 0.5 * _GlitchIntensity);

                // 5. Adrenaline Blood-Red Heartbeat Vignette (Intense pressure during monster chase!)
                float redVig = smoothstep(0.35, 1.50, centerDistSq);
                float redAlpha = redVig * _PursuitPulse * 0.48;
                float3 crimson = float3(0.85, 0.04, 0.04);

                // 6. Blending
                float3 darkColor = float3(0.02, 0.02, 0.02);
                float3 brightColor = float3(0.95, 0.96, 0.92) * (0.8 + 0.25 * noise);

                float totalAlpha = saturate(scanAlpha + staticAlpha + trackingAlpha + vigAlpha);
                // Keep the center world always visible and crisp so player can navigate effortlessly
                totalAlpha = min(totalAlpha, _MaxGlitchAlpha);

                float brightWeight = (staticAlpha + trackingAlpha) / max(totalAlpha, 0.001);
                float3 baseEffectColor = lerp(darkColor, brightColor, saturate(brightWeight * 1.2));

                // Blend blood-red heartbeat vignette over borders
                float3 finalColor = lerp(baseEffectColor, crimson, saturate(redAlpha * 2.2));
                float finalAlpha = saturate(totalAlpha + redAlpha);

                return fixed4(finalColor * i.color.rgb, finalAlpha * i.color.a);
            }
            ENDCG
        }
    }
}
