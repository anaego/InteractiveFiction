using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Serializable container that links a modifier with its parse rule.
    /// Tracks registration state and ownership to prevent bugs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note: "Initialized" state (buffers created, events subscribed) is tracked
    /// by BaseModifier.IsInitialized and happens lazily on first Apply().
    /// </para>
    /// <para>
    /// Ownership rules:
    /// - Style tracks its owner UniText
    /// - Registration fails if already registered to different owner
    /// - State resets on deserialization
    /// </para>
    /// </remarks>
    /// <seealso cref="BaseModifier"/>
    /// <seealso cref="IParseRule"/>
    /// <seealso cref="AttributeParser"/>
    [Serializable]
    public sealed class Style
    {
        [SerializeReference, TypeSelector] private BaseModifier modifier;
        [SerializeReference, TypeSelector] private IParseRule rule;

        [NonSerialized] private UniTextBase owner;
        [NonSerialized] private bool isRegistered;

        /// <summary>Gets or sets the modifier, with proper lifecycle management on hot-swap.</summary>
        public BaseModifier Modifier
        {
            get => modifier;
            set => SetModifier(value);
        }

        /// <summary>Gets or sets the parse rule, with proper lifecycle management on hot-swap.</summary>
        public IParseRule Rule
        {
            get => rule;
            set => SetRule(value);
        }

        /// <summary>Gets the UniTextBase that owns this Style.</summary>
        public UniTextBase Owner => owner;

        /// <summary>
        /// Returns true if the style is ready for registration.
        /// Requires a rule; modifier is required unless <see cref="IParseRule.IsStandalone"/> is true.
        /// </summary>
        public bool IsValid => rule != null && (modifier != null || rule.IsStandalone);

        /// <summary>Returns true if registered to a UniText.</summary>
        public bool IsRegistered => isRegistered;

        /// <summary>
        /// Registers this style with a UniText and its AttributeParser.
        /// </summary>
        /// <param name="uniText">The UniText component.</param>
        /// <param name="parser">The AttributeParser to register with.</param>
        /// <returns>True if registration succeeded, false if invalid, already registered, or owned by different UniText.</returns>
        internal bool Register(UniTextBase uniText, AttributeParser parser)
        {
            if (!IsValid) return false;

            if (isRegistered && owner == uniText) return true;

            if (owner != null && owner != uniText)
            {
                Debug.LogError($"[UniText] Cannot register to {uniText.name}: already owned by {owner.name}. " +
                               "Create separate Style instances for each UniText.");
                return false;
            }

            owner = uniText;
            modifier?.SetOwner(uniText);
            parser.Register(rule, modifier);
            isRegistered = true;
            return true;
        }

        internal void Unregister(AttributeParser parser)
        {
            if (!isRegistered) return;

            if (modifier != null && modifier.IsInitialized)
            {
                modifier.Destroy();
            }

            if (parser != null)
            {
                if (modifier != null) parser.Unregister(modifier);
                else if (rule != null) parser.UnregisterRule(rule);
            }

            isRegistered = false;
            owner = null;
        }

        /// <summary>
        /// Resets state for deserialization. Called when Unity recreates the object.
        /// </summary>
        internal void ResetState()
        {
            owner = null;
            isRegistered = false;
        }

        /// <summary>
        /// Deinitializes the modifier but keeps it registered (for font changes).
        /// </summary>
        internal void DeinitializeModifier()
        {
            if (modifier != null && modifier.IsInitialized)
            {
                modifier.Destroy();
            }
        }

        private void SetModifier(BaseModifier value)
        {
            if (modifier == value) return;

            var wasInitialized = modifier != null && modifier.IsInitialized;
            var wasRegistered = isRegistered;
            var cachedOwner = owner;

            if (wasRegistered && cachedOwner != null)
            {
                cachedOwner.UnregisterStyleFromParser(this);
            }

            modifier = value;

            if (wasRegistered && cachedOwner != null && IsValid)
            {
                cachedOwner.RegisterStyleWithParser(this);

                if (wasInitialized)
                {
                    cachedOwner.SetDirty(UniTextDirtyFlags.Text);
                }
            }
        }

        private void SetRule(IParseRule value)
        {
            if (rule == value) return;

            var wasInitialized = modifier != null && modifier.IsInitialized;
            var wasRegistered = isRegistered;
            var cachedOwner = owner;

            if (wasRegistered && cachedOwner != null)
            {
                cachedOwner.UnregisterStyleFromParser(this);
            }

            rule = value;

            if (wasRegistered && cachedOwner != null && IsValid)
            {
                cachedOwner.RegisterStyleWithParser(this);

                if (wasInitialized)
                {
                    cachedOwner.SetDirty(UniTextDirtyFlags.Text);
                }
            }
        }

        /// <summary>
        /// Builds a style that applies <paramref name="modifier"/> to the entire text via a
        /// whole-text <see cref="RangeRule"/>.
        /// </summary>
        /// <param name="modifier">The modifier to apply. Must not be null.</param>
        /// <param name="parameter">Optional parameter forwarded to the modifier's <c>OnApply</c>.</param>
        public static Style WholeText(BaseModifier modifier, string parameter = null)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));

            return new Style
            {
                Modifier = modifier,
                Rule = new RangeRule
                {
                    data = new List<RangeRule.Data>
                    {
                        new() { range = RangeEx.WholeText, parameter = parameter }
                    }
                }
            };
        }

        /// <summary>
        /// Builds a style that applies <paramref name="modifier"/> to a fixed codepoint range
        /// via a <see cref="RangeRule"/>.
        /// </summary>
        /// <param name="modifier">The modifier to apply. Must not be null.</param>
        /// <param name="start">Inclusive start codepoint index.</param>
        /// <param name="end">Exclusive end codepoint index.</param>
        /// <param name="parameter">Optional parameter forwarded to the modifier's <c>OnApply</c>.</param>
        public static Style Range(BaseModifier modifier, int start, int end, string parameter = null)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));

            return new Style
            {
                Modifier = modifier,
                Rule = new RangeRule
                {
                    data = new List<RangeRule.Data>
                    {
                        new() { range = $"{start}..{end}", parameter = parameter }
                    }
                }
            };
        }

        /// <summary>
        /// Builds a style that activates <paramref name="modifier"/> on ranges matched by a
        /// rich-text tag <c>&lt;tagName&gt;...&lt;/tagName&gt;</c>.
        /// </summary>
        /// <param name="modifier">The modifier to apply. Must not be null.</param>
        /// <param name="tagName">Tag name without angle brackets (e.g. <c>"color"</c>, <c>"lang"</c>).</param>
        /// <param name="defaultParameter">
        /// Optional default parameter used when the tag is written without <c>=value</c>.
        /// </param>
        public static Style Tag(BaseModifier modifier, string tagName, string defaultParameter = null)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (string.IsNullOrEmpty(tagName)) throw new ArgumentException("tagName must be non-empty", nameof(tagName));

            return new Style
            {
                Modifier = modifier,
                Rule = new TagRule(tagName) { defaultParameter = defaultParameter }
            };
        }
    }

}
