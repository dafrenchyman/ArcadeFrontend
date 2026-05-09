## ADDED Requirements

### Requirement: The system SHALL build a multi-platform-capable unified dataset from local source inputs

The system SHALL build a unified SQLite database that can represent more than one platform even when a given build run targets SNES first. The build SHALL ingest Datomatic input as the root source of identity, SHALL ingest TGDB and LaunchBox metadata from local source files, and SHALL ingest IGDB data through the existing cache-backed request model.

#### Scenario: Building a platform-scoped unified database

- **WHEN** the user runs the unified dataset build for a platform with valid Datomatic, TGDB, LaunchBox, and IGDB inputs
- **THEN** the system creates a unified SQLite database containing normalized source mirrors and canonical internal tables for that platform

### Requirement: The system SHALL publish the required canonical, source, and diagnostics table families

The unified SQLite database SHALL include table families that cover at least the following roles:

- canonical identity tables for libraries, platforms, games, game releases, release ROMs, game names, release names, and game descriptions
- canonical classification and metadata tables for languages, regions, genres, companies, game-to-genre links, game-to-company links, release-to-language links, and release-to-region links
- series tables for platform-scoped series, cross-platform series, and their membership links
- asset-candidate tables that can attach assets at game or release scope with nullable region and language fields when unknown
- normalized source mirror tables for Datomatic, IGDB, TGDB, and LaunchBox
- source-association tables that map canonical entities to source-native rows without reusing external IDs as canonical primary keys
- diagnostics and build-status tables that record unresolved records, candidate matches, build errors, and overall build status

The first implementation MAY use different concrete table names than the design examples, but it MUST provide this coverage in the published unified database.

#### Scenario: Verifying required table-family coverage

- **WHEN** a unified database is produced successfully or as a partial build
- **THEN** the database contains canonical, source-mirror, source-association, and diagnostics/build-status table families that satisfy the required roles

### Requirement: The system SHALL preserve typed names and typed descriptions

The unified dataset SHALL preserve names and descriptions with explicit type information rather than as untyped text collections. Name storage SHALL support at least canonical, localized, alternate, and source-display forms. Description storage SHALL support at least short, long, summary, storyline, and overview-style forms when those source values exist.

#### Scenario: Preserving a localized alternate title

- **WHEN** a game has both an English-facing title and a region-specific localized title such as a Japanese market name
- **THEN** the dataset stores both names with explicit type and locale/region metadata instead of collapsing them into one canonical string

#### Scenario: Preserving multiple description forms

- **WHEN** multiple source systems provide summary, storyline, or overview text for the same game
- **THEN** the dataset stores those descriptions with explicit description types and preserves them for canonical selection and later frontend use

### Requirement: The system SHALL model canonical game, release, and ROM identities separately

The unified dataset SHALL model a frontend-facing game as a distinct entity from its selectable releases and exact ROM identities. A game release SHALL support multiple regions and multiple playable languages, and a release SHALL support one or more ROM rows.

#### Scenario: Modeling a multilingual European release

- **WHEN** a Datomatic row represents a European release with multiple supported languages
- **THEN** the system creates one game release, associates multiple release-language rows, associates the relevant release-region rows, and links one or more ROM rows to that release

### Requirement: The system SHALL distinguish separately browseable titles from release variants

The unified dataset SHALL create separate game rows for products that users should browse as distinct title entries, including competition cartridges and other standalone special editions. The unified dataset SHALL keep region variants, revisions, versions, prototypes, demos, translation patches, and bugfix patches as game releases under a game unless an override explicitly forces a different grouping.

#### Scenario: Storing a competition cartridge

- **WHEN** a Datomatic row represents a competition cartridge or comparable standalone special edition
- **THEN** the system creates a separate game row rather than storing it only as a release under another game

#### Scenario: Storing a regional revision

- **WHEN** a Datomatic row represents a regional variant or revision of an existing title
- **THEN** the system stores it as a game release under the existing game unless an override says otherwise

### Requirement: The system SHALL preserve the planned canonical data relationships

The unified dataset SHALL preserve these canonical relationship rules:

- a platform belongs to one library
- a game belongs to one platform
- a game release belongs to one game
- a release ROM belongs to one game release
- a game may have zero or more names and descriptions
- a game release may have zero or more release names
- a game release may have zero or more release-language links and zero or more release-region links
- a game may belong to zero or more platform-series memberships and zero or more cross-platform-series memberships
- a `World` release is represented through explicit release-region data rather than by omitting region data

#### Scenario: Inspecting a game with releases and series memberships

- **WHEN** a canonical game is queried from the unified database
- **THEN** the database structure supports traversing from the game to its platform, releases, ROMs, names, descriptions, and optional series memberships through explicit relational links

### Requirement: The system SHALL keep translation patches and bugfix patches in the official library

