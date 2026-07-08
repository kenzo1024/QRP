namespace Rendering.MatDataTransfer.Runtime
{
    internal static class MatDataTransferSubmitSequence
    {
        private static int s_FrameIndex = -1;
        private static int s_Sequence;

        internal static int Next(out int frameIndex)
        {
            frameIndex = MatDataTransferRuntime.SubmitFrameIndex;
            if (s_FrameIndex != frameIndex)
            {
                s_FrameIndex = frameIndex;
                s_Sequence = 0;
            }

            return unchecked(++s_Sequence);
        }
    }
}
