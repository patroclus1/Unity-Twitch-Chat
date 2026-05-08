// =============================================================================
//  Optional integration with netpyoung/unity.webp for real animated 7TV emotes.
//
//  Setup:
//    1. Install Unity.WebP via UPM (Window > Package Manager > + > Add from git URL):
//         https://github.com/netpyoung/unity.webp.git?path=unity_project/Assets/unity.webp
//       (or via OpenUPM: com.netpyoung.webp)
//    2. Enable "Allow 'unsafe' code" in Player Settings (this file uses pointer-based
//       calls into libwebp's anim demuxer).
//    3. Add the scripting define symbol "WEBP_INSTALLED" in
//       Project Settings > Player > Other Settings > Scripting Define Symbols.
//    4. In your bootstrap (e.g. on Start of any GameObject):
//
//         WebPEmoteIntegration.Install(myChatRenderer);
//
//       This wires up an `emoteTargetLoader` that:
//         - For animated 7TV WebPs decodes all frames via libwebp, attaches a
//           WebPEmoteAnimator to the RawImage, and plays back at real speed.
//         - For static 7TV WebPs decodes via WebP.Texture2DExt and assigns to
//           RawImage.texture.
//         - For everything else falls back to UnityWebRequestTexture.
//
//    5. (Recommended) Switch the renderer to fetch animated 7TV as .webp instead
//       of .gif so the animation actually exists in the bytes:
//
//         myChatRenderer.animatedSevenTVFormat = ChatRendererExample.SevenTVAnimatedFormat.Webp;
//
//  Without WEBP_INSTALLED defined this file compiles to empty and has zero impact.
// =============================================================================

#if WEBP_INSTALLED

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Lexone.UnityTwitchChat;
using WebP;
using unity.libwebp;
using unity.libwebp.Interop;

public static class WebPEmoteIntegration
{
    /// <summary>
    /// Cached decoded animations, keyed by URL. Frames are shared across animators —
    /// each animator only stores its own playhead.
    /// </summary>
    private struct AnimationData
    {
        public Texture2D[] frames;
        public int[] timestampsMs;
        public int totalDurationMs;
        public int width;
        public int height;
    }

    private static readonly Dictionary<string, Texture2D> StaticCache = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, AnimationData> AnimCache = new Dictionary<string, AnimationData>();
    private static readonly Dictionary<string, List<Action<UnityWebRequest>>> InFlight =
        new Dictionary<string, List<Action<UnityWebRequest>>>();

