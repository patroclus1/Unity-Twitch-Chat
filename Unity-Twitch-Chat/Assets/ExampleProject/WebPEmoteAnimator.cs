using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a pre-decoded list of frames on a <see cref="RawImage"/>, looping forever.
/// Intentionally has no dependency on libwebp — pass any pre-decoded <see cref="Texture2D"/> array
/// (animated WebP, GIF, custom decoder, anything) and the matching cumulative timestamps in ms.
///
/// Pairs with <c>WebPEmoteIntegration</c>, but can also be used standalone.
/// </summary>
[DisallowMultipleComponent]
public class WebPEmoteAnimator : MonoBehaviour
{
    [Tooltip("Target RawImage whose 'texture' is swapped each frame. If null, this MonoBehaviour's GetComponent<RawImage>() is used.")]
    public RawImage target;

    [Tooltip("Pre-decoded animation frames. Indexed in the same order as timestampsMs.")]
    public Texture2D[] frames;

    [Tooltip("Absolute end-of-frame timestamps in milliseconds (as returned by libwebp's WebPAnimDecoderGetNext).")]
    public int[] timestampsMs;

    [Tooltip("Total animation duration in milliseconds. If zero or negative, animator stays on the first frame.")]
    public int totalDurationMs;

    [Tooltip("If true, frames are owned by this animator and destroyed in OnDestroy. Set to false when frames are shared across multiple animators.")]
    public bool ownsFrames = false;

    [Tooltip("Playback speed multiplier (1 = real time).")]
    public float speed = 1f;

    private float playheadSec;
    private int currentFrame = -1;

    private void Reset() => target = GetComponent<RawImage>();

    public void Initialize(RawImage rawImage, Texture2D[] decodedFrames, int[] cumulativeTimestampsMs, int totalMs, bool framesAreOwned)
    {
        target = rawImage;
        frames = decodedFrames;
        timestampsMs = cumulativeTimestampsMs;
        totalDurationMs = totalMs;
        ownsFrames = framesAreOwned;
        playheadSec = 0f;
        currentFrame = -1;

        if (target != null && frames != null && frames.Length > 0)
        {
            target.texture = frames[0];
            currentFrame = 0;
        }
    }

    private void Update()
    {
        if (target == null || frames == null || frames.Length == 0) return;
        if (totalDurationMs <= 0) return;

        playheadSec += Time.unscaledDeltaTime * speed;
        int elapsedMs = (int)((playheadSec * 1000f) % totalDurationMs);

        // Pick the frame whose end-timestamp is the smallest one >= elapsedMs.
        int idx = 0;
        for (int i = 0; i < timestampsMs.Length; ++i)
        {
            if (timestampsMs[i] > elapsedMs)
            {
                idx = i;
                break;
            }
            idx = i;
        }

        if (idx != currentFrame)
        {
            target.texture = frames[idx];
            currentFrame = idx;
        }
    }

    private void OnDestroy()
    {
        if (!ownsFrames || frames == null) return;
        foreach (var t in frames)
            if (t != null) Destroy(t);
        frames = null;
    }
}
