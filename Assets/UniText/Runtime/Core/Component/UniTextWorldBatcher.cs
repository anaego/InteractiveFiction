using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
#endif

namespace LightSide
{
    /// <summary>
    /// Invisible singleton that batches <see cref="UniTextWorld"/> components into combined meshes
    /// while honoring Unity's sorting model (SortingLayer, OrderInLayer, SortingGroup) so batched
    /// text interleaves correctly with <c>SpriteRenderer</c> and other renderers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two-level hierarchy:</b>
    /// <list type="bullet">
    /// <item>
    ///   <see cref="BatchGroup"/> is a logical grouping by
    ///   <see cref="BatchKey"/> = (<c>materialInstanceId</c>, <c>sortingLayerID</c>, <c>sortingOrder</c>,
    ///   <c>sortingGroupID</c>). Conceptually "one draw call worth of text".
    /// </item>
    /// <item>
    ///   <see cref="BatchShard"/> is a physical mesh. A group holds one shard normally; when a group
    ///   grows past <see cref="UniTextSettings.WorldBatcherShardTargetVertexCount"/> vertices, new shards
    ///   are added so that structural rebuilds of one shard do not touch unrelated components.
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Dirty classification:</b>
    /// <list type="bullet">
    /// <item><b>Structural</b> — vertex count changed, entry added/removed, sorting/material changed.
    ///   Triggers a full rebuild of the affected shard (not the whole group).</item>
    /// <item><b>Positional</b> — transform moved. Writes Position+Normal only, for exactly this entry's
    ///   slice of the shard's vertex buffer.</item>
    /// <item><b>Attributive</b> — text rebuild without vertex count change (colors/UVs only).
    ///   Writes the affected attribute streams for this entry's slice.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Vertex buffer layout</b> — fixed four streams:
    /// <list type="bullet">
    /// <item>Stream 0: Position (float3) + Normal (float3) = 24 bytes/vertex</item>
    /// <item>Stream 1: Color (UNorm8×4) = 4 bytes/vertex</item>
    /// <item>Stream 2: UV0 (float4) = 16 bytes/vertex</item>
    /// <item>Stream 3: UV1 + UV2 + UV3 (float4×3) = 48 bytes/vertex</item>
    /// </list>
    /// Separate streams let partial updates write only the affected attribute without
    /// rewriting unrelated data.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("")]
    public sealed class UniTextWorldBatcher : MonoBehaviour
    {
        private static UniTextWorldBatcher instance;
#if UNITY_EDITOR
        private static UniTextWorldBatcher mainInstance;
#endif

        private readonly Dictionary<BatchKey, BatchGroup> groups = new();
        private readonly Dictionary<UniTextWorld, ComponentSlot> slots = new();

        private bool uploadDirty;

        private readonly List<BatchKey> invalidGroupKeys = new();
        private readonly List<UniTextWorld> staleSlotKeys = new();

        private const string BatchLayerNamePrefix = "-_UTWB_";

        #region Singleton

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRuntime()
        {
            UniTextWorld.Activated -= EnsureInstanceOnFirstActivation;
            UniTextWorld.Activated += EnsureInstanceOnFirstActivation;
        }
#endif

        private static void EnsureInstanceOnFirstActivation(UniTextWorld component)
        {
            EnsureInstance();
        }

        internal static void EnsureInstance()
        {
            if (instance != null) return;

            foreach (var b in Resources.FindObjectsOfTypeAll<UniTextWorldBatcher>())
            {
                if (!IsRuntimeInstance(b)) continue;
                ObjectUtils.SafeDestroy(b.gameObject);
            }

            var go = new GameObject("UniTextWorldBatcher")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

#if UNITY_EDITOR
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.IsValid() && scene.isLoaded && scene != prefabStage.scene)
                    {
                        SceneManager.MoveGameObjectToScene(go, scene);
                        break;
                    }
                }
            }
#endif

            instance = go.AddComponent<UniTextWorldBatcher>();
        }

        private static bool IsRuntimeInstance(UniTextWorldBatcher b)
        {
            if (b == null) return false;
            var go = b.gameObject;
            if (go == null) return false;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(go)) return false;
#endif
            return go.scene.IsValid();
        }

        private static UniTextWorldBatcher GetTargetBatcher(UniTextWorld component)
        {
#if UNITY_EDITOR
            if (mainInstance != null && instance != null && mainInstance != instance)
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null && component.gameObject.scene != prefabStage.scene)
                    return mainInstance;
            }
