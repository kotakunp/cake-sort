// Soft stylized lighting for cakes, plates, and wood: wrapped diffuse with a warm sky / cool ground
// ambient, a specular glint, and a rim light. Self-contained (no scene lights needed).
Shader "CakeSort/Lit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Spec ("Specular", Range(0, 1.5)) = 0.2
        _Gloss ("Gloss", Range(1, 128)) = 24
        _Rim ("Rim", Range(0, 1)) = 0.2
        _Flash ("Flash", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Spec, _Gloss, _Rim, _Flash;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 wp : TEXCOORD2; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float3 l = normalize(float3(-0.45, 1.0, -0.6));
                float ndl = dot(n, l);
                float wrap = saturate(ndl * 0.5 + 0.5);
                float3 sky = float3(1.04, 1.0, 0.95);
                float3 ground = float3(0.66, 0.55, 0.56);
                float3 light = lerp(ground, sky, wrap * wrap) + saturate(ndl) * float3(0.16, 0.13, 0.08);
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color * i.color;
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), _Gloss) * _Spec;
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _Rim;
                float3 c = albedo.rgb * light + spec + rim * float3(1.0, 0.94, 0.88);
                c = lerp(c, float3(1.0, 0.96, 0.8), _Flash);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
