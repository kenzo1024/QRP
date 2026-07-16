using System.IO;
using Rendering.MatDataTransfer.Editor;
using Rendering.MatDataTransfer.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rendering.MatDataTransfer.PerformanceTests.Editor
{
    internal static class MatDataTransferBatchEnvironmentBuilder
    {
        private const string ShaderPath = "Packages/Q Render Pipeline/Shaders/Unlit.shader";
        private const string CatalogPath = "Packages/Q Render Pipeline/MatDataTransferFeature/Configs/QRP_Unlit_ShaderPropertyCatalog.asset";
        private const string SemanticKeyProfilePath = "Packages/Q Render Pipeline/MatDataTransferFeature/Configs/MaterialSemanticKeyProfile.asset";
        private const string ScenePath = "Packages/Q Render Pipeline/MatDataTransferFeature/PerformanceTests/Scenes/MatDataTransferBatchModePerformance.unity";
        private const string ShaderGuid = "6a12d72a332b4a94aa08c930676f5c24";
        private const string CatalogGuid = "ac899ca6127493845976ec24e89a4cb2";
        private const string SemanticKeyProfileGuid = "361a3e27598330c4b9247e7c1076e3da";

        [MenuItem("Rendering/MatDataTransfer/Performance Tests/Rebuild Batch Environment")]
        public static void Rebuild()
        {
            SyncUnlitCatalog();
            CreateScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void SyncUnlitCatalog()
        {
            Shader shader = LoadAsset<Shader>(ShaderPath, ShaderGuid);
            if (shader == null)
                shader = Shader.Find("QRP/Unlit");
            ShaderPropertyCatalog catalog = LoadAsset<ShaderPropertyCatalog>(CatalogPath, CatalogGuid);
            MaterialSemanticKeyProfile profile = LoadAsset<MaterialSemanticKeyProfile>(SemanticKeyProfilePath, SemanticKeyProfileGuid);

            if (shader == null)
                throw new FileNotFoundException("Shader not found.", ShaderPath);
            if (catalog == null)
                throw new FileNotFoundException("Catalog not found.", CatalogPath);

            ShaderPropertyCatalogBuilder.SyncCatalog(catalog, shader, profile);
            MarkAllPropertiesOk(catalog);
            EditorUtility.SetDirty(catalog);
        }

        public static void CreateScene()
        {
            EnsureDirectory(Path.GetDirectoryName(ScenePath));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject cameraObject = new GameObject("MDT_Batch_Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 12f, -24f);
            camera.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            GameObject driverObject = new GameObject("MatDataTransferBatchModePerformance");
            driverObject.AddComponent<MatDataTransferBatchLoadDriver>();

            GameObject marker = new GameObject("Generated objects are created by the driver at runtime");
            marker.transform.SetParent(driverObject.transform, false);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void MarkAllPropertiesOk(ShaderPropertyCatalog catalog)
        {
            for (int i = 0; i < catalog.Properties.Count; i++)
            {
                CatalogProperty property = catalog.Properties[i];
                if (property != null)
                    property.Status = CatalogPropertyStatus.Ok;
            }
        }

        private static void EnsureDirectory(string assetDirectory)
        {
            if (string.IsNullOrEmpty(assetDirectory))
                return;

            string fullPath = Path.GetFullPath(assetDirectory);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        private static T LoadAsset<T>(string assetPath, string guid)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                return asset;

            string guidPath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(guidPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<T>(guidPath);
        }
    }
}
