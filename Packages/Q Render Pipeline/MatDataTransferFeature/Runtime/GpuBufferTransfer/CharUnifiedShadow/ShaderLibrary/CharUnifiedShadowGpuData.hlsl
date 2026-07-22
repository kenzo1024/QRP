#ifndef MAT_DATA_TRANSFER_CHAR_UNIFIED_SHADOW_GPU_DATA_INCLUDED
#define MAT_DATA_TRANSFER_CHAR_UNIFIED_SHADOW_GPU_DATA_INCLUDED

#include "../../ShaderLibrary/GpuBufferInstanceIndex.hlsl"

StructuredBuffer<uint4> _MdtCharUnifiedShadowRanges;
StructuredBuffer<float4> _MdtCharUnifiedShadowSamples;
uint _MdtCharUnifiedShadowInstanceCapacity;

#define MDT_CHAR_UNIFIED_SHADOW_RANGE_VALID 1u

bool TryGetMdtCharUnifiedShadowRange(
    float encodedInstanceIndex,
    out uint baseIndex,
    out uint count)
{
    uint instanceIndex;
    if (!TryDecodeGpuBufferInstanceIndex(
            encodedInstanceIndex,
            _MdtCharUnifiedShadowInstanceCapacity,
            instanceIndex))
    {
        baseIndex = 0u;
        count = 0u;
        return false;
    }

    uint4 range = _MdtCharUnifiedShadowRanges[instanceIndex];
    baseIndex = range.x;
    count = range.y;
    return (range.z & MDT_CHAR_UNIFIED_SHADOW_RANGE_VALID) != 0u;
}

bool TryLoadMdtCharUnifiedShadowSample(
    float encodedInstanceIndex,
    uint sampleIndex,
    out float4 sampleData)
{
    uint baseIndex;
    uint count;
    if (!TryGetMdtCharUnifiedShadowRange(encodedInstanceIndex, baseIndex, count)
        || sampleIndex >= count)
    {
        sampleData = 0.0.xxxx;
        return false;
    }

    sampleData = _MdtCharUnifiedShadowSamples[baseIndex + sampleIndex];
    return true;
}

#endif
