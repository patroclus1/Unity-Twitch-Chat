using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using Lexone.UnityTwitchChat;

/// <summary>
/// Renders incoming chat messages as UGUI lines with mixed text + emote images.
///
/// Setup:
///   1) Twitch IRC component on a GameObject (with anonymous login enabled is fine).
///   2) Third Party Emotes component on a GameObject (loads 7TV / BTTV / FFZ).
///   3) A ScrollView -> Viewport -> Content with a VerticalLayoutGroup; pass the Content as <see cref="messagesParent"/>.
///   4) Two prefabs:
///        textPrefab : a single TextMeshProUGUI element with your font and color.
///        imagePrefab: a single RawImage element (any size; will be resized to <see cref="emoteHeight"/>).
///   5) Drop this script anywhere and connect references in the inspector.
///
/// Two emote sources are merged automatically:
///   - Twitch global / sub / bits emotes from chatter.tags.emotes (indices come pre-tagged from IRC)
///   - 7TV / BTTV / FFZ emotes from chatter.GetThirdPartyEmotes() (matched by token text)
/// </summary>
public class ChatRendererExample : MonoBehaviour
{
    [Header("UI references")]
    [Tooltip("Parent transform that lists chat lines (typically a ScrollView Content with VerticalLayoutGroup).")]
    public RectTransform messagesParent;

    [Tooltip("Prefab for a single TextMeshProUGUI run.")]
    public TextMeshProUGUI textPrefab;

    [Tooltip("Prefab for a single RawImage run (one emote).")]
    public RawImage imagePrefab;

    [Header("Layout")]
    [Tooltip("Height (and width for square emotes) of every emote image, in pixels.")]
    public float emoteHeight = 32f;

    [Tooltip("Maximum number of chat lines kept in the UI; older lines are destroyed.")]
    public int maxLines = 80;

    [Tooltip("Pixels of horizontal spacing between elements within a chat line.")]
    public float spacing = 4f;

    [Header("Emote URL preferences")]
    [Tooltip("Twitch emote image size (1.0 / 2.0 / 3.0).")]
    [Range(1, 3)] public int twitchEmoteSize = 3;

    [Tooltip("3rd-party emote size: 1, 2 or 4 (passed to ThirdPartyEmote.GetUrl).")]
    [Range(1, 4)] public int thirdPartyEmoteSize = 4;

    /// <summary>
    /// File container served by 7TV CDN for STATIC emotes. 7TV does NOT expose .gif for
    /// non-animated emotes (returns 404), so it's not listed here.
    /// Unity's <see cref="UnityWebRequestTexture"/> decodes PNG natively. Webp/Avif require
    /// a custom decoder hooked up via <see cref="emoteTextureLoader"/>.
    /// </summary>
    public enum SevenTVStaticFormat { Png, Webp, Avif }

    /// <summary>
    /// File container served by 7TV CDN for ANIMATED emotes. 7TV does NOT expose .png for
    /// animated emotes (returns 404). GIF is the only animated container Unity can read out
    /// of the box (first frame); for full animation use webp/avif via <see cref="emoteTextureLoader"/>.
    /// </summary>
    public enum SevenTVAnimatedFormat { Gif, Webp, Avif }

    [Header("7TV format selection")]
    [Tooltip("File extension requested for STATIC 7TV emotes. PNG is the safest default — Unity decodes it natively without any extra package.")]
    public SevenTVStaticFormat staticSevenTVFormat = SevenTVStaticFormat.Png;

    [Tooltip("File extension requested for ANIMATED 7TV emotes. GIF stays alive without a WebP decoder (Unity reads the first frame as a static snapshot). Switch to WebP/AVIF and supply emoteTextureLoader if you want real animation.")]
    public SevenTVAnimatedFormat animatedSevenTVFormat = SevenTVAnimatedFormat.Gif;

    /// <summary>
    /// Optional hook to fully override the URL chosen for a given 3rd-party emote, e.g. to
    /// route animated emotes to a custom decoder endpoint. Return null to fall through to
    /// the default rewrite logic.
    /// </summary>
    public Func<ThirdPartyEmote, string /*defaultUrl*/, string /*overrideOrNull*/> emoteUrlResolver;

