## Context

The current repository already contains source-specific code for No-Intro/Datomatic, IGDB, TGDB, and LaunchBox, but each source is consumed independently and with source-native identifiers. The new unified dataset needs to support frontend browsing by platform-scoped game, selectable releases, multilingual releases, source-backed metadata, region-aware assets, platform series, and deterministic rebuilds from refreshed source inputs. Existing `game_db/*` modules must remain untouched, so the unified pipeline must live in new files and treat the current code as reference-only.

Datomatic is the root import source because it provides the strongest release and ROM identity for emulation use cases. IGDB is the preferred semantic enrichment source and must retain its current cache-first request model, with exact-query reuse by default, automatic fetch-and-store on miss, offline operation when requested, and explicit refresh that replaces cached values for the exact query. TGDB is a local full dump, and LaunchBox metadata comes from local XML; both can be mirrored directly during build. The unified output must be multi-platform capable even though SNES is the first target.

## Goals / Non-Goals

**Goals:**

- Build a multi-platform-capable unified SQLite database from Datomatic, IGDB, TGDB, and LaunchBox.
- Preserve normalized source mirror tables and per-source association tables in the unified database for joins and diagnostics.
- Model canonical libraries, platforms, games, releases, ROMs, names, descriptions, genres, companies, regions, languages, series memberships, and asset candidates.
- Keep translation patches and bugfix patches in the official library while placing gameplay hacks, total conversions, randomizers, and similar patch-driven content into hacks libraries.
- Apply conservative grouping and quality-gated fuzzy matching so strong matches can be accepted automatically while low-confidence or ambiguous results are recorded for later correction instead of silently guessed.
- Support deterministic workbook-driven overrides, workbook template generation, build diagnostics, partial-database publication, and optional review workbook export.
- Support both CLI execution and IDE-friendly execution through a shared build API and typed configuration.

**Non-Goals:**

- Modifying the existing source-specific code in `game_db/*`.
- Downloading asset binaries as part of this change.
- Solving cross-platform population for every system in the first implementation; SNES is the first populated platform, but the model must remain multi-platform capable.
- Making series membership mandatory for every game.
- Hiding all source disagreements by flattening them into a single denormalized table.

## Decisions

### Build a new isolated pipeline instead of extending existing source-specific modules

The unified dataset will be implemented in new files only. Existing `game_db/*` modules remain unchanged and serve as reference implementations for source access patterns and parsing behavior.

This avoids regressions in the current codebase and allows the unified model to adopt richer schema design, explicit override workflows, and diagnostics-oriented processing without being constrained by the older runtime behavior.

Alternative considered:

- Extend existing source-specific modules directly. Rejected because it would mix the new dataset design into code that the user wants preserved as-is.

### Use Datomatic/No-Intro as the root source of identity

Datomatic rows will be the starting point for import, grouping, and release construction. The unified pipeline will parse source rows into deterministic internal records before any external matching occurs.

Datomatic is the best fit for:

- exact release and ROM identity
- region and language extraction where encoded in source names
- official vs hacks classification inputs
- deterministic rebuilds from source files

Alternative considered:

- Build identity around IGDB or TGDB first. Rejected because those sources are weaker for exact emulation release identity.

### Standardize on the current Datomatic DAT/XML input format for the first implementation

The first implementation will treat the existing DAT/XML flow as the required Datomatic input format and will reuse the current repository logic as reference for parsing behavior. If richer Datomatic exports are evaluated later, they can be added as a future enhancement without changing the initial contract.

This keeps the first implementation aligned with the files and logic already in use in the repository and avoids blocking the work on speculative alternate export formats.

Alternative considered:

- Delay implementation until a richer Datomatic export format is chosen. Rejected because the current DAT/XML inputs already support the immediate use case and match existing repository logic.

### Use deterministic string keys instead of relying on generated numeric IDs in workflows

Overrides and diagnostics will use stable keys rather than internal numeric row IDs. Datomatic rows will use deterministic source keys derived from system plus raw title plus hash and size information. Internal game and release keys will be derived from content-based grouping decisions rather than insertion order.

This keeps rebuilds stable and makes workbook-driven overrides practical across repeated runs.

Alternative considered:

- Use integer IDs in overrides. Rejected because IDs can change across rebuilds.

### Use explicit readable key shapes for canonical and series entities

The first implementation will standardize key shapes so generated diagnostics, workbook rows, and helper commands are consistent:

