Shader "Backrooms/VHSGlitch"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 0
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.25
        _ScanlineCount ("Scanline Count", Float) = 480
        _NoiseIntensity ("Static Noise Intensity", Range(0, 1)) = 0.12
        _TrackingIntensity ("Tracking Glitch Intensity", Range(0, 1)) = 0.20
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.20
        _MaxGlitchAlpha ("Max Glitch Alpha", Range(0, 0.5)) = 0.16
    }
    SubShader
    {
        Tags { "Queue"="Transparent+500" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

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

                // 1. Scanlines
                float scanline = sin(uv.y * _ScanlineCount + t * 4.0);
                float scanFactor = scanline * 0.5 + 0.5;
                scanFactor = pow(scanFactor, 1.8);
                float scanAlpha = (1.0 - scanFactor) * _ScanlineIntensity * (0.25 + 0.35 * _GlitchIntensity);

                // 2. High-frequency static noise
                float2 noiseUV = uv * float2(420.0, 320.0) + float2(frac(t * 31.0), frac(t * 73.0));
                float noise = hash21(noiseUV);
                float staticAlpha = noise * _NoiseIntensity * (0.04 + 0.65 * _GlitchIntensity);

                // 3. Tracking Glitch Bars (horizontal bands that roll vertically)
                float barY1 = frac(t * 0.35);
                float bar1 = smoothstep(0.05 * (0.4 + _GlitchIntensity), 0.0, abs(uv.y - barY1));

                float barY2 = frac(t * 1.6 + 0.4);
                float bar2 = smoothstep(0.02 * (0.4 + _GlitchIntensity), 0.0, abs(uv.y - barY2)) * step(0.15, _GlitchIntensity);

                float trackingBars = saturate(bar1 * 0.75 + bar2 * 1.0);
                float barNoise = hash21(float2(floor(uv.y * 100.0), frac(t * 43.0)));
                float barGlitch = trackingBars * (0.2 + 0.8 * barNoise);
                float trackingAlpha = barGlitch * _TrackingIntensity * (0.05 + 0.85 * _GlitchIntensity);

                // 4. Vignette
                float2 vigCoord = (uv - 0.5) * 2.0;
                float vig = dot(vigCoord, vigCoord);
                float vignette = smoothstep(1.3, 0.45, vig);
                float vigAlpha = (1.0 - vignette) * _VignetteIntensity * (0.5 + 0.5 * _GlitchIntensity);

                // 5. Blending
                float3 darkColor = float3(0.02, 0.02, 0.02);
                float3 brightColor = float3(0.95, 0.96, 0.92) * (0.8 + 0.25 * noise);

                float totalAlpha = saturate(scanAlpha + staticAlpha + trackingAlpha + vigAlpha);
                // Keep the world always visible so the player can see Clark and navigate
                totalAlpha = min(totalAlpha, _MaxGlitchAlpha);
                float brightWeight = (staticAlpha + trackingAlpha) / max(totalAlpha, 0.001);
                float3 finalColor = lerp(darkColor, brightColor, saturate(brightWeight * 1.3));

                return fixed4(finalColor, totalAlpha);
            }
            ENDCG
        }
    }
}