    /// <summary>
    /// Optional hook to fully override static texture loading. If set, the renderer calls
    /// this instead of <see cref="UnityWebRequestTexture"/>; useful for a custom static
    /// PNG / WebP / AVIF decoder. Pass back the produced <see cref="Texture2D"/> via the
    /// <c>onReady</c> callback (pass null on failure).
    ///
    /// Used only when <see cref="emoteTargetLoader"/> is null.
    /// </summary>
    public Action<string /*url*/, Action<Texture2D> /*onReady*/> emoteTextureLoader;

    /// <summary>
    /// Richer hook: takes full control over a single emote's RawImage. The loader receives:
    ///   - <c>url</c>:   resolved URL (after rewriting / proxying)
    ///   - <c>emote</c>: the matched ThirdPartyEmote, or <c>null</c> for Twitch-native emotes
    ///   - <c>target</c>: the RawImage element to populate (set <c>texture</c>, attach an
    ///                   animator MonoBehaviour for animated WebP, etc.)
    ///   - <c>onSized</c>: optional callback the loader should invoke once it knows the
    ///                    texture's pixel size, so the renderer can adjust LayoutElement
    ///                    width / height to preserve aspect ratio.
    ///
    /// When set, <see cref="emoteTextureLoader"/> and the built-in
    /// <see cref="UnityWebRequestTexture"/> path are skipped for emote runs.
    /// Use this to plug in animated WebP / GIF decoders (see WebPEmoteIntegration).
    /// </summary>
    public Action<string /*url*/, ThirdPartyEmote /*emoteOrNull*/, RawImage /*target*/, Action<int, int> /*onSized*/> emoteTargetLoader;

    // Texture cache; same URL is downloaded only once.
    private readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, List<RawImage>> pendingTargets = new Dictionary<string, List<RawImage>>();

    private void Start()
    {
        if (IRC.Instance == null)
        {
            Debug.LogError("ChatRendererExample: IRC.Instance is null. Add a Twitch IRC component to the scene.");
            return;
        }

        IRC.Instance.OnChatMessage += OnChatMessage;
    }

    private void OnDestroy()
    {
        if (IRC.Instance != null)
            IRC.Instance.OnChatMessage -= OnChatMessage;
    }

    // ----- Token model -----

    private struct EmoteSpan
    {
        public int startIndex;
        public int endIndex;
        public string imageUrl;
        public bool zeroWidth;
        public ThirdPartyEmote thirdPartyEmote; // null for Twitch-native emotes
    }

    private struct Run
    {
        public bool isEmote;
        public string text;       // for text runs
        public string url;        // for emote runs
        public bool zeroWidth;    // for emote runs
        public ThirdPartyEmote thirdPartyEmote; // for emote runs; null for Twitch-native
    }

    // ----- Main entry -----

    private void OnChatMessage(Chatter chatter)
    {
        // Build the chat line container (one HorizontalLayoutGroup per message).
        var line = new GameObject("ChatLine", typeof(RectTransform));
        line.transform.SetParent(messagesParent, false);

        var hlg = line.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.spacing = spacing;

        var fitter = line.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Username header: "<color=#xxx>DisplayName:</color> "
        string nameColor = string.IsNullOrEmpty(chatter.tags.colorHex) ? "#FFFFFF" : chatter.tags.colorHex;
        AddTextRun(line.transform, $"<color={nameColor}><b>{chatter.tags.displayName}</b>:</color>");

        // Body: text + emotes.
        var runs = BuildRuns(chatter);
        foreach (var run in runs)
        {
            if (run.isEmote)
                AddEmoteRun(line.transform, run.url, run.thirdPartyEmote);
            else
                AddTextRun(line.transform, run.text);
        }

        TrimHistory();
    }

    private void TrimHistory()
    {
        while (messagesParent.childCount > maxLines)
            Destroy(messagesParent.GetChild(0).gameObject);
    }

    // ----- Run building: merge Twitch + 3rd-party emotes -----

