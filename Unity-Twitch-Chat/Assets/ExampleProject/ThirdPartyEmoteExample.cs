using System.Text;
using UnityEngine;
using Lexone.UnityTwitchChat;

/// <summary>
/// Example that prints out which 7TV / BTTV / FFZ emotes were used in each chat message.
///
/// Setup:
///   1) Add the Twitch IRC component to a GameObject.
///   2) Add the Third Party Emotes component to a GameObject (auto-loads globals on start
///      and listens to ROOMSTATE to load channel emotes).
///   3) Add this script anywhere in the scene.
/// </summary>
public class ThirdPartyEmoteExample : MonoBehaviour
{
    private void Start()
    {
        IRC.Instance.OnChatMessage += OnChatMessage;

        if (ThirdPartyEmotes.Instance != null)
        {
            ThirdPartyEmotes.Instance.OnProviderLoaded += (provider, scope, count) =>
                Debug.Log($"<b>[3rd-party emotes]</b> {provider} {scope} -> {count} emotes added.");

            ThirdPartyEmotes.Instance.OnGlobalsLoaded += () =>
                Debug.Log($"<b>[3rd-party emotes]</b> All globals ready: {ThirdPartyEmotes.Instance.Count} emotes total.");

            ThirdPartyEmotes.Instance.OnChannelLoaded += channelId =>
                Debug.Log($"<b>[3rd-party emotes]</b> Channel emotes for {channelId} ready: {ThirdPartyEmotes.Instance.Count} emotes total.");

            ThirdPartyEmotes.Instance.OnError += (provider, scope, msg) =>
                Debug.LogWarning($"<b>[3rd-party emotes]</b> Error from {provider} {scope}: {msg}");
        }
    }

    private void OnChatMessage(Chatter chatter)
    {
        var occurrences = chatter.GetThirdPartyEmotes();
        if (occurrences.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.Append($"<b>[3rd-party emotes]</b> {chatter.tags.displayName}: ");
        for (int i = 0; i < occurrences.Count; ++i)
        {
            var o = occurrences[i];
            sb.Append($"{o.emote.code}({o.emote.provider}");
            if (o.emote.zeroWidth) sb.Append(", zw");
            if (o.emote.animated) sb.Append(", anim");
            sb.Append(") ");
        }
        Debug.Log(sb.ToString());
    }
}
