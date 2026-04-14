#ifndef CUSTOM_ADDITIONAL_LIGHTS_SHADOWS_INCLUDED
#define CUSTOM_ADDITIONAL_LIGHTS_SHADOWS_INCLUDED

// Подключаем только Input.hlsl, где уже объявлены все нужные текстуры и массивы
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"

// Вспомогательная функция для сэмплирования тени дополнительного источника
float SampleAdditionalLightShadow(int lightIndex, float3 worldPos)
{
    // Получаем параметры тени для этого источника
    float4 shadowParams = _AdditionalLightsShadowParams[lightIndex];
    int shadowSliceIndex = (int)shadowParams.w;
    
    // Если индекс теневой карты отрицательный, значит источник не отбрасывает тени
    if (shadowSliceIndex < 0) return 1.0;
    
    // Преобразуем мировую позицию в координаты теневой карты
    float4 shadowCoord = mul(_AdditionalLightsWorldToShadow[shadowSliceIndex], float4(worldPos, 1.0));
    shadowCoord.xyz /= shadowCoord.w;
    
    // Проверяем, попадает ли точка в пределы теневой карты
    if (shadowCoord.x < 0.0 || shadowCoord.x > 1.0 || 
        shadowCoord.y < 0.0 || shadowCoord.y > 1.0 || 
        shadowCoord.z < 0.0 || shadowCoord.z > 1.0)
        return 1.0;
    
    // Сэмплируем глубинную текстуру
    float shadowDepth = SAMPLE_TEXTURE2D(_AdditionalLightsShadowmapTexture, 
                                         sampler_AdditionalLightsShadowmapTexture, 
                                         shadowCoord.xy).r;
    
    // Сравнение глубины с учётом реверсивного Z в URP
    #if UNITY_REVERSED_Z
        return (shadowDepth > shadowCoord.z) ? 1.0 : 0.0;
    #else
        return (shadowDepth < shadowCoord.z) ? 1.0 : 0.0;
    #endif
}

void CalculateAdditionalLighting_float(
    float3 WorldPos,
    float3 WorldNormal,
    out float3 DiffuseLighting)
{
    DiffuseLighting = float3(0, 0, 0);
    
    // Получаем количество дополнительных источников
    int lightCount = min(_AdditionalLightsCount, MAX_VISIBLE_LIGHTS);
    
    for (int i = 0; i < lightCount; i++)
    {
        // Данные о свете из массивов URP
        float4 lightPosWS = _AdditionalLightsPosition[i];
        float3 lightColor = _AdditionalLightsColor[i].rgb;
        float lightRange = _AdditionalLightsColor[i].w;
        
        // Направление к источнику и затухание
        float3 lightVec = lightPosWS.xyz - WorldPos * lightPosWS.w;
        float distanceSqr = dot(lightVec, lightVec);
        float3 lightDir = normalize(lightVec);
        
        // Затухание по расстоянию (модель URP)
        float rangeSqr = lightRange * lightRange;
        float distanceAtten = saturate(1.0 - distanceSqr / rangeSqr);
        distanceAtten *= distanceAtten;
        
        if (distanceAtten <= 0.0) continue;
        
        // Тень
        float shadowAtten = SampleAdditionalLightShadow(i, WorldPos);
        
        // Диффузное освещение
        float NdotL = saturate(dot(WorldNormal, lightDir));
        
        DiffuseLighting += lightColor * NdotL * distanceAtten * shadowAtten;
    }
}
#endif