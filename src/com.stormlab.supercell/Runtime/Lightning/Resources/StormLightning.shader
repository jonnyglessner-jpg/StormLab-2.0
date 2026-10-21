Shader "StormLab/Lightning"
{
    Properties { [HDR] _Color ("Emission", Color) = (6,7,10,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.stormlab.supercell/Runtime/Lightning/Shaders/StormLightningMedium.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 world : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.color = v.color;
                o.world = TransformObjectToWorld(v.positionOS.xyz); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float across = abs(i.uv.y * 2 - 1);
                // One narrow radiating core within a broad, dim optical halo. The CPU sizes the
                // ribbon per camera, keeping the core sampled even at long viewing distances.
                float core = exp2(-across * across * 36);
                float halo = exp2(-across * across * 10) * .045;
                float profile = (core + halo) * (1 - smoothstep(.8, 1, across));
                half3 tint = lerp(half3(.56, .66, 1), half3(1, .97, 1), core);
                float visibility = 1;
                if (_SLActive > .5)
                    visibility = exp(-slOpticalDepth(slWorldToVolume(_WorldSpaceCameraPos),slWorldToVolume(i.world),384,1));
                return half4(_Color.rgb * i.color.rgb * tint * visibility, profile * _Color.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