- `datomatic_source_key`: deterministic key derived from platform/system identifier, raw title, and hash-or-size identity inputs
- `internal_game_key`: readable key derived from library slug, platform slug, normalized canonical grouping title, and game kind
- `internal_release_key`: readable key derived from `internal_game_key` plus release identity inputs such as primary region set, release type, revision/version markers, and patch classification when applicable
- `platform_series_key`: readable key derived from library slug, platform slug, and normalized series name
- `cross_platform_series_key`: readable key derived from normalized source-backed franchise or collection identity

The implementation may choose separators and normalization details, but the key contents must remain readable, deterministic, and reproducible from source data plus applied overrides.

Alternative considered:

- Leave key structure entirely implementation-defined. Rejected because diagnostics, workbook templates, and manual correction workflows depend on stable, understandable key formats.

### Separate game, release, and ROM identity

The canonical hierarchy will be:

`library -> platform -> game -> game_release -> release_rom`

`library` is the top-level browse and classification boundary. It separates official catalogs from hacks/mod catalogs and defines which kinds of releases are allowed to appear together in the same frontend root. For example, official SNES releases, translation patches, and bugfix patches belong in the official SNES library, while gameplay hacks, total conversions, and randomizers belong in the SNES hacks library.

`platform` is the hardware-family boundary inside a library. It identifies the console or system context that a game belongs to and prevents similarly named titles from being collapsed across systems. A platform belongs to exactly one library, so `SNES` in the official library and `SNES` in the hacks library are intentionally distinct platform contexts even though they refer to the same underlying hardware family.

`game` is the frontend-facing title entry. It is used for anything the user should browse as a distinct title entry, including numbered sequels, subtitle-distinct products, competition cartridges, and other standalone special editions that should appear separately in the frontend.

`game_release` is the selectable release variant under a game. It is used for region variants, revisions, versions, betas, prototypes, demos, samples, translation patches, bugfix patches, and other selectable variants that do not deserve their own browseable title entry.

`release_rom` is the exact file-level identity from Datomatic. The schema remains one-to-many from release to ROM even if most cartridge releases are effectively one-to-one in practice.

Multilingual releases will use join tables such as `release_language` rather than overloading release names. Regions will also use join tables so releases can remain accurate when a release spans multiple regions. `World` releases will be modeled as explicit release/region values rather than treated as regionless data.

Alternative considered:

- Force `game_release` and `release_rom` to be one-to-one. Rejected because it would break on dump variants and future multi-file or multi-dump cases.

### Keep official and hacks libraries separate, with translation and bugfix patches kept in official

Library classification is a first-class concept. Translation patches and bugfix patches stay in the official library because they preserve the underlying official title and are expected to be selectable from the official frontend catalog. Gameplay hacks, total conversions, randomizers, and similar patch-driven releases belong in hacks libraries.

This preserves browseability while still exposing translation-patched releases as user-selectable variants.

Alternative considered:

- Put all patches in hacks. Rejected because translation patches and bugfixes are better modeled as official-playable variants.

### Preserve source mirrors and use per-source association tables

The unified database will include normalized source mirror tables for Datomatic, IGDB, TGDB, and LaunchBox. Internal entities will not reuse external IDs directly; instead, source-specific association tables will map internal games and releases to source-native rows.

This allows one internal game to map to multiple TGDB or LaunchBox rows when regional naming or asset differences require it, while still exposing source-native joins for future features and debugging.

Alternative considered:

- Store only canonical flattened rows. Rejected because it would discard too much source detail and make debugging and asset work harder.

### Keep normalized source mirrors in the final unified database

Normalized source mirror tables will remain in the final published unified database rather than existing only in temporary build databases. This allows downstream joins, provenance tracking, diagnostics review, and future asset/download workflows to use the published database directly.

If database size becomes a problem later, pruning or archive strategies can be handled as an explicit future change rather than weakening the first implementation’s auditability.

Alternative considered:

- Keep full source mirrors only in temporary build databases. Rejected because it would make the published database less useful for joins, debugging, and future enrichment work.

### Make grouping deterministic and matching quality-gated

Grouping from Datomatic into internal game and release entities will be deterministic. Matching from internal games to IGDB, TGDB, and LaunchBox will be conservative, rule-based, source-specific, and fuzzy-match capable. Strong fuzzy matches may be accepted automatically when they clear configured quality gates and clearly beat competing candidates. Low-quality or unresolved matches will be recorded as build errors, left unresolved in the canonical associations, and surfaced for correction after processing completes.

Diagnostics will record candidate matches, rejected matches, accepted matches, and suggested override rows for correction.

Alternative considered:

- Require exact-only matching. Rejected because source names vary too much across databases and exact-only matching would force unnecessary overrides.

### Lock canonical field precedence by field instead of leaving it implicit

