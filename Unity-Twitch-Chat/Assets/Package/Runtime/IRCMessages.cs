using System.Collections.Generic;

namespace Lexone.UnityTwitchChat
{
    /// <summary>
    /// ROOMSTATE info. Sent by Twitch IRC when joining a channel; carries the broadcaster's Twitch user id.
    /// </summary>
    [System.Serializable]
    public struct RoomStateInfo
    {
        public string channel;
        public string channelId;

        public RoomStateInfo(string channel, string channelId)
        {
            this.channel = channel;
            this.channelId = channelId;
        }
    }

    [System.Serializable]
    public struct ChatterEmote
    {
        [System.Serializable]
        public struct Index
        {
            public int startIndex, endIndex;
        }

        public string id;
        public Index[] indexes;
    }

    [System.Serializable]
    public struct ChatterBadge
    {
        public string id;
        public string version;
    }

    [System.Serializable]
    public class IRCTags
    {
        public string colorHex = string.Empty;
        public string displayName = string.Empty;
        public string channelId = string.Empty;
        public string userId = string.Empty;

        public ChatterBadge[] badges = new ChatterBadge[0];
        public ChatterEmote[] emotes = new ChatterEmote[0];

        public bool ContainsEmote(string emoteId)
        {
            foreach (ChatterEmote e in emotes)
            {
                if (e.id == emoteId)
                    return true;
            }

            return false;
        }

        public bool HasBadge(string badge)
        {
            foreach (ChatterBadge b in badges)
            {
                if (b.id == badge)
                    return true;
            }

            return false;
        }
    }
}