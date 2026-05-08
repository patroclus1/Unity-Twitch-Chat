#if UNITY_EDITOR
using System.IO;
using Lexone.UnityTwitchChat;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor utility: builds a complete, ready-to-play chat scene at
///   Assets/CyanChat/Scenes/CyanChatScene.unity
/// with two prefabs at
///   Assets/CyanChat/Prefabs/{TextRunPrefab,ImageRunPrefab}.prefab
///
/// Resulting scene contains:
///   - EventSystem
///   - TwitchIRC          (IRC, anonymous login, channel field empty for the user to fill)
///   - ThirdPartyEmotes   (proxy https://ext.rte.net.ru:8443/ enabled by default)
///   - Canvas + ScrollView (Viewport + Content with Vertical layout) for chat lines
///   - ChatRenderer       (ChatRendererExample wired to Content + both prefabs)
///   - WebPBootstrap      (calls WebPEmoteIntegration.Install when WEBP_INSTALLED is defined)
///
/// Run: Tools / Cyan Chat / Build Example Scene
/// </summary>
public static class CyanChatSceneBuilder
{
    private const string SceneFolder = "Assets/CyanChat/Scenes";
    private const string PrefabFolder = "Assets/CyanChat/Prefabs";
    private const string ScenePath = SceneFolder + "/CyanChatScene.unity";
    private const string TextPrefabPath = PrefabFolder + "/TextRunPrefab.prefab";
    private const string ImagePrefabPath = PrefabFolder + "/ImageRunPrefab.prefab";

    [MenuItem("Tools/Cyan Chat/Build Example Scene")]
    public static void Build()
    {
        EnsureDir(SceneFolder);
        EnsureDir(PrefabFolder);

        // If a scene with this name is already loaded, prompt the user to save before we overwrite.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 1. EventSystem
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        SceneManager.MoveGameObjectToScene(eventSystem, scene);

        // 2. Twitch IRC
        var ircGO = new GameObject("TwitchIRC");
        var irc = ircGO.AddComponent<IRC>();
        var ircSO = new SerializedObject(irc);
        SetBool(ircSO, "useAnonymousLogin", true);
        SetString(ircSO, "channel", "");           // <- the user fills this in
        SetBool(ircSO, "joinChannelOnStart", true);
        ircSO.ApplyModifiedPropertiesWithoutUndo();

        // 3. Third-party emote loader (proxy already on by default)
        var tpeGO = new GameObject("ThirdPartyEmotes");
        tpeGO.AddComponent<ThirdPartyEmotes>();

        // 4. Canvas + ScrollView
        var canvasGO = new GameObject(
            "Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // ChatScroll (anchored to the lower-left half of the screen by default)
        var scrollGO = new GameObject(
            "ChatScroll",
            typeof(RectTransform),
            typeof(Image),
            typeof(ScrollRect));
        scrollGO.transform.SetParent(canvasGO.transform, false);
        var scrollRT = (RectTransform)scrollGO.transform;
        scrollRT.anchorMin = new Vector2(0.02f, 0.05f);
        scrollRT.anchorMax = new Vector2(0.45f, 0.95f);
        scrollRT.offsetMin = Vector2.zero;
        scrollRT.offsetMax = Vector2.zero;
        var scrollBg = scrollGO.GetComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0.35f);
        var scrollRect = scrollGO.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Viewport (RectMask2D clips children; no Image required)
        var viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var viewportRT = (RectTransform)viewportGO.transform;
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = new Vector2(8, 8);
        viewportRT.offsetMax = new Vector2(-8, -8);
        scrollRect.viewport = viewportRT;

        // Content (lower-left aligned vertical layout that grows upward as new lines arrive)
        var contentGO = new GameObject(
            "Content",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = (RectTransform)contentGO.transform;
        contentRT.anchorMin = new Vector2(0f, 0f);
        contentRT.anchorMax = new Vector2(1f, 0f);
        contentRT.pivot = new Vector2(0.5f, 0f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.LowerLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        var fit = contentGO.GetComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        scrollRect.content = contentRT;

        // 5. Prefabs
        GameObject textPrefab = BuildTextPrefab();
        GameObject imagePrefab = BuildImagePrefab();

        // 6. ChatRenderer
        var rendererGO = new GameObject("ChatRenderer");
        var renderer = rendererGO.AddComponent<ChatRendererExample>();
        var rendSO = new SerializedObject(renderer);
        SetReference(rendSO, "messagesParent", contentRT);
        SetReference(rendSO, "textPrefab", textPrefab.GetComponent<TextMeshProUGUI>());
        SetReference(rendSO, "imagePrefab", imagePrefab.GetComponent<RawImage>());
        SetFloat(rendSO, "emoteHeight", 32f);
        SetInt(rendSO, "maxLines", 80);
        SetInt(rendSO, "twitchEmoteSize", 3);
        SetInt(rendSO, "thirdPartyEmoteSize", 4);
        rendSO.ApplyModifiedPropertiesWithoutUndo();

        // 7. WebP bootstrap (no-op without WEBP_INSTALLED, becomes active once you flip the define).
        var webpGO = new GameObject("WebPBootstrap");
        var webp = webpGO.AddComponent<WebPEmoteSetup>();
        webp.chatRenderer = renderer;

        // 8. Save scene and assets. The scene remains loaded after SaveScene, so the
        //    GameObject reference below is still valid.
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = ircGO;
        EditorGUIUtility.PingObject(ircGO);

        EditorUtility.DisplayDialog(
            "Cyan Chat",
            $"Scene built:\n  {ScenePath}\n\nPrefabs:\n  {TextPrefabPath}\n  {ImagePrefabPath}\n\n" +
            "Next step:\n  Select 'TwitchIRC' and fill the 'Channel' field with the Twitch channel name (without '#').",
            "OK");
    }

    // ------------------------- prefab builders -------------------------

    private static GameObject BuildTextPrefab()
    {
        var go = new GameObject("TextRunPrefab", typeof(RectTransform));
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.text = "text";
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.alignment = TextAlignmentOptions.Left;

        // Try to attach the default TMP font if TMP Essentials are imported.
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, TextPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static GameObject BuildImagePrefab()
    {
        var go = new GameObject("ImageRunPrefab", typeof(RectTransform));
        var raw = go.AddComponent<RawImage>();
        raw.raycastTarget = false;
        raw.color = Color.white;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 32;
        le.preferredHeight = 32;
        le.flexibleWidth = 0;
        le.flexibleHeight = 0;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, ImagePrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ------------------------- helpers -------------------------

    private static void EnsureDir(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static void SetBool(SerializedObject so, string name, bool v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.boolValue = v;
    }

    private static void SetString(SerializedObject so, string name, string v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.stringValue = v;
    }

    private static void SetFloat(SerializedObject so, string name, float v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.floatValue = v;
    }

    private static void SetInt(SerializedObject so, string name, int v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.intValue = v;
    }

    private static void SetReference(SerializedObject so, string name, Object v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.objectReferenceValue = v;
    }
}
#endif
