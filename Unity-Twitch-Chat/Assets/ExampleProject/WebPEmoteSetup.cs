using UnityEngine;

/// <summary>
/// Tiny bootstrap that installs <c>WebPEmoteIntegration</c> on a <see cref="ChatRendererExample"/>
/// at <c>Start</c>.
///
/// Without the <c>WEBP_INSTALLED</c> scripting define this script is a harmless no-op
/// (the integration class itself compiles to nothing). Once you install netpyoung/unity.webp
/// and add the define, animated 7TV emotes start playing real WebP frames automatically —
/// no further code changes required.
/// </summary>
[AddComponentMenu("Unity Twitch Chat/WebP Bootstrap (optional)")]
public class WebPEmoteSetup : MonoBehaviour
{
    [Tooltip("Renderer to install the WebP loader onto. Required only when WEBP_INSTALLED is defined.")]
    public ChatRendererExample chatRenderer;

    [Tooltip("If true, switches both static and animated 7TV formats to WebP so libwebp actually receives WebP bytes. Disable to keep static 7TV emotes on PNG (Unity-native) and only run libwebp for animated ones.")]
    public bool setSevenTVFormatToWebP = true;

    private void Start()
    {
#if WEBP_INSTALLED
        if (chatRenderer == null)
        {
            Debug.LogWarning("WebPEmoteSetup: chatRenderer is not assigned; nothing to install.");
            return;
        }
        WebPEmoteIntegration.Install(chatRenderer, setSevenTVFormatToWebP);
        Debug.Log("WebPEmoteSetup: WebPEmoteIntegration installed.");
#endif
    }
}
