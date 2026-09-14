Shader "Hidden/PixelateDepthEdge"
{
    Properties
    {
        _BlitTexture ("Texture", 2D) = "white" {}
        _DepthTexture ("Depth", 2D) = "white" {}
        _PixelSize ("Pixel Size", Float) = 8
        _MinDepthDiff ("Min Depth Difference", Float) = 0.002
        _MaxDepthDiff ("Max Depth Difference", Float) = 0.01
        _MinDarkening ("Min Darkening", Float) = 0
        _MaxDarkening ("Max Darkening", Float) = 0.5
        _EdgeSide ("Edge Side", Float) = 0
        _ModulateByLuminance ("Modulate by Luminance", Float) = 0
        _LuminanceModStrength ("Luminance Modulation Strength", Float) = 1
        _MinLuminance ("Min Luminance", Float) = 0.2
        _MaxLuminance ("Max Luminance", Float) = 0.8
        _UseStencilFilter ("Use Stencil Filter", Float) = 0
        _StencilReference ("Stencil Reference", Int) = 0
        _StencilCompare ("Stencil Compare", Int) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100 ZWrite Off Cull Off
        Pass
        {
            Name "DepthEdge"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ DEPTH_PREVIEW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BlitTexture); SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_DepthTexture); SAMPLER(sampler_DepthTexture);
            float _PixelSize; float4 _BlitTexture_TexelSize;
            float _MinDepthDiff; float _MaxDepthDiff;
            float _MinDarkening; float _MaxDarkening;
            float _EdgeSide;
            float _ModulateByLuminance; float _LuminanceModStrength;
            float _MinLuminance; float _MaxLuminance;
            float _UseStencilFilter; int _StencilReference; int _StencilCompare;

            Varyings Vert(Attributes input) {
                Varyings o;
                o.pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }

            float2 GetPixelBlockSize() {
                return min(max(_PixelSize, 1.0) * _BlitTexture_TexelSize.xy, float2(1.0, 1.0));
            }

            float2 ClampBlockMin(float2 blockMin, float2 stepUV) {
                return min(max(blockMin, float2(0.0, 0.0)), max(float2(0.0, 0.0), float2(1.0, 1.0) - stepUV));
            }

            float2 GetPixelBlockMin(float2 uv, float2 stepUV) {
                return ClampBlockMin(floor(uv / stepUV) * stepUV, stepUV);
            }

            float2 GetPixelBlockCenter(float2 blockMin, float2 stepUV) {
                return saturate(blockMin + stepUV * 0.5);
            }

            float GetDepth(float2 uv) {
                float rawDepth = SAMPLE_TEXTURE2D(_DepthTexture, sampler_DepthTexture, saturate(uv)).r;
                // Compare edges in linearized normalized depth, not device depth. This keeps thresholds usable
                // across different near/far planes and avoids the extreme sensitivity of raw depth values.
                return Linear01Depth(rawDepth, _ZBufferParams);
            }

            float GetLuminance(float3 color) {
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float GetStencilValue(float2 uv) {
                // Known limitation: URP camera depth textures do not expose stencil here. Keep the stencil
                // filter disabled until a real mask/stencil texture is provided by a separate pass.
                return SAMPLE_TEXTURE2D(_DepthTexture, sampler_DepthTexture, saturate(uv)).y * 255.0;
            }

            bool StencilCompare(float stencilVal) {
                if (_UseStencilFilter < 0.5) return true;
                int s = (int)round(stencilVal);
                if (_StencilCompare == 0) return true;
                if (_StencilCompare == 1) return false;
                if (_StencilCompare == 2) return s == _StencilReference;
                if (_StencilCompare == 3) return s != _StencilReference;
                return true;
            }

            float GetBlockDepth(float2 blockMin, float2 stepUV) {
                blockMin = ClampBlockMin(blockMin, stepUV);
                float2 inset = min(stepUV * 0.001, _BlitTexture_TexelSize.xy * 0.5);
                float2 blockMax = min(blockMin + stepUV - inset, float2(1.0, 1.0));
                blockMin += inset;
                float2 corners[4] = {
                    blockMin,
                    float2(blockMax.x, blockMin.y),
                    float2(blockMin.x, blockMax.y),
                    blockMax
                };
                float minDepth = 1.0;
                for (int sampleIndex = 0; sampleIndex < 4; sampleIndex++) {
                    float d = GetDepth(corners[sampleIndex]);
                    if (d < minDepth) minDepth = d;
                }
                return minDepth;
            }

            float4 Frag(Varyings input) : SV_Target {
                if (!StencilCompare(GetStencilValue(input.uv))) {
                    return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
                }

                #if DEPTH_PREVIEW
                    float depth = GetDepth(input.uv);
                    float gray = saturate(depth);
                    return float4(gray, gray, gray, 1.0);
                #else
                    float2 stepUV = GetPixelBlockSize();
                    float2 blockMin = GetPixelBlockMin(input.uv, stepUV);
                    float2 blockCenter = GetPixelBlockCenter(blockMin, stepUV);

                    float4 blockColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, blockCenter);
                    float blockDepth = GetBlockDepth(blockMin, stepUV);
                    if (blockDepth >= 0.9999) return blockColor;

                    float2 stepX = float2(stepUV.x, 0);
                    float2 stepY = float2(0, stepUV.y);
                    float leftDepth = GetBlockDepth(blockMin - stepX, stepUV);
                    float rightDepth = GetBlockDepth(blockMin + stepX, stepUV);
                    float downDepth = GetBlockDepth(blockMin - stepY, stepUV);
                    float upDepth = GetBlockDepth(blockMin + stepY, stepUV);

                    float maxDiff = 0.0;
                    int side = (int)_EdgeSide;
                    float diffs[4] = {
                        (side == 0) ? (blockDepth - leftDepth) : (leftDepth - blockDepth),
                        (side == 0) ? (blockDepth - rightDepth) : (rightDepth - blockDepth),
                        (side == 0) ? (blockDepth - downDepth) : (downDepth - blockDepth),
                        (side == 0) ? (blockDepth - upDepth) : (upDepth - blockDepth)
                    };
                    for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++) {
                        if (diffs[edgeIndex] > 0.0 && diffs[edgeIndex] > maxDiff) maxDiff = diffs[edgeIndex];
                    }

                    float darken = 0.0;
                    if (maxDiff > 0.0) {
                        float t = saturate((maxDiff - _MinDepthDiff) / max(_MaxDepthDiff - _MinDepthDiff, 0.000001));
                        darken = lerp(_MinDarkening, _MaxDarkening, t);
                    }

                    if (_ModulateByLuminance > 0.5) {
                        float lum = GetLuminance(blockColor.rgb);
                        float lumT = saturate((lum - _MinLuminance) / max(_MaxLuminance - _MinLuminance, 0.000001));
                        darken *= lerp(1.0, lumT, _LuminanceModStrength);
                    }

                    return float4(blockColor.rgb * (1.0 - darken), blockColor.a);
                #endif
            }
            ENDHLSL
        }
    }
}