#endif
            return instance;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void CleanupOnDomainReload()
        {
            Reseter.ManagedCleaning -= OnManagedCleaning;
            Reseter.ManagedCleaning += OnManagedCleaning;

            foreach (var b in Resources.FindObjectsOfTypeAll<UniTextWorldBatcher>())
            {
                if (!IsRuntimeInstance(b)) continue;
                ObjectUtils.SafeDestroy(b.gameObject);
            }

            CleanupOrphanedBatchLayers();

            instance = null;
            mainInstance = null;

            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            UniTextWorld.Activated -= EnsureInstanceOnFirstActivation;
            UniTextWorld.Activated += EnsureInstanceOnFirstActivation;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (stage != null) OnPrefabStageOpened(stage);
                };
        }

        private static void OnManagedCleaning()
        {
            if (!Reseter.isDomainReloading) return;
            TearDownInstance(instance);
            TearDownInstance(mainInstance);
        }

        private static void TearDownInstance(UniTextWorldBatcher b)
        {
            if (b == null) return;
            foreach (var kvp in b.slots)
            {
                b.DetachComponentEvents(kvp.Key);
                kvp.Value.ReturnBuffers();
            }
            foreach (var group in b.groups.Values)
                group.Destroy();
            b.slots.Clear();
            b.groups.Clear();
            ObjectUtils.SafeDestroy(b.gameObject);
        }

        private static void CleanupOrphanedBatchLayers()
        {
            foreach (var marker in Resources.FindObjectsOfTypeAll<BatchLayerMarker>())
            {
                if (marker == null) continue;
                var go = marker.gameObject;
                if (go == null) continue;
                if (UnityEditor.EditorUtility.IsPersistent(go)) continue;
                if (!go.scene.IsValid()) continue;
                ObjectUtils.SafeDestroy(go);
            }
        }

        private static void OnPrefabStageOpened(PrefabStage stage)
        {
            if (stage == null || !stage.scene.IsValid()) return;

            mainInstance = instance;
            instance = null;

            var go = new GameObject("UniTextWorldBatcher")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SceneManager.MoveGameObjectToScene(go, stage.scene);
            go.AddComponent<UniTextWorldBatcher>();

            var root = stage.prefabContentsRoot;
            if (root == null) return;

            if (mainInstance != null)
            {
                foreach (var world in root.GetComponentsInChildren<UniTextWorld>(true))
                {
                    mainInstance.ForceUnregister(world);
                    if (world.isActiveAndEnabled)
                        world.SetAllDirty();
                }
            }
        }

        private static void OnPrefabStageClosing(PrefabStage stage)
        {
            if (instance != null && instance != mainInstance)
                ObjectUtils.SafeDestroy(instance.gameObject);

            instance = mainInstance;
            mainInstance = null;
        }
