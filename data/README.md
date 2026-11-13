# data/

The overlay can label each **far exit** with the name of the area it leads to
(e.g. "The Causeway") and can tell real exits from other points of interest
(waypoints, quest items, chests). That uses an optional data file:

```
data/radar-targets.json
```

This file is **not** included in this repository. It is the `targets.json` of the
Radar plugin for ExileApi ([exApiTools/Radar](https://github.com/exApiTools/Radar)),
a hand-curated per-area list. That repository declares no license, so
redistributing it under this project's MIT license would not be correct and it
is left out.

## Getting it

Download `targets.json` from the repository above and save it as
`data/radar-targets.json`. The build copies it next to the executable
automatically. With the release zip, put it into the `data` folder next to
the exe.

## Format

```json
{
  "2_7_11": [
    { "Name": "top",  "ExpectedCount": 1, "DisplayName": "The Causeway" },
    { "Name": "exit", "ExpectedCount": 1, "DisplayName": "The Temple of Decay Level 1" }
  ]
}
```

- The key is the area id, derived at runtime from the area's terrain graph path
  (`Metadata/Terrain/Act7/Area11/Graphs/...` becomes `2_7_11`; acts 1-5 are
  prefixed `1_`, acts 6-10 `2_`).
- `Name` is the **detail name** of a terrain tile (a free-text tag the level
  designers put on the tile). It is arbitrary: "top", "exit", "cave",
  "groveentrance"... which is exactly why a list is needed.
- `DisplayName` is the destination, or a generic label ("Exit", "Stairs Up")
  when the destination is not specific.

## Without the file

Everything else still works: exits close to you are read from the game's own
door entities (with the game's own destination names), and far exits are
found by two built-in rules (tiles under an `AreaTransitions` folder, and tile
detail names containing `entrance`, `stairsup`, `stairsdown` or `transition`).
What is missed without the list are far exits whose detail name looks like
neither (such as `top` or `exit`), and the generic ones are labelled from the
tile name instead of the destination.

`MapScan exitlist` prints how every entry of a list is classified, which is
the quickest check after replacing the file with a newer one.

# Optional: icon previews (`data/icons/`)

The **Icons** tab of the settings window lists map icons by category (Abyss,
Breach, Heist, ...) with a size ruler per icon. The names and the pictures come
from two optional files that are **not** included in this repository, because
the icons are the game's own artwork and the repository they come from
([exApiTools/ExileApi-Compiled](https://github.com/exApiTools/ExileApi-Compiled))
declares no license:

```
data/icons/catalog.txt   icon index and name, one per line ("14<TAB>Abyss")
data/icons/Icons.png     the icon sheet (textures/Icons.png of that repository)
```

## Getting them

Nothing to do. Both end up in the `data\icons\` folder next to the exe by themselves:

- `Icons.png`: on first start the overlay downloads `textures/Icons.png` from that
  repository (about 3 MB, a second). It is the only file downloaded; no program
  files are downloaded.
- `catalog.txt`: the icon names are read from the game's own icon table
  (`MinimapIcons.dat`, in game memory) the first time you are in an area. The
  table is in the same order as the sheet: row `n` is cell `n + 1`.

Without internet the map works as usual, only without icon pictures; the download
is retried on the next start. Developers: `MapScan icontable` prints the game's
table and compares it with an existing `catalog.txt`.

## Sheet layout

64x64 cells, 14 per row. Icon index 1 is the first cell, so the cell of index `n`
is column `(n-1) % 14`, row `(n-1) / 14`. Loot-filter shapes (396 of the sheet's 699
icons) are not listed in the tab. Icons newer than the sheet (the game has 708)
are listed but have no picture.

## Without the files

The Icons tab shows a short note instead of the list and no icons are drawn on
the map; paths, monsters and everything else work as usual. The sizes
are stored in the settings file as `icon.<Name> = <pixels>` (0 or no line = off).
