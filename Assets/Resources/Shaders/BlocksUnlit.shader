// Шейдер блоков: без освещения, яркость граней берётся из цвета вершин (как в Майнкрафте).
// Положите файл в любую папку Assets (например Assets/Shaders) и выберите его в материале BlocksMaterial.
Shader "MinecraftClone/BlocksUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas", 2D) = "white" {}
        [MainColor]   _BaseColor ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }

        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed4 _BaseColor;
            fixed _Cutoff;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed3 shade : TEXCOORD1; float fog : TEXCOORD2; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                // яркости граней заданы «в гамма-пространстве», как в оригинале
                #ifdef UNITY_COLORSPACE_GAMMA
                    o.shade = v.color.rgb;
                #else
                    o.shade = GammaToLinearSpace(v.color.rgb);
                #endif
                o.fog = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.pos.z);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_BaseMap, i.uv);
                clip(c.a - _Cutoff);
                c.rgb *= i.shade * _BaseColor.rgb;
                // линейный туман из RenderSettings
                float f = saturate(i.fog * unity_FogParams.z + unity_FogParams.w);
                c.rgb = lerp(unity_FogColor.rgb, c.rgb, f);
                return c;
            }
            ENDCG
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed _Cutoff;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                clip(tex2D(_BaseMap, i.uv).a - _Cutoff);
                return 0;
            }
            ENDCG
        }
    }
}
