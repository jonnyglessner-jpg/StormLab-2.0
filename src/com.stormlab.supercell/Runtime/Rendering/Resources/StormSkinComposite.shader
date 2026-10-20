// Composites the storm (rgb = light the storm adds, a = transmittance) over the camera colour: out = rgb + a * scene.
Shader "Hidden/StormLab/SkinComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Blend One SrcAlpha
        Pass
        {
            Name "StormComposite"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            Texture2D _StormTex;
            SamplerState sampler_LinearClamp;
            float _FlipY;
            struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
            V vert(uint id : SV_VertexID)
            {
                V o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.pos = float4(p * 2.0 - 1.0, 0.0, 1.0);
                o.uv = p;
                if (_FlipY > 0.5) o.uv.y = 1.0 - o.uv.y;
                return o;
            }
            float4 frag(V i) : SV_Target { return _StormTex.SampleLevel(sampler_LinearClamp, i.uv, 0); }
            ENDHLSL
        }
    }
}
