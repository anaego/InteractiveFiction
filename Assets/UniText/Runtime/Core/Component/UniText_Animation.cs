namespace LightSide
{
    public partial class UniText
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
        /// Animation handler for <see cref="UniText"/>. Diffs the base <see cref="UniTextBase"/>
        /// fields only — <see cref="UniText"/> has no Canvas-specific animatable serialized
        /// fields of its own.
        /// </summary>
        public sealed class AnimationHandler : AnimationHandlerBase<UniText>
        {
            public AnimationHandler(UniText target) : base(target) { }
        }
    }
}
