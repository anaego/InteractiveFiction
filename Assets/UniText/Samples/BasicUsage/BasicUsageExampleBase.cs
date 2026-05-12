using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace LightSide.Samples
{
    /// <summary>
    /// Shared driver for the BasicUsage example set. Builds the markup/style pipeline,
    /// navigates through a curated list of example texts, and routes link interactions
    /// (theme/language/font switching).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subclass and override <see cref="OnInit"/>, <see cref="ApplyText(string)"/>, and
    /// <see cref="ApplySetText"/> to splice extra behavior into the text application path
    /// (e.g. mirroring Unity-originated text to an external surface).
    /// </para>
    /// </remarks>
    public abstract class BasicUsageExampleBase : MonoBehaviour
    {
        [Header("UniText Components")]
        [SerializeField] protected UniText demoText;
        [SerializeField] protected UniText statusText;

        [Header("Configuration")]
        [SerializeField] protected bool registerModifiersOnStart = true;

        protected readonly List<Style> registeredStyles = new();
        protected int currentExample;
        protected string[] examples;
        protected LinkModifier linkModifier;
        protected Style languageTagStyle;
        protected StylePreset firePreset;
        protected StylePreset oceanPreset;
        protected StylePreset neonPreset;

        private readonly char[] setTextBuffer = new char[2048];
        private readonly PrefixResolver demoResolver = new()
        {
            prefix = "🔧 [Resolver substituted prefix]\n\n"
        };
        private bool wasPressed;

        /// <summary>Hook invoked from <see cref="Awake"/>, before anything else runs.</summary>
        protected virtual void OnInit() { }

        /// <summary>
        /// Applies a Unity-originated text change to <see cref="demoText"/>. Override to
        /// splice forwarding/observation logic.
        /// </summary>
        protected virtual void ApplyText(string text)
        {
            if (demoText != null && text != null) demoText.Text = text;
        }

        /// <summary>
        /// Zero-alloc variant of <see cref="ApplyText(string)"/>. <paramref name="browserPayload"/>
        /// is the already-known string equivalent of the char-range, useful for external mirrors
        /// that still want the string.
        /// </summary>
        protected virtual void ApplySetText(char[] buffer, int offset, int length, string browserPayload)
        {
            if (demoText != null && buffer != null) demoText.SetText(buffer, offset, length);
        }

        /// <summary>
        /// Assigns text via <see cref="UniTextBase.SetText(ReadOnlyMemory{char})"/> without
        /// persisting to the serialized field. <paramref name="browserPayload"/> mirrors the
        /// string form for external observers.
        /// </summary>
        protected virtual void ApplySetTextMemory(ReadOnlyMemory<char> source, string browserPayload)
        {
            if (demoText != null) demoText.SetText(source);
        }

        private void Awake()
        {
            OnInit();
        }

        private void Start()
        {
            if (demoText == null)
            {
                Debug.LogError($"[{GetType().Name}] DemoText is not assigned!");
                return;
            }

            if (registerModifiersOnStart)
                AddAllStyles();

            CreatePresets();
            SetupExamples();
            SetupLinkEvents();
            ShowExample(0);
        }

        private void AddAllStyles()
        {
            AddStyle(new BoldModifier(), new TagRule("b"));
            AddStyle(new ItalicModifier(), new TagRule("i"));
            AddStyle(new UnderlineModifier(), new TagRule("u"));
            AddStyle(new StrikethroughModifier(), new TagRule("s"));

            AddStyle(new ColorModifier(), new TagRule("color"));
            AddStyle(new GradientModifier(), new TagRule("gradient"));
            AddStyle(new SizeModifier(), new TagRule("size"));

            AddStyle(new LetterSpacingModifier(), new TagRule("cspace"));
            AddStyle(new LineHeightModifier(), new TagRule("line-height"));

            languageTagStyle = AddStyle(new LanguageModifier(), new TagRule("lang"));
            AddStyle(new FontModifier(), new TagRule("font"));

            linkModifier = new LinkModifier();
            AddStyle(linkModifier, new TagRule("link"));
        }

        private Style AddStyle(BaseModifier modifier, IParseRule rule)
        {
            var style = new Style { Modifier = modifier, Rule = rule };
            demoText.AddStyle(style);
            registeredStyles.Add(style);
            return style;
        }

        private bool StylesContain(Style style)
        {
            var styles = demoText.Styles;
            for (var i = 0; i < styles.Count; i++)
                if (styles[i] == style) return true;
            return false;
        }

        private void CreatePresets()
        {
            firePreset = ScriptableObject.CreateInstance<StylePreset>();
            AddToPreset(firePreset, "t", "#FF4500", new ColorModifier(), new BoldModifier());
            AddToPreset(firePreset, "t2", "#FF6B35", new ColorModifier(), new ItalicModifier());
            AddToPreset(firePreset, "accent", "#FFD700", new ColorModifier(), new UnderlineModifier());

            oceanPreset = ScriptableObject.CreateInstance<StylePreset>();
            AddToPreset(oceanPreset, "t", "#0077B6", new ColorModifier(), new ItalicModifier());
            AddToPreset(oceanPreset, "t2", "#00B4D8;2", new ColorModifier(), new LetterSpacingModifier());
            AddToPreset(oceanPreset, "accent", "#90E0EF", new ColorModifier(), new UnderlineModifier());

            neonPreset = ScriptableObject.CreateInstance<StylePreset>();
            AddToPreset(neonPreset, "t", "#FF006E", new ColorModifier(), new BoldModifier());
            AddToPreset(neonPreset, "t2", "#8338EC", new ColorModifier(), new UppercaseModifier());
            AddToPreset(neonPreset, "accent", "#3A86FF;3,3,0.5", new ColorModifier(), new WobbleAnimationModifier());

            demoText.AddStylePreset(neonPreset);
        }

        private static void AddToPreset(StylePreset preset, string tag, string param, params BaseModifier[] mods)
        {
            BaseModifier modifier = mods.Length == 1
                ? mods[0]
                : new CompositeModifier { modifiers = new TypedList<BaseModifier>(mods) };
            var rule = new TagRule(tag);
            if (param != null) rule.defaultParameter = param;
            preset.styles.Add(new Style { Modifier = modifier, Rule = rule });
        }

        private void SwitchTheme(string theme)
        {
            demoText.ClearStylePresets();

            var preset = theme switch
            {
                "fire" => firePreset,
                "ocean" => oceanPreset,
                "neon" => neonPreset,
                _ => firePreset
            };

            demoText.AddStylePreset(preset);
            UpdateStatus($"<color=#2ECC71>Theme:</color> {theme}");
        }

        private void SwitchLanguage(string language)
        {
            var isClear = string.IsNullOrEmpty(language);

            if (isClear)
            {
                demoText.Language = null;
                if (languageTagStyle != null && !StylesContain(languageTagStyle))
                    demoText.AddStyle(languageTagStyle);
                UpdateStatus("<color=#2ECC71>Language:</color> none (per-range <lang> tags re-enabled)");
            }
            else
            {
                if (languageTagStyle != null && StylesContain(languageTagStyle))
                    demoText.RemoveStyle(languageTagStyle);
                demoText.Language = language;
                UpdateStatus($"<color=#2ECC71>Language:</color> {language} (per-range <lang> tags suspended)");
            }
        }

        private void SwitchFont(string familyName)
        {
            if (string.IsNullOrEmpty(familyName))
            {
                demoText.ClearWholeText<FontModifier>();
                UpdateStatus("<color=#2ECC71>Font:</color> cleared");
            }
            else
            {
                demoText.SetWholeText<FontModifier>(familyName);
                UpdateStatus($"<color=#2ECC71>Font:</color> {familyName}");
            }
        }

        private void SetupExamples()
        {
            examples = new[]
            {
                "✨ <b><color=#FFD700>UniText</color></b> ✨\n<color=#888>Professional Unicode Text Rendering</color>\n\n👉 Press <b>Space</b> or ⬅️➡️ to explore",
                "🔀 <b>Bidirectional Text</b>\n\nThe word <color=#4ECDC4>مرحبا</color> means \"hello\" in Arabic\nUser <color=#FF6B6B>יוסי כהן</color> sent you a message\nFile: <color=#A06CD5>تقرير_٢٠٢٤.pdf</color> (15 MB)\n\nPrices: $99 | ٩٩ ريال | ₪199\nDate: 25 يناير 2024 | 25 בינואר 2024\n\n<color=#888>⬅️ Automatic direction detection ➡️</color>\n\nمرحباً يا صديقي! كيف حالك اليوم؟ 😊 أتمنى أن يكون يومك رائعاً. هل شاهدت المباراة أمس؟ كانت مثيرة جداً! الفريق لعب بشكل ممتاز وسجّل ثلاثة أهداف في الشوط الثاني. أنا سعيد جداً بالنتيجة. ما رأيك في أداء اللاعبين؟\n\nHey there! I'm doing great, thanks for asking! 🎉 Yes, I watched the game yesterday and it was absolutely incredible! The team played so well, especially in the second half. Those three goals were amazing — did you see that last one? The goalkeeper had no chance! By the way, are you free this weekend? We should hang out and watch the next match together. Let me know what you think!\n\nأنا موافق تماماً! 👍 The match was legendary — لم أرَ مثل هذا الأداء منذ سنوات! خاصة الهدف الثالث، it was pure magic! نعم، أنا متفرغ يوم السبت. Let's meet at the usual place around 7 PM? يمكننا مشاهدة المباراة القادمة معاً ونطلب بيتزا 🍕 — what do you think? أخبرني إذا كان الموعد مناسباً لك!",
                "😀 <b>Emoji Support</b> 🎉\n\nFaces: 😀 😃 😄 😁 😆 🥹 😅 😂 🤣 😍 🥰 😎 🤔 🤩 🥳 😴 🤯 🤗 🥺 😇\nGestures: 👋 👍 👎 👏 🙌 🤝 ✌️ 🤘 🤟 🫶 🙏 💪 🫡 🤌 👌\nFlags: 🇺🇸 🇬🇧 🇯🇵 🇰🇷 🇩🇪 🇫🇷 🇮🇹 🇪🇸 🇷🇺 🇨🇳 🇧🇷 🇮🇳 🇨🇦 🇲🇽 🇦🇺\nFamily: 👨‍👩‍👧‍👦 👨‍👨‍👧 👩‍👩‍👦 👨‍👧‍👦 👩‍👧‍👧\nSkin tones: 👋🏻 👋🏼 👋🏽 👋🏾 👋🏿 • 👍🏻 👍🏽 👍🏿\nAnimals: 🐶 🐱 🦊 🐼 🦁 🐯 🐨 🐸 🦄 🐢 🦋 🐙 🦉 🦖 🐳 🦔\nFood: 🍕 🍔 🍟 🌮 🍣 🍜 🍩 🍰 🍓 🍇 🥑 ☕ 🍷 🍺 🥗 🍿\nNature: 🌸 🌻 🌈 🌊 🔥 ❄️ ⭐ 🌙 ☀️ ⛅ 🌋 🌎 🍂 🌴 ⛰️ 🌌\nSports: ⚽ 🏀 🏈 ⚾ 🎾 🏐 🏓 🎱 🏆 🎯 🥇 🎮 🎳 🏂 🏊 🚴\nVehicles: 🚗 🚕 🚙 🚌 🏎️ 🚓 🚑 🚒 ✈️ 🚀 🛸 🚂 🚢 🛵 🚁 🛴\nHearts: ❤️ 🧡 💛 💚 💙 💜 🤎 🖤 🤍 💔 ❣️ 💕 💖 💗 💘 💝\nObjects: 🎁 🎈 🎨 🎭 🎬 🎸 🎹 🎺 📱 💻 📷 🔑 💎 📚 🕹️ ⏰\nSymbols: ✨ 💫 ⚡ 💥 🔔 🎵 ♾️ ✅ ⛔ 🆕 🆒 🆗 ☯️ ♻️ 🔱 ⚛️",
                "🎭 <b>Style Presets</b>\n\n<t>The quick brown fox</t> jumps over <t2>the lazy dog</t2>.\n<accent>Each theme brings its own personality</accent>.\n<t>Same markup</t>, <t2>different preset</t2> — <accent>instant transformation</accent>.\n\n<link=preset:fire>🔥 Fire</link>  <link=preset:ocean>🌊 Ocean</link>  <link=preset:neon>⚡ Neon</link>",
                "<b>Bold</b> • <i>Italic</i> • <u>Underline</u> • <s>Strike</s>\n<b><i>Bold Italic</i></b> • <b><u>Bold Underline</u></b>\n\n🎨 Mix styles: <b><i><color=#FF6B6B>Bold Italic Red</color></i></b>",
                "🌈 <color=#FF6B6B>Red</color> <color=#FFE66D>Yellow</color> <color=#4ECDC4>Teal</color> <color=#45B7D1>Blue</color> <color=#A06CD5>Purple</color>\n\n<color=#FF6B6B>⁜</color><color=#FF8E6B>⁜</color><color=#FFB06B>⁜</color><color=#FFD26B>⁜</color><color=#FFF46B>⁜</color><color=#D2FF6B>⁜</color><color=#6BFF6B>⁜</color><color=#6BFFD2>⁜</color><color=#6BD2FF>⁜</color><color=#6B8EFF>⁜</color><color=#8E6BFF>⁜</color>",
                "🎨 <b>Linear Gradients</b>\n\n<gradient=rainbow>Horizontal Rainbow</gradient>\n\n<gradient=ocean,linear,90>Vertical Ocean</gradient>\n\n<gradient=rainbow,linear,45>Diagonal\nRainbow</gradient>\n\n<gradient=fire>Fire</gradient> • <gradient=ocean>Ocean</gradient>",
                "🎨 <b>Radial & Angular</b>\n\n<gradient=rainbow,radial>Rainbow radiates\nfrom the center\nof this block</gradient>\n\n<gradient=rainbow,angular>Color wheel\nsweeps around\nthe center</gradient>",
                "<size=60%>tiny</size> <size=80%>small</size> normal <size=120%>large</size> <size=150%>huge</size>\n\n📏 Dynamic sizing for emphasis",
                "<b>العربية</b>\n\nمرحباً بالعالم! 👋\nهذا نص عربي مع <color=#4ECDC4>ألوان</color> و <b>تنسيق</b>.\n\nأرقام: ٠١٢٣٤٥٦٧٨٩ 🔢",
                "<b>עברית</b>\n\nשלום עולם! 👋\nזהו טקסט עברי עם <color=#FF6B6B>צבעים</color> ו<b>עיצוב</b>.\n\nמספרים: 0123456789 🔢",
                "🔤 <b>Complex Scripts</b>\n\n<color=#4ECDC4>Arabic ligatures:</color> لا الله بسم الله\n<color=#FF6B6B>Hindi:</color> नमस्ते दुनिया 🙏\n<color=#A06CD5>Arabic joining:</color> ب‍ ‍ب‍ ‍ب (initial, medial, final)",
                "🔗 <b>Interactive Links</b>\n\n📖 <link=https://unity.lightside.media/unitext/docs><color=#45B7D1><u>Documentation</u></color></link> - Full API reference\n🌐 <link=https://unity.lightside.media><color=#A06CD5><u>LightSide</u></color></link> - Our website\n\n<color=#888>Click links to open • Hover to preview</color>",
                "<cspace=15>S P A C E D</cspace>\n<cspace=-2>Tight kerning</cspace>\n\n<line-height=150%>Line 1 with\nincreased height\nbetween lines</line-height>",

                "🌏 <b>Language & OpenType 'locl'</b>\n\n" +
                "Identical code points rendered four times with different language tags:\n" +
                "<lang=zh-Hans>zh-Hans  直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n" +
                "<lang=zh-Hant>zh-Hant  直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n" +
                "<lang=ja>ja       直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n" +
                "<lang=ko>ko       直 骨 雪 今 家 字 漢 社 海 神 真 食</lang>\n\n" +
                "Component-level <b>Language</b> property (click to switch):\n" +
                "<link=lang:><color=#4ECDC4><u>none</u></color></link>  " +
                "<link=lang:zh-Hans><color=#4ECDC4><u>zh-Hans</u></color></link>  " +
                "<link=lang:zh-Hant><color=#4ECDC4><u>zh-Hant</u></color></link>  " +
                "<link=lang:ja><color=#4ECDC4><u>ja</u></color></link>  " +
                "<link=lang:ko><color=#4ECDC4><u>ko</u></color></link>\n\n" +
                "<color=#888>Use the bundled Fonts/SourceHanSans-Demo.otf (96 KB subset of Adobe\n" +
                "Source Han Sans with full locl coverage). Region-specific cuts like Noto Serif SC\n" +
                "only contain one set of glyphs and look identical across rows.\n\n" +
                "Per-range tags always win over the component-level default.</color>",

                "🔤 <b>Font override with ⟨font=name⟩</b>\n\n" +
                "Add unique names to families in your FontStack inspector, then reference\n" +
                "them here. Unknown names fall back and log a warning.\n\n" +
                "Per-range ⟨font=…⟩:\n" +
                "<font=header>HEADER FAMILY SAMPLE</font>\n" +
                "<font=body>Body family — paragraph sample with more words.</font>\n" +
                "<font=mono>monospace_function(arg1, arg2)</font>\n\n" +
                "Set whole-text family (click to apply):\n" +
                "<link=font:><color=#4ECDC4><u>clear</u></color></link>  " +
                "<link=font:header><color=#4ECDC4><u>header</u></color></link>  " +
                "<link=font:body><color=#4ECDC4><u>body</u></color></link>  " +
                "<link=font:mono><color=#4ECDC4><u>mono</u></color></link>\n\n" +
                "<color=#888>Per-range tags always win over the whole-text default.</color>",

                "🚀 <b><color=#FFD700>UniText</color></b> <size=80%>v1.0</size>\n\n✅ <color=#4ECDC4>Full Unicode</color> support\n✅ <color=#FF6B6B>RTL</color>: العربية עברית\n✅ <color=#A06CD5>Emoji</color>: 😀🎉👨‍👩‍👧‍👦🇺🇸\n✅ <color=#FFE66D>HarfBuzz</color> shaping\n✅ <color=#45B7D1>Markup</color> system\n✅ <gradient=rainbow>Gradients</gradient>\n\n<link=start><color=#4ECDC4>▶️ Start using UniText</color></link>"
            };
        }

        private void SetupLinkEvents()
        {
            if (linkModifier != null)
            {
                linkModifier.AutoOpenUrl = false;
                linkModifier.LinkClicked += OnLinkClicked;
                linkModifier.LinkEntered += OnLinkEnter;
                linkModifier.LinkExited += OnLinkExit;
            }
        }

        private void Update()
        {
            var leftPressed = false;
            var rightPressed = false;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = Keyboard.current;
            if (kb != null)
            {
                leftPressed = kb.leftArrowKey.isPressed || kb.aKey.isPressed;
                rightPressed = kb.rightArrowKey.isPressed || kb.dKey.isPressed || kb.spaceKey.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            leftPressed = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
            rightPressed = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Space);
#endif

            if (leftPressed)
            {
                if (!wasPressed) { PreviousExample(); wasPressed = true; }
            }
            else if (rightPressed)
            {
                if (!wasPressed) { NextExample(); wasPressed = true; }
            }
            else
            {
                wasPressed = false;
            }
        }

        public void NextExample()
        {
            currentExample = (currentExample + 1) % examples.Length;
            ShowExample(currentExample);
        }

        public void PreviousExample()
        {
            currentExample = (currentExample - 1 + examples.Length) % examples.Length;
            ShowExample(currentExample);
        }

        private void ShowExample(int index)
        {
            var src = examples[index];
            string mode;
            switch (index % 4)
            {
                case 0:
                    if (demoText.TextResolver != null) demoText.TextResolver = null;
                    ApplyText(src);
                    mode = "Text (persists)";
                    Debug.Log($"[SetText Test] Text setter ← \"{src}\"");
                    break;
                case 1:
                    if (demoText.TextResolver != null) demoText.TextResolver = null;
                    src.CopyTo(0, setTextBuffer, 0, src.Length);
                    ApplySetText(setTextBuffer, 0, src.Length, src);
                    mode = "SetText(char[])";
                    Debug.Log($"[SetText Test] SetText(char[]) ← \"{src}\"");
                    break;
                case 2:
                    if (demoText.TextResolver != null) demoText.TextResolver = null;
                    ApplySetTextMemory(src.AsMemory(), src);
                    mode = "SetText(ReadOnlyMemory<char>)";
                    Debug.Log($"[SetText Test] SetText(ROM) ← \"{src}\"");
                    break;
                default:
                    if (demoText.TextResolver != demoResolver) demoText.TextResolver = demoResolver;
                    ApplySetTextMemory(src.AsMemory(), src);
                    mode = "SetText + IUniTextResolver";
                    Debug.Log($"[SetText Test] Resolver active ← \"{src}\" (prefix added at render)");
                    break;
            }

            UpdateStatus($"Example {index + 1}/{examples.Length} ({mode}) - Press Arrow keys");
        }

        private void OnLinkClicked(string url)
        {
            if (url.StartsWith("preset:")) { SwitchTheme(url.Substring(7)); return; }
            if (url.StartsWith("lang:"))   { SwitchLanguage(url.Substring(5)); return; }
            if (url.StartsWith("font:"))   { SwitchFont(url.Substring(5)); return; }

            UpdateStatus($"<color=#2ECC71>Clicked:</color> {url}");

            if (url.StartsWith("http"))
                Application.OpenURL(url);
        }

        private void OnLinkEnter(string url)
        {
            UpdateStatus($"<color=#3498DB>Hovering:</color> {url}");
        }

        private void OnLinkExit()
        {
            UpdateStatus($"Example {currentExample + 1}/{examples.Length}");
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null) statusText.Text = message;
        }

        private void OnDestroy()
        {
            if (linkModifier != null)
            {
                linkModifier.LinkClicked -= OnLinkClicked;
                linkModifier.LinkEntered -= OnLinkEnter;
                linkModifier.LinkExited -= OnLinkExit;
            }

            demoText?.ClearStylePresets();

            foreach (var style in registeredStyles)
                demoText?.RemoveStyle(style);
            registeredStyles.Clear();

            if (firePreset != null) Destroy(firePreset);
            if (oceanPreset != null) Destroy(oceanPreset);
            if (neonPreset != null) Destroy(neonPreset);
        }

        private sealed class PrefixResolver : IUniTextResolver
        {
            public string prefix;

            public bool TryResolve(ReadOnlyMemory<char> source, out ReadOnlyMemory<char> result)
            {
                result = (prefix + source).AsMemory();
                return true;
            }
        }
    }
}
