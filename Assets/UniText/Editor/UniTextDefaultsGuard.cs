using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal sealed class UniTextDefaultsGuard : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (deletedAssets.Length == 0) return;
            EditorApplication.delayCall += () => UniTextSettingsProvider.EnsureDefaults();
        }
    }

    internal sealed class UniTextSettingsMoveGuard : AssetModificationProcessor
    {
        private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
        {
            if (destinationPath == sourcePath) return AssetMoveResult.DidNotMove;
            if (AssetDatabase.LoadAssetAtPath<UniTextSettings>(sourcePath) == null)
                return AssetMoveResult.DidNotMove;

            var destDir = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/') ?? "";
            if (destDir.EndsWith("/Resources") || destDir == "Resources")
                return AssetMoveResult.DidNotMove;

            Debug.LogWarning("[UniText] UniTextSettings must be in a Resources/ folder for runtime loading.");
            return AssetMoveResult.FailedMove;
        }
    }
}
