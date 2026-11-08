// Положите в Assets/Resources/MobUnlit.shader (папка Resources обязательна: оттуда шейдер гарантированно попадает в билд).
Shader "MinecraftClone/MobUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor]   _BaseColor ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }

        // Основной проход: без keywords, alpha-clip и Cull Off зашиты в сам шейдер
        Pass
        {
            Cull Off
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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.pos.z);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_BaseMap, i.uv) * _BaseColor;
                clip(c.a - _Cutoff);
                // линейный туман из RenderSettings (вода/лава/дальность), без keywords
                float f = saturate(i.fog * unity_FogParams.z + unity_FogParams.w);
                c.rgb = lerp(unity_FogColor.rgb, c.rgb, f);
                return c;
            }
            ENDCG
        }

        // Глубина (чтобы моб попадал в depth-текстуру URP и корректно работал с водой/лавой)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
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
