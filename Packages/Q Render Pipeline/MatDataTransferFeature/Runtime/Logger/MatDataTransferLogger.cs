using UnityEngine;

namespace Rendering.MatDataTransfer.Runtime
{
    public static class MatDataTransferLogger
    {
        private const string Prefix = "[MatDataTransfer] ";

        private static bool s_UnityConsoleErrorsEnabled = true;
        private static bool s_RuntimeFileOutputEnabled;

        public static bool RuntimeFileOutputEnabled => s_RuntimeFileOutputEnabled;

        public static void ConfigureUnityConsole(bool errorsEnabled)
        {
            s_UnityConsoleErrorsEnabled = errorsEnabled;
        }

        public static void SetRuntimeFileOutputEnabled(bool enabled)
        {
            s_RuntimeFileOutputEnabled = enabled;
        }

        public static void Log(string message)
        {
            if (s_UnityConsoleErrorsEnabled)
                Debug.Log(Prefix + message);
        }

        public static void LogWarning(string message)
        {
            if (s_UnityConsoleErrorsEnabled)
                Debug.LogWarning(Prefix + message);
        }

        public static void LogError(string message)
        {
            if (s_UnityConsoleErrorsEnabled)
                Debug.LogError(Prefix + message);
        }
    }
}
