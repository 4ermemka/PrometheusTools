Shader "Custom/GrassBlade"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        _BottomColor ("Bottom Color", Color) = (0.2,0.6,0.2,1)
        _TopColor ("Top Color", Color) = (0.8,1,0.5,1)

        // Wind
        _WindStrength ("Wind Strength", Range(0, 2)) = 0.8
        _WindSpeed ("Wind Speed", Range(0, 5)) = 1.5
        _WindFrequency ("Wind Frequency", Range(0.1, 5)) = 2.0
        _WindDirection ("Wind Direction (X, Y, Z)", Vector) = (1,0,0,0)

        // Shadow settings
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        // ------------------------------------------------------------------
        // FORWARD LIT PASS
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<float4> _GrassBuffer; // xyz = position, w = randomSeed
            #endif

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            half4 _Color;
            half4 _BottomColor;
            half4 _TopColor;
            float _WindStrength;
            float _WindSpeed;
            float _WindFrequency;
            float3 _WindDirection;
            float _Cutoff;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float3 lightingNormalWS : TEXCOORD2; // Для освещения (вверх)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            void setup()
            {
                float4 data = _GrassBuffer[unity_InstanceID];
                float3 worldPos = data.xyz;
                unity_ObjectToWorld = float4x4(
                    1,0,0, worldPos.x,
                    0,1,0, worldPos.y,
                    0,0,1, worldPos.z,
                    0,0,0, 1
                );
                unity_WorldToObject = unity_ObjectToWorld;
                unity_WorldToObject._14_24_34 *= -1;
                unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
            }
            #endif

            float3 ApplyWind(float3 vertexOS, float3 worldPos, float seed)
            {
                float time = _Time.y * _WindSpeed;
                float3 windDir = normalize(_WindDirection);
                float noise = sin(worldPos.x * _WindFrequency + worldPos.z * 0.7 + seed * 10.0 + time);
                noise += cos(worldPos.z * _WindFrequency * 0.5 + seed * 5.0 + time * 0.8) * 0.5;
                float heightFactor = saturate(vertexOS.y + 0.5); // +0.5 т.к. pivot смещён
                float2 offsetXZ = windDir.xz * noise * _WindStrength * heightFactor;
                vertexOS.x += offsetXZ.x;
                vertexOS.z += offsetXZ.y;
                return vertexOS;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 worldPos = float3(unity_ObjectToWorld[0].w, unity_ObjectToWorld[1].w, unity_ObjectToWorld[2].w);
                float seed = frac(worldPos.x * 0.1 + worldPos.z * 0.1) * 10.0;

                // Смещаем pivot к основанию модели (предполагаем, что высота Quad = 1, центр в 0)
                float3 pivotOffset = float3(0, -0.5, 0);
                float3 adjustedVertexOS = input.positionOS.xyz + pivotOffset;

                float3 vertexOS = ApplyWind(adjustedVertexOS, worldPos, seed);

                // Billboard
                float3 cameraPosWS = GetCameraPositionWS();
                float3 viewDirWS = normalize(cameraPosWS - worldPos);
                float3 upWS = float3(0, 1, 0);
                float3 rightWS = normalize(cross(upWS, viewDirWS));
                float3 forwardWS = viewDirWS;
                upWS = normalize(cross(forwardWS, rightWS));

                float3 localPos = vertexOS;
                float3 billboardedPosWS = worldPos +
                                          localPos.x * rightWS +
                                          localPos.y * upWS +
                                          localPos.z * forwardWS;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(billboardedPosWS);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;

                // Нормаль для освещения – строго вверх
                output.lightingNormalWS = float3(0, 1, 0);

                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = lerp(_BottomColor, _TopColor, input.uv.y);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 baseColor = texColor * _Color * input.color;
                clip(baseColor.a - _Cutoff);

                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = input.positionWS;
                lightingInput.normalWS = input.lightingNormalWS; // Используем нормаль вверх
                lightingInput.viewDirectionWS = GetWorldSpaceViewDir(input.positionWS);
                lightingInput.shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                Light mainLight = GetMainLight(lightingInput.shadowCoord);
                half3 lightColor = mainLight.color;
                half3 lightDir = mainLight.direction;

                half NdotL = saturate(dot(input.lightingNormalWS, lightDir));
                half3 diffuse = baseColor.rgb * lightColor * NdotL * mainLight.shadowAttenuation;

                #ifdef _ADDITIONAL_LIGHTS
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint i = 0; i < pixelLightCount; ++i)
                {
                    Light light = GetAdditionalLight(i, input.positionWS, half4(1,1,1,1));
                    half3 lightColorAdd = light.color;
                    half3 lightDirAdd = light.direction;
                    half NdotLAdd = saturate(dot(input.lightingNormalWS, lightDirAdd));
                    half atten = light.distanceAttenuation * light.shadowAttenuation;
                    diffuse += baseColor.rgb * lightColorAdd * NdotLAdd * atten;
                }
                #endif

                half3 ambient = SampleSH(input.lightingNormalWS) * baseColor.rgb;
                half3 finalColor = diffuse + ambient;
                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // SHADOWCASTER PASS (рабочий в URP 17+)
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<float4> _GrassBuffer;
            #endif

            float _WindStrength;
            float _WindSpeed;
            float _WindFrequency;
            float3 _WindDirection;
            float _Cutoff;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            void setup()
            {
                float4 data = _GrassBuffer[unity_InstanceID];
                float3 worldPos = data.xyz;
                unity_ObjectToWorld = float4x4(
                    1,0,0, worldPos.x,
                    0,1,0, worldPos.y,
                    0,0,1, worldPos.z,
                    0,0,0, 1
                );
                unity_WorldToObject = unity_ObjectToWorld;
                unity_WorldToObject._14_24_34 *= -1;
                unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
            }
            #endif

            float3 ApplyWind(float3 vertexOS, float3 worldPos, float seed)
            {
                float time = _Time.y * _WindSpeed;
                float3 windDir = normalize(_WindDirection);
                float noise = sin(worldPos.x * _WindFrequency + worldPos.z * 0.7 + seed * 10.0 + time);
                noise += cos(worldPos.z * _WindFrequency * 0.5 + seed * 5.0 + time * 0.8) * 0.5;
                float heightFactor = saturate(vertexOS.y + 0.5);
                float2 offsetXZ = windDir.xz * noise * _WindStrength * heightFactor;
                vertexOS.x += offsetXZ.x;
                vertexOS.z += offsetXZ.y;
                return vertexOS;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 worldPos = float3(unity_ObjectToWorld[0].w, unity_ObjectToWorld[1].w, unity_ObjectToWorld[2].w);
                float seed = frac(worldPos.x * 0.1 + worldPos.z * 0.1) * 10.0;

                float3 pivotOffset = float3(0, -0.5, 0);
                float3 adjustedVertexOS = input.positionOS.xyz + pivotOffset;
                float3 vertexOS = ApplyWind(adjustedVertexOS, worldPos, seed);

                // Billboard
                float3 cameraPosWS = GetCameraPositionWS();
                float3 viewDirWS = normalize(cameraPosWS - worldPos);
                float3 upWS = float3(0, 1, 0);
                float3 rightWS = normalize(cross(upWS, viewDirWS));
                float3 forwardWS = viewDirWS;
                upWS = normalize(cross(forwardWS, rightWS));

                float3 localPos = vertexOS;
                float3 billboardedPosWS = worldPos +
                                          localPos.x * rightWS +
                                          localPos.y * upWS +
                                          localPos.z * forwardWS;

                // Расчёт позиции в shadow map
                float4 positionCS = mul(UNITY_MATRIX_VP, float4(billboardedPosWS, 1.0));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                output.uv = input.uv;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}