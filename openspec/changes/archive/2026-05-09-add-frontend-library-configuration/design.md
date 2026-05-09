## Context

ArcadeFrontend currently boots from a hand-maintained `frontend/config.json` tree and treats menu data as static runtime input. The Godot frontend already has two useful interaction primitives: the root wheel can open child wheels for nested menus, and a dimmed overlay screen can appear on top of the wheel while leaving the background visible. Separately, the repository now has a unified SNES master database at `database/database/unified_snes.db` with canonical games, releases, ROM hashes, descriptions, companies, series memberships, and asset candidates.

The requested change moves the frontend from a static menu definition toward a hybrid runtime model:

- the master unified database remains the canonical metadata source
- the frontend owns local scan state, preferences, overrides, favorites, and cached asset state
- the wheel is generated at runtime from both databases
- `config.json` remains available for export and transitional compatibility

This is a cross-cutting change because it affects startup, configuration, input handling, overlays, data storage, scan workflows, asset caching, and launch resolution.

## Goals / Non-Goals

**Goals:**

- Add a frontend-local SQLite database that persists runtime settings and owned-library state.
- Keep the master unified SNES database fixed at `database/database/unified_snes.db`.
- Support SNES only in the first implementation while keeping the local schema multi-system-capable.
- Replace immediate root-wheel exit with a dimmed exit/configuration menu flow.
- Allow users to configure an SNES ROM root path as plain text and scan it recursively.
- Match owned ROMs to unified releases using SHA-1 first and MD5 second.
- Build a runtime SNES wheel from canonical owned games and a sibling `Unidentified SNES` wheel from unmatched files.
- Open a non-wheel canonical game screen over the wheel that shows play controls, owned releases, metadata, screenshots, favorites, and related owned games.
- Persist release-default overrides, favorites, emulator overrides, scan state, and cached asset selections in the frontend-local database.
- Export a full snapshot configuration, including generated library data and artwork references, without removing `config.json` support.

**Non-Goals:**

- Supporting non-SNES systems end to end in this change.
- Adding folder-browse UI; ROM roots are entered as strings.
- Scanning archive contents such as `.zip` in the first implementation.
- Making the master-database path configurable.
- Introducing ratings-based related-content ordering.
- Replacing the unified dataset build or changing its source precedence rules.

## Decisions

### Use a two-database runtime model with normalized local ownership data

The frontend will read from two SQLite databases:

- master DB: `database/database/unified_snes.db`
- frontend DB: a local runtime database owned by the frontend

The frontend DB will store only local/runtime facts:

- app settings
- enabled systems
- ROM root path and scan timestamps
- discovered files and hashes
- matched owned releases
- canonical-game favorites
- canonical-game preferred owned release
- launch overrides at system, canonical-game, and owned-release scope
- cached asset selections and local file paths

The wheel and detail screens will be built in memory from these normalized facts rather than stored as a prebuilt menu tree in SQLite.

This keeps master metadata authoritative while avoiding duplicated canonical content in the local DB.

Alternative considered:

- Store the fully baked wheel hierarchy in the frontend DB. Rejected because UI structure would become hard to evolve and would force needless migrations.

### Keep `config.json` as a compatibility and export format, not the primary live library source

The frontend will no longer treat `config.json` as the only runtime source of the library, but it will remain part of the system:

- used for transitional compatibility where needed
- used as an export format for the generated library snapshot
- retained so the project does not hard cut away from the current file-based workflow

The runtime library will be generated from the databases first, with exported JSON treated as a snapshot rather than the primary source of truth.

Alternative considered:

- Remove `config.json` entirely. Rejected because the user explicitly wants to preserve it for now and export remains valuable for backup/debugging.

### Scope the first implementation to SNES while making the local schema system-aware

The first end-to-end implementation will support SNES only, because the master dataset is only lightly cleaned for that system today. The frontend DB schema will still include system identifiers so additional consoles can be added later without redesigning the local data model.

Alternative considered:

