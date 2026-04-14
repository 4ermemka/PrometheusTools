#ifndef CUSTOM_ALL_LIGHTS_DIFFUSE_FIXED
#define CUSTOM_ALL_LIGHTS_DIFFUSE_FIXED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

void GetAllLightsDiffuse_float(
    float3 WorldPosition,
    float3 WorldNormal,
    out float3 DiffuseLighting
)
{
    #ifdef SHADERGRAPH_PREVIEW
        DiffuseLighting = float3(0.5, 0.5, 0.5);
    #else
        // ### НОВОЕ: Проверка, не находимся ли мы в проходе ShadowCaster ###
        // Если да, то сложные расчеты освещения не нужны, выходим.
        #if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_SHADOWCASTER)
            DiffuseLighting = float3(0, 0, 0);
            return;
        #endif

        DiffuseLighting = float3(0, 0, 0);

        // --- 1. Основной источник (Main Light) ---
        Light mainLight = GetMainLight();
        float3 mainLightColor = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
        float mainDiffuse = saturate(dot(WorldNormal, mainLight.direction));
        DiffuseLighting += mainLightColor * mainDiffuse;

        // --- 2. Дополнительные источники (Additional Lights) ---
        uint lightCount = GetAdditionalLightsCount();
        for (uint i = 0; i < lightCount; i++)
        {
            Light light = GetAdditionalLight(i, WorldPosition);
            float3 lightColor = light.color * light.distanceAttenuation * light.shadowAttenuation;
            float diffuse = saturate(dot(WorldNormal, light.direction));
            DiffuseLighting += lightColor * diffuse;
        }
    #endif
}
#endif