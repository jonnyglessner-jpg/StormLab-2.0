Shader "StormLab/RainStreak"
{
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            // HDRP draws SRPDefaultUnlit passes with its forward transparents
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            // the storm's light scale (StormSkin): x luminance of a unit (0: none set, 1), y 1 to apply the camera's exposure
            float4 _StormLabLight;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varying { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Varying vert(Input v)
            {
                Varying o; o.position = TransformObjectToHClip(v.vertex.xyz); o.uv = v.uv; o.color = v.color; return o;
            }
            half4 frag(Varying i) : SV_Target
            {
                float edge = saturate(1 - abs(i.uv.x*2-1));
                float endFade = saturate(i.uv.y*8)*saturate((1-i.uv.y)*4);
                float scale = (_StormLabLight.x > 0 ? _StormLabLight.x : 1) * (_StormLabLight.y > .5 ? GetCurrentExposureMultiplier() : 1);
                return half4(i.color.rgb * scale, i.color.a * edge*edge * endFade);
            }
            ENDHLSL
        }
    }
}
