using System.Collections.Generic;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private void ApplyWriteCommands(IReadOnlyList<ParamWriteCommand> commands)
        {
            m_Writer?.Apply(commands);
        }

        private void ClearWrittenState()
        {
            m_Writer?.ClearWrittenState();
        }
    }
}