The unified dataset SHALL classify translation patches and bugfix patches into the official library for the platform title they derive from. Gameplay hacks, total conversions, randomizers, and similar mod-style releases SHALL be classified into hacks libraries instead.

#### Scenario: Classifying a translation patch

- **WHEN** a parsed source record is identified as a translation patch of an official title
- **THEN** the system stores it in the official library as a separate selectable release under the same game

### Requirement: The system SHALL preserve source-native joins through source mirrors and association tables

The unified dataset SHALL retain normalized source mirror tables for Datomatic, IGDB, TGDB, and LaunchBox, and SHALL map canonical internal entities to source-native rows through source-specific association tables rather than by reusing external IDs as canonical primary keys.

#### Scenario: Mapping one internal game to multiple source rows

- **WHEN** one internal game corresponds to more than one TGDB or LaunchBox record because of region-specific naming or metadata differences
- **THEN** the system stores multiple source associations without duplicating the canonical game row

### Requirement: The system SHALL support the required source-association scopes

The unified dataset SHALL support source associations at the scopes needed by the planned model:

- game-level associations for IGDB, TGDB, LaunchBox, and Datomatic-derived game groupings
- release-level associations for Datomatic-derived release records and any source-specific release-level mappings that become available
- ROM-level associations for Datomatic-derived exact file identities

#### Scenario: Linking a canonical release ROM back to its source record

- **WHEN** a release ROM row is inspected in the unified database
- **THEN** the database supports linking that ROM back to the Datomatic-derived source row through an explicit source-association path

### Requirement: The system SHALL use quality-gated fuzzy matching for external source associations

The unified dataset SHALL use source-specific fuzzy matching to find candidate IGDB, TGDB, and LaunchBox associations for internal games. The system SHALL accept strong fuzzy matches automatically when they exceed the configured quality threshold and clearly outrank competing candidates, and SHALL record unresolved build errors when no candidate is good enough or when multiple candidates remain too close to choose safely.

#### Scenario: Accepting a strong fuzzy match

- **WHEN** the best candidate for a source association exceeds the required quality threshold and clearly outranks the next-best candidate
- **THEN** the system accepts that match automatically and records the accepted candidate in diagnostics and source association tables

#### Scenario: Rejecting a weak or near-tied fuzzy match

- **WHEN** the best candidate does not meet the required quality threshold or remains too close to another plausible candidate
- **THEN** the build treats the source association as unresolved, records a diagnostics error, and leaves the association unset unless an override resolves it

#### Scenario: Recording candidate options for an unresolved match

- **WHEN** a source association remains unresolved after fuzzy matching
- **THEN** the build records the considered candidate options, including source IDs, candidate names, and match scores, in the diagnostics output

### Requirement: The system SHALL use cache-backed IGDB access during the main build

The unified dataset build SHALL use cached IGDB results when an exact query already exists, SHALL fetch and store results when an exact query is missing, SHALL support an offline mode that records a build error on cache miss, and SHALL support a refresh mode that replaces the cached result for an exact query.

#### Scenario: Fetching an uncached IGDB query during build

- **WHEN** the build needs an IGDB query result that is not already cached and offline mode is not enabled
- **THEN** the system fetches the result from IGDB, stores it in the cache, and uses it for the current build

#### Scenario: Running in offline IGDB mode

- **WHEN** the build needs an IGDB query result that is not already cached and offline mode is enabled
- **THEN** the build records the missing query as a diagnostics error and continues processing other records

#### Scenario: Refreshing a cached IGDB query

- **WHEN** refresh mode is enabled and the build executes an IGDB query that already exists in cache
- **THEN** the system fetches a fresh result and replaces the cached result for that exact query

### Requirement: The system SHALL choose canonical display values while preserving all source values

The unified dataset SHALL preserve all names, descriptions, company links, genre links, and asset candidates that are imported from source systems, and SHALL also publish preferred canonical values on the main game rows for frontend use. Canonical display names SHALL remain human-normal, and canonical sort names SHALL apply normalization such as article cleanup and roman numeral normalization.

#### Scenario: Selecting canonical and sort names

- **WHEN** a game has multiple candidate names from IGDB, TGDB, LaunchBox, and Datomatic-derived parsing
- **THEN** the system stores the preferred display name as the canonical name and stores a normalized sort name separately

### Requirement: The system SHALL apply an English/American-preferred default locale policy

The unified dataset SHALL choose canonical presentation defaults using an English/American-preferred locale policy. When multiple localized names exist, canonical display defaults SHALL prefer English and English-speaking-region values before non-English localized variants. The dataset SHALL still preserve the localized variants in supporting tables.

#### Scenario: Choosing between English and Japanese title forms

- **WHEN** a game has both an English-facing title and a Japanese localized title in imported source data
- **THEN** the canonical display default uses the English-facing title while the Japanese title remains preserved as a localized or alternate name

### Requirement: The system SHALL apply fixed source precedence for canonical field selection