Canonical values on the main game rows will follow fixed precedence rules so rebuilds stay deterministic:

- canonical display name: `name_override`, then IGDB, then TGDB, then LaunchBox, then Datomatic-derived cleaned title
- canonical sort name: `name_override.sort_name`, then deterministic normalization of the chosen canonical display name
- short description: IGDB summary, then TGDB overview, then LaunchBox overview
- long description: IGDB storyline, then TGDB overview, then LaunchBox overview
- release year: IGDB first-release year, then TGDB, then LaunchBox, then Datomatic when trustworthy
- players and coop: IGDB, then TGDB, then LaunchBox
- genres, developers, and publishers: preserve all imported values, but prefer IGDB for primary selection, then TGDB, then LaunchBox
- release identity, regions, and languages: Datomatic is authoritative
- asset candidates: preserve all imported candidates, with LaunchBox preferred ahead of TGDB when ranking defaults

This keeps semantic metadata centered on IGDB while preserving the Datomatic-led release model that the frontend needs.

Alternative considered:

- Let implementations choose field precedence opportunistically. Rejected because it would create inconsistent rebuild results and weaken the usefulness of overrides and diagnostics.

### Use an English/American-preferred locale policy for canonical presentation defaults

The initial implementation will use an English/American-preferred locale policy when choosing canonical presentation defaults. Canonical display names should prefer English and English-speaking-region variants when multiple localized names exist. Region-aware assets should prefer USA first, then other English-compatible or broad fallback regions such as North America or World, before falling back further.

Localized and regional names will still be preserved in supporting tables so future frontend work can switch presentation by user locale without changing the dataset structure.

Alternative considered:

- Let canonical names and assets fall out of source precedence without a locale policy. Rejected because it could choose Japanese or other region-specific titles/assets as defaults even when an English-first frontend presentation is desired.

### Preserve typed names and typed descriptions instead of flattening them

Names and descriptions will be stored with type information rather than as undifferentiated text lists. Names need to distinguish canonical, localized, alternate, and source-display forms. Descriptions need to distinguish short, long, summary, storyline, and overview-style values so canonical field selection and future frontend presentation can work without reparsing raw source text.

Alternative considered:

- Preserve names and descriptions only as generic text collections. Rejected because it would weaken canonical selection, localized presentation, and provenance clarity.

### Integrate IGDB cache behavior into the main build

There will be no separate IGDB refresh command. `build-unified-db` will own IGDB access behavior:

- default: use exact cached query results when present, fetch missing results, and always store newly fetched values
- refresh mode: fetch and replace the cached value for exact queries
- offline mode: use cache only and record a build error on cache miss

This matches the existing repository’s request-cache behavior while keeping the operational workflow simple.

Alternative considered:

- Maintain a separate cache refresh lifecycle. Rejected because it adds unnecessary operational complexity and diverges from the current request model.

### Support workbook-driven overrides and generated templates from the start

Overrides are expected, so they will be first-class artifacts rather than deferred future work. A single workbook will contain multiple sheets for grouping, source associations, names, releases, series, and ignored rows. The system will validate workbook contents before the main build and generate templates automatically when requested or when missing.

Alternative considered:

- Wait to add overrides after the first automatic build. Rejected because ambiguity and special cases are already known to exist.

### Make unresolved-match diagnostics actionable without requiring guesswork

When a record cannot be resolved automatically, diagnostics must provide enough information for correction without forcing the user to reverse-engineer workbook entries or source IDs manually. Each unresolved match diagnostic will capture:

- the stable internal and source keys involved
- the source system that failed
- the configured override workbook file path
- the candidate rows that were considered, including source IDs, names, relevant metadata, and match scores
- the workbook sheet that should be edited to resolve the issue
- a ready-to-paste workbook row or line for the appropriate override sheet
- a helper command that can be run outside the main build to issue a manual search against the failed source with custom search terms

This makes the correction loop faster and reduces the chance of entering malformed overrides.

Alternative considered:

- Emit only generic error text and expect the user to compose override rows manually. Rejected because it would make ambiguity resolution too slow and error-prone.

### Add helper entrypoints for manual source search and diagnostics review

The unified dataset tooling will include small helper commands in addition to the main build flow so a user can investigate bad matches without rerunning the whole pipeline. At minimum, the toolset will support manual source searches for IGDB, TGDB, and LaunchBox-compatible data sources using user-provided search terms and platform context.

These helper commands are not replacements for the build; they exist to support correction workflows and override authoring after a partial build surfaces unresolved records.

Alternative considered:

- Force all investigation to happen through the main build only. Rejected because it is too slow and does not help when the user already knows a better search term than the automatic matcher used.

