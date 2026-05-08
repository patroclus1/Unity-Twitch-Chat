# Unity Twitch Chat

This is a lightweight and efficient [Twitch.tv IRC](https://dev.twitch.tv/docs/irc/) client for Unity.

In short, this allows you to integrate Twitch Chat to your Unity projects.<br>The primary goal is to be able to read and send chat messages as efficiently as possible.



## Chat message example
<img src="https://user-images.githubusercontent.com/18125997/210407885-b2c49e1e-4537-41e9-ad5b-2d8c4c8f1077.png">

### Supported features
- Reading and sending chat messages
- Parsing Twitch emotes and badges
- Optional anonymous login
- Automatic ratelimit checks
- Name color normalization (similar to native Twitch chat)
- Third-party emote loading (7TV, BTTV, FFZ — global and per-channel) with optional proxy support

### Unsupported features
- Special messages (whispers, sub/resub, raids, first time viewers, etc)
- Bits cheering, channel points, predictions, etc
- Moderation (ban, timeout, etc)
- and more...

### Other limitations
- WebGL builds are not supported

## Installation

- Open Unity Package Manager <i>(Window -> Package Manager)</i>
- Click the `+` button in the top left corner
- Select `Add package from git URL...`
- Copy and paste the following URL and finish by clicking `Add`<br>
```
https://github.com/lexonegit/Unity-Twitch-Chat.git?path=/Unity-Twitch-Chat/Assets/Package
```


## Quick start
1. Install the Unity package (see above)
2. Create a new empty GameObject and add the `Twitch IRC` component.
3. In the inspector, set your Twitch details (OAuth, username, channel) 
    - You can generate an OAuth token at https://twitchapps.com/tmi/
    - Alternatively you can enable `Use Anonymous Login` to use without OAuth
4. Make sure `Connect IRC On Start` and `Join Channel On Start` are enabled and press play – You should now see JOIN messages, etc. in the console.
5. Create a new script that has a listener for the `IRC.OnChatMessage` event.
    - See <a href="https://github.com/lexonegit/Unity-Twitch-Chat/blob/main/Unity-Twitch-Chat/Assets/ExampleProject/ListenerExample.cs">ListenerExample.cs</a> for reference.
    -  The listener will receive `Chatter` objects which contain information about each chat message, such as the chatter name, message, emotes, etc...

<i>Having issues? Check out the included ExampleProject for a better understanding.</i>

## Example project
Spawn chatters as jumping boxes. Box color is based on their primary badge.

<img src="https://user-images.githubusercontent.com/18125997/210427322-27d2231c-5123-4785-997e-53838cfc8972.gif">

## API documentation

### IRC.cs
- `void` **Connect()** - Connects to Twitch IRC
- `void` **Disconnect()** - Disconnects from Twitch IRC
- `void` **SendChatMessage(string message)** - Sends a chat message to the channel
- `void` **JoinChannel(string channel)** - Join a Twitch channel
- `void` **LeaveChannel(string channel)** - Leave a Twitch channel
- `void` **Ping()** - Sends a PING message to the Twitch IRC server
- `event` **OnChatMessage** - Event that is invoked when a chat message is received
- `event` **OnConnectionAlert** - Event that is invoked when a connection alert is received
- `IRCTags` **ClientUserTags** - Returns the tags of the client user (badges, name color, etc)

### Chatter.cs
- `Color` **GetNameColor()** - Returns the color of the chatter's name
- `bool` **IsDisplayNameFontSafe()** - Returns true if displayName is "font-safe" meaning that it only contains characters: a-z, A-Z, 0-9, _
- `bool` **ContainsEmote(string emoteId)** - Returns true if the chatter's message contains the specified emote (by id)
- `bool` **HasBadge(string badgeName)** - Returns true if the chatter has the specified badge
- `List<ThirdPartyEmoteOccurrence>` **GetThirdPartyEmotes()** - Returns every 7TV / BTTV / FFZ emote occurrence in the message (requires a `ThirdPartyEmotes` component in the scene)
- `bool` **ContainsThirdPartyEmote(string code)** - Returns true if the chatter's message contains a third-party emote with the given code

## Third-party emotes (7TV / BTTV / FFZ)

Add the `Third Party Emotes` component to a GameObject (alongside `Twitch IRC` or on its own).

By default it will:
- Load global emotes from 7TV, BTTV and FFZ on `Start`.
- Listen for `IRC.OnRoomStateReceived` and automatically load the joined channel's emotes once Twitch reports the channel's user id.

Disable any provider/scope from the inspector if you don't want to fetch a particular set.

### Proxy support

When `Use Proxy` is enabled, every API request (and optionally every emote CDN URL) is rewritten by prepending `Proxy Prefix`. Example:

```
Proxy Prefix:    https://ext.rte.net.ru:8443/
Original URL:    https://7tv.io/v3/emote-sets/global
Resolved URL:    https://ext.rte.net.ru:8443/https://7tv.io/v3/emote-sets/global
```

The component ships with `Use Proxy = true` and `Proxy Prefix = https://ext.rte.net.ru:8443/` by default — the same proxy used by the rest of this project — so 7tv/bttv/ffz work out of the box where direct access is blocked. Override either field in the inspector to point at your own proxy, or uncheck `Use Proxy` to call the upstream APIs directly. Toggle `Proxy Emote Cdn` to control whether emote image URLs are also routed through the proxy.

### Public API

```csharp
ThirdPartyEmotes.Instance.LoadGlobalEmotes();              // coroutine
ThirdPartyEmotes.Instance.LoadChannelEmotes(twitchUserId); // coroutine
ThirdPartyEmotes.Instance.TryGetEmote("PauseChamp", out var emote);
ThirdPartyEmotes.Instance.FindEmotesInMessage(chatter.message);

ThirdPartyEmotes.Instance.OnGlobalsLoaded   += () => { /* ... */ };
ThirdPartyEmotes.Instance.OnChannelLoaded   += channelId => { /* ... */ };
ThirdPartyEmotes.Instance.OnProviderLoaded  += (provider, scope, count) => { /* ... */ };
ThirdPartyEmotes.Instance.OnError           += (provider, scope, msg) => { /* ... */ };
```

Each `ThirdPartyEmote` exposes `id`, `code`, `provider`, `scope`, `animated`, `zeroWidth`, and CDN URLs (`url1x`, `url2x`, `url4x`). Use `emote.GetUrl(size)` to safely pick the largest available size.

See [`ThirdPartyEmoteExample.cs`](Unity-Twitch-Chat/Assets/ExampleProject/ThirdPartyEmoteExample.cs) for a working listener.

### Rendering chat with emote images

Three example scripts ship in `ExampleProject/`:

- [`ChatRendererExample.cs`](Unity-Twitch-Chat/Assets/ExampleProject/ChatRendererExample.cs) – a complete UGUI renderer that merges Twitch native emotes (from `chatter.tags.emotes`) and 3rd-party emotes (from `chatter.GetThirdPartyEmotes()`) into a single chat line with mixed text + `RawImage` runs. Static-only out of the box (Unity decodes PNG/JPG natively, plus the first frame of GIF).
- [`WebPEmoteAnimator.cs`](Unity-Twitch-Chat/Assets/ExampleProject/WebPEmoteAnimator.cs) – tiny `MonoBehaviour` that cycles a pre-decoded `Texture2D[]` on a `RawImage` based on cumulative timestamps. Works with any animated frame source.
- [`WebPEmoteSetup.cs`](Unity-Twitch-Chat/Assets/ExampleProject/WebPEmoteSetup.cs) – inspector-friendly bootstrap that wires `WebPEmoteIntegration.Install` to a `ChatRendererExample` at `Start`. No-op until `WEBP_INSTALLED` is defined.

### One-click scene builder

`Tools → Cyan Chat → Build Example Scene` (provided by [`Editor/CyanChatSceneBuilder.cs`](Unity-Twitch-Chat/Assets/ExampleProject/Editor/CyanChatSceneBuilder.cs)) generates a complete, ready-to-play chat scene with everything wired up:

```
Assets/CyanChat/Scenes/CyanChatScene.unity
Assets/CyanChat/Prefabs/TextRunPrefab.prefab
Assets/CyanChat/Prefabs/ImageRunPrefab.prefab
```

The scene contains:

- `EventSystem`
- `TwitchIRC` – `IRC` with anonymous login on; you only need to fill the `Channel` field.
- `ThirdPartyEmotes` – proxy `https://ext.rte.net.ru:8443/` enabled by default, all three providers on.
- `Canvas` + `ChatScroll` (Viewport + Content with vertical layout group anchored at bottom).
- `ChatRenderer` – `ChatRendererExample` referencing the Content and the two prefabs.
- `WebPBootstrap` – `WebPEmoteSetup` that becomes active once you flip the `WEBP_INSTALLED` define.

After running the menu item: select `TwitchIRC`, set `Channel`, press Play.

### Optional: animated 7TV emotes (libwebp via Unity.WebP)

For real animation of 7TV's WebP emotes, the example ships [`WebPEmoteIntegration.cs`](Unity-Twitch-Chat/Assets/ExampleProject/WebPEmoteIntegration.cs) which wires up [`netpyoung/unity.webp`](https://github.com/netpyoung/unity.webp) to the renderer. To enable it:

1. Install Unity.WebP via UPM (`Window → Package Manager → + → Add from git URL`):
   ```
   https://github.com/netpyoung/unity.webp.git?path=unity_project/Assets/unity.webp
   ```
   (or via OpenUPM as `com.netpyoung.webp`)
2. Enable `Allow 'unsafe' code` in `Project Settings → Player → Other Settings`.
3. Add scripting define symbol `WEBP_INSTALLED` in `Project Settings → Player → Scripting Define Symbols`.
4. In your bootstrap code:
   ```csharp
   WebPEmoteIntegration.Install(chatRenderer);
   ```
   `Install` flips both static and animated 7TV formats to WebP and registers a libwebp-backed loader. Pass `setSevenTVFormatToWebP: false` if you only want libwebp for animated and keep static 7TV emotes on PNG.

After this, animated 7TV emotes (`RainTime`, `catJAM`, etc.) actually animate; static 7TV WebPs decode through libwebp; everything else (BTTV, FFZ, Twitch native) keeps using `UnityWebRequestTexture`.

If `WEBP_INSTALLED` is not defined, `WebPEmoteIntegration.cs` compiles to nothing and the package has no dependency on Unity.WebP.

## License
<a href="https://github.com/lexonegit/Unity-Twitch-Chat/blob/master/LICENSE">MIT License</a>

## Projects made with Unity Twitch Chat

Intro Fighters, stream overlay game https://lexone.itch.io/introfighters

*Did you make something cool? Contact me (lexone on Discord) to get featured here!*