#endif

        private void OnEnable()
        {
            if (instance != null && instance != this)
            {
                ObjectUtils.SafeDestroy(gameObject);
                return;
            }

            instance = this;
            UniTextBase.AfterProcess += OnAfterProcess;
            UniTextWorld.Activated += OnComponentActivated;
            UniTextWorld.Deactivated += OnComponentDeactivated;

            AdoptExistingComponents();
        }

        private void OnDisable()
        {
            UniTextBase.AfterProcess -= OnAfterProcess;
            UniTextWorld.Activated -= OnComponentActivated;
            UniTextWorld.Deactivated -= OnComponentDeactivated;

            foreach (var kvp in slots)
                DetachComponentEvents(kvp.Key);

            if (instance == this) instance = null;
        }

        private void OnDestroy()
        {
            foreach (var kvp in slots)
            {
                DetachComponentEvents(kvp.Key);
                kvp.Value.ReturnBuffers();
            }
            foreach (var group in groups.Values)
                group.Destroy();
            groups.Clear();
            slots.Clear();
        }

        private void AdoptExistingComponents()
        {
            foreach (var component in Resources.FindObjectsOfTypeAll<UniTextWorld>())
            {
                if (component == null) continue;
                if (!component.isActiveAndEnabled) continue;
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.IsPersistent(component.gameObject)) continue;
#endif
                if (!component.gameObject.scene.IsValid()) continue;
                if (GetTargetBatcher(component) != this) continue;
                RegisterComponent(component);
            }
        }

        #endregion

        #region Component Event Handlers

        private void OnComponentActivated(UniTextWorld component)
        {
            if (component == null) return;
            if (GetTargetBatcher(component) != this) return;
            RegisterComponent(component);
        }

        private void OnComponentDeactivated(UniTextWorld component)
        {
            if (component == null) return;
            ForceUnregister(component);
        }

        internal void ForceUnregister(UniTextWorld component)
        {
            if (component == null) return;
            if (!slots.TryGetValue(component, out var slot)) return;

            DetachComponentEvents(component);
            FreeAllEntries(slot);
            slot.ReturnBuffers();
            slots.Remove(component);
            uploadDirty = true;
        }

        private void OnComponentRenderDataAvailable(UniTextWorld component)
        {
            CopySlotDataInternal(component);
        }

        private void OnComponentRenderDataCleared(UniTextWorld component)
        {
            if (!slots.TryGetValue(component, out var slot)) return;
            FreeAllEntries(slot);
            slot.ClearEntries();
            uploadDirty = true;
        }

        private void OnComponentSortingChanged(UniTextWorld component) => RemapEntriesToCurrentKey(component);

        private void OnComponentLayerChanged(UniTextWorld component) => RemapEntriesToCurrentKey(component);

        private void RemapEntriesToCurrentKey(UniTextWorld component)
        {
            if (!slots.TryGetValue(component, out var slot)) return;

            var (sortingGroup, sortingGroupID, sortingLayerID, sortingOrder, unityLayer)
                = ResolveKeyContext(slot, component);

            if (slot.entries.Count == 0) return;

            for (var i = 0; i < slot.entries.Count; i++)
            {
                var entry = slot.entries[i];
                FreeEntryFromShard(entry);

                var newKey = new BatchKey(
                    entry.materialInstanceId,
                    sortingLayerID,
                    sortingOrder,
                    sortingGroupID,
                    unityLayer);
                entry.groupKey = newKey;

                var group = GetOrCreateGroup(newKey, entry.material, sortingGroup);
                if (group != null)
                    PlaceEntryInGroup(group, entry);
            }

            uploadDirty = true;
        }

        private static (SortingGroup sortingGroup, int sortingGroupID, int sortingLayerID, int sortingOrder, int unityLayer)
            ResolveKeyContext(ComponentSlot slot, UniTextWorld component)
        {
            if (!slot.sortingGroupCached)
            {
                slot.cachedSortingGroup = component.GetComponentInParent<SortingGroup>();
                slot.sortingGroupCached = true;
            }
            var sortingGroup = slot.cachedSortingGroup;
            var sortingGroupID = sortingGroup != null ? ObjectUtils.GetInstanceIdCompat(sortingGroup) : 0;
            var sortingLayerID = component.SortingLayerID;
            var sortingOrder = component.SortingOrder;
            var unityLayer = component.gameObject.layer;
            slot.lastLayer = unityLayer;
            return (sortingGroup, sortingGroupID, sortingLayerID, sortingOrder, unityLayer);
        }

        private void OnComponentParentChanged(UniTextWorld component)
        {
            if (slots.TryGetValue(component, out var slot))
                slot.sortingGroupCached = false;
        }

        private void RegisterComponent(UniTextWorld component)
        {
            if (slots.ContainsKey(component)) return;
            slots[component] = new ComponentSlot();
            AttachComponentEvents(component);
        }

        private void AttachComponentEvents(UniTextWorld component)
        {
            component.RenderDataAvailable += OnComponentRenderDataAvailable;
            component.RenderDataCleared += OnComponentRenderDataCleared;
            component.SortingChanged += OnComponentSortingChanged;
            component.ParentChanged += OnComponentParentChanged;
        }

        private void DetachComponentEvents(UniTextWorld component)
        {
            if (component == null) return;
            component.RenderDataAvailable -= OnComponentRenderDataAvailable;
            component.RenderDataCleared -= OnComponentRenderDataCleared;
            component.SortingChanged -= OnComponentSortingChanged;
            component.ParentChanged -= OnComponentParentChanged;
        }

        #endregion

        #region Data Capture

        private void CopySlotDataInternal(UniTextWorld component)
        {
            var gen = component.MeshGenerator;
            if (gen == null || !gen.HasGeneratedData) return;
            if (!slots.TryGetValue(component, out var slot)) return;

            var (sortingGroup, sortingGroupID, sortingLayerID, sortingOrder, unityLayer)
                = ResolveKeyContext(slot, component);

            var segments = gen.CollectRenderData();
            var isMsdf = gen.RenderMode == UniTextRenderMode.MSDF;
            var defaultSdfMat = isMsdf ? UniTextMaterialCache.Msdf : UniTextMaterialCache.Sdf;

            var prevEntries = slot.entries;
            var reuseIndex = 0;

            var newEntries = slot.AcquireWorkList();
            newEntries.Clear();

            for (var i = 0; i < segments.Count; i++)
            {
                var data = segments[i];
                if (data.vertexCount <= 0 || data.triangleCount <= 0) continue;

                Material material;
                if (data.materialOverride != null)
                    material = data.materialOverride;
                else
                    material = data.fontId == EmojiFont.FontId ? EmojiFont.Material : defaultSdfMat;

                if (material == null) continue;

                var materialInstanceId = ObjectUtils.GetInstanceIdCompat(material);
                var groupKey = new BatchKey(materialInstanceId, sortingLayerID, sortingOrder, sortingGroupID, unityLayer);

                SubMeshEntry entry = null;
                for (var k = reuseIndex; k < prevEntries.Count; k++)
                {
                    if (prevEntries[k].groupKey.Equals(groupKey))
                    {
                        entry = prevEntries[k];
                        prevEntries[k] = null;
                        reuseIndex = k + 1;
                        break;
                    }
                }

                var isNewEntry = entry == null;
                if (isNewEntry)
                    entry = slot.AcquireEntry();

                var newHasUv1 = data.hasUv1 && data.uvs1 != null;
                var newHasUv2 = data.hasUv2 && data.uvs2 != null;
                var newHasUv3 = data.hasUv3 && data.uvs3 != null;

                var structurallyChanged =
                    isNewEntry
                    || entry.vertexCount != data.vertexCount
                    || entry.triangleCount != data.triangleCount
                    || entry.hasUv1 != newHasUv1
                    || entry.hasUv2 != newHasUv2
                    || entry.hasUv3 != newHasUv3
                    || entry.shard == null;

                if (structurallyChanged)
                {
                    if (entry.shard != null)
                        FreeEntryFromShard(entry);

                    entry.material = material;
                    entry.materialInstanceId = materialInstanceId;
                    entry.vertexCount = data.vertexCount;
                    entry.triangleCount = data.triangleCount;
                    entry.hasUv1 = newHasUv1;
                    entry.hasUv2 = newHasUv2;
                    entry.hasUv3 = newHasUv3;
                    entry.groupKey = groupKey;
                    entry.component = component;

                    CopyEntrySourceData(entry, in data);

                    var group = GetOrCreateGroup(groupKey, material, sortingGroup);
                    if (group != null)
                        PlaceEntryInGroup(group, entry);
                }
                else
                {
                    entry.component = component;
                    CopyEntrySourceData(entry, in data);
                    MarkEntryAttributiveDirty(entry);
                }

                newEntries.Add(entry);
            }

            for (var k = 0; k < prevEntries.Count; k++)
            {
                var stale = prevEntries[k];
                if (stale == null) continue;
                FreeEntryFromShard(stale);
                slot.ReleaseEntry(stale);
            }

            slot.entries.Clear();
            for (var k = 0; k < newEntries.Count; k++)
                slot.entries.Add(newEntries[k]);
            slot.ReleaseWorkList(newEntries);

            slot.lastMatrix = component.transform.localToWorldMatrix;
            slot.lastMatrixValid = true;

            for (var i = 0; i < slot.entries.Count; i++)
                MarkEntryPositionalDirty(slot.entries[i]);

            uploadDirty = true;
        }

        private static void CopyEntrySourceData(SubMeshEntry entry, in UniTextRenderData data)
        {
            var vc = data.vertexCount;
            var tc = data.triangleCount;

            entry.vertices.EnsureCount(vc);
            Array.Copy(data.vertices, data.vertexOffset, entry.vertices.data, 0, vc);
            entry.uvs0.EnsureCount(vc);
            Array.Copy(data.uvs0, data.vertexOffset, entry.uvs0.data, 0, vc);
            entry.colors.EnsureCount(vc);
            Array.Copy(data.colors, data.vertexOffset, entry.colors.data, 0, vc);

            if (entry.hasUv1)
            {
                entry.uvs1.EnsureCount(vc);
                Array.Copy(data.uvs1, data.vertexOffset, entry.uvs1.data, 0, vc);
            }
            if (entry.hasUv2)
            {
                entry.uvs2.EnsureCount(vc);
                Array.Copy(data.uvs2, data.vertexOffset, entry.uvs2.data, 0, vc);
            }
            if (entry.hasUv3)
            {
                entry.uvs3.EnsureCount(vc);
                Array.Copy(data.uvs3, data.vertexOffset, entry.uvs3.data, 0, vc);
            }

            entry.triangles.EnsureCount(tc);
            Array.Copy(data.triangles, data.triangleOffset, entry.triangles.data, 0, tc);
        }

        private void FreeAllEntries(ComponentSlot slot)
        {
            for (var i = 0; i < slot.entries.Count; i++)
                FreeEntryFromShard(slot.entries[i]);
        }

        private static void FreeEntryFromShard(SubMeshEntry entry)
        {
            var shard = entry.shard;
            if (shard == null) return;
            shard.entries.Remove(entry);
            shard.usedVertexCount -= entry.vertexCount;
            shard.usedIndexCount -= entry.triangleCount;
            shard.structuralDirty = true;
            shard.positionalDirty.Remove(entry);
            shard.attributiveDirty.Remove(entry);
            entry.shard = null;
            entry.vertexOffsetInShard = 0;
            entry.indexOffsetInShard = 0;
        }

        private static void MarkEntryPositionalDirty(SubMeshEntry entry)
        {
            var shard = entry.shard;
            if (shard == null || shard.structuralDirty) return;
            shard.positionalDirty.Add(entry);
        }

        private static void MarkEntryAttributiveDirty(SubMeshEntry entry)
        {
            var shard = entry.shard;
            if (shard == null || shard.structuralDirty) return;
            shard.attributiveDirty.Add(entry);
        }

        private static void PlaceEntryInGroup(BatchGroup group, SubMeshEntry entry)
        {
            var capacity = Mathf.Max(64, UniTextSettings.WorldBatcherShardTargetVertexCount);

            for (var i = 0; i < group.shards.Count; i++)
            {
                var shard = group.shards[i];
                if (shard.usedVertexCount + entry.vertexCount <= capacity)
                {
                    shard.entries.Add(entry);
                    entry.shard = shard;
                    shard.usedVertexCount += entry.vertexCount;
                    shard.usedIndexCount += entry.triangleCount;
                    shard.structuralDirty = true;
                    return;
                }
            }

            var fresh = new BatchShard(group);
            group.shards.Add(fresh);
            fresh.entries.Add(entry);
            entry.shard = fresh;
            fresh.usedVertexCount = entry.vertexCount;
            fresh.usedIndexCount = entry.triangleCount;
            fresh.structuralDirty = true;
        }

        #endregion

        #region Upload

        private void OnAfterProcess()
        {
            if (uploadDirty)
            {
                UploadMeshes();
                uploadDirty = false;
            }
        }

        private void LateUpdate()
        {
            PruneStaleSlots();
            PruneInvalidGroups();

            foreach (var kvp in slots)
            {
                var component = kvp.Key;
                if (component == null) continue;
                var slot = kvp.Value;

                if (slot.entries.Count > 0 && slot.lastLayer != component.gameObject.layer)
                {
                    OnComponentLayerChanged(component);
                    continue;
                }

                if (slot.entries.Count == 0) continue;

                var currentMatrix = component.transform.localToWorldMatrix;
                if (slot.lastMatrixValid && currentMatrix == slot.lastMatrix) continue;

                slot.lastMatrix = currentMatrix;
                slot.lastMatrixValid = true;

                for (var i = 0; i < slot.entries.Count; i++)
                    MarkEntryPositionalDirty(slot.entries[i]);

                uploadDirty = true;
            }

            if (uploadDirty)
            {
                UploadMeshes();
                uploadDirty = false;
            }
        }

        private void UploadMeshes()
        {
            PruneInvalidGroups();

            foreach (var kvp in groups)
            {
                var group = kvp.Value;
                if (!group.IsValid) continue;

                for (var i = group.shards.Count - 1; i >= 0; i--)
                {
                    var shard = group.shards[i];
                    if (shard.entries.Count == 0)
                    {
                        shard.Destroy();
                        group.shards.RemoveAt(i);
                    }
                }

                for (var i = 0; i < group.shards.Count; i++)
                {
                    var shard = group.shards[i];
                    ProcessShard(shard);
                }
            }
        }

        private static void ProcessShard(BatchShard shard)
        {
            if (shard.structuralDirty)
            {
                RebuildShardStructural(shard);
                shard.structuralDirty = false;
                shard.positionalDirty.Clear();
                shard.attributiveDirty.Clear();
                return;
            }

            if (shard.positionalDirty.Count > 0)
            {
                foreach (var entry in shard.positionalDirty)
                    WriteEntryPositionStream(shard, entry);
                shard.positionalDirty.Clear();

                if (shard.layer != null && shard.layer.mesh != null)
                    ApplyShardBounds(shard, shard.layer.mesh);
            }

            if (shard.attributiveDirty.Count > 0)
            {
                foreach (var entry in shard.attributiveDirty)
                {
                    WriteEntryColorStream(shard, entry);
                    WriteEntryUv0Stream(shard, entry);
                    WriteEntryUv123Stream(shard, entry);
                }
                shard.attributiveDirty.Clear();
            }
        }

        private static readonly VertexAttributeDescriptor[] vertexLayout =
        {
            new(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3, stream: 0),
            new(VertexAttribute.Normal,    VertexAttributeFormat.Float32, 3, stream: 0),
            new(VertexAttribute.Color,     VertexAttributeFormat.UNorm8,  4, stream: 1),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, stream: 2),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4, stream: 3),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4, stream: 3),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4, stream: 3),
        };

        private const MeshUpdateFlags MeshUploadFlags =
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

        private static void RebuildShardStructural(BatchShard shard)
        {
            var totalV = shard.usedVertexCount;
            var totalT = shard.usedIndexCount;

            if (totalV == 0 || totalT == 0)
            {
                shard.ClearMesh();
                return;
            }

            shard.stream0.EnsureCount(totalV);
            shard.stream1.EnsureCount(totalV);
            shard.stream2.EnsureCount(totalV);
            shard.stream3.EnsureCount(totalV);
            shard.indices.EnsureCount(totalT);

            var batchMatrix = shard.group.WorldToBatchMatrix;

            var vOff = 0;
            var tOff = 0;

            for (var i = 0; i < shard.entries.Count; i++)
            {
                var entry = shard.entries[i];
                entry.vertexOffsetInShard = vOff;
                entry.indexOffsetInShard = tOff;

                FillEntryIntoStagingStreams(shard, entry, batchMatrix, vOff, tOff);

                vOff += entry.vertexCount;
                tOff += entry.triangleCount;
            }

            shard.EnsureMeshAllocated(totalV, totalT);

            var mesh = shard.layer.mesh;
            mesh.SetVertexBufferData(shard.stream0.data, 0, 0, totalV, stream: 0,
                MeshUploadFlags);
            mesh.SetVertexBufferData(shard.stream1.data, 0, 0, totalV, stream: 1,
                MeshUploadFlags);
            mesh.SetVertexBufferData(shard.stream2.data, 0, 0, totalV, stream: 2,
                MeshUploadFlags);
            mesh.SetVertexBufferData(shard.stream3.data, 0, 0, totalV, stream: 3,
                MeshUploadFlags);
            mesh.SetIndexBufferData(shard.indices.data, 0, 0, totalT,
                MeshUploadFlags);

            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, totalT),
                MeshUploadFlags);

            ApplyShardBounds(shard, mesh);

            shard.layer.go.SetActive(true);
            shard.layer.renderer.sharedMaterial = shard.group.material;
            shard.layer.renderer.sortingLayerID = shard.group.sortingLayerID;
            shard.layer.renderer.sortingOrder = shard.group.sortingOrder;
        }

        private static void RecomputeShardBounds(BatchShard shard)
        {
            var entries = shard.entries;
            if (entries.Count == 0)
            {
                shard.boundsMin = Vector3.zero;
                shard.boundsMax = Vector3.zero;
                return;
            }

            var first = entries[0];
            var min = first.localBoundsMin;
            var max = first.localBoundsMax;

            for (var i = 1; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.localBoundsMin.x < min.x) min.x = e.localBoundsMin.x;
                if (e.localBoundsMin.y < min.y) min.y = e.localBoundsMin.y;
                if (e.localBoundsMin.z < min.z) min.z = e.localBoundsMin.z;
                if (e.localBoundsMax.x > max.x) max.x = e.localBoundsMax.x;
                if (e.localBoundsMax.y > max.y) max.y = e.localBoundsMax.y;
                if (e.localBoundsMax.z > max.z) max.z = e.localBoundsMax.z;
            }

            shard.boundsMin = min;
            shard.boundsMax = max;
        }

        private static void ApplyShardBounds(BatchShard shard, Mesh mesh)
        {
            RecomputeShardBounds(shard);
            var center = (shard.boundsMin + shard.boundsMax) * 0.5f;
            var size = shard.boundsMax - shard.boundsMin;
            mesh.bounds = new Bounds(center, size);
        }

        private static void FillEntryIntoStagingStreams(
            BatchShard shard, SubMeshEntry entry, Matrix4x4 batchMatrix, int vOff, int tOff)
        {
            var component = entry.component;
            var worldMatrix = component != null ? component.transform.localToWorldMatrix : Matrix4x4.identity;
            var vc = entry.vertexCount;
            var tc = entry.triangleCount;

            var srcVerts = entry.vertices.data;
            var s0 = shard.stream0.data;

            WritePositionsAndNormals(
                s0, vOff, srcVerts, vc,
                in worldMatrix, in batchMatrix,
                out var entryMin, out var entryMax);

            entry.localBoundsMin = entryMin;
            entry.localBoundsMax = entryMax;

            Array.Copy(entry.colors.data, 0, shard.stream1.data, vOff, vc);
            Array.Copy(entry.uvs0.data, 0, shard.stream2.data, vOff, vc);

            WriteUv123Block(shard.stream3.data, vOff, vc, entry);

            var srcTris = entry.triangles.data;
            var dstTris = shard.indices.data;
            for (var i = 0; i < tc; i++)
                dstTris[tOff + i] = (ushort)(srcTris[i] + vOff);
        }

        private static void WriteEntryPositionStream(BatchShard shard, SubMeshEntry entry)
        {
            if (entry.shard != shard) return;

            var component = entry.component;
            if (component == null) return;

            var worldMatrix = component.transform.localToWorldMatrix;
            var batchMatrix = shard.group.WorldToBatchMatrix;
            var vc = entry.vertexCount;

            shard.stream0.EnsureCount(shard.usedVertexCount);
            var s0 = shard.stream0.data;
            var srcVerts = entry.vertices.data;
            var baseOff = entry.vertexOffsetInShard;

            WritePositionsAndNormals(
                s0, baseOff, srcVerts, vc,
                in worldMatrix, in batchMatrix,
                out var entryMin, out var entryMax);

            entry.localBoundsMin = entryMin;
            entry.localBoundsMax = entryMax;

            if (shard.layer == null || shard.layer.mesh == null) return;
            shard.layer.mesh.SetVertexBufferData(s0, baseOff, baseOff, vc, stream: 0,
                MeshUploadFlags);
        }

        private static void WritePositionsAndNormals(
            PositionNormal[] s0, int baseOff,
            Vector3[] srcVerts, int vc,
            in Matrix4x4 worldMatrix, in Matrix4x4 batchMatrix,
            out Vector3 entryMin, out Vector3 entryMax)
        {
            var matrix = batchMatrix * worldMatrix;
            var quadCount = vc >> 2;

            entryMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            entryMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (var q = 0; q < quadCount; q++)
            {
                var baseIdx = q * 4;
                var v0 = srcVerts[baseIdx];
                var v1 = srcVerts[baseIdx + 1];
                var v3 = srcVerts[baseIdx + 3];
                var edgeUp = v1 - v0;
                var edgeRight = v3 - v0;
                var localNormal = Vector3.Cross(edgeUp, edgeRight);
                if (localNormal.sqrMagnitude > 1e-12f) localNormal.Normalize();
                var worldNormal = worldMatrix.MultiplyVector(localNormal);
                if (worldNormal.sqrMagnitude > 1e-12f) worldNormal.Normalize();

                for (var i = 0; i < 4; i++)
                {
                    ref var p = ref s0[baseOff + baseIdx + i];
                    var pos = matrix.MultiplyPoint3x4(srcVerts[baseIdx + i]);
                    p.position = pos;
                    p.normal = worldNormal;

                    if (pos.x < entryMin.x) entryMin.x = pos.x;
                    if (pos.y < entryMin.y) entryMin.y = pos.y;
                    if (pos.z < entryMin.z) entryMin.z = pos.z;
                    if (pos.x > entryMax.x) entryMax.x = pos.x;
                    if (pos.y > entryMax.y) entryMax.y = pos.y;
                    if (pos.z > entryMax.z) entryMax.z = pos.z;
                }
            }
        }

        private static void WriteEntryColorStream(BatchShard shard, SubMeshEntry entry)
        {
            if (entry.shard != shard) return;
            if (shard.layer == null || shard.layer.mesh == null) return;

            var vc = entry.vertexCount;
            var baseOff = entry.vertexOffsetInShard;

            shard.stream1.EnsureCount(shard.usedVertexCount);
            Array.Copy(entry.colors.data, 0, shard.stream1.data, baseOff, vc);

            shard.layer.mesh.SetVertexBufferData(shard.stream1.data, baseOff, baseOff, vc, stream: 1,
                MeshUploadFlags);
        }

        private static void WriteEntryUv0Stream(BatchShard shard, SubMeshEntry entry)
        {
            if (entry.shard != shard) return;
            if (shard.layer == null || shard.layer.mesh == null) return;

            var vc = entry.vertexCount;
            var baseOff = entry.vertexOffsetInShard;

            shard.stream2.EnsureCount(shard.usedVertexCount);
            Array.Copy(entry.uvs0.data, 0, shard.stream2.data, baseOff, vc);

            shard.layer.mesh.SetVertexBufferData(shard.stream2.data, baseOff, baseOff, vc, stream: 2,
                MeshUploadFlags);
        }

        private static void WriteEntryUv123Stream(BatchShard shard, SubMeshEntry entry)
        {
            if (entry.shard != shard) return;
            if (shard.layer == null || shard.layer.mesh == null) return;

            var vc = entry.vertexCount;
            var baseOff = entry.vertexOffsetInShard;

            shard.stream3.EnsureCount(shard.usedVertexCount);
            var s3 = shard.stream3.data;

            WriteUv123Block(s3, baseOff, vc, entry);

            shard.layer.mesh.SetVertexBufferData(s3, baseOff, baseOff, vc, stream: 3,
                MeshUploadFlags);
        }

        private static void WriteUv123Block(Uv123[] s3, int baseOff, int vc, SubMeshEntry entry)
        {
            if (entry.hasUv1)
            {
                var src = entry.uvs1.data;
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv1 = src[i];
            }
            else
            {
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv1 = default;
            }
            if (entry.hasUv2)
            {
                var src = entry.uvs2.data;
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv2 = src[i];
            }
            else
            {
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv2 = default;
            }
            if (entry.hasUv3)
            {
                var src = entry.uvs3.data;
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv3 = src[i];
            }
            else
            {
                for (var i = 0; i < vc; i++) s3[baseOff + i].uv3 = default;
            }
        }

        #endregion

        #region Maintenance

        private void PruneInvalidGroups()
        {
            invalidGroupKeys.Clear();
            foreach (var kvp in groups)
            {
                if (kvp.Value.IsValid) continue;
                kvp.Value.Destroy();
                invalidGroupKeys.Add(kvp.Key);
            }

            for (var i = 0; i < invalidGroupKeys.Count; i++)
                groups.Remove(invalidGroupKeys[i]);
        }

        private void PruneStaleSlots()
        {
            staleSlotKeys.Clear();
            foreach (var kvp in slots)
            {
                if (kvp.Key == null) staleSlotKeys.Add(kvp.Key);
            }

            for (var i = 0; i < staleSlotKeys.Count; i++)
            {
                if (slots.TryGetValue(staleSlotKeys[i], out var slot))
                {
                    FreeAllEntries(slot);
                    slot.ReturnBuffers();
                }
                slots.Remove(staleSlotKeys[i]);
            }

            if (staleSlotKeys.Count > 0) uploadDirty = true;
        }

        private BatchGroup GetOrCreateGroup(BatchKey key, Material material, SortingGroup sortingGroup)
        {
            if (groups.TryGetValue(key, out var group))
            {
                if (group.IsValid) return group;
                group.Destroy();
                groups.Remove(key);
            }

            Transform parentTransform;
            if (sortingGroup != null && sortingGroup.transform != null)
                parentTransform = sortingGroup.transform;
            else
                parentTransform = transform;

            if (parentTransform == null) return null;

            group = new BatchGroup(material, key, parentTransform);
            groups[key] = group;
            return group;
        }

        #endregion

        #region Types

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public readonly bool isValid;
            public readonly int materialInstanceId;
            public readonly int sortingLayerID;
            public readonly int sortingOrder;
            public readonly int sortingGroupID;
            public readonly int unityLayer;

            public bool IsValid => isValid;

            public BatchKey(int materialInstanceId, int sortingLayerID, int sortingOrder, int sortingGroupID, int unityLayer)
            {
                isValid = true;
                this.materialInstanceId = materialInstanceId;
                this.sortingLayerID = sortingLayerID;
                this.sortingOrder = sortingOrder;
                this.sortingGroupID = sortingGroupID;
                this.unityLayer = unityLayer;
            }

            public bool Equals(BatchKey other) =>
                isValid == other.isValid
                && materialInstanceId == other.materialInstanceId
                && sortingLayerID == other.sortingLayerID
                && sortingOrder == other.sortingOrder
                && sortingGroupID == other.sortingGroupID
                && unityLayer == other.unityLayer;

            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(materialInstanceId, sortingLayerID, sortingOrder, sortingGroupID, unityLayer);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PositionNormal
        {
            public Vector3 position;
            public Vector3 normal;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Uv123
        {
            public Vector4 uv1;
            public Vector4 uv2;
            public Vector4 uv3;
        }

        private sealed class ComponentSlot
        {
            public SortingGroup cachedSortingGroup;
            public bool sortingGroupCached;

            public readonly List<SubMeshEntry> entries = new(2);
            private readonly Stack<SubMeshEntry> entryPool = new();
            private readonly Stack<List<SubMeshEntry>> workListPool = new();

            public Matrix4x4 lastMatrix;
            public bool lastMatrixValid;

            public int lastLayer;

            public SubMeshEntry AcquireEntry()
            {
                return entryPool.Count > 0 ? entryPool.Pop() : new SubMeshEntry();
            }

            public void ReleaseEntry(SubMeshEntry e)
            {
                e.groupKey = default;
                e.material = null;
                e.materialInstanceId = 0;
                e.vertexCount = 0;
                e.triangleCount = 0;
                e.hasUv1 = e.hasUv2 = e.hasUv3 = false;
                e.component = null;
                e.shard = null;
                e.vertexOffsetInShard = 0;
                e.indexOffsetInShard = 0;
                e.vertices.FakeClear();
                e.uvs0.FakeClear();
                e.uvs1.FakeClear();
                e.uvs2.FakeClear();
                e.uvs3.FakeClear();
                e.colors.FakeClear();
                e.triangles.FakeClear();
                entryPool.Push(e);
            }

            public List<SubMeshEntry> AcquireWorkList()
            {
                return workListPool.Count > 0 ? workListPool.Pop() : new List<SubMeshEntry>(2);
            }

            public void ReleaseWorkList(List<SubMeshEntry> list)
            {
                list.Clear();
                workListPool.Push(list);
            }

            public void ClearEntries()
            {
                for (var i = 0; i < entries.Count; i++)
                    ReleaseEntry(entries[i]);
                entries.Clear();
            }

            public void ReturnBuffers()
            {
                for (var i = 0; i < entries.Count; i++)
                    entries[i].ReturnBuffers();
                entries.Clear();

                while (entryPool.Count > 0)
                    entryPool.Pop().ReturnBuffers();
            }
        }

        private sealed class SubMeshEntry
        {
            public Material material;
            public int materialInstanceId;
            public BatchKey groupKey;
            public int vertexCount;
            public int triangleCount;
            public bool hasUv1;
            public bool hasUv2;
            public bool hasUv3;

            /// <summary>Owning component. Used during structural rebuild and positional updates to
            /// read the current <c>localToWorldMatrix</c> and rotation.</summary>
            public UniTextWorld component;

            public PooledBuffer<Vector3> vertices;
            public PooledBuffer<Vector4> uvs0;
            public PooledBuffer<Vector4> uvs1;
            public PooledBuffer<Vector4> uvs2;
            public PooledBuffer<Vector4> uvs3;
            public PooledBuffer<Color32> colors;
            public PooledBuffer<int> triangles;

            public BatchShard shard;
            public int vertexOffsetInShard;
            public int indexOffsetInShard;

            public Vector3 localBoundsMin;
            public Vector3 localBoundsMax;

            public void ReturnBuffers()
            {
                vertices.Return();
                uvs0.Return();
                uvs1.Return();
                uvs2.Return();
                uvs3.Return();
                colors.Return();
                triangles.Return();
            }
        }

        private sealed class BatchGroup
        {
            public Material material;
            public readonly int materialInstanceId;
            public readonly int sortingLayerID;
            public readonly int sortingOrder;
            public readonly int unityLayer;
            public readonly Transform batcherTransform;

            public readonly List<BatchShard> shards = new(1);

            public bool IsValid => batcherTransform != null;
            public Matrix4x4 WorldToBatchMatrix => batcherTransform.worldToLocalMatrix;

            public BatchGroup(Material material, BatchKey key, Transform parent)
            {
                this.material = material;
                materialInstanceId = key.materialInstanceId;
                sortingLayerID = key.sortingLayerID;
                sortingOrder = key.sortingOrder;
                unityLayer = key.unityLayer;
                batcherTransform = parent;
            }

            public void Destroy()
            {
                for (var i = 0; i < shards.Count; i++)
                    shards[i].Destroy();
                shards.Clear();
            }
        }

        private sealed class BatchShard
        {
            public readonly BatchGroup group;
            public readonly List<SubMeshEntry> entries = new(4);

            public int usedVertexCount;
            public int usedIndexCount;

            public bool structuralDirty;
            public readonly HashSet<SubMeshEntry> positionalDirty = new();
            public readonly HashSet<SubMeshEntry> attributiveDirty = new();

            public PooledBuffer<PositionNormal> stream0;
            public PooledBuffer<Color32> stream1;
            public PooledBuffer<Vector4> stream2;
            public PooledBuffer<Uv123> stream3;
            public PooledBuffer<ushort> indices;

            public Vector3 boundsMin;
            public Vector3 boundsMax;

            public BatchLayer layer;
            private int allocatedVertexCount;
            private int allocatedIndexCount;

            public BatchShard(BatchGroup group)
            {
                this.group = group;
            }

            public void EnsureMeshAllocated(int vertexCount, int indexCount)
            {
                if (layer == null || !layer.IsAlive)
                {
                    if (layer != null) DestroyLayerStatic(layer);
                    layer = CreateLayer(group);
                    allocatedVertexCount = 0;
                    allocatedIndexCount = 0;
                }

                var mesh = layer.mesh;
                if (vertexCount != allocatedVertexCount)
                {
                    mesh.SetVertexBufferParams(vertexCount, vertexLayout);
                    allocatedVertexCount = vertexCount;
                }
                if (indexCount != allocatedIndexCount)
                {
                    mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
                    allocatedIndexCount = indexCount;
                }
            }

            public void ClearMesh()
            {
                if (layer == null) return;
                if (!layer.IsAlive)
                {
                    DestroyLayerStatic(layer);
                    layer = null;
                    allocatedVertexCount = 0;
                    allocatedIndexCount = 0;
                    return;
                }
                layer.mesh.Clear();
                layer.go.SetActive(false);
                allocatedVertexCount = 0;
                allocatedIndexCount = 0;
            }

            public void Destroy()
            {
                stream0.Return();
                stream1.Return();
                stream2.Return();
                stream3.Return();
                indices.Return();

                if (layer != null) DestroyLayerStatic(layer);
                layer = null;

                entries.Clear();
                positionalDirty.Clear();
                attributiveDirty.Clear();
                usedVertexCount = 0;
                usedIndexCount = 0;
            }

            private static BatchLayer CreateLayer(BatchGroup group)
            {
                var go = new GameObject($"{BatchLayerNamePrefix}{group.materialInstanceId}_-")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = group.unityLayer
                };
                go.transform.SetParent(group.batcherTransform, false);

                var marker = go.AddComponent<BatchLayerMarker>();
                marker.owned = true;
                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sortingLayerID = group.sortingLayerID;
                renderer.sortingOrder = group.sortingOrder;

                var mesh = new Mesh
                {
                    name = $"UniTextWorldBatch_{group.materialInstanceId}",
                    hideFlags = HideFlags.HideAndDontSave
                };
                mesh.MarkDynamic();
                filter.sharedMesh = mesh;

                return new BatchLayer { go = go, renderer = renderer, mesh = mesh };
            }

            private static void DestroyLayerStatic(BatchLayer layer)
            {
                if (layer == null) return;
                if (layer.mesh != null) ObjectUtils.SafeDestroy(layer.mesh);
                if (layer.go != null) ObjectUtils.SafeDestroy(layer.go);
            }
        }

        private class BatchLayer
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Mesh mesh;

            public bool IsAlive => go != null && renderer != null && mesh != null;
        }

        [AddComponentMenu("")]
        private sealed class BatchLayerMarker : MonoBehaviour, ISerializationCallbackReceiver
        {
            private static Action afterProcess;

            static BatchLayerMarker()
            {
                UniTextBase.AfterProcess += OnAfterProcess;
                return;

                void OnAfterProcess() => afterProcess?.Invoke();
            }

            [NonSerialized] public bool owned;

            public void OnBeforeSerialize() { }
            public void OnAfterDeserialize()
            {
                afterProcess += OnAfterProcess;
                return;

                void OnAfterProcess()
                {
                    if (!owned)
                    {
                        afterProcess -= OnAfterProcess;
                        if (this) ObjectUtils.SafeDestroy(gameObject);
                    }
                }
            }
        }

        #endregion
    }
}
