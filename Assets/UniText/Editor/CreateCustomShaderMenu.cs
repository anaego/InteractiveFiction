using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal static class CreateCustomShaderMenu
    {
        private const string TemplateGuidSearchFilter =
            "t:Shader UniText_Custom-Example";
        private const string IncludeFileSearchFilter =
            "UniText_Custom t:TextAsset";

        [MenuItem("Assets/Create/UniText/Custom Material Shader", false, 200)]
        private static void Create()
        {
            if (!TryLocateTemplate(out var templateAssetPath))
            {
                Debug.LogError(
                    "[UniText] Template shader 'UniText_Custom-Example' not found. " +
                    "Reinstall the package or verify that Assets/UniText/Shaders/Templates exists.");
                return;
            }

            if (!TryLocateInclude(out var includeAssetPath))
            {
                Debug.LogError(
                    "[UniText] Include file 'UniText_Custom.cginc' not found. " +
                    "Reinstall the package or verify that Assets/UniText/Shaders exists.");
                return;
            }

            ProjectWindowUtil.CreateAssetWithContent(
                "UniTextCustom.shader",
                BuildShaderContent(templateAssetPath, includeAssetPath));
        }

        private static bool TryLocateTemplate(out string assetPath)
        {
            var guids = AssetDatabase.FindAssets(TemplateGuidSearchFilter);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith("/UniText_Custom-Example.shader"))
                {
                    assetPath = path;
                    return true;
                }
            }
            assetPath = null;
            return false;
        }

        private static bool TryLocateInclude(out string assetPath)
        {
            var guids = AssetDatabase.FindAssets(IncludeFileSearchFilter);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith("/UniText_Custom.cginc"))
                {
                    assetPath = path;
                    return true;
                }
            }
            assetPath = null;
            return false;
        }

        private static string BuildShaderContent(string templateAssetPath, string includeAssetPath)
        {
            var templateAbs = Path.GetFullPath(templateAssetPath);
            var source = File.ReadAllText(templateAbs);

            source = source.Replace(
                "\"../UniText_Custom.cginc\"",
                $"\"{includeAssetPath}\"");

            source = source.Replace(
                "Shader \"UniText/Custom/Example\"",
                "Shader \"UniText/Custom/MyShader\"");

            return source;
        }
    }
}
