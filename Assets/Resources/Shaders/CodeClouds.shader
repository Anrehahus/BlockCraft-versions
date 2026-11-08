Shader "Custom/CodeClouds"
{
    Properties
    {
        _Color ("Cloud Color", Color) = (1,1,1,0.85)
        _FogColor ("Fog Color", Color) = (1,1,1,1)
        _FogStart ("Fog Start", Float) = 40
        _FogEnd ("Fog End", Float) = 160
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _FogColor;
            float _FogStart;
            float _FogEnd;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR0; float3 wpos : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // туман по горизонтальному расстоянию: облака прямо над головой не тускнеют
                float d = distance(i.wpos.xz, _WorldSpaceCameraPos.xz);
                float f = saturate((d - _FogStart) / max(_FogEnd - _FogStart, 0.001));

                fixed4 c = i.color * _Color;
                c.rgb = lerp(c.rgb, _FogColor.rgb, f);   // цвет уходит в цвет тумана
                c.a *= (1.0 - f);                        // и облако растворяется
                return c;
            }
            ENDCG
        }
    }
}