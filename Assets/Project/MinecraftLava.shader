Shader "Minecraft/LavaAtlas"
{
    Properties
    {
        _MainTex ("Atlas Texture", 2D) = "white" {}
        _BaseColor ("Color Tint", Color) = (1, 1, 1, 1)
        _Speed ("Flow Speed", Float) = 0.8
        _Frequency ("Distortion Frequency", Float) = 1.5
        _Amplitude ("Distortion Amplitude", Range(0.0001, 0.05)) = 0.015
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 uv           : TEXCOORD0; // xMin, yMin, xMax, yMax из ChunkMeshBuilder
                float4 color        : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float4 uvRect       : TEXCOORD0;
                float4 color        : COLOR;
                float3 positionWS   : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Speed;
                float _Frequency;
                float _Amplitude;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uvRect = input.uv;
                output.color = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float time = _Time.y * _Speed;

                // 1. Берем мировые координаты плоскости (X, Z)
                float2 worldPos = input.positionWS.xz;

                // 2. Рассчитываем синусоидальные волны
                float waveX = sin(worldPos.y * _Frequency + time) * _Amplitude;
                float waveZ = cos(worldPos.x * _Frequency + time * 1.2) * _Amplitude;
                float2 waveOffset = float2(waveX, waveZ);

                // 3. Зацикливаем 0..1 внутри каждого блока 1x1 для бесшовности
                float2 localUV = frac(worldPos + waveOffset);

                // 4. Границы текстуры блока внутри атласа
                float2 uvMin = input.uvRect.xy;
                float2 uvMax = input.uvRect.zw;

                // Небольшой отступ (padding), чтобы предотвратить затекание на соседние плитки в атласе
                float2 pixelOffset = float2(0.0005, 0.0005); 

                // Равномерная интерполяция по localUV без сжатия и зажимания краев
                float2 sampledUV = lerp(uvMin + pixelOffset, uvMax - pixelOffset, localUV);

                // 5. Выборка пикселя из атласа (без искажений от Mip-уровней)
                float4 texColor = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sampledUV, 0);
                float4 finalColor = texColor * _BaseColor;

                // Учитываем вершинный цвет (например, биомное окрашивание или AO)
                if (input.color.r > 0 || input.color.g > 0 || input.color.b > 0)
                {
                    finalColor.rgb *= input.color.rgb;
                }

                return finalColor;
            }
            ENDHLSL
        }
    }
}