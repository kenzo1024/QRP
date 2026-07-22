using System.Runtime.InteropServices;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime.GpuBuffer.CharUnifiedShadow
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CharUnifiedShadowGpuInput
    {
        public Vector4 BoundsCenterAndMode;
        public Vector4 BoundsExtentsAndCount;
        public Vector4 LightDirectionAndDeltaTime;
        public Vector4 Settings;
        public Vector4 Control;

        public Vector4 AnchorPosition0;
        public Vector4 AnchorPosition1;
        public Vector4 AnchorPosition2;
        public Vector4 AnchorPosition3;
        public Vector4 AnchorPosition4;
        public Vector4 AnchorPosition5;

        public Vector4 AnchorParameters0;
        public Vector4 AnchorParameters1;
        public Vector4 AnchorParameters2;
        public Vector4 AnchorParameters3;
        public Vector4 AnchorParameters4;
        public Vector4 AnchorParameters5;

        public void SetAnchor(int index, Vector4 positionAndWeight, Vector4 parameters)
        {
            switch (index)
            {
                case 0: AnchorPosition0 = positionAndWeight; AnchorParameters0 = parameters; break;
                case 1: AnchorPosition1 = positionAndWeight; AnchorParameters1 = parameters; break;
                case 2: AnchorPosition2 = positionAndWeight; AnchorParameters2 = parameters; break;
                case 3: AnchorPosition3 = positionAndWeight; AnchorParameters3 = parameters; break;
                case 4: AnchorPosition4 = positionAndWeight; AnchorParameters4 = parameters; break;
                case 5: AnchorPosition5 = positionAndWeight; AnchorParameters5 = parameters; break;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CharUnifiedShadowGpuRange
    {
        internal uint BaseIndex;
        internal uint Count;
        internal uint Flags;
        internal uint Reserved;
    }
}
