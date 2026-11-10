using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CakeSort.EditorTools
{
    /// <summary>Applies the player settings the game expects (identity, orientation, icon, iOS backend).</summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string BundleId = "com.example.cakesort.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (PlayerSettings.productName != "Cake Sort") Apply();
            };
        }

        [MenuItem("Cake Sort/Apply Project Settings")]
        public static void Apply()
        {
            PlayerSettings.companyName = "CakeSort";
            PlayerSettings.productName = "Cake Sort";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.runInBackground = false;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/AppIcon.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log("[CakeSort] Project settings applied");
        }
    }

    /// <summary>Imports generated art as full-quality sprites without power-of-two rescaling.</summary>
    public class ArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Art/") && !assetPath.StartsWith("Assets/Art/")) return;
            var importer = (TextureImporter)assetImporter;
            bool isIcon = assetPath.StartsWith("Assets/Art/");
            importer.textureType = isIcon ? TextureImporterType.Default : TextureImporterType.Sprite;
            importer.spriteImportMode = isIcon ? SpriteImportMode.None : SpriteImportMode.Single;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = !isIcon;
            importer.mipmapEnabled = !isIcon;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 2048;
        }
    }
}
