#ifndef MAT_DATA_TRANSFER_GPU_BUFFER_INSTANCE_INDEX_INCLUDED
#define MAT_DATA_TRANSFER_GPU_BUFFER_INSTANCE_INDEX_INCLUDED

bool TryDecodeGpuBufferInstanceIndex(
    float encodedValue,
    uint instanceCapacity,
    out uint instanceIndex)
{
    if (encodedValue > -0.5)
    {
        instanceIndex = 0u;
        return false;
    }

    instanceIndex = (uint)round(-encodedValue) - 1u;
    return instanceIndex < instanceCapacity;
}

#endif
