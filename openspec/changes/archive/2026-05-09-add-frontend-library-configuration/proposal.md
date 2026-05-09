## Why

ArcadeFrontend currently depends on a hand-authored `config.json` menu tree, which makes the library brittle to maintain and prevents the frontend from using the unified SNES dataset as a live source of truth. The frontend now needs a first-class configuration and library flow that can scan owned ROMs, persist local preferences, build canonical game menus from the master database, and still support exportable configuration snapshots.

## What Changes

- Add a frontend-owned SQLite database for runtime settings, scanned ROM files, owned release matches, launch overrides, favorites, and cached asset metadata.
- Add a root escape menu that opens a dimmed configuration/exit UI instead of quitting immediately.
- Add a configuration flow for SNES setup, including ROM root path entry, recursive scan, progress reporting, locale defaults, and config export.
- Add runtime library generation for SNES that combines the master unified database with the frontend-owned database to build:
  - a canonical SNES wheel
  - an `Unidentified SNES` wheel for unmatched files
- Add canonical-game detail screens that open over the wheel, keep the wheel visible in the background, and show:
  - play action
  - owned release selection with default persistence
  - favorite toggle
  - master-database metadata and descriptions
  - cached poster/logo/screenshot assets
  - related owned games from the same system
- Add deterministic asset selection and caching rules for clear logos, posters, and screenshots using LaunchBox first and TGDB as fallback.
- Keep `config.json` support for export and transitional compatibility instead of removing it immediately.

## Capabilities

### New Capabilities

- `frontend-library-config`: Configure frontend runtime settings, root escape/configuration menus, system setup, scanning, and config export behavior.
- `scanned-system-library`: Build runtime wheel content from the unified SNES database plus frontend-local scan state, including owned canonical games, owned releases, and unmatched ROM buckets.
- `canonical-game-details`: Present a non-wheel canonical game screen with owned release selection, favorites, metadata, screenshots, and related-content navigation.
- `frontend-asset-cache`: Select, cache, and reuse logos, posters, and screenshots for scanned games using deterministic source and region fallback rules.

### Modified Capabilities

None.

## Impact

- Affects frontend startup/config loading and introduces a new runtime source of truth alongside `frontend/config.json`.
- Affects root-wheel input handling, overlay/menu behavior, and game-selection navigation in the Godot frontend.
- Depends on the existing unified SNES master database at `database/database/unified_snes.db`.
- Introduces a frontend-local SQLite database, runtime scan/match workflows, asset download/cache workflows, and export generation.
