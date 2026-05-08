using System;
using System.Collections.Generic;

namespace Lexone.UnityTwitchChat
{
    /// <summary>
    /// Source/provider of a third-party emote.
    /// </summary>
    public enum EmoteProvider
    {
        SevenTV = 0,
        BTTV = 1,
        FFZ = 2,
    }

    /// <summary>
    /// Scope of an emote: global (loaded for the whole platform) or channel (loaded per broadcaster).
    /// </summary>
    public enum EmoteScope
    {
        Global = 0,
        Channel = 1,
    }

    /// <summary>
    /// A single third-party emote (7TV / BTTV / FFZ).
    /// </summary>
    [Serializable]
    public class ThirdPartyEmote
    {
        /// <summary>Provider-specific emote id.</summary>
        public string id;

        /// <summary>The text token used in chat to reference this emote (e.g. "PauseChamp").</summary>
        public string code;

        /// <summary>Source provider of this emote.</summary>
        public EmoteProvider provider;

        /// <summary>Whether this emote was loaded as a global emote or as a channel emote.</summary>
        public EmoteScope scope;

        /// <summary>1x size CDN URL (small).</summary>
        public string url1x;

        /// <summary>2x size CDN URL (medium). May be empty if provider does not expose this size.</summary>
        public string url2x;

        /// <summary>4x size CDN URL (large/original). Falls back to highest available size.</summary>
        public string url4x;

        /// <summary>True if the emote is animated (gif/webp).</summary>
        public bool animated;

        /// <summary>True if the emote should be rendered as a zero-width overlay on top of the previous emote.</summary>
        public bool zeroWidth;

        /// <summary>
        /// Returns the best URL for the requested size (1, 2 or 4). Falls back to the next
        /// available size if the exact one is not provided by the source.
        /// </summary>
        public string GetUrl(int size = 4)
        {
            if (size <= 1)
                return !string.IsNullOrEmpty(url1x) ? url1x
                     : !string.IsNullOrEmpty(url2x) ? url2x : url4x;

            if (size == 2)
                return !string.IsNullOrEmpty(url2x) ? url2x
                     : !string.IsNullOrEmpty(url4x) ? url4x : url1x;

            return !string.IsNullOrEmpty(url4x) ? url4x
                 : !string.IsNullOrEmpty(url2x) ? url2x : url1x;
        }
    }

    /// <summary>
    /// Occurrence of a third-party emote inside a chat message (text token + character indices).
    /// </summary>
    [Serializable]
    public struct ThirdPartyEmoteOccurrence
    {
        public ThirdPartyEmote emote;
        public int startIndex;
        public int endIndex;

        public ThirdPartyEmoteOccurrence(ThirdPartyEmote emote, int startIndex, int endIndex)
        {
            this.emote = emote;
            this.startIndex = startIndex;
            this.endIndex = endIndex;
        }
    }

    /// <summary>
    /// Per-provider settings (which scopes to load).
    /// </summary>
    [Serializable]
    public class EmoteProviderSettings
    {
        public bool loadGlobal = true;
        public bool loadChannel = true;
    }
}
