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
        internal const string ProviderName = MatDataTransferProviderNames.GenericMaterialParameter;

        private static readonly List<ParamTransferPayload> s_Requests =
            new List<ParamTransferPayload>();
        internal GenericMaterialParameterProvider() { }

        public string Name => ProviderName;

        internal static bool TryQueue(ref ParamTransferPayload payload)
        {
            MatDataTransferFeature feature = MatDataTransferFeature.Instance;
            if (feature == null
                || !feature.IsGenericMaterialParameterProviderEnabled
                || feature.GetRequestProvider(ProviderName) == null)
            {
                ClearAllRequests();
                MatDataTransferLogging.AppendSubmitStep(
                    ref payload,
                    ParamSubmitStep.Rejected(
                        "Submit.Queue",
                        ParamWriteResultCode.ProviderUnavailable,
                        "Generic material parameter provider rejected the request."));
                return false;
            }

            payload.ProviderName = ProviderName;
            MatDataTransferLogging.AppendSubmitStep(
                ref payload,
                ParamSubmitStep.Queued(
                    "Submit.Queue",
                    "Submit accepted."));
            s_Requests.Add(payload);

            return true;
        }

        internal static bool TryClearQueuedRequests(MatDataTransferInstance instance)
        {
            if (!IsProviderEnabled() || instance == null)
                return false;

            bool removed = false;
            for (int i = s_Requests.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(s_Requests[i].Identity.Target, instance))
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
