// Flat-colour lit shader used for everything in the world.
// One directional light (the moon) with shadows, flat ambient, linear fog,
// plus a single global "lantern" light that follows the coach.
Shader "Reins/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Emission ("Emission", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _Color;
            sampler2D _MainTex;
            fixed4 _Emission;
            float4 _LanternPos;    // xyz = world position, w = radius
            fixed4 _LanternColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                SHADOW_COORDS(2)
                UNITY_FOG_COORDS(3)
                float2 uv : TEXCOORD4;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                o.uv = v.uv;
                TRANSFER_SHADOW(o)
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                fixed3 albedo = _Color.rgb * i.color.rgb * tex2D(_MainTex, i.uv).rgb;

                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                fixed shadow = SHADOW_ATTENUATION(i);
                float3 light = UNITY_LIGHTMODEL_AMBIENT.rgb + _LightColor0.rgb * ndl * shadow;

                // Coach lantern: cheap distance falloff with a soft wrap term.
                float3 toLantern = _LanternPos.xyz - i.worldPos;
                float dist = length(toLantern);
                float att = saturate(1.0 - dist / max(_LanternPos.w, 0.001));
                att *= att;
                float wrap = 0.35 + 0.65 * saturate(dot(n, toLantern / max(dist, 0.001)));
                light += _LanternColor.rgb * att * wrap;

                fixed4 col = fixed4(albedo * light + _Emission.rgb, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback "VertexLit"
}