- Implement generic multi-system UX immediately. Rejected because it adds validation and UI complexity before the first complete system flow exists.

### Use recursive file discovery with explicit extension allowlists and phase-based scan progress

The scan workflow will proceed in explicit phases:

1. discover candidate files under the configured ROM root recursively
2. hash each candidate file
3. match hashes against unified release ROM rows
4. persist ownership and unmatched records
5. fetch/cache required assets for identified games

The first implementation will scan `.sfc` and `.smc` files only. The frontend DB will leave room for future archive-aware fields such as outer archive path and inner ROM filename so zip support can be added later without redefining owned-file identity.

Alternative considered:

- Stream directly into hashing without pre-discovery. Rejected because the user wants a real progress bar with known total work.

### Match owned ROMs by hash and persist file-to-release ownership explicitly

Each discovered file will be hashed and matched against `release_roms` in the master DB using:

1. SHA-1
2. MD5 fallback

Matched files will persist a direct association to a unified `release_key`. Unmatched files will persist as unidentified entries for the `Unidentified SNES` wheel.

This allows the runtime to distinguish:

- canonical games the user owns
- which releases under those games are owned
- files that were scanned but could not be matched

Alternative considered:

- Infer ownership only from filenames. Rejected because the unified dataset already carries exact hash identity and filename matching would be brittle.

### Build two top-level SNES entries: canonical owned library and unidentified files

The runtime root menu will expose:

- `Super Nintendo Entertainment System`
- `Unidentified SNES`

The SNES wheel will include one item per owned canonical game. The unidentified wheel will include one item per unmatched scanned file using filename text only.

Alternative considered:

- Hide unmatched files entirely or bury them under configuration diagnostics. Rejected because the user wants a visible wheel for unidentified SNES content.

### Use overlay-style menus and screens instead of wheels for exit/configuration and canonical game details

The root escape action will open a dimmed exit/configuration menu instead of quitting immediately. Configuration screens and canonical game detail screens will also use overlay-style menus rather than nested wheels. The wheel remains visible in the background for these screens, with a darker faded backdrop.

This matches the current Godot primitives better than building non-library UI as wheels.

Alternative considered:

- Represent configuration and release selection as more wheel levels. Rejected because it blurs library browsing and utility UI, and the user specifically wants non-wheel screens.

### Model the canonical game screen as section-focused navigation with nested interaction modes

The canonical game screen will use a section-based focus model:

- up/down moves between page sections
- left/right acts within the focused section
- accept enters or activates the focused section
- cancel backs out of the focused interaction mode or closes the screen

Release selection is a nested mode:

- when the release section is highlighted, the whole list is framed
- accept enters release-edit mode
- up/down moves the default checkmark among owned releases
- accept saves the new default immediately
- cancel leaves release-edit mode and returns to section navigation

Screenshot viewing is also nested:

- left/right switches screenshots when the screenshot section is focused
- accept opens a fullscreen screenshot viewer
- left/right cycles screenshots inside fullscreen
- cancel closes fullscreen back to the detail screen

Alternative considered:

- Make releases a popup or always-on list that consumes up/down. Rejected because it conflicts with page scrolling and is weaker than the requested “enter the list, then back out” interaction.

### Persist canonical-game default release and favorite state in the frontend DB

The frontend DB will store:

- a boolean favorite flag and favorite timestamp per canonical game
- a preferred owned release per canonical game

The preferred release becomes the default launch target for the canonical game screen and must save immediately when the user confirms a change.

Alternative considered:

- Keep defaults in memory only or export-only. Rejected because the user wants persistence between visits.

### Use a launch-resolution precedence chain across system, canonical game, and owned release scopes

Launch configuration will be layered:

1. owned-release override
2. canonical-game override
3. system default
4. exported/config fallback

This supports cases like a widescreen emulator for one game while keeping a normal emulator at the SNES system level.

Alternative considered:

- Store only one emulator path per system. Rejected because per-game and per-release overrides are explicit requirements.

### Resolve wheel logos and detail assets with deterministic source and region precedence

