## Why

The project already pulls game data from multiple databases, but each source uses different identifiers, naming conventions, and coverage. A new unified dataset is needed so the frontend can treat a platform-scoped game as a stable entity, expose release variants and regional differences cleanly, preserve richer metadata and asset references, and rebuild deterministically from refreshed source inputs.

## What Changes

- Add a new unified SQLite dataset build that starts from Datomatic/No-Intro data and enriches it with IGDB, TGDB, and LaunchBox metadata.
- Introduce a canonical internal model for libraries, platforms, games, releases, ROMs, names, descriptions, languages, regions, series memberships, companies, genres, and asset candidates.
- Treat competition or other standalone special editions as distinct games when they are intended to be separately browsable frontend entries, while keeping region/revision/patch variants as releases under a game.
- Preserve normalized source mirror tables and per-source association tables so the unified dataset can join back to source-native records instead of flattening them away.
- Add strict grouping and validation rules plus quality-gated fuzzy matching so strong matches are accepted automatically while ambiguous or unsafe results are recorded for correction instead of silently guessed.
- Add workbook-driven overrides for grouping, source associations, names, releases, series membership, and ignored rows.
- Add integrated IGDB cache behavior to the main build flow: use cached results when available, fetch and store missing results automatically, support offline mode, and support explicit refresh that replaces cached values for the exact query.
- Add diagnostics, preserved build-error state, partial-database publication, optional review workbook export, and override template generation to support correction workflows.
- Expand unresolved-match diagnostics so they include candidate options, the target override sheet, ready-to-paste override rows, and helper commands for manual source searches outside the main build flow.
- Lock in canonical field precedence so semantic metadata prefers IGDB, then TGDB, then LaunchBox, while release identity, regions, and languages remain Datomatic-led.
- Lock in a default English/American-preferred locale policy for canonical names and fallback asset selection while preserving localized names and descriptions for future regional presentation.
- Define stable internal key shapes, typed names/descriptions, and the concrete command surface so generated overrides, diagnostics, and helper commands stay consistent across rebuilds.
- Add a Python configuration-driven runner alongside CLI entrypoints so the same build flow can be run from an IDE without manually typing arguments.
- Keep all existing source-specific code untouched; implement the unified dataset pipeline in new files only.

## Capabilities

### New Capabilities

- `unified-game-dataset`: Build a multi-platform-capable unified SQLite dataset from Datomatic, IGDB, TGDB, and LaunchBox while modeling canonical games, releases, ROMs, metadata, series, and asset candidates.
- `override-workbooks`: Define workbook templates, validation rules, and application order for deterministic manual overrides and diagnostics-assisted correction workflows.

### Modified Capabilities

None.

## Impact

- Adds a new OpenSpec-defined dataset build workflow and new modules/files for parsing, normalization, matching, validation, export, and CLI/IDE runners.
- Relies on Datomatic DAT/XML inputs, the existing TGDB dump, LaunchBox metadata XML, and the existing IGDB cache database pattern.
- Introduces a new unified SQLite output database plus optional review workbook outputs and generated override workbook templates.
- Preserves the current `game_db/*` implementation by treating it as reference-only and leaving existing files unchanged.
