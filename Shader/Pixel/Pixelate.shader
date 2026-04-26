Shader "Hidden/Pixelate"
{
    Properties { _BlitTexture ("Texture", 2D) = "white" {} _PixelSize ("Pixel Size", Float) = 8 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100 ZWrite Off Cull Off
        Pass
        {
            Name "Pixelate"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_BlitTexture); SAMPLER(sampler_BlitTexture);
            float _PixelSize; float4 _BlitTexture_TexelSize;
            Varyings Vert(Attributes input) {
                Varyings o;
                o.pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }
            float4 Frag(Varyings i) : SV_Target {
                float2 texelCount = _BlitTexture_TexelSize.zw;
                float2 stepUV = 1.0 / (texelCount / _PixelSize);
                float2 pixelatedUV = floor(i.uv / stepUV) * stepUV;
                pixelatedUV = min(pixelatedUV, 1.0 - stepUV);
                float2 sampleUV = pixelatedUV + stepUV * 0.5;
                float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, sampleUV);
                col.a = 1.0;
                return col;
            }
            ENDHLSL
        }
    }
}