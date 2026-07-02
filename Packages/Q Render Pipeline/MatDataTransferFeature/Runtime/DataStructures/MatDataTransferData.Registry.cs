namespace Rendering.MatDataTransfer.Runtime
{
    public readonly struct InstanceRegisterEntry
    {
        public readonly int Id;
        public readonly MatDataTransferInstance Instance;
        public readonly string SourceId;
        public readonly string DisplayName;

        public InstanceRegisterEntry(int id, MatDataTransferInstance instance)
        {
            Id = id;
            Instance = instance;
            SourceId = instance != null ? instance.SourceId : string.Empty;
            DisplayName = instance != null ? instance.name : "<Missing>";
        }
    }
}
