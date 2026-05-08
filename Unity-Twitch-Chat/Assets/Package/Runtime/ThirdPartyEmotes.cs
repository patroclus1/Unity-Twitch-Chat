using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Lexone.UnityTwitchChat
{
    /// <summary>
    /// Loads global and channel emotes from 7TV / BTTV / FFZ and exposes a unified
    /// dictionary of emote codes -> ThirdPartyEmote.
    ///
    /// Add this component to a GameObject (alongside or separate from the IRC component).
    /// You can either:
    ///   - leave <see cref="autoSubscribeToIRC"/> on, in which case channel emotes
    ///     will be loaded automatically when the IRC client receives the ROOMSTATE
    ///     message for the joined channel; or
    ///   - call <see cref="LoadGlobalEmotes"/> / <see cref="LoadChannelEmotes"/> manually.
    /// </summary>
    [AddComponentMenu("Unity Twitch Chat/Third Party Emotes")]
    public class ThirdPartyEmotes : MonoBehaviour
    {
        public static ThirdPartyEmotes Instance { get; private set; }

        // -------- 7TV --------
        [Header("7TV")]
        [SerializeField] public EmoteProviderSettings sevenTV = new EmoteProviderSettings();

        [Tooltip("Base URL for the 7TV API.")]
        [SerializeField] public string sevenTVApiBase = "https://7tv.io/v3";

        [Tooltip("Base URL for the 7TV CDN. The emote id is appended to this.")]
        [SerializeField] public string sevenTVCdnBase = "https://cdn.7tv.app/emote";

        // -------- BTTV --------
        [Header("BTTV")]
        [SerializeField] public EmoteProviderSettings bttv = new EmoteProviderSettings();

        [Tooltip("Base URL for the BTTV API.")]
        [SerializeField] public string bttvApiBase = "https://api.betterttv.net/3";

        [Tooltip("Base URL for the BTTV CDN.")]
        [SerializeField] public string bttvCdnBase = "https://cdn.betterttv.net/emote";

        // -------- FFZ (via BTTV cache) --------
        [Header("FrankerFaceZ (via BTTV cache)")]
        [SerializeField] public EmoteProviderSettings ffz = new EmoteProviderSettings();

        [Tooltip("FFZ emotes are loaded through BTTV's cached endpoint:\nhttps://api.betterttv.net/3/cached/frankerfacez/...")]
        [SerializeField] public string ffzApiBase = "https://api.betterttv.net/3/cached/frankerfacez";

        // -------- Proxy --------
        [Header("Proxy (optional)")]
        [Tooltip("If true, all API and (optionally) CDN URLs will be prefixed with proxyPrefix.\nUseful when direct access to 7tv/bttv/ffz is restricted.")]
        [SerializeField] public bool useProxy = true;

        [Tooltip("URL prefix prepended to the original URL when proxy is enabled.\nExample: \"https://my-proxy.example/\" results in \"https://my-proxy.example/https://7tv.io/...\"\nDefault is the rte.net.ru proxy used by the rest of the project.")]
        [SerializeField] public string proxyPrefix = "https://ext.rte.net.ru:8443/";

        [Tooltip("If true, emote CDN image URLs are also rewritten through the proxy. If false, only API requests are proxied.")]
        [SerializeField] public bool proxyEmoteCdn = true;

        // -------- General --------
        [Header("General")]
        [Tooltip("Automatically load global emotes on Start.")]
        [SerializeField] public bool loadGlobalOnStart = true;

        [Tooltip("If true, listens to IRC.OnRoomStateReceived and loads channel emotes automatically when a channel is joined.")]
        [SerializeField] public bool autoSubscribeToIRC = true;

        [Tooltip("Maximum number of retries per HTTP request.")]
        [SerializeField, Range(1, 10)] public int maxRetries = 3;

        [Tooltip("Delay (seconds) between retries.")]
        [SerializeField] public float retryDelaySeconds = 2f;

        [Tooltip("Per-request HTTP timeout, in seconds.")]
        [SerializeField, Range(1, 60)] public int requestTimeoutSeconds = 15;

        [Tooltip("If true, the component will be set to DontDestroyOnLoad and duplicate instances destroyed.")]
        [SerializeField] public bool dontDestroyOnLoad = true;

        [Tooltip("If true, network requests and progress will be logged to the console.")]
        [SerializeField] public bool showDebug = true;

        [Tooltip("Override emote when the same code is loaded from multiple providers.\nResolution order: SevenTV > BTTV > FFZ for global; channel emotes always override globals.")]
        [SerializeField] public bool channelOverridesGlobal = true;

        // -------- State --------
        private readonly Dictionary<string, ThirdPartyEmote> emotes = new Dictionary<string, ThirdPartyEmote>();
        private readonly HashSet<string> loadedChannelIds = new HashSet<string>();
        private bool globalsLoaded = false;

        /// <summary>Read-only view over all currently loaded emotes (code -> emote).</summary>
        public IReadOnlyDictionary<string, ThirdPartyEmote> Emotes => emotes;

        /// <summary>Number of currently loaded emotes across all providers and scopes.</summary>
        public int Count => emotes.Count;

        /// <summary>True if global emote loading has finished (regardless of how many were loaded).</summary>
        public bool GlobalsLoaded => globalsLoaded;

        /// <summary>True if channel emotes for the given Twitch channel id have been loaded.</summary>
        public bool IsChannelLoaded(string channelId) =>
            !string.IsNullOrEmpty(channelId) && loadedChannelIds.Contains(channelId);

        // Events
        /// <summary>Invoked when a single provider finishes loading a scope (global or channel).</summary>
        public event Action<EmoteProvider, EmoteScope, int /*added*/> OnProviderLoaded;
        /// <summary>Invoked when the entire global load (across all enabled providers) completes.</summary>
        public event Action OnGlobalsLoaded;
        /// <summary>Invoked when the entire channel load (across all enabled providers) completes for a given channel id.</summary>
        public event Action<string /*channelId*/> OnChannelLoaded;
        /// <summary>Invoked on any non-fatal error (failed HTTP request, JSON parse error, etc.).</summary>
        public event Action<EmoteProvider, EmoteScope, string /*error*/> OnError;

        // BTTV emotes that are conventionally rendered as zero-width overlays.
        // Source: https://github.com/night/betterttv/blob/master/src/util/emotes.js (mirrored across community projects).
        private static readonly HashSet<string> BttvZeroWidthIds = new HashSet<string>
        {
            "5e76d338d6581c3724c0f0b2", // cvHazmat
            "5e76d399d6581c3724c0f0b8", // cvMask
            "567b5b520e984428652809b6", // SoSnowy
            "58487cc6f52be01a7ee5f79d", // IceCold
            "5849c9a4f52be01a7ee5f205", // SantaHat
            "567b5c080e984428652809ba", // TopHat
            "567b5dc00e984428652809bd", // ReinDeer
            "567b5e110e984428652809be", // CandyCane
            "5e76d419d6581c3724c0f0bc", // weebSquad (zero-width)
        };

        // -------- Unity lifecycle --------
        private void Awake()
        {
            if (Instance && Instance != this)
            {
                if (dontDestroyOnLoad)
                {
                    gameObject.SetActive(false);
                    Destroy(gameObject);
                }
                return;
            }

            Instance = this;
            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (autoSubscribeToIRC && IRC.Instance != null)
                IRC.Instance.OnRoomStateReceived += HandleRoomState;

            if (loadGlobalOnStart)
                StartCoroutine(LoadGlobalEmotes());
        }

        private void OnDestroy()
        {
            if (autoSubscribeToIRC && IRC.Instance != null)
                IRC.Instance.OnRoomStateReceived -= HandleRoomState;

            if (Instance == this)
                Instance = null;
        }

        private void HandleRoomState(RoomStateInfo info)
        {
            if (string.IsNullOrEmpty(info.channelId))
                return;

            if (loadedChannelIds.Contains(info.channelId))
                return;

            StartCoroutine(LoadChannelEmotes(info.channelId));
        }

        // -------- Public API --------

        /// <summary>
        /// Removes every loaded emote from the cache (both global and channel emotes).
        /// </summary>
        public void Clear()
        {
            emotes.Clear();
            loadedChannelIds.Clear();
            globalsLoaded = false;
        }

        /// <summary>
        /// Try to look up a third-party emote by its chat token (e.g. "PauseChamp").
        /// </summary>
        public bool TryGetEmote(string code, out ThirdPartyEmote emote) =>
            emotes.TryGetValue(code, out emote);

        /// <summary>
        /// Returns the third-party emote for the given chat token, or null if not loaded.
        /// </summary>
        public ThirdPartyEmote GetEmote(string code) =>
            emotes.TryGetValue(code, out var e) ? e : null;

        /// <summary>
        /// Scans the chat message and returns every occurrence of a known third-party emote.
        /// Indices are character indices into the message string.
        /// </summary>
        public List<ThirdPartyEmoteOccurrence> FindEmotesInMessage(string message)
        {
            var result = new List<ThirdPartyEmoteOccurrence>();
            if (string.IsNullOrEmpty(message) || emotes.Count == 0)
                return result;

            int start = 0;
            int len = message.Length;

            for (int i = 0; i <= len; ++i)
            {
                bool atBoundary = (i == len) || message[i] == ' ';
                if (!atBoundary) continue;

                if (i > start)
                {
                    string token = message.Substring(start, i - start);
                    if (emotes.TryGetValue(token, out var emote))
                        result.Add(new ThirdPartyEmoteOccurrence(emote, start, i - 1));
                }
                start = i + 1;
            }

            return result;
        }

        /// <summary>
        /// Loads global emotes from every enabled provider. Safe to call multiple times.
        /// </summary>
        public IEnumerator LoadGlobalEmotes()
        {
            if (sevenTV.loadGlobal) yield return LoadSevenTVGlobal();
            if (bttv.loadGlobal)    yield return LoadBttvGlobal();
            if (ffz.loadGlobal)     yield return LoadFfzGlobal();

            globalsLoaded = true;
            OnGlobalsLoaded?.Invoke();

            if (showDebug)
                Debug.Log($"{Tags.alert} Third-party globals loaded. Total emotes cached: {emotes.Count}");
        }

        /// <summary>
        /// Loads channel emotes for the given Twitch channel id (numeric user id of the broadcaster)
        /// from every enabled provider. Safe to call multiple times for different channels.
        /// </summary>
        public IEnumerator LoadChannelEmotes(string twitchChannelId)
        {
            if (string.IsNullOrEmpty(twitchChannelId))
            {
                Debug.LogWarning($"{Tags.alert} Cannot load third-party channel emotes: channelId is empty.");
                yield break;
            }

            if (sevenTV.loadChannel) yield return LoadSevenTVChannel(twitchChannelId);
            if (bttv.loadChannel)    yield return LoadBttvChannel(twitchChannelId);
            if (ffz.loadChannel)     yield return LoadFfzChannel(twitchChannelId);

            loadedChannelIds.Add(twitchChannelId);
            OnChannelLoaded?.Invoke(twitchChannelId);

            if (showDebug)
                Debug.Log($"{Tags.alert} Third-party channel emotes loaded for {twitchChannelId}. Total emotes cached: {emotes.Count}");
        }

        // -------- 7TV --------

        private IEnumerator LoadSevenTVGlobal()
        {
            string url = $"{sevenTVApiBase}/emote-sets/global";
            yield return FetchJson(url, EmoteProvider.SevenTV, EmoteScope.Global, json =>
            {
                int added = ParseSevenTVEmoteSet(json, EmoteScope.Global);
                OnProviderLoaded?.Invoke(EmoteProvider.SevenTV, EmoteScope.Global, added);
            });
        }

        private IEnumerator LoadSevenTVChannel(string twitchChannelId)
        {
            string userUrl = $"{sevenTVApiBase}/users/twitch/{UnityWebRequest.EscapeURL(twitchChannelId)}";

            string emoteSetId = null;
            int inlineAdded = -1; // -1 means "no inline emote set seen"

            yield return FetchJson(userUrl, EmoteProvider.SevenTV, EmoteScope.Channel, json =>
            {
                var root = JsonNode.Parse(json);
                var emoteSet = root["emote_set"];
                emoteSetId = emoteSet["id"].AsString;

                // The /users/twitch endpoint usually returns the full emote set inline; if so, parse it
                // directly and avoid a second request.
                if (emoteSet["emotes"].IsArray && emoteSet["emotes"].Count > 0)
                    inlineAdded = ParseSevenTVEmoteSetNode(emoteSet, EmoteScope.Channel);
            });

            if (inlineAdded >= 0)
            {
                OnProviderLoaded?.Invoke(EmoteProvider.SevenTV, EmoteScope.Channel, inlineAdded);
                yield break;
            }

            if (string.IsNullOrEmpty(emoteSetId))
            {
                if (showDebug)
                    Debug.Log($"{Tags.alert} 7TV: channel {twitchChannelId} has no active emote set.");
                OnProviderLoaded?.Invoke(EmoteProvider.SevenTV, EmoteScope.Channel, 0);
                yield break;
            }

            string setUrl = $"{sevenTVApiBase}/emote-sets/{UnityWebRequest.EscapeURL(emoteSetId)}";
            yield return FetchJson(setUrl, EmoteProvider.SevenTV, EmoteScope.Channel, json =>
            {
                int added = ParseSevenTVEmoteSet(json, EmoteScope.Channel);
                OnProviderLoaded?.Invoke(EmoteProvider.SevenTV, EmoteScope.Channel, added);
            });
        }

        private int ParseSevenTVEmoteSet(string json, EmoteScope scope)
        {
            try
            {
                var root = JsonNode.Parse(json);
                return ParseSevenTVEmoteSetNode(root, scope);
            }
            catch (Exception ex)
            {
                ReportError(EmoteProvider.SevenTV, scope, $"Failed to parse response: {ex.Message}");
                return 0;
            }
        }

        private int ParseSevenTVEmoteSetNode(JsonNode emoteSet, EmoteScope scope)
        {
            int added = 0;
            var arr = emoteSet["emotes"];
            if (!arr.IsArray) return 0;

            foreach (var item in arr.Items)
            {
                string id = item["id"].AsString;
                string code = item["name"].AsString;
                int flags = item["flags"].AsInt;

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(code))
                    continue;

                bool animated = item["data"]["animated"].AsBool;
                bool zeroWidth = (flags & 1) != 0; // ZERO_WIDTH active emote flag

                string baseUrl = $"{sevenTVCdnBase}/{id}";
                var emote = new ThirdPartyEmote
                {
                    id = id,
                    code = code,
                    provider = EmoteProvider.SevenTV,
                    scope = scope,
                    animated = animated,
                    zeroWidth = zeroWidth,
                    url1x = ProxifyCdn($"{baseUrl}/1x.webp"),
                    url2x = ProxifyCdn($"{baseUrl}/2x.webp"),
                    url4x = ProxifyCdn($"{baseUrl}/4x.webp"),
                };

                if (StoreEmote(emote))
                    added++;
            }
            return added;
        }

        // -------- BTTV --------

        private IEnumerator LoadBttvGlobal()
        {
            string url = $"{bttvApiBase}/cached/emotes/global";
            yield return FetchJson(url, EmoteProvider.BTTV, EmoteScope.Global, json =>
            {
                int added = ParseBttvEmoteArray(json, EmoteScope.Global);
                OnProviderLoaded?.Invoke(EmoteProvider.BTTV, EmoteScope.Global, added);
            });
        }

        private IEnumerator LoadBttvChannel(string twitchChannelId)
        {
            string url = $"{bttvApiBase}/cached/users/twitch/{UnityWebRequest.EscapeURL(twitchChannelId)}";
            yield return FetchJson(url, EmoteProvider.BTTV, EmoteScope.Channel, json =>
            {
                int added = 0;
                try
                {
                    var root = JsonNode.Parse(json);
                    added += ParseBttvArrayNode(root["channelEmotes"], EmoteScope.Channel);
                    added += ParseBttvArrayNode(root["sharedEmotes"], EmoteScope.Channel);
                }
                catch (Exception ex)
                {
                    ReportError(EmoteProvider.BTTV, EmoteScope.Channel, $"Failed to parse response: {ex.Message}");
                }
                OnProviderLoaded?.Invoke(EmoteProvider.BTTV, EmoteScope.Channel, added);
            });
        }

        private int ParseBttvEmoteArray(string json, EmoteScope scope)
        {
            try
            {
                var root = JsonNode.Parse(json);
                return ParseBttvArrayNode(root, scope);
            }
            catch (Exception ex)
            {
                ReportError(EmoteProvider.BTTV, scope, $"Failed to parse response: {ex.Message}");
                return 0;
            }
        }

        private int ParseBttvArrayNode(JsonNode arr, EmoteScope scope)
        {
            if (arr == null || !arr.IsArray) return 0;

            int added = 0;
            foreach (var item in arr.Items)
            {
                string id = item["id"].AsString;
                string code = item["code"].AsString;

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(code))
                    continue;

                bool animated = item["animated"].AsBool;
                bool zeroWidth = BttvZeroWidthIds.Contains(id);

                string baseUrl = $"{bttvCdnBase}/{id}";
                var emote = new ThirdPartyEmote
                {
                    id = id,
                    code = code,
                    provider = EmoteProvider.BTTV,
                    scope = scope,
                    animated = animated,
                    zeroWidth = zeroWidth,
                    url1x = ProxifyCdn($"{baseUrl}/1x"),
                    url2x = ProxifyCdn($"{baseUrl}/2x"),
                    url4x = ProxifyCdn($"{baseUrl}/3x"), // BTTV's largest size is 3x
                };

                if (StoreEmote(emote))
                    added++;
            }
            return added;
        }

        // -------- FFZ (via BTTV cache) --------

        private IEnumerator LoadFfzGlobal()
        {
            string url = $"{ffzApiBase}/emotes/global";
            yield return FetchJson(url, EmoteProvider.FFZ, EmoteScope.Global, json =>
            {
                int added = ParseFfzEmoteArray(json, EmoteScope.Global);
                OnProviderLoaded?.Invoke(EmoteProvider.FFZ, EmoteScope.Global, added);
            });
        }

        private IEnumerator LoadFfzChannel(string twitchChannelId)
        {
            string url = $"{ffzApiBase}/users/twitch/{UnityWebRequest.EscapeURL(twitchChannelId)}";
            yield return FetchJson(url, EmoteProvider.FFZ, EmoteScope.Channel, json =>
            {
                int added = ParseFfzEmoteArray(json, EmoteScope.Channel);
                OnProviderLoaded?.Invoke(EmoteProvider.FFZ, EmoteScope.Channel, added);
            });
        }

        private int ParseFfzEmoteArray(string json, EmoteScope scope)
        {
            try
            {
                var root = JsonNode.Parse(json);
                if (!root.IsArray) return 0;

                int added = 0;
                foreach (var item in root.Items)
                {
                    string id = item["id"].AsString;
                    if (string.IsNullOrEmpty(id) && item["id"].Type == JsonNode.NodeType.Number)
                        id = item["id"].AsLong.ToString();

                    string code = item["code"].AsString;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(code))
                        continue;

                    bool animated = item["animated"].AsBool;
                    var images = item["images"];

                    string url1 = images["1x"].AsString;
                    string url2 = images["2x"].AsString;
                    string url4 = images["4x"].AsString;

                    var emote = new ThirdPartyEmote
                    {
                        id = id,
                        code = code,
                        provider = EmoteProvider.FFZ,
                        scope = scope,
                        animated = animated,
                        zeroWidth = false,
                        url1x = ProxifyCdn(url1),
                        url2x = ProxifyCdn(url2),
                        url4x = ProxifyCdn(url4),
                    };

                    if (StoreEmote(emote))
                        added++;
                }
                return added;
            }
            catch (Exception ex)
            {
                ReportError(EmoteProvider.FFZ, scope, $"Failed to parse response: {ex.Message}");
                return 0;
            }
        }

        // -------- Storage / proxy / HTTP --------

        private bool StoreEmote(ThirdPartyEmote emote)
        {
            if (string.IsNullOrEmpty(emote.code))
                return false;

            if (emotes.TryGetValue(emote.code, out var existing))
            {
                bool replace =
                    // Channel emotes always override global emotes when channelOverridesGlobal is true.
                    (channelOverridesGlobal && emote.scope == EmoteScope.Channel && existing.scope == EmoteScope.Global)
                    // Within the same scope, prefer 7TV > BTTV > FFZ.
                    || (emote.scope == existing.scope && (int)emote.provider < (int)existing.provider);

                if (!replace)
                    return false;
            }

            emotes[emote.code] = emote;
            return true;
        }

        private string ProxifyApi(string url)
        {
            if (!useProxy || string.IsNullOrEmpty(proxyPrefix))
                return url;
            return proxyPrefix + url;
        }

        private string ProxifyCdn(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;
            if (!useProxy || !proxyEmoteCdn || string.IsNullOrEmpty(proxyPrefix))
                return url;
            return proxyPrefix + url;
        }

        private void ReportError(EmoteProvider provider, EmoteScope scope, string message)
        {
            if (showDebug)
                Debug.LogWarning($"{Tags.alert} [{provider} / {scope}] {message}");
            OnError?.Invoke(provider, scope, message);
        }

        private IEnumerator FetchJson(string apiUrl, EmoteProvider provider, EmoteScope scope, Action<string> onSuccess)
        {
            string finalUrl = ProxifyApi(apiUrl);

            if (showDebug)
                Debug.Log($"{Tags.alert} [{provider} / {scope}] GET {finalUrl}");

            for (int attempt = 1; attempt <= maxRetries; ++attempt)
            {
                using (var req = UnityWebRequest.Get(finalUrl))
                {
                    req.timeout = requestTimeoutSeconds;
                    yield return req.SendWebRequest();

                    bool failed =
#if UNITY_2020_1_OR_NEWER
                        req.result != UnityWebRequest.Result.Success;
#else
                        req.isNetworkError || req.isHttpError;
#endif

                    if (!failed)
                    {
                        try
                        {
                            onSuccess?.Invoke(req.downloadHandler.text);
                        }
                        catch (Exception ex)
                        {
                            ReportError(provider, scope, $"Handler threw: {ex.Message}");
                        }
                        yield break;
                    }

                    // 404 is a legitimate "no entry" response (e.g. channel never registered with this provider).
                    // Don't treat it as an error and don't retry.
                    if (req.responseCode == 404)
                    {
                        if (showDebug)
                            Debug.Log($"{Tags.alert} [{provider} / {scope}] not found (HTTP 404)");
                        yield break;
                    }

                    string err = $"Attempt {attempt}/{maxRetries} failed: {req.error} (HTTP {req.responseCode})";
                    if (attempt == maxRetries)
                    {
                        ReportError(provider, scope, err);
                        yield break;
                    }

                    if (showDebug)
                        Debug.LogWarning($"{Tags.alert} [{provider} / {scope}] {err}");
                }

                yield return new WaitForSecondsRealtime(retryDelaySeconds);
            }
        }
    }
}
