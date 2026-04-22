<p align="center">
  <img src="screenshots/logo-banner.png" alt="DraftModeTOUM" width="700"/>
</p>

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Among Us** running [Town of Us: Mira (TOU:M)](https://github.com/AU-Avengers/TOU-Mira) that adds a **Draft Mode** — players take turns picking their roles before the game begins instead of having them assigned randomly.

<div align="center">
  <a href="https://github.com/ninja-au/DraftModeTOUM/releases/"> <img alt="Latest Release" src="https://badgen.net/github/release/ninja-au/DraftModeTOUM?icon=github"></a>
  <a href="https://github.com/ninja-au/DraftModeTOUM/releases"> <img alt="GitHub Downloads" src="https://img.shields.io/github/downloads/ninja-au/DraftModeTOUM/total"></a>
  <a href="https://discord.gg/vHdyRs8dfu"> <img alt="Mod Discord" src="https://img.shields.io/discord/1279057211339051079.svg?label=&logo=discord&logoColor=ffffff&color=7389D8&labelColor=6A7EC2"></a>
</div>
<br/>

---

## Requirements

- Among Us (compatible version with TOUM)
- [BepInEx IL2CPP](https://github.com/BepInEx/BepInEx)
- [Reactor](https://github.com/NuclearPowered/Reactor)
- [Town of Us: Mira](https://github.com/AU-Avengers/TOU-Mira)

---

## Installation

1. Make sure BepInEx, Reactor, and TOU:M are already installed and working.
2. Download the latest `DraftModeTOUM.dll` from [Releases](../../releases).
3. Drop it into your `BepInEx/plugins/` folder.
4. Launch Among Us — the mod will load automatically.

---

## How It Works

### Starting a Draft

Only the **Host** can start a draft. Once everyone is in the lobby, the host can press the **Start** button (auto-starts the draft)

This kicks off the draft sequence:

1. Each player is assigned a random **slot number** (their turn order).
2. The role pool is built from the host's current TOUM role settings.
3. Players pick in slot order. The host can allow **1 or 2** players to pick at the same time via the Concurrent Picks setting.

---

### Taking Your Turn

When it's your turn, a role picker UI will appear. Choose one of the offered roles - or pick **Random** to be 
assigned any available role from the pool. Other players will see a waiting screen showing who is currently picking. If concurrent picks are enabled, the waiting screen shows **MULTI** while more than one player is picking.

If the timer runs out before you pick, a random role is automatically assigned and the draft moves on.

---

## UI

Roles are presented as large clickable cards spread across the screen. Each card shows the role name, faction, and icon with a colored glow.

![Card Style UI](screenshots/cards.png)

---

## Draft Settings

The host can configure Draft Mode from the **Mira settings menu** in the lobby. All options are synced to all players.

![Draft Mode Settings](screenshots/Settings.png)

Key settings:
- Offered Roles Per Turn
- Concurrent Picks Per Turn (1 or 2)
- Turn Duration
- Auto-start After Draft
- Show Draft Recap
---

## Draft Recap

After every player has picked, a **Draft Recap** is shown on screen for all players, listing each pick slot and the role they chose. Role names are color-coded by their in-game color for easy readability.

![Draft Recap](screenshots/recap.png)

The recap can be toggled off so roles stay secret — only the player who picked knows what they got. To toggle it, the host can use the **Show Draft Recap** option in settings

---

## Local Settings

Each player can toggle whether they hear audio for when the draft starts, and more. Open **Settings → Mira → Draft Mode** to find:

| Setting       | Description |
|---------------|-------------|
| Audio Setting | • No Sound – Disables all audio.<br>• Sound on Draft Start – Plays a sound when the draft phase begins.<br>• Sound on Turn Start – Plays a sound at the start of each turn. |

---

## Role Pool

The roles available to be drafted are controlled by the host's **TOUM Role Settings**. Only roles with a non-zero count and non-zero chance will appear in the pool. The following roles are permanently banned from the draft regardless of settings:

- Haunter
- Spectre
- Pestilence
- Mayor
- HnS Roles
- Traitor

Faction caps (Max Impostors, Max Neutral Killings, Max Neutral Other) are applied globally across the entire draft — once a cap is hit, no more roles of that faction will be offered to any player.

---

## License

MIT


