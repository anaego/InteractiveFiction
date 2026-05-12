using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Custom body renderer for <see cref="MaterialModifier"/> inside <c>[SerializeReference, TypeSelector]</c>
    /// fields. Auto-discovers per-text shader parameters marked with the <c>_UniTextInst*</c> prefix and
    /// <c>[HideInInspector]</c>, and draws them as typed controls (slider / float / color / vector)
    /// backed by constantUv2/constantUv3. Raw Vector4 fields are intentionally hidden — only
    /// user-configurable controls are shown.
    /// </summary>
    internal sealed class MaterialModifierDrawer : IManagedReferenceDrawer
    {
        private const string InstPrefix = "_UniTextInst";

        [InitializeOnLoadMethod]
        private static void Register() =>
            TypedManagedReferenceDrawerRegistry.Register(typeof(MaterialModifier), new MaterialModifierDrawer());

        private struct InstField
        {
            public string displayName;
            public ShaderPropertyType type;
            public int channel;
            public int componentIdx;
            public Vector2 rangeLimits;
        }

        private static readonly Dictionary<int, List<InstField>> cache = new();

        public void OnGUI(Rect position, SerializedProperty property)
        {
            var materialProp      = property.FindPropertyRelative("material");
            var constantUv2Prop   = property.FindPropertyRelative("constantUv2");
            var constantUv3Prop   = property.FindPropertyRelative("constantUv3");

            var cursor = new Rect(position.x, position.y, position.width, 0);

            EditorGUI.BeginChangeCheck();
            DrawField(ref cursor, materialProp);
            var materialJustChanged = EditorGUI.EndChangeCheck();

            DrawField(ref cursor, property.FindPropertyRelative("renderOrder"));
            DrawField(ref cursor, property.FindPropertyRelative("sortIndex"));
            DrawField(ref cursor, property.FindPropertyRelative("emojiMaterial"));
            DrawField(ref cursor, property.FindPropertyRelative("cloneMaterial"));
            DrawField(ref cursor, property.FindPropertyRelative("quadPaddingOverride"));

            var material = materialProp.objectReferenceValue as Material;
            var shader = material != null ? material.shader : null;
            var fields = ResolveInstFields(shader);

            if (materialJustChanged && shader != null)
                ApplyShaderDefaults(shader, fields, constantUv2Prop, constantUv3Prop);

            DrawPerTextParameters(ref cursor, fields, constantUv2Prop, constantUv3Prop);
        }

        public float GetPropertyHeight(SerializedProperty property)
        {
            var height = 0f;
            height += FieldHeight(property.FindPropertyRelative("material"));
            height += FieldHeight(property.FindPropertyRelative("renderOrder"));
            height += FieldHeight(property.FindPropertyRelative("sortIndex"));
            height += FieldHeight(property.FindPropertyRelative("emojiMaterial"));
            height += FieldHeight(property.FindPropertyRelative("cloneMaterial"));
            height += FieldHeight(property.FindPropertyRelative("quadPaddingOverride"));

            var material = property.FindPropertyRelative("material")?.objectReferenceValue as Material;
            var fields = ResolveInstFields(material != null ? material.shader : null);
            height += PerTextParametersHeight(fields);

            return height;
        }

        private static void DrawField(ref Rect cursor, SerializedProperty prop)
        {
            var h = EditorGUI.GetPropertyHeight(prop, true);
            EditorGUI.PropertyField(new Rect(cursor.x, cursor.y, cursor.width, h), prop, true);
            cursor.y += h + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float FieldHeight(SerializedProperty prop) =>
            EditorGUI.GetPropertyHeight(prop, true) + EditorGUIUtility.standardVerticalSpacing;

        private static void DrawPerTextParameters(ref Rect cursor, List<InstField> fields,
            SerializedProperty uv2Prop, SerializedProperty uv3Prop)
        {
            if (fields.Count == 0) return;

            var lh = EditorGUIUtility.singleLineHeight;
            var sp = EditorGUIUtility.standardVerticalSpacing;

            cursor.y += sp;
            EditorGUI.LabelField(new Rect(cursor.x, cursor.y, cursor.width, lh),
                "Per-Text Shader Parameters", EditorStyles.boldLabel);
            cursor.y += lh + sp;

            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                var uvProp = f.channel == 3 ? uv3Prop : uv2Prop;
                DrawInstField(new Rect(cursor.x, cursor.y, cursor.width, lh), uvProp, f);
                cursor.y += lh + sp;
            }
        }

        private static float PerTextParametersHeight(List<InstField> fields)
        {
            if (fields.Count == 0) return 0f;
            var lh = EditorGUIUtility.singleLineHeight;
            var sp = EditorGUIUtility.standardVerticalSpacing;
            return sp + (lh + sp) + fields.Count * (lh + sp);
        }

        private static void DrawInstField(Rect rect, SerializedProperty uvProp, in InstField f)
        {
            var label = new GUIContent(f.displayName);

            switch (f.type)
            {
                case ShaderPropertyType.Range:
                case ShaderPropertyType.Float:
                {
                    if (f.componentIdx < 0)
                    {
                        EditorGUI.HelpBox(rect,
                            $"{f.displayName}: Float requires UV slot suffix X/Y/Z/W (got whole vector).",
                            MessageType.Warning);
                        return;
                    }
                    var v = uvProp.vector4Value;
                    var cur = GetComponent(v, f.componentIdx);
                    float nv;
                    if (f.type == ShaderPropertyType.Range)
                        nv = EditorGUI.Slider(rect, label, cur, f.rangeLimits.x, f.rangeLimits.y);
                    else
                        nv = EditorGUI.FloatField(rect, label, cur);
                    if (!Mathf.Approximately(nv, cur))
                    {
                        v = SetComponent(v, f.componentIdx, nv);
                        uvProp.vector4Value = v;
                    }
                    break;
                }
                case ShaderPropertyType.Color:
                {
                    if (f.componentIdx >= 0)
                    {
                        EditorGUI.HelpBox(rect,
                            $"{f.displayName}: Color uses a whole UV slot — drop the X/Y/Z/W suffix.",
                            MessageType.Warning);
                        return;
                    }
                    var v = uvProp.vector4Value;
                    var c = new Color(v.x, v.y, v.z, v.w);
                    var nc = EditorGUI.ColorField(rect, label, c);
                    if (nc != c)
                        uvProp.vector4Value = new Vector4(nc.r, nc.g, nc.b, nc.a);
                    break;
                }
                case ShaderPropertyType.Vector:
                {
                    if (f.componentIdx >= 0)
                    {
                        EditorGUI.HelpBox(rect,
                            $"{f.displayName}: Vector uses a whole UV slot — drop the X/Y/Z/W suffix.",
                            MessageType.Warning);
                        return;
                    }
                    var v = uvProp.vector4Value;
                    var nv = EditorGUI.Vector4Field(rect, label, v);
                    if (nv != v) uvProp.vector4Value = nv;
                    break;
                }
                default:
                    EditorGUI.HelpBox(rect,
                        $"{f.displayName}: shader property type {f.type} is not supported in UV slots.",
                        MessageType.Warning);
                    break;
            }
        }

        private static void ApplyShaderDefaults(Shader shader, List<InstField> fields,
            SerializedProperty uv2Prop, SerializedProperty uv3Prop)
        {
            if (shader == null || fields.Count == 0) return;

            var count = shader.GetPropertyCount();
            var uv2 = Vector4.zero;
            var uv3 = Vector4.zero;

            for (var i = 0; i < count; i++)
            {
                var name = shader.GetPropertyName(i);
                if (!name.StartsWith(InstPrefix, System.StringComparison.Ordinal)) continue;
                var flags = shader.GetPropertyFlags(i);
                if ((flags & ShaderPropertyFlags.HideInInspector) == 0) continue;
                if (!TryParseSlot(name.Substring(InstPrefix.Length), out var channel, out var componentIdx)) continue;

                var type = shader.GetPropertyType(i);
                ref var target = ref (channel == 3 ? ref uv3 : ref uv2);

                switch (type)
                {
                    case ShaderPropertyType.Range:
                    case ShaderPropertyType.Float:
                        if (componentIdx >= 0)
                            target = SetComponent(target, componentIdx, shader.GetPropertyDefaultFloatValue(i));
                        break;
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        if (componentIdx < 0)
                            target = shader.GetPropertyDefaultVectorValue(i);
                        break;
                }
            }

            uv2Prop.vector4Value = uv2;
            uv3Prop.vector4Value = uv3;
        }

        private static float GetComponent(Vector4 v, int idx) =>
            idx == 0 ? v.x : idx == 1 ? v.y : idx == 2 ? v.z : v.w;

        private static Vector4 SetComponent(Vector4 v, int idx, float val)
        {
            switch (idx)
            {
                case 0: v.x = val; break;
                case 1: v.y = val; break;
                case 2: v.z = val; break;
                case 3: v.w = val; break;
            }
            return v;
        }

        private static List<InstField> ResolveInstFields(Shader shader)
        {
            if (shader == null) return EmptyList;

            var key = ObjectUtils.GetInstanceIdCompat(shader);
            if (cache.TryGetValue(key, out var cached)) return cached;

            var list = new List<InstField>();
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                var name = shader.GetPropertyName(i);
                if (!name.StartsWith(InstPrefix, System.StringComparison.Ordinal)) continue;

                var flags = shader.GetPropertyFlags(i);
                if ((flags & ShaderPropertyFlags.HideInInspector) == 0) continue;

                var suffix = name.Substring(InstPrefix.Length);
                if (!TryParseSlot(suffix, out var channel, out var componentIdx)) continue;

                var type = shader.GetPropertyType(i);
                var range = type == ShaderPropertyType.Range
                    ? shader.GetPropertyRangeLimits(i)
                    : Vector2.zero;

                list.Add(new InstField
                {
                    displayName  = shader.GetPropertyDescription(i),
                    type         = type,
                    channel      = channel,
                    componentIdx = componentIdx,
                    rangeLimits  = range,
                });
            }

            list.Sort((a, b) => a.channel != b.channel
                ? a.channel - b.channel
                : a.componentIdx - b.componentIdx);

            cache[key] = list;
            return list;
        }

        private static readonly List<InstField> EmptyList = new();

        private static bool TryParseSlot(string suffix, out int channel, out int componentIdx)
        {
            channel = 0;
            componentIdx = -1;

            if (suffix == "Uv2") { channel = 2; return true; }
            if (suffix == "Uv3") { channel = 3; return true; }
            if (suffix.Length != 4) return false;
            if (suffix[0] != 'U' || suffix[1] != 'v') return false;

            var digit = suffix[2];
            if (digit != '2' && digit != '3') return false;
            channel = digit - '0';

            componentIdx = suffix[3] switch
            {
                'X' => 0,
                'Y' => 1,
                'Z' => 2,
                'W' => 3,
                _ => -2,
            };
            return componentIdx >= 0;
        }
    }
}
