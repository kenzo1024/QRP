using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        private void ApplyWriteCommands(IReadOnlyList<ParamWriteCommand> commands)
        {
            m_Writer?.Apply(commands);
        }

        private void ApplyWriterSettings()
        {
            m_Writer?.SetWriteMode(GetActiveWriteMode());
        }

        private ParamWriteMethod GetActiveWriteMode()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return m_EditModeWriteMode;
#endif
            return m_RuntimeWriteMode;
        }

        private void ClearWrittenState()
        {
            m_Writer?.ClearWrittenState();
        }
    }
}