The unified dataset SHALL apply fixed source precedence rules when selecting canonical game-level fields:

- canonical display name: `name_override`, then IGDB, then TGDB, then LaunchBox, then Datomatic-derived cleaned title
- canonical sort name: `name_override.sort_name`, then deterministic normalization of the chosen canonical display name
- short description: IGDB summary, then TGDB overview, then LaunchBox overview
- long description: IGDB storyline, then TGDB overview, then LaunchBox overview
- release year: IGDB first-release year, then TGDB, then LaunchBox, then Datomatic when trustworthy
- players and coop: IGDB, then TGDB, then LaunchBox
- primary genres, developers, and publishers: IGDB, then TGDB, then LaunchBox, while preserving all imported values in supporting tables
- release identity, regions, and languages: Datomatic
- default asset ranking: LaunchBox, then TGDB, while preserving all imported candidates

#### Scenario: Selecting a preferred description

- **WHEN** IGDB, TGDB, and LaunchBox each provide game-level descriptive text
- **THEN** the system selects the canonical short and long descriptions according to the fixed precedence rules while still preserving all imported descriptions in supporting tables

### Requirement: The system SHALL rank default asset candidates deterministically

The unified dataset SHALL preserve all imported asset candidates and SHALL rank default candidates deterministically using source precedence and region-aware fallback. For English/American-preferred defaults, candidate ranking SHALL prefer exact release-region matches first, then USA, then North America, then World, then Europe, and then other fallback regions, unless overrides or stronger release-specific evidence apply.

#### Scenario: Choosing default box art for an English-first frontend

- **WHEN** a game or release has box-art candidates from multiple sources and regions
- **THEN** the dataset ranks default candidates deterministically according to the asset source and region fallback rules while still preserving all asset rows

### Requirement: The system SHALL support platform-scoped and cross-platform series separately

The unified dataset SHALL represent platform-scoped series for frontend browsing separately from cross-platform series for broader franchise or collection metadata. Platform series SHALL remain constrained to one library and one platform.

#### Scenario: Grouping games into a platform series

- **WHEN** multiple games on the same library and platform are grouped into the same platform series through overrides or strong source-backed evidence
- **THEN** the system stores the shared platform series membership without forcing series rows for unrelated singleton games

### Requirement: The system SHALL complete processing and publish diagnostics-backed partial builds

The unified dataset build SHALL continue processing after record-level grouping, matching, or canonical-selection failures so long as the build inputs themselves are valid. The build SHALL preserve diagnostics, SHALL publish a unified database even when unresolved records remain, and SHALL mark the build result so downstream workflows can detect that the output contains known gaps.

#### Scenario: Ambiguous source match

- **WHEN** multiple plausible external matches remain for a game and no override resolves them
- **THEN** the build records diagnostics for the unresolved game, leaves the ambiguous association unset, and still publishes the unified database with build errors recorded

#### Scenario: No acceptable fuzzy source match

- **WHEN** no candidate source match meets the required quality threshold for a game
- **THEN** the build records diagnostics for the unresolved game, leaves the association unset, and still publishes the unified database with build errors recorded

#### Scenario: Invalid build input

- **WHEN** the override workbook structure or another required build input is invalid before processing begins
- **THEN** the build stops before data processing starts and reports the input validation errors

### Requirement: The system SHALL persist structured diagnostics in the published database

The unified dataset SHALL persist structured diagnostics in the published database for unresolved grouping, matching, canonical-selection, and source-access issues. Diagnostics SHALL retain enough information to identify the affected stable keys, source systems, candidate options, and build stage.

#### Scenario: Reviewing unresolved records in the published database

- **WHEN** a partial unified build is opened after processing completes
- **THEN** the published database contains structured diagnostics rows that identify unresolved records and the information needed to correct them

### Requirement: The system SHALL support both CLI and IDE-driven execution through one shared build API

The unified dataset build SHALL expose a CLI entrypoint and a Python configuration-driven runner that both use the same underlying build orchestration logic.

#### Scenario: Running the build from an IDE

- **WHEN** a developer configures the build in a Python runner and starts it from an IDE
- **THEN** the system executes the same build behavior and validation rules as the CLI entrypoint

### Requirement: The system SHALL expose the agreed command surface

The unified dataset tooling SHALL expose `build-unified-db` as the main operational command, `validate-overrides` for workbook validation, `export-review-workbook` for diagnostics/review export, and helper manual-search commands for source investigation. `build-unified-db` SHALL own the IGDB cache behavior modes for on-demand cache use, refresh-and-replace, and offline/cache-only operation.

#### Scenario: Running the main build with IGDB refresh mode

- **WHEN** the user runs `build-unified-db` with the IGDB refresh behavior enabled
- **THEN** the build uses the main build command to refresh-and-replace cached IGDB query results instead of requiring a separate refresh command