    private List<Run> BuildRuns(Chatter chatter)
    {
        var spans = new List<EmoteSpan>();
        string msg = chatter.message ?? string.Empty;

        // Twitch native emotes (positions come pre-tagged by Twitch IRC).
        if (chatter.tags?.emotes != null)
        {
            foreach (var e in chatter.tags.emotes)
            {
                if (e.indexes == null) continue;
                foreach (var idx in e.indexes)
                {
                    spans.Add(new EmoteSpan
                    {
                        startIndex = idx.startIndex,
                        endIndex = idx.endIndex,
                        imageUrl = $"https://static-cdn.jtvnw.net/emoticons/v2/{e.id}/default/dark/{twitchEmoteSize}.0",
                        zeroWidth = false,
                        thirdPartyEmote = null,
                    });
                }
            }
        }

        // 3rd-party emotes (matched by us via token scanning).
        var thirdParty = chatter.GetThirdPartyEmotes();
        foreach (var occ in thirdParty)
        {
            string url = occ.emote.GetUrl(thirdPartyEmoteSize);
            url = MaybeRewriteEmoteUrl(occ.emote, url);
            spans.Add(new EmoteSpan
            {
                startIndex = occ.startIndex,
                endIndex = occ.endIndex,
                imageUrl = url,
                zeroWidth = occ.emote.zeroWidth,
                thirdPartyEmote = occ.emote,
            });
        }

        // Sort by start index. Note: chatter.message is treated as char array; emote indices
        // from Twitch are character indices into the original message (matches C# string indexing
        // for ASCII; for messages with surrogate pairs Twitch uses Unicode code points and you'd
        // need to convert. Keeping it simple here.)
        spans.Sort((a, b) => a.startIndex.CompareTo(b.startIndex));

        var runs = new List<Run>();
        int cursor = 0;

        foreach (var s in spans)
        {
            // Skip overlapping span (e.g. Twitch + 3rd party for the same word).
            if (s.startIndex < cursor) continue;
            if (s.startIndex >= msg.Length) break;

            int safeEnd = Mathf.Min(s.endIndex, msg.Length - 1);

            if (s.startIndex > cursor)
                runs.Add(new Run { isEmote = false, text = msg.Substring(cursor, s.startIndex - cursor) });

            runs.Add(new Run
            {
                isEmote = true,
                url = s.imageUrl,
                zeroWidth = s.zeroWidth,
                thirdPartyEmote = s.thirdPartyEmote,
            });
            cursor = safeEnd + 1;
        }

        if (cursor < msg.Length)
            runs.Add(new Run { isEmote = false, text = msg.Substring(cursor) });

        return runs;
    }

    private string MaybeRewriteEmoteUrl(ThirdPartyEmote emote, string url)
    {
        // First, try a sane provider-aware default rewrite.
        string rewritten = url;

        if (emote.provider == EmoteProvider.SevenTV)
        {
            // 7TV CDN: same emote available as 1x.{webp,png,gif,avif}, but with caveats:
            //   static    -> webp / png / avif  (gif returns 404)
            //   animated  -> webp / gif / avif  (png returns 404)
            // Pick the right extension for the emote's animated state.
            string ext = emote.animated
                ? AnimatedExtension(animatedSevenTVFormat)
                : StaticExtension(staticSevenTVFormat);

            int dot = url.LastIndexOf('.');
            int slash = url.LastIndexOf('/');
            if (dot > slash && dot > 0)
                rewritten = url.Substring(0, dot) + ext;
        }
        // BTTV / FFZ: URLs have no extension; CDN returns the right Content-Type
        // based on imageType. Animated BTTV emotes come as GIF, and Unity's
        // UnityWebRequestTexture loads the first frame — no rewrite needed.

        // Then let the user fully override if they want to (e.g. route animated through
        // a custom decoder endpoint).
        if (emoteUrlResolver != null)
        {
            string overriden = emoteUrlResolver(emote, rewritten);
            if (!string.IsNullOrEmpty(overriden))
                return overriden;
        }

        return rewritten;
    }

    private static string StaticExtension(SevenTVStaticFormat f)
    {
        switch (f)
        {
            case SevenTVStaticFormat.Webp: return ".webp";
            case SevenTVStaticFormat.Avif: return ".avif";
            case SevenTVStaticFormat.Png:
            default:                       return ".png";
        }
    }

