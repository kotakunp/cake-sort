// Clear glass for the cake cloche: mostly transparent with fresnel edges and a sharp highlight.
Shader "CakeSort/Glass"
{
    Properties
    {
        _Color ("Tint", Color) = (0.85, 0.95, 1, 0.12)
        _Fresnel ("Fresnel", Range(0, 2)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        CGINCLUDE
        #include "UnityCG.cginc"
        fixed4 _Color;
        half _Fresnel;
        struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
        struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; };
        v2f vert (appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.n = UnityObjectToWorldNormal(v.normal);
            o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            return o;
        }
        fixed4 shade (v2f i, float side)
        {
            float3 n = normalize(i.n) * side;
            float3 v = normalize(_WorldSpaceCameraPos - i.wp);
            float3 l = normalize(float3(-0.45, 1.0, -0.6));
            float f = pow(1.0 - saturate(abs(dot(n, v))), 2.5) * _Fresnel;
            float spec = pow(saturate(dot(n, normalize(l + v))), 90.0) * 1.6;
            float a = saturate(_Color.a + f * 0.55 + spec);
            return fixed4(_Color.rgb + spec, a);
        }
        ENDCG

        Pass
        {
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { fixed4 c = shade(i, -1); c.a *= 0.6; return c; }
            ENDCG
        }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return shade(i, 1); }
            ENDCG
        }
    }
}
