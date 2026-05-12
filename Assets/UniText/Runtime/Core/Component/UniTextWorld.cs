using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    /// <summary>
    /// World-space text rendering component with full Unicode support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The component is self-contained — it publishes its state through events and does not know
    /// about any specific renderer. An invisible batcher (<see cref="UniTextWorldBatcher"/>)
    /// subscribes to the <see cref="Activated"/> / <see cref="Deactivated"/> static events and
    /// to per-instance events (<see cref="RenderDataAvailable"/>, <see cref="RenderDataCleared"/>,
    /// <see cref="SortingChanged"/>, <see cref="ParentChanged"/>) to assemble combined meshes.
    /// </para>
    /// <para>
    /// Conceptual analogy: <c>UniTextWorld</c> is to <see cref="UniTextWorldBatcher"/> what
    /// <c>CanvasRenderer</c> is to <c>Canvas</c> — a data provider; the batcher observes and
    /// draws.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    public partial class UniTextWorld : UniTextBase
    {
        #region World State

        [SerializeField]
        [Tooltip("Sorting order within the current sorting layer.")]
        private int sortingOrder;

        [SerializeField]
        [Tooltip("Sorting layer ID for render ordering.")]
        private int sortingLayerID;

        [SerializeField]
        [Tooltip("If enabled, this text receives pointer events from UniTextWorldRaycaster (clicks, hover, interactive ranges). Disable for purely decorative text.")]
        private bool raycastTarget = true;

        #endregion

        #region Events

        /// <summary>Raised when a <see cref="UniTextWorld"/> instance becomes active (<c>OnEnable</c>).</summary>
        /// <remarks>Fires after <see cref="UniTextBase.OnEnable"/> base work has completed.</remarks>
        public static event Action<UniTextWorld> Activated;

        /// <summary>Raised when a <see cref="UniTextWorld"/> instance becomes inactive (<c>OnDisable</c>).</summary>
        /// <remarks>Fires after <see cref="UniTextBase.OnDisable"/> base work has completed.</remarks>
        public static event Action<UniTextWorld> Deactivated;

        /// <summary>
        /// Raised right after the component's mesh has been generated and render data is available
        /// through <see cref="UniTextBase.MeshGenerator"/>.<see cref="UniTextMeshGenerator.CollectRenderData"/>.
        /// </summary>
        /// <remarks>
        /// <b>Lifetime:</b> pooled buffers backing the render data are only valid until the next
        /// rebuild on the same generator. Consumers must read/copy the data during this callback
        /// and must not retain the arrays.
        /// </remarks>
        public event Action<UniTextWorld> RenderDataAvailable;

        /// <summary>Raised when the component's render data should be discarded (e.g. empty text, disabled).</summary>
        public event Action<UniTextWorld> RenderDataCleared;

        /// <summary>Raised when <see cref="SortingOrder"/> or <see cref="SortingLayerID"/> changes.</summary>
        public event Action<UniTextWorld> SortingChanged;

        /// <summary>Raised when the component's transform parent changes (may invalidate <c>SortingGroup</c> inheritance).</summary>
        public event Action<UniTextWorld> ParentChanged;

        #endregion

        #region Active Registry

        private static readonly List<UniTextWorld> activeInstances = new(16);
        private static readonly ReadOnlyList<UniTextWorld> activeView = new(activeInstances);

        /// <summary>
        /// Gets all currently enabled <see cref="UniTextWorld"/> instances. Populated by
        /// <c>OnEnable</c>/<c>OnDisable</c>. Used by <see cref="UniTextWorldRaycaster"/> to
        /// iterate hit-testable components without Unity scene scans.
        /// </summary>
        public static IReadOnlyList<UniTextWorld> Active => activeView;

        private readonly struct ReadOnlyList<T> : IReadOnlyList<T>
        {
            private readonly List<T> inner;
            public ReadOnlyList(List<T> inner) { this.inner = inner; }
            public int Count => inner.Count;
            public T this[int index] => inner[index];
            public List<T>.Enumerator GetEnumerator() => inner.GetEnumerator();
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => inner.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => inner.GetEnumerator();
        }

        #endregion

        #region Public API

        /// <summary>Gets or sets the sorting order for render ordering.</summary>
        public int SortingOrder
        {
            get => sortingOrder;
            set
            {
                if (sortingOrder == value) return;
                sortingOrder = value;
                SetDirty(UniTextDirtyFlags.Sorting);
                SortingChanged?.Invoke(this);
            }
        }

        /// <summary>Gets or sets the sorting layer ID.</summary>
        public int SortingLayerID
        {
            get => sortingLayerID;
            set
            {
                if (sortingLayerID == value) return;
                sortingLayerID = value;
                SetDirty(UniTextDirtyFlags.Sorting);
                SortingChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// When true, this text receives pointer events from <see cref="UniTextWorldRaycaster"/>
        /// (clicks, hover, interactive ranges). Set to false for purely decorative text — the
        /// raycaster will skip it entirely. Default: true.
        /// </summary>
        public bool RaycastTarget
        {
            get => raycastTarget;
            set => raycastTarget = value;
        }

        #endregion

        #region Canvas Pipeline Suppression

        public override void SetAllDirty() { SetDirty(UniTextDirtyFlags.All); }
        public override void SetVerticesDirty() { }
        public override void SetMaterialDirty() { }
        protected override void OnCanvasHierarchyChanged() { }
        protected override void UpdateGeometry() { }

#if UNITY_EDITOR
        private bool validateDeferred;

        protected override void OnValidate()
        {
            if (validateDeferred) return;
            validateDeferred = true;
            EditorApplication.update += DeferredValidate;
        }

        private void DeferredValidate()
        {
            EditorApplication.update -= DeferredValidate;
            validateDeferred = false;
            if (this == null) return;
            SetAllDirty();
        }
#endif

        #endregion

        #region Abstract Implementations

        protected override void UpdateRendering()
        {
#if UNITY_EDITOR
            if (sceneVisibilityHidden) return;
#endif
            RenderDataAvailable?.Invoke(this);
        }

        protected override void ClearAllRenderers()
        {
            RenderDataCleared?.Invoke(this);
        }

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            CleanupLegacySubMeshes();
            if (!activeInstances.Contains(this))
                activeInstances.Add(this);
            EnsureAnimationHandler();
            Activated?.Invoke(this);
            WarnIfNoRaycaster();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            activeInstances.Remove(this);
            Deactivated?.Invoke(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CleanupLegacySubMeshes();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            ParentChanged?.Invoke(this);
        }

        /// <summary>
        /// Removes legacy child GameObjects from the old per-object rendering system.
        /// </summary>
        private void CleanupLegacySubMeshes()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("-_UTWSM_-") || child.name.StartsWith("-_DEBUG_-"))
                    ObjectUtils.SafeDestroy(child.gameObject);
            }
        }

        private static bool raycasterCheckedInScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRaycasterCheckOnSceneChange()
        {
            raycasterCheckedInScene = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => raycasterCheckedInScene = false;

        private void WarnIfNoRaycaster()
        {
            if (raycasterCheckedInScene) return;
            if (!Application.isPlaying) return;
            if (!raycastTarget) return;
            raycasterCheckedInScene = true;
            if (FindObjectOfType<UniTextWorldRaycaster>(includeInactive: true) != null) return;
            Debug.LogWarning(
                $"[UniText] '{name}' is an interactive UniTextWorld but no UniTextWorldRaycaster was found in the scene. " +
                "Pointer events (clicks, hover, interactive ranges) will not fire. " +
                "Add a UniTextWorldRaycaster component to the camera that should pick up these events, " +
                "or set RaycastTarget = false on this text if it is purely decorative.",
                this);
        }

        #endregion
    }
}