    /// <summary>
    /// Installs WebP-aware loading on the given <see cref="ChatRendererExample"/>.
    /// Any existing emoteTargetLoader is replaced.
    /// </summary>
    /// <param name="renderer">Target renderer.</param>
    /// <param name="setSevenTVFormatToWebP">
    /// When true (default), also flips both the static and animated 7TV format to WebP
    /// so the libwebp path actually receives WebP bytes. Set to false if you want to
    /// keep static 7TV emotes as PNG (decoded by Unity natively) and only use libwebp
    /// for animated ones — but note that without animatedSevenTVFormat = Webp animated
    /// 7TV emotes will still be requested as .gif and won't animate.
    /// </param>
    public static void Install(ChatRendererExample renderer, bool setSevenTVFormatToWebP = true)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));

        if (setSevenTVFormatToWebP)
        {
            renderer.animatedSevenTVFormat = ChatRendererExample.SevenTVAnimatedFormat.Webp;
            renderer.staticSevenTVFormat = ChatRendererExample.SevenTVStaticFormat.Webp;
        }

        renderer.emoteTargetLoader = (url, emote, target, onSized) =>
            renderer.StartCoroutine(LoadEmote(renderer, url, emote, target, onSized));
    }

    private static IEnumerator LoadEmote(ChatRendererExample owner, string url, ThirdPartyEmote emote, RawImage target, Action<int, int> onSized)
    {
        if (target == null || string.IsNullOrEmpty(url)) yield break;

        bool isAnimatedWebP =
            emote != null
            && emote.animated
            && emote.provider == EmoteProvider.SevenTV
            && url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

        bool isStaticWebP =
            url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            && !isAnimatedWebP;

        // Hot path: cached.
        if (isAnimatedWebP && AnimCache.TryGetValue(url, out var anim))
        {
            AttachAnimator(target, anim);
            onSized?.Invoke(anim.width, anim.height);
            yield break;
        }
        if (StaticCache.TryGetValue(url, out var cachedTex) && cachedTex != null)
        {
            target.texture = cachedTex;
            onSized?.Invoke(cachedTex.width, cachedTex.height);
            yield break;
        }

        if (isAnimatedWebP || isStaticWebP)
        {
            yield return DownloadBytes(url, bytes =>
            {
                if (bytes == null) return;

                if (isAnimatedWebP)
                {
                    var data = DecodeAnimatedWebP(bytes);
                    if (data.frames == null || data.frames.Length == 0)
                    {
                        Debug.LogWarning($"WebPEmoteIntegration: animated decode produced no frames for {url}; falling back to static decode.");
                        var tex = DecodeStaticWebP(bytes);
                        if (tex != null)
                        {
                            StaticCache[url] = tex;
                            target.texture = tex;
                            onSized?.Invoke(tex.width, tex.height);
                        }
                        return;
                    }
                    AnimCache[url] = data;
                    AttachAnimator(target, data);
                    onSized?.Invoke(data.width, data.height);
                }
                else
                {
                    var tex = DecodeStaticWebP(bytes);
                    if (tex != null)
                    {
                        StaticCache[url] = tex;
                        target.texture = tex;
                        onSized?.Invoke(tex.width, tex.height);
                    }
                }
            });
            yield break;
        }

        // Non-WebP URL (BTTV / FFZ / Twitch / 7TV-as-PNG): use Unity's native decoder.
        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif
            if (!ok)
            {
                Debug.LogWarning($"WebPEmoteIntegration: GET {url} failed: {req.error}");
                yield break;
            }

            var tex = DownloadHandlerTexture.GetContent(req);
            StaticCache[url] = tex;
            if (target != null)
            {
                target.texture = tex;
                onSized?.Invoke(tex.width, tex.height);
            }
        }
    }

    private static IEnumerator DownloadBytes(string url, Action<byte[]> onReady)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 15;
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !(req.isNetworkError || req.isHttpError);
#endif
            if (!ok)
            {
                Debug.LogWarning($"WebPEmoteIntegration: GET {url} failed: {req.error}");
                onReady?.Invoke(null);
                yield break;
            }

            onReady?.Invoke(req.downloadHandler.data);
        }
    }

    private static void AttachAnimator(RawImage target, AnimationData data)
    {
        if (target == null) return;
        var animator = target.gameObject.GetComponent<WebPEmoteAnimator>();
        if (animator == null)
            animator = target.gameObject.AddComponent<WebPEmoteAnimator>();
        // Frames are shared across animators (cached per URL), so this animator does not own them.
        animator.Initialize(target, data.frames, data.timestampsMs, data.totalDurationMs, framesAreOwned: false);
    }

    // ------------- libwebp decoding -------------

    private static Texture2D DecodeStaticWebP(byte[] bytes)
    {
        try
        {
            return Texture2DExt.CreateTexture2DFromWebP(bytes, lMipmaps: false, lLinear: false, out Error err);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"WebPEmoteIntegration: static WebP decode threw: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Decodes every frame of an animated WebP into a flat list of (Texture2D, cumulativeTimestampMs).
    /// Mirrors the official sample at netpyoung/unity.webp/Samples/example2/WebpAnimation.cs.
    /// </summary>
    private static unsafe AnimationData DecodeAnimatedWebP(byte[] bytes)
    {
        var result = new AnimationData();
        if (bytes == null || bytes.Length == 0) return result;

        var options = new WebPAnimDecoderOptions
        {
            use_threads = 1,
            color_mode = WEBP_CSP_MODE.MODE_RGBA,
        };
        options.padding[5] = 1;
        NativeLibwebpdemux.WebPAnimDecoderOptionsInit(&options);

        fixed (byte* p = bytes)
        {
            var webpdata = new WebPData
            {
                bytes = p,
                size = new UIntPtr((uint)bytes.Length),
            };

            WebPAnimDecoder* dec = NativeLibwebpdemux.WebPAnimDecoderNew(&webpdata, &options);
            if (dec == null)
            {
                Debug.LogWarning("WebPEmoteIntegration: WebPAnimDecoderNew returned null.");
                return result;
            }

            try
            {
                var info = new WebPAnimInfo();
                if (NativeLibwebpdemux.WebPAnimDecoderGetInfo(dec, &info) == 0 || info.frame_count == 0)
                    return result;

                int width = (int)info.canvas_width;
                int height = (int)info.canvas_height;
                int frameByteCount = width * 4 * height;

                var frames = new Texture2D[info.frame_count];
                var timestamps = new int[info.frame_count];

                IntPtr pp = IntPtr.Zero;
                byte** unmanagedPointer = (byte**)&pp;
                int timestamp = 0;

                for (int i = 0; i < info.frame_count; ++i)
                {
                    if (NativeLibwebpdemux.WebPAnimDecoderGetNext(dec, unmanagedPointer, &timestamp) == 0)
                    {
                        Debug.LogWarning("WebPEmoteIntegration: WebPAnimDecoderGetNext failed at frame " + i);
                        break;
                    }

                    var tex = Texture2DExt.CreateWebpTexture2D(width, height, isUseMipmap: false, isLinear: false);
                    tex.LoadRawTextureData(pp, frameByteCount);

                    // libwebp returns frames upside-down vs Unity's coordinate space.
                    SoftwareFlipY(tex, width, height);

                    tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                    frames[i] = tex;
                    timestamps[i] = timestamp;
                }

                int total = timestamps.Length > 0 ? timestamps[timestamps.Length - 1] : 0;
                result.frames = frames;
                result.timestampsMs = timestamps;
                result.totalDurationMs = total;
                result.width = width;
                result.height = height;
            }
            finally
            {
                NativeLibwebpdemux.WebPAnimDecoderReset(dec);
                NativeLibwebpdemux.WebPAnimDecoderDelete(dec);
            }
        }

        return result;
    }

    private static void SoftwareFlipY(Texture2D tex, int width, int height)
    {
        var pixels = tex.GetPixels();
        var flipped = new Color[pixels.Length];
        for (int y = 0; y < height; ++y)
            Array.Copy(pixels, y * width, flipped, (height - y - 1) * width, width);
        tex.SetPixels(flipped);
    }

    /// <summary>
    /// Drops every cached texture / frame list (call on scene change or when emote set is rebuilt).
    /// </summary>
    public static void ClearCache()
    {
        foreach (var t in StaticCache.Values)
            if (t != null) UnityEngine.Object.Destroy(t);
        StaticCache.Clear();

        foreach (var a in AnimCache.Values)
        {
            if (a.frames == null) continue;
            foreach (var t in a.frames)
                if (t != null) UnityEngine.Object.Destroy(t);
        }
        AnimCache.Clear();
    }
}

#endif // WEBP_INSTALLED
