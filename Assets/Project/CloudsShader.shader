Shader "Custom/MinecraftClouds"
{
    Properties
    {
        _MainTex ("Cloud Noise Texture", 2D) = "white" {}
        _Color ("Cloud Color", Color) = (1, 1, 1, 0.8)
        _Speed ("Wind Speed (X, Z)", Vector) = (0.01, 0.005, 0, 0)
        _Tiling ("Texture Tiling", Float) = 10.0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
        }

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
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Texture2D _MainTex;
            SamplerState sampler_MainTex;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _Speed;
                float _Tiling;
                float _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                
                output.uv = input.uv * _Tiling + _Speed.xy * _Time.y;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 noise = _MainTex.Sample(sampler_MainTex, input.uv);

                if (noise.r < _Cutoff)
                {
                    discard;
                }

                return _Color;
            }
            ENDHLSL
        }
    }
}