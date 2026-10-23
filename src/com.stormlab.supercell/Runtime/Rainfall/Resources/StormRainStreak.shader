Shader "StormLab/RainStreak"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varying { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Varying vert(Input v)
            {
                Varying o; o.position = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o;
            }
            half4 frag(Varying i) : SV_Target
            {
                float edge = saturate(1 - abs(i.uv.x*2-1));
                float endFade = saturate(i.uv.y*8)*saturate((1-i.uv.y)*4);
                return half4(i.color.rgb, i.color.a * edge*edge * endFade);
            }
            ENDHLSL
        }
    }
}
