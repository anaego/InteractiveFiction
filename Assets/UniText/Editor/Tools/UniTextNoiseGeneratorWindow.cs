using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Editor window that generates grayscale noise textures (value noise / FBM) with seamless tiling
    /// and saves them as importable PNG assets. Used by MaterialModifier example shaders (Dissolve,
    /// Hologram, Plasma…) and available to users for their own procedural needs.
    /// </summary>
    public sealed class UniTextNoiseGeneratorWindow : EditorWindow
    {
        private enum NoiseKind
        {
            Value,
            Fbm,
        }

        private NoiseKind kind = NoiseKind.Fbm;
        private int size = 512;
        private int seed;
        private int frequency = 8;
        private int octaves = 4;
        private int lacunarity = 2;
        private float gain = 0.5f;
        private bool tileable = true;
        private bool invert;

        private Texture2D preview;
        private byte[] pixelBuffer;
        private bool dirty = true;

        private static readonly int[] SizeOptions = { 64, 128, 256, 512, 1024 };
        private static readonly string[] SizeLabels = { "64", "128", "256", "512", "1024" };

        [MenuItem("Tools/UniText/Noise Generator", false, 100)]
        private static void Open()
        {
            var win = GetWindow<UniTextNoiseGeneratorWindow>("Noise Generator");
            win.minSize = new Vector2(320, 560);
        }

        private void OnEnable()
        {
            dirty = true;
        }

        private void OnDisable()
        {
            if (preview != null)
            {
                DestroyImmediate(preview);
                preview = null;
            }
            pixelBuffer = null;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Generator", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            kind = (NoiseKind)EditorGUILayout.EnumPopup("Type", kind);
            size = EditorGUILayout.IntPopup("Size", size, SizeLabels, SizeOptions);
            seed = EditorGUILayout.IntField("Seed", seed);
            frequency = Mathf.Max(1, EditorGUILayout.IntField("Frequency (cells)", frequency));

            if (kind == NoiseKind.Fbm)
            {
                octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 8);
                lacunarity = Mathf.Max(2, EditorGUILayout.IntField("Lacunarity", lacunarity));
                gain = Mathf.Clamp(EditorGUILayout.FloatField("Gain", gain), 0.01f, 1f);
            }

            tileable = EditorGUILayout.Toggle("Tileable (seamless)", tileable);
            invert = EditorGUILayout.Toggle("Invert", invert);

            if (EditorGUI.EndChangeCheck())
                dirty = true;

            EditorGUILayout.Space();

            if (GUILayout.Button("Regenerate Preview", GUILayout.Height(24)))
                dirty = true;

            if (dirty)
            {
                Generate();
                dirty = false;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            var rect = GUILayoutUtility.GetAspectRect(1f);
            if (preview != null)
                EditorGUI.DrawPreviewTexture(rect, preview);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(preview == null))
            {
                if (GUILayout.Button("Save as PNG asset…", GUILayout.Height(28)))
                    SaveAsset();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Seamless tiling requires integer frequency and lacunarity (wraps on the integer " +
                "lattice). Saved PNG imports as sRGB-off, bilinear, repeat, no mipmaps — suitable for " +
                "procedural sampling in custom shaders.",
                MessageType.None);
        }

        private void Generate()
        {
            if (preview == null || preview.width != size)
            {
                if (preview != null) DestroyImmediate(preview);
                preview = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
                {
                    name = "UniText Noise Preview",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                pixelBuffer = new byte[size * size * 4];
            }

            var hashSeed = unchecked((uint)seed);
            float invSize = 1f / size;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = x * invSize * frequency;
                    var v = y * invSize * frequency;
                    var n = Sample(u, v, hashSeed);
                    if (invert) n = 1f - n;
                    var b = (byte)(Mathf.Clamp01(n) * 255f);
                    var idx = (y * size + x) * 4;
                    pixelBuffer[idx + 0] = b;
                    pixelBuffer[idx + 1] = b;
                    pixelBuffer[idx + 2] = b;
                    pixelBuffer[idx + 3] = 255;
                }
            }

            preview.SetPixelData(pixelBuffer, 0);
            preview.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private float Sample(float u, float v, uint hashSeed)
        {
            var wrap = tileable ? frequency : 0;
            if (kind == NoiseKind.Value)
                return ValueNoise(u, v, hashSeed, wrap);

            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (var i = 0; i < octaves; i++)
            {
                var w = tileable ? frequency * (int)freq : 0;
                sum += amp * ValueNoise(u * freq, v * freq, hashSeed + (uint)i * 7919u, w);
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        private static float ValueNoise(float x, float y, uint seed, int wrapSize)
        {
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            var x1 = x0 + 1;
            var y1 = y0 + 1;

            if (wrapSize > 0)
            {
                x0 = Mod(x0, wrapSize);
                y0 = Mod(y0, wrapSize);
                x1 = Mod(x1, wrapSize);
                y1 = Mod(y1, wrapSize);
            }

            var h00 = Hash01(x0, y0, seed);
            var h10 = Hash01(x1, y0, seed);
            var h01 = Hash01(x0, y1, seed);
            var h11 = Hash01(x1, y1, seed);

            return Lerp(Lerp(h00, h10, fx), Lerp(h01, h11, fx), fy);
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Hash01(int x, int y, uint seed)
        {
            var h = unchecked((uint)x * 0x27d4eb2du ^ (uint)y * 0x165667b1u ^ seed);
            h ^= h >> 15;
            h = unchecked(h * 0x2c1b3c6du);
            h ^= h >> 12;
            h = unchecked(h * 0x297a2d39u);
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / (float)0xFFFFFFu;
        }

        private void SaveAsset()
        {
            if (preview == null) return;

            var path = EditorUtility.SaveFilePanelInProject(
                "Save Noise Texture",
                "UniTextNoise",
                "png",
                "Choose a location inside the Assets folder.");
            if (string.IsNullOrEmpty(path)) return;

            var bytes = preview.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (saved != null)
            {
                EditorGUIUtility.PingObject(saved);
                Selection.activeObject = saved;
            }
        }
    }
}
