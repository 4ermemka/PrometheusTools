#ifndef STENCIL_UTILS_INCLUDED
#define STENCIL_UTILS_INCLUDED

float GetStencilValue(float2 uv, float4 screenSize)
{
    uint2 pixelCoord = uv * screenSize.xy;
    // _CameraDepthTexture - должно быть объявлено в шейдере
    float depthStencil = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).y;
    return fmod(depthStencil * 255, 256); // простейший способ извлечь stencil
}

bool StencilCompare(float stencil, int refVal, int compareFunc)
{
    int stencilInt = (int) round(stencil);
    switch (compareFunc)
    {
        case 0:
            return true; // Always
        case 1:
            return false; // Never
        case 2:
            return stencilInt == refVal; // Equal
        case 3:
            return stencilInt != refVal; // NotEqual
        // остальные можно добавить
        default:
            return true;
    }
}

#endif