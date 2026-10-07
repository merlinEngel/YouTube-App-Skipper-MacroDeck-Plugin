# YouTube Ad Skipper

A Macro Deck 3 plugin for macOS that skips YouTube ads in Safari.

## What it provides

| Item | Id | What it does |
| --- | --- | --- |
| Action | `skip-youtube-ad` | Presses the skip button of every YouTube tab in Safari that currently shows one. Fails with a clear message when no skippable ad is showing. |
| Variable | `youtube_ad_skippable` (boolean) | `true` while a video is playing and a skip button is visible. The host refreshes it every 2 seconds. `false` when Safari is closed or nothing skippable is showing. Empty when Safari cannot be read. |

The plugin reads and clicks the page through `osascript` and Safari's `do JavaScript`, not through screen
coordinates, so window position and size do not matter and the mouse stays free.

## Setup

1. In Safari, enable the Develop menu (Settings > Advanced) and turn on
   **Develop > Allow JavaScript from Apple Events**.
2. The first time the plugin talks to Safari, macOS asks whether Macro Deck may control Safari. Allow it
   (System Settings > Privacy & Security > Automation).

Use the variable to react automatically, for example a button or trigger that runs the action whenever
`youtube_ad_skippable` becomes `true`.

## Development

Requires the .NET SDK 10.0 and a running Macro Deck desktop app for interactive debugging.

```bash
dotnet build
dotnet test
macrodeck-plugin test --project src/YouTubeAdSkipper
macrodeck-plugin build --source src/YouTubeAdSkipper --output ./artifacts
```

Run it against the installed Macro Deck with the **Macro Deck - Real Host** launch profile. The one-time
enrollment token setup is described in the
[plugin template guide](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template#run-and-debug-against-macro-deck).

## AI disclosure

This plugin was written with the help of an AI assistant (Claude Code) and reviewed by the author.

## License

MIT, see [LICENSE](LICENSE).