Asset selection rules will distinguish between single-choice assets and multi-choice assets:

- wheel/logo asset: single chosen clear logo
- detail poster asset: single chosen poster
- detail screenshots: all available screenshots

Source precedence:

- LaunchBox first
- TGDB fallback

Clear-logo region precedence for the default USA/EN policy:

1. `USA`
2. `NORTH_AMERICA`
3. `WORLD`
4. `EUROPE`
5. `OCEANIA`
6. `JAPAN`
7. any remaining region
8. text fallback

Poster precedence:

1. `Fanart - Box - Front`
2. `Box - Front`

Screenshot behavior:

- collect all available screenshots across supported screenshot asset types
- cache them as a one-to-many set for the canonical game screen

Alternative considered:

- Use TGDB first for logos or keep only one screenshot. Rejected because LaunchBox region data is already preserved in the unified DB and the user wants all screenshots.

### Cache downloaded assets using semantic names rather than unstable dataset IDs

The asset cache will name files from stable semantic values such as system slug, canonical game slug, asset role, and a short hash derived from the source reference. Cached file naming must not depend on numeric row IDs that can change across master-database rebuilds.

The frontend DB will track which cached files are active for:

- clear logo
- poster
- screenshots

Alternative considered:

- Name files directly from master-database row IDs. Rejected because the user expects master-database regenerations to remain compatible with cached assets where possible.

### Limit related-content strips to owned games on the same system and use a detail-screen stack

The canonical game screen will show related strips for:

- games in the same series
- more from the same publisher
- more from the same developer

These strips will include only canonical games that:

- belong to SNES
- have been scanned and matched into the owned library

Ordering:

- favorites first
- for series: source sort order
- otherwise: newest release year first

Selecting a related game opens another canonical game screen on a detail-screen stack. Cancel returns to the previous game screen, not directly to the wheel.

Alternative considered:

- Jump the wheel selection directly and discard the current detail screen context. Rejected because the user wants back-navigation to unwind through prior detail screens.

## Risks / Trade-offs

- [The change touches startup, persistence, input handling, scanning, and rendering at once] → Separate the work into distinct layers: local DB/data access, scan pipeline, runtime library builder, and UI flows.
- [Hashing large ROM libraries may make the UI feel stalled] → Use a phase-based progress model with known totals and update the UI between scan stages.
- [The canonical game screen could become overloaded] → Use section-based navigation, vertical scrolling, and a strict split between hero, releases, screenshots, and related-content strips.
- [Master-database keys may evolve across rebuilds] → Use semantic cache names, store fallback descriptive fields where useful, and keep the fixed master path while the dataset stabilizes.
- [The local schema must anticipate future zip support without implementing it yet] → Reserve archive-aware fields now and treat the first implementation as flat-file-only.
- [Exporting the “whole thing” can drift from runtime behavior if generated ad hoc] → Export from the same in-memory runtime library model used by the UI rather than from partial raw tables.

## Migration Plan

1. Add the frontend-local SQLite schema and persistence access layer without removing `config.json`.
2. Add startup logic that can initialize the local DB and load settings while preserving current frontend boot behavior.
3. Add the root escape/configuration overlay flow.
4. Add SNES system setup, recursive scan orchestration, progress UI, and ownership persistence.
5. Add runtime wheel generation for owned SNES games and the `Unidentified SNES` wheel.
6. Add canonical game detail screens, release selection persistence, favorite persistence, and related-content navigation.
7. Add asset download/caching plus export generation from the runtime library model.

Rollback is additive and low risk:

- disable the new runtime-library path
- continue booting from `config.json`
- leave the frontend-local DB unused

## Open Questions

- Which exact screenshot asset-type list should be recognized as “screenshots” for LaunchBox/TGDB aggregation in the first implementation.
- Whether the exported snapshot should inline absolute local asset paths only or also preserve source provenance metadata for debugging.
- Whether unidentified entries should expose any lightweight metadata beyond filename in the first pass, such as file extension or scan time.
