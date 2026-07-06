using System;

namespace Rendering.MatDataTransfer.Runtime
{
    [Serializable]
    public sealed class MatDataTransferLoggingSettings
    {
        public bool EnableLogging;
        public bool AllowReleaseFileLogging;
        public int MaxTimelineFrames = MatDataTransferLogging.DefaultMaxTimelineFramesValue;
    }
}
