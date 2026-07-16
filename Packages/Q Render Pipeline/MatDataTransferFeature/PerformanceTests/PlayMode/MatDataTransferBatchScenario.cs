using System;

namespace Rendering.MatDataTransfer.PerformanceTests
{
    internal enum MatDataTransferBatchApiMode
    {
        None,
        ForMaterial,
        ForInstance
    }

    internal readonly struct MatDataTransferBatchScenario
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int ObjectCount;
        public readonly int PropertyCount;
        public readonly int SourceCount;
        public readonly MatDataTransferBatchApiMode ApiMode;
        public readonly bool EnableLogging;
        public readonly int WarmupFrames;
        public readonly int MeasurementFrames;

        public MatDataTransferBatchScenario(
            string id,
            string name,
            int objectCount,
            int propertyCount,
            int sourceCount,
            MatDataTransferBatchApiMode apiMode,
            bool enableLogging,
            int warmupFrames,
            int measurementFrames)
        {
            Id = id;
            Name = name;
            ObjectCount = objectCount;
            PropertyCount = propertyCount;
            SourceCount = sourceCount;
            ApiMode = apiMode;
            EnableLogging = enableLogging;
            WarmupFrames = warmupFrames;
            MeasurementFrames = measurementFrames;
        }

        public int ExpectedPayloadCount => ObjectCount * PropertyCount * SourceCount;
        public int ExpectedGroupCount => ObjectCount * PropertyCount;
        public int ExpectedCommandCount => SourceCount > 0 ? ObjectCount * PropertyCount : 0;
        public int ExpectedAppliedCount => ExpectedCommandCount;
        public int ExpectedOverriddenCount => ObjectCount * PropertyCount * Math.Max(0, SourceCount - 1);

        public string TestName => Id + "_" + Name;

        public static MatDataTransferBatchScenario B00_EmptyDriver()
        {
            return Baseline("B00", "EmptyDriver", 300, 12, 0, MatDataTransferBatchApiMode.None, false);
        }

        public static MatDataTransferBatchScenario B01_SmallSingleSource()
        {
            return Baseline("B01", "SmallSingleSource", 100, 12, 1, MatDataTransferBatchApiMode.ForMaterial, false);
        }

        public static MatDataTransferBatchScenario B02_SmallConflict()
        {
            return Baseline("B02", "SmallConflict", 100, 12, 3, MatDataTransferBatchApiMode.ForMaterial, false);
        }

        public static MatDataTransferBatchScenario B03_ObjectScale()
        {
            return Baseline("B03", "ObjectScale", 300, 12, 1, MatDataTransferBatchApiMode.ForMaterial, false);
        }

        public static MatDataTransferBatchScenario B04_MainRealLoad()
        {
            return Baseline("B04", "MainRealLoad", 300, 12, 3, MatDataTransferBatchApiMode.ForMaterial, false);
        }

        public static MatDataTransferBatchScenario B05_LargeObjectPressure()
        {
            return Pressure("B05", "LargeObjectPressure", 500, 12, 3, MatDataTransferBatchApiMode.ForMaterial, false);
        }

        public static MatDataTransferBatchScenario B06_ManySourcesPressure()
        {
            return Pressure("B06", "ManySourcesPressure", 300, 12, 5, MatDataTransferBatchApiMode.ForMaterial, false);
        }

        public static MatDataTransferBatchScenario B07_ForInstance()
        {
            return Baseline("B07", "ForInstance", 300, 12, 3, MatDataTransferBatchApiMode.ForInstance, false);
        }

        public static MatDataTransferBatchScenario B08_NarrowProperties()
        {
            return Baseline("B08", "NarrowProperties", 300, 4, 3, MatDataTransferBatchApiMode.ForMaterial, false);
        }

        public static MatDataTransferBatchScenario B09_Logging()
        {
            return Pressure("B09", "Logging", 300, 12, 3, MatDataTransferBatchApiMode.ForMaterial, true);
        }

        public static MatDataTransferBatchScenario Smoke()
        {
            return new MatDataTransferBatchScenario(
                "SMK",
                "Smoke",
                10,
                4,
                2,
                MatDataTransferBatchApiMode.ForMaterial,
                false,
                8,
                12);
        }

        private static MatDataTransferBatchScenario Baseline(
            string id,
            string name,
            int objectCount,
            int propertyCount,
            int sourceCount,
            MatDataTransferBatchApiMode apiMode,
            bool enableLogging)
        {
            return new MatDataTransferBatchScenario(
                id,
                name,
                objectCount,
                propertyCount,
                sourceCount,
                apiMode,
                enableLogging,
                120,
                300);
        }

        private static MatDataTransferBatchScenario Pressure(
            string id,
            string name,
            int objectCount,
            int propertyCount,
            int sourceCount,
            MatDataTransferBatchApiMode apiMode,
            bool enableLogging)
        {
            return new MatDataTransferBatchScenario(
                id,
                name,
                objectCount,
                propertyCount,
                sourceCount,
                apiMode,
                enableLogging,
                60,
                120);
        }
    }
}
