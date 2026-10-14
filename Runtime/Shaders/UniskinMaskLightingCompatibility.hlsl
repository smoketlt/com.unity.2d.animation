#ifndef UNISKIN_MASK_LIGHTING_COMPATIBILITY_INCLUDED
#define UNISKIN_MASK_LIGHTING_COMPATIBILITY_INCLUDED

// Shader compilation exposes UNITY_VERSION rather than the C# version symbols.
#if UNITY_VERSION >= 600050 && !defined(UNITY_6000_5_OR_NEWER)
    #define UNITY_6000_5_OR_NEWER 1
#endif

#if UNITY_6000_5_OR_NEWER
    // CombinedShapeLightShared includes LightingUtility and declares shape lights.
#else
    #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
    #if USE_SHAPE_LIGHT_TYPE_0
        SHAPE_LIGHT(0)
    #endif
    #if USE_SHAPE_LIGHT_TYPE_1
        SHAPE_LIGHT(1)
    #endif
    #if USE_SHAPE_LIGHT_TYPE_2
        SHAPE_LIGHT(2)
    #endif
    #if USE_SHAPE_LIGHT_TYPE_3
        SHAPE_LIGHT(3)
    #endif
#endif

#endif
