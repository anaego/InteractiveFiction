namespace LightSide
{
    public partial class UniTextWorld
    {
        private AnimationHandler animationHandler;

        private void EnsureAnimationHandler()
        {
            if (animationHandler == null)
                animationHandler = new AnimationHandler(this);
            else
                animationHandler.CaptureBaseline();
        }

        protected override void HandleAnimation()
        {
            animationHandler?.Handle();
        }

        /// <summary>
        /// Animation handler for <see cref="UniTextWorld"/>. Adds diff coverage for
        /// <see cref="SortingOrder"/> and <see cref="SortingLayerID"/> on top of the base
        /// text fields, and re-fires <see cref="SortingChanged"/> so observers
        /// (e.g. <see cref="UniTextWorldBatcher"/>) react to animated sorting just as they
        /// would to a setter call.
        /// </summary>
        public sealed class AnimationHandler : AnimationHandlerBase<UniTextWorld>
        {
            private int sortingOrderCache;
            private int sortingLayerIDCache;

            public AnimationHandler(UniTextWorld target) : base(target) { }

            protected override void CaptureSubclassBaseline()
            {
                sortingOrderCache = target.sortingOrder;
                sortingLayerIDCache = target.sortingLayerID;
            }

            protected override UniTextDirtyFlags DiffSubclassFields()
            {
                var flags = UniTextDirtyFlags.None;
                var sortingChanged = false;

                if (sortingOrderCache != target.sortingOrder)
                {
                    sortingOrderCache = target.sortingOrder;
                    flags |= UniTextDirtyFlags.Sorting;
                    sortingChanged = true;
                }

                if (sortingLayerIDCache != target.sortingLayerID)
                {
                    sortingLayerIDCache = target.sortingLayerID;
                    flags |= UniTextDirtyFlags.Sorting;
                    sortingChanged = true;
                }

                if (sortingChanged)
                    target.SortingChanged?.Invoke(target);

                return flags;
            }
        }
    }
}
