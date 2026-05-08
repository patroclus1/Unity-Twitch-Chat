
using System.Collections.Generic;
using UnityEngine;

namespace Lexone.UnityTwitchChat
{
    [System.Serializable]
    public class Chatter
    {
        public Chatter(string login, string channel, string message, IRCTags tags)
        {
            this.login = login;
            this.channel = channel;
            this.message = message;
            this.tags = tags;
        }

        public string login, channel, message;
        public IRCTags tags = null;

        /// <summary>
        /// <para>Returns the RGBA color of the chatter's name (tags.colorHex)</para>
        /// <param name="normalize">Should the name color be normalized, if needed?</param>
        /// </summary>
        public Color GetNameColor(bool normalize = true)
        {
            if (ColorUtility.TryParseHtmlString(tags.colorHex, out Color color))
            {
                if (normalize)
                    return ChatColors.NormalizeColor(color);
                else
                    return color;
            }
            else
                return Color.white; // Parsing failed somehow, return default white
        }

        /// <summary>
        /// <para>Returns true if displayName is "font-safe" 
        /// meaning that it only contains characters: a-z, A-Z, 0-9, _</para>
        /// <para>Useful because most fonts do not support unusual characters</para>
        /// </summary>
        public bool IsDisplayNameFontSafe()
        {
            return ParseHelper.CheckNameRegex(tags.displayName);
        }

        /// <summary>
        /// <para>Returns true if the chatter's message contains a given emote (by emote ID)</para>
        /// <para>You can find emote IDs by using the Twitch API, or 3rd party sites</para>
        /// </summary>
        public bool ContainsEmote(string emoteId) => tags.ContainsEmote(emoteId);

        /// <summary>
        /// Returns true if the chatter has a given badge.
        /// </summary>
        public bool HasBadge(string badgeName) => tags.HasBadge(badgeName);

        /// <summary>
        /// <para>Scans the chat message for third-party emotes (7TV / BTTV / FFZ) loaded by
        /// <see cref="ThirdPartyEmotes"/> and returns each occurrence with its character indices.</para>
        /// <para>Returns an empty list if no <see cref="ThirdPartyEmotes"/> instance is active.</para>
        /// </summary>
        public List<ThirdPartyEmoteOccurrence> GetThirdPartyEmotes()
        {
            return ThirdPartyEmotes.Instance != null
                ? ThirdPartyEmotes.Instance.FindEmotesInMessage(message)
                : new List<ThirdPartyEmoteOccurrence>();
        }

        /// <summary>
        /// Returns true if this chatter's message contains a third-party emote with the given code.
        /// </summary>
        public bool ContainsThirdPartyEmote(string code)
        {
            if (ThirdPartyEmotes.Instance == null || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(message))
                return false;

            int len = message.Length;
            int start = 0;
            for (int i = 0; i <= len; ++i)
            {
                bool atBoundary = (i == len) || message[i] == ' ';
                if (!atBoundary) continue;

                if (i - start == code.Length)
                {
                    bool match = true;
                    for (int k = 0; k < code.Length; ++k)
                    {
                        if (message[start + k] != code[k]) { match = false; break; }
                    }
                    if (match) return true;
                }
                start = i + 1;
            }
            return false;
        }
    }
}