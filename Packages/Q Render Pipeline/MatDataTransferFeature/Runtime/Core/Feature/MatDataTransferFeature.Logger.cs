namespace Rendering.MatDataTransfer.Runtime
{
    public partial class MatDataTransferFeature
    {
        public MatDataTransferLogging Logging => MatDataTransferLogging.Instance;

        private void ApplyLoggerSettings()
        {
            Logging.BindFeature(this);
            Logging.ApplySettings(m_LoggingSettings);
            MatDataTransferLogger.ConfigureUnityConsole(true);
            MatDataTransferRuntime.RequestEditorUpdate();
        }

        private void DisposeLogger()
        {
            Logging.UnbindFeature(this);
        }
    }
}