    private static string AnimatedExtension(SevenTVAnimatedFormat f)
    {
        switch (f)
        {
            case SevenTVAnimatedFormat.Webp: return ".webp";
            case SevenTVAnimatedFormat.Avif: return ".avif";
            case SevenTVAnimatedFormat.Gif:
            default:                         return ".gif";
        }
    }

    // ----- UI run builders -----

    private void AddTextRun(Transform parent, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var t = Instantiate(textPrefab, parent);
        t.richText = true;
        t.text = text;
    }

    private void AddEmoteRun(Transform parent, string url, ThirdPartyEmote emote)
    {
        var img = Instantiate(imagePrefab, parent);
        // Reserve square space; final aspect can be adjusted once we know the texture's dimensions.
        var le = img.gameObject.GetComponent<LayoutElement>() ?? img.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = emoteHeight;
        le.preferredHeight = emoteHeight;

        // Hide until the loader populates it.
        img.color = new Color(1, 1, 1, 0);

        if (emoteTargetLoader != null)
        {
            // Custom loader (e.g. animated WebP integration). Caller is responsible for
            // setting img.texture (or attaching an animator), and may report final pixel
            // size via onSized so that we can update LayoutElement preferred size.
            emoteTargetLoader(url, emote, img, (w, h) => UpdateLayoutAspect(le, img, w, h));
            // Ensure the image becomes visible eventually (the loader is expected to either
            // set img.texture itself or assign a non-null first frame).
            img.color = Color.white;
        }
        else
        {
            LoadTextureInto(url, img, le);
        }
    }

    private void UpdateLayoutAspect(LayoutElement layoutElement, RawImage target, int width, int height)
    {
        if (height <= 0 || width <= 0) return;
        float aspect = (float)width / height;
        if (layoutElement != null)
        {
            layoutElement.preferredHeight = emoteHeight;
            layoutElement.preferredWidth = emoteHeight * aspect;
        }
        else if (target != null)
        {
            target.rectTransform.sizeDelta = new Vector2(emoteHeight * aspect, emoteHeight);
        }
    }

    // ----- Texture cache + async load -----

    private void LoadTextureInto(string url, RawImage target, LayoutElement layoutElement)
    {
        if (string.IsNullOrEmpty(url) || target == null) return;

        if (textureCache.TryGetValue(url, out var cached))
        {
            ApplyTexture(target, layoutElement, cached);
            return;
        }

        if (pendingTargets.TryGetValue(url, out var list))
        {
            list.Add(target);
            return;
        }

        pendingTargets[url] = new List<RawImage> { target };

        if (emoteTextureLoader != null)
        {
            emoteTextureLoader(url, tex => OnTextureReady(url, tex));
        }
        else
        {
            StartCoroutine(LoadTextureCoroutine(url));
        }
    }

    private IEnumerator LoadTextureCoroutine(string url)
    {
        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

            Texture2D tex = null;
#if UNITY_2020_1_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif
            if (ok)
                tex = DownloadHandlerTexture.GetContent(req);
            else
                Debug.LogWarning($"Failed to load emote texture: {url} ({req.error})");

            OnTextureReady(url, tex);
        }
    }

    private void OnTextureReady(string url, Texture2D tex)
    {
        textureCache[url] = tex;

        if (pendingTargets.TryGetValue(url, out var list))
        {
            pendingTargets.Remove(url);
            foreach (var img in list)
            {
                if (img == null) continue;
                var le = img.GetComponent<LayoutElement>();
                ApplyTexture(img, le, tex);
            }
        }
    }

    private void ApplyTexture(RawImage target, LayoutElement layoutElement, Texture2D tex)
    {
        if (target == null) return;

        if (tex == null)
        {
            // Leave invisible on failure.
            return;
        }

        target.texture = tex;
        target.color = Color.white;

        // Preserve emote aspect ratio so wide emotes (e.g. catJAM) don't get squashed.
        float aspect = (float)tex.width / Mathf.Max(1, tex.height);
        if (layoutElement != null)
        {
            layoutElement.preferredHeight = emoteHeight;
            layoutElement.preferredWidth = emoteHeight * aspect;
        }
        else
        {
            target.rectTransform.sizeDelta = new Vector2(emoteHeight * aspect, emoteHeight);
        }
    }
}
