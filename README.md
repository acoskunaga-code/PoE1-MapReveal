<h1 align="center">PoE1 Map Helper</h1>

<p align="center">
  The whole area map, every monster and the way out,<br>
  drawn right on top of the game's own map.
</p>

<p align="center">
  <img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-2ea44f">
  <img alt="Windows x64" src="https://img.shields.io/badge/Windows-x64-0078d4">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="Status: pre-release" src="https://img.shields.io/badge/status-pre--release-f59e0b">
</p>

---

## ✨ What you get

- 🗺️ **Whole map on entry** — the explored *and* unexplored layout, lined up with the game's own large map as you zoom and pan.
- 👹 **Monsters by rarity** — 🔴 normal · 🔵 magic · 🟡 rare · 🟤 unique. Each rarity can be switched off or resized. Rare **Fungal Shamans** get a green ring of their own.
- ⚔️ **Legion** — the frozen army of a Legion you have not opened yet stays off the map until you click the monolith. After the click every monster you have **not hit yet** gets a ring filled with its rarity colour (red normal, blue magic, yellow rare, orange unique), so you can find the last one and release the whole army without waiting for the timer (Settings → General → *Legion*: on/off and size).
- 🟣 **Abyssal Wasting** — the Wasting ground (purple disc) and the Wasting Well / Tarhulk monsters that leave it (purple ring) are marked (Settings → Altars; can be switched off).
- 🎯 **Clean player spot** — the red player ring is off by default, so it never covers the game's own arrow (Settings → General → *Player marker on the map* brings it back).
- 🚪 **Exit paths** — a coloured route from you to every exit, labelled with where it leads. Pick which exits are shown at all (Settings → Paths, with a search box); an unticked exit loses its route, marker and name. Routes follow doors and barriers that open and close (e.g. the Breach Hive arms).
- 💎 **Uniques on the ground** — knows which unique dropped even while it is unidentified (a Heavy Belt can be *Mageblood*). For each unique you choose: its name on the map, a route to it, its name in big letters on the screen, and/or a frame around the game's own item label with a line from you to it. All off by default; the list groups uniques the way your loot filter does (Settings → Uniques → Build list).
- 📍 **Map icons** — shrines, strongboxes, essences, NPCs, Fate cauldrons and more, with their names. Opened strongboxes and taken shrines disappear. Size per icon, searchable list.
- 🔮 **Eldritch altars** — every "Player gains" choice framed, plus a gold frame for any text you type (e.g. *Divine Orb*). Colours are yours to pick.
- 🪶 **Fates (Covens)** — the moment the "Select a Fate" panel opens, each card shows what its small round reward icons say, written above the card. No need to hover them one by one. Rewards that contain a word you type (e.g. *Rare Rings*) are coloured and their card's box is framed. On each "Select a Fate" pick a green **TAKE** marks the card that leads to the best combination with the cards you already hold, and offered cards with the same name as one in your hand get a gold **COPY** frame. On the "Invoke Fates" panel the best cards get numbers in the order to click them. Both follow your own reward list (Settings → Fates; can be switched off). Cards that spawn an extra campaign boss (Savage, Pestilent, Sadist) are marked **+ CAMPAIGN BOSS**. Cards carrying one of the new 3.30.0c chase rewards (Divine Orb, Mirror Shard, Fracturing Orb, Hinekora's Lock) get a gold frame and a big tag such as **+ DIVINE ORB (2 COPIES)**. Nothing is drawn over the Confirm button.
- 🏺 **Djinn's Caches** — marked on the map with their metal and contents (Settings → Altars; can be switched off).
- 🌿 **The Viridian Wildwood** — wisps by colour and size, fuel orbs, altars and Nameless Shrines.
- ⌨️ **Hotkeys** — one key hides the whole overlay (default **F9**), another hides only the exit paths (default **F10**). Both can be changed.
- 🧪 **Flasks (optional, off by default)** — presses a key you choose when your life or energy shield drops below a percentage you set, only while the game window is in front (Settings → Flasks).
- ⚡ **Quick** — the first map appears about 4–5 s after starting, a new zone is picked up in about 0.1–0.2 s, and newly dropped uniques are named right away. The map background is drawn once and reused, so the overlay uses far less CPU.
- 🔒 **Hands off the game** — it only reads game memory. It does not modify the game or inject code. The only input it ever sends is the optional Flasks key above.

## 🚀 Quick start

1. Download the latest zip from **Releases** and unzip it (no .NET install needed).
2. Start Path of Exile and enter an area.
3. Run `BunuYazanTosun.exe` — a tray icon appears.
4. Press **Tab** to open the large map. The overlay is drawn on top of it.
5. Tray icon → **Settings** for line colour, player marker, monster icons, Legion marker, hotkeys (General), map icons (Icons), altar frames and Abyssal Wasting (Altars), Fate rewards (Fates), exit paths (Paths), uniques (Uniques) and the flask key (Flasks).

Everything it writes (settings, log, downloaded icons) stays in a `data` folder next to the exe — nothing in AppData, no registry, no installer. Delete the folder to reset. (If the exe sits in a write-protected folder such as Program Files, it falls back to `%LOCALAPPDATA%\poe1-map`.) Files of older versions are moved there on first start. The log is capped at about 2 MB.

> The memory layout it reads belongs to one game build. After a patch the map may need its constants re-derived — the `MapScan` tools are there for that.

## 🧰 Requirements

- Windows 10 or later (x64)
- Path of Exile (the original game)
- To build from source: [.NET 10 SDK](https://dotnet.microsoft.com/)

## 🔧 Build from source

```bash
dotnet build MapOverlay/MapOverlay.csproj -c Release
```

## 📦 In this repo

- **MapOverlay** — the tray app, the product
- **MapView** — development / calibration tool
- **MapScan** — memory diagnostics used to find the offsets

Far-exit detection and names use an optional list that is not shipped here, see [`data/README.md`](data/README.md). The overlay runs without it; some far exits are then missed or labelled generically. Map icon pictures are not shipped either: the overlay downloads them by itself on first start (about 9 MB from GitHub, nothing to copy by hand; details in the same file). The unique list and unique names are built on your own PC with the **Build list** button (poewiki and RePoE data, same file).

## ⚠️ Disclaimer

An independent, unofficial project — **not** affiliated with or endorsed by Grinding Gear Games. It reads game memory to render a map, and third-party tools may be against the game's Terms of Service. Use it at your own risk. Provided "as is", without warranty.

## 📄 License

[MIT](LICENSE)