### Keep the command surface explicit and stable

The first implementation will expose:

- `build-unified-db` as the main operational entrypoint, including IGDB cache modes for on-demand cache use, refresh-and-replace, and offline/cache-only behavior
- `validate-overrides` for workbook validation without a full build
- `export-review-workbook` for diagnostics-oriented workbook export
- helper manual-search commands for source investigation outside the main build
- a Python runner module for IDE execution that calls the same build API as the CLI

Alternative considered:

- Leave the final command surface open-ended. Rejected because the workflow and diagnostics guidance already depend on known command names and behaviors.

### Keep series generation conservative and optional

Platform series and cross-platform series are separate concerns. Platform series support frontend browsing within one library and one platform. Cross-platform series support broader franchise or collection metadata. Series generation will use overrides first, then strong source-backed evidence, and will skip ambiguous cases instead of recording invalid groupings unless override data itself is invalid.

Alternative considered:

- Infer series aggressively from fuzzy title similarity. Rejected because incorrect frontend grouping is highly visible and not worth guessing.

### Preserve and rank asset candidates with region-aware fallback defaults

The unified dataset will keep all imported asset candidates and will rank default candidates using both source preference and region-aware fallback rules. LaunchBox is the preferred source for default ranking, followed by TGDB. For English/American-preferred presentation, asset fallback should prefer USA, then North America, then World, then Europe, then other regions, unless a more specific release/region match exists.

Language and region fields remain nullable when the source does not provide them, but the ranking behavior must still be deterministic.

Alternative considered:

- Preserve assets without any default ranking behavior. Rejected because the frontend and diagnostics need deterministic default candidate ordering.

### Provide both CLI and IDE-friendly entrypoints over the same build API

The implementation will expose a typed configuration object and a shared build orchestration layer. CLI parsing will populate that configuration, and a Python runner module for IDE use will call the same build API directly.

Alternative considered:

- Maintain separate CLI and IDE code paths. Rejected because duplicated control flow would drift.

### Make sort-name normalization explicit and fixed in the initial implementation

Canonical display names will remain human-normal, while canonical sort names will apply deterministic normalization rules. The initial implementation will normalize sort names by:

- moving trailing article forms such as `, The`, `, A`, and `, An` to the front
- converting roman numerals in recognizable sequel/title contexts to Arabic numbers
- normalizing repeated whitespace
- removing or normalizing punctuation that does not materially affect sort order
- folding case for sort-key generation

The initial implementation will keep these rules fixed rather than configurable so grouping, matching, and browse ordering remain deterministic.

Alternative considered:

- Make sort-name normalization configurable from the start. Rejected because fixed rules are simpler to reason about and match the current need better.

## Risks / Trade-offs

- [Datomatic title parsing will be messy and platform-specific] → Start from the existing parsing logic as reference, keep parsing output materialized in intermediate tables, and allow workbook overrides for grouping and release corrections.
- [Conservative matching will increase manual override work] → Emit detailed diagnostics and suggested override rows so correction is fast and deterministic.
- [Users may not agree with the automatic search term used for a failed match] → Include helper commands for manual source searches and surface the exact source system and target sheet in diagnostics.
- [Storing source mirrors increases database size] → Accept the size increase because normalized mirrors are needed for joins, auditability, and future asset/download workflows.
- [IGDB cache refresh can overwrite better historical results with newer source changes] → Restrict refresh behavior to exact-query replacement and preserve build diagnostics so changes are observable.
- [Keeping translation patches in official while other patches go to hacks can create borderline classification cases] → Encode patch taxonomy explicitly and allow release/grouping overrides to force classification when automatic parsing is unclear.
- [Series generation can still be subjective] → Treat series as optional, prefer overrides, and skip ambiguous auto-generated series rather than forcing a bad grouping.

## Migration Plan

1. Add the new unified dataset package, schema creation, typed configuration, and build orchestration in new files only.
2. Add Datomatic source mirrors and parsed-record generation using the existing codebase as reference.
3. Add source mirrors for TGDB, LaunchBox metadata XML, and IGDB cache tables.
4. Add canonical internal tables and deterministic key generation.
5. Add workbook template generation, workbook validation, and override application order.
6. Add external matching, canonical field selection, series generation, validation, and diagnostics.
7. Add optional review workbook export, helper investigation commands, and IDE-friendly runner modules.
8. Publish the unified SQLite build as a new output artifact, including partial builds with recorded diagnostics, without replacing any existing source-specific workflow.

Rollback is low risk because the change is additive and implemented in new files only. Reverting means not using the new build output and removing the new modules/artifacts without touching the existing code paths.
