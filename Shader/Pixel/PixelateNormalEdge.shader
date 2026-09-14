Shader "Hidden/PixelateNormalEdge"
{
    Properties
    {
        _BlitTexture ("Texture", 2D) = "white" {}
        _NormalsTexture ("Normals", 2D) = "white" {}
        _DepthTexture ("Depth", 2D) = "white" {}
        _PixelSize ("Pixel Size", Float) = 8
        _NormalBias ("Normal Bias", Float) = 0.3
        _DepthThreshold ("Depth Threshold", Float) = 0.001
        _BrightenAmount ("Brighten Amount", Float) = 0.3
        _EdgeWidth ("Edge Width", Float) = 1
        _ModulateByLuminance ("Modulate by Luminance", Float) = 1
        _LuminanceContribution ("Luminance Contribution", Float) = 1
        _MinLuminance ("Min Luminance", Float) = 0.2
        _MaxLuminance ("Max Luminance", Float) = 0.8
        _UseDepthFilter ("Use Depth Filter", Float) = 1
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
            Name "NormalEdge"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ NORMAL_PREVIEW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BlitTexture); SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_NormalsTexture); SAMPLER(sampler_NormalsTexture);
            TEXTURE2D(_DepthTexture); SAMPLER(sampler_DepthTexture);
            float _PixelSize; float4 _BlitTexture_TexelSize;
            float _NormalBias; float _DepthThreshold; float _BrightenAmount;
            float _EdgeWidth; float _ModulateByLuminance; float _LuminanceContribution;
            float _MinLuminance; float _MaxLuminance; float _UseDepthFilter;
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

            float2 GetPixelBlockMin(float2 uv, float2 stepUV) {
                float2 blockMin = floor(uv / stepUV) * stepUV;
                return min(max(blockMin, float2(0.0, 0.0)), max(float2(0.0, 0.0), float2(1.0, 1.0) - stepUV));
            }

            float2 GetPixelBlockCenter(float2 blockMin, float2 stepUV) {
                return saturate(blockMin + stepUV * 0.5);
            }

            float3 GetNormal(float2 uv) {
                float3 n = SAMPLE_TEXTURE2D(_NormalsTexture, sampler_NormalsTexture, saturate(uv)).xyz;
                return normalize(n * 2.0 - 1.0);
            }

            float GetDepth(float2 uv) {
                float rawDepth = SAMPLE_TEXTURE2D(_DepthTexture, sampler_DepthTexture, saturate(uv)).r;
                // Match depth-edge filtering by using linearized normalized depth instead of device depth.
                return Linear01Depth(rawDepth, _ZBufferParams);
            }

            bool IsValidNormal(float3 normal) {
                return dot(normal, normal) > 0.5;
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

            bool IsEdge(float2 uv, float2 texelSize) {
                float centerDepth = GetDepth(uv);
                if (centerDepth >= 0.9999) return false;

                float3 centerNormal = GetNormal(uv);
                if (!IsValidNormal(centerNormal)) return false;

                int radius = max(1, (int)_EdgeWidth);
                [loop]
                for (int dy = -radius; dy <= radius; dy++) {
                    [loop]
                    for (int dx = -radius; dx <= radius; dx++) {
                        if (dx == 0 && dy == 0) continue;
                        float2 neighborUV = uv + float2(dx * texelSize.x, dy * texelSize.y);
                        if (neighborUV.x < 0 || neighborUV.x > 1 || neighborUV.y < 0 || neighborUV.y > 1) continue;
                        float neighborDepth = GetDepth(neighborUV);
                        if (neighborDepth >= 0.9999) continue;
                        if (_UseDepthFilter > 0.5 && abs(centerDepth - neighborDepth) > _DepthThreshold) continue;
                        float3 neighborNormal = GetNormal(neighborUV);
                        if (!IsValidNormal(neighborNormal)) continue;
                        float normalDiff = 1.0 - saturate(dot(centerNormal, neighborNormal));
                        if (normalDiff > _NormalBias) return true;
                    }
                }
                return false;
            }

            float4 Frag(Varyings input) : SV_Target {
                if (!StencilCompare(GetStencilValue(input.uv))) {
                    return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
                }

                float4 original = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);

                #if NORMAL_PREVIEW
                    float3 normal = GetNormal(input.uv);
                    return float4(normal * 0.5 + 0.5, 1.0);
                #else
                    float2 stepUV = GetPixelBlockSize();
                    float2 pixelatedUV = GetPixelBlockMin(input.uv, stepUV);
                    float2 centerUV = GetPixelBlockCenter(pixelatedUV, stepUV);

                    bool isEdge = IsEdge(centerUV, _BlitTexture_TexelSize.xy);
                    float4 result = original;
                    if (isEdge) {
                        float luminance = GetLuminance(original.rgb);
                        float t = saturate((luminance - _MinLuminance) / max(_MaxLuminance - _MinLuminance, 0.000001));
                        float addAmount = _BrightenAmount;
                        if (_ModulateByLuminance > 0.5) {
                            addAmount *= lerp(1.0, t, _LuminanceContribution);
                        } else {
                            addAmount *= t;
                        }
                        result.rgb = saturate(result.rgb + addAmount);
                    }
                    return result;
                #endif
            }
            ENDHLSL
        }
    }
}
