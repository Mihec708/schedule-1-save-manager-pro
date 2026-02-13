# Schedule 1 Save Manager Pro (MelonLoader)

A polished **MelonLoader** mod for **Schedule 1** to simplify save management:

- ✅ Unlimited save snapshots (no fixed slot cap)
- ✅ One-click restore
- ✅ One-click delete
- ✅ Confirm dialogs for destructive actions
- ✅ Snapshot size + timestamp shown in UI

## What it does

The mod creates snapshots as full folder copies under:

`<SaveRoot>/Snapshots/`

This avoids brittle patching of the game’s internal slot system and remains stable across updates.

## Requirements

- MelonLoader installed for Schedule 1
- .NET SDK (for building locally)

## Build

1. Put MelonLoader assemblies at either:
   - `./lib/MelonLoader/net6/*.dll` (default), or
   - set `MELONLOADER_DIR` to your MelonLoader assembly root.
2. Build:

```bash
dotnet build -c Release
```

3. Copy output DLL:

`bin/Release/net6.0/Schedule1SaveManagerPro.dll`

into:

`<GameFolder>/Mods/`

## In-game controls

- `F6`: open/close save manager window.
- `ToggleKey` can be changed via MelonPreferences (any Unity `KeyCode` string).

## Default save root detection

The mod checks these locations in order:

1. `%USERPROFILE%/AppData/LocalLow/TVGS/Schedule I`
2. `%USERPROFILE%/AppData/LocalLow/TVGS/Schedule 1`

You can override with `SaveRootPath` in MelonPreferences.

## Releasing on GitHub

This repo includes `.github/workflows/release.yml`.

- Push a tag like `v1.0.0`.
- GitHub Actions builds the DLL and attaches it to a GitHub Release automatically.

Example:

```bash
git tag v1.0.0
git push origin v1.0.0
```
