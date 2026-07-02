using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    [Serializable]
    public sealed class GenericMaterialParameterProviderSettings
    {
        public bool Enabled = true;
    }

    internal sealed class GenericMaterialParameterProvider : IMatDataTransferRequestProvider
    {
        internal const string ProviderName = MatDataTransferAPI.GenericProviderName;

        private static readonly List<MaterialParameterSubmitPayload> s_Requests =
            new List<MaterialParameterSubmitPayload>();
        internal GenericMaterialParameterProvider() { }

        public string Name => ProviderName;

        internal static bool TryQueue(MaterialParameterSubmitPayload payload)
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            if (feature == null
                || !feature.IsGenericMaterialParameterProviderEnabled
                || feature.GetRequestProvider(ProviderName) == null)
            {
                ClearAllRequests();
                return false;
            }

            payload.Identity.ProviderName = ProviderName;
            s_Requests.Add(payload);

            return true;
        }

        internal static bool TryClearQueuedRequests(int instanceId)
        {
            if (!IsProviderEnabled())
                return false;

            bool removed = false;
            for (int i = s_Requests.Count - 1; i >= 0; i--)
            {
                if (s_Requests[i].InstanceId != instanceId)
                    continue;

                s_Requests.RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        public void Dispose()
        {
            ClearAllRequests();
        }

        public void SubmitRequests(MaterialWriteContext context)
        {
            if (context == null || !IsProviderEnabled())
            {
                ClearAllRequests();
                return;
            }

            for (int i = 0; i < s_Requests.Count; i++)
                context.Submit(s_Requests[i]);

            s_Requests.Clear();
        }

        public static void ClearAllRequests()
        {
            s_Requests.Clear();
        }

        private static bool IsProviderEnabled()
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            return feature != null
                && feature.IsGenericMaterialParameterProviderEnabled
                && feature.GetRequestProvider(ProviderName) != null;
        }

    }
}
