## 1. Project Skeleton

- [x] 1.1 Create a new unified dataset package/module tree in new files only, with shared configuration types, build orchestration, and stage boundaries
- [x] 1.2 Add a CLI entrypoint for `build-unified-db`, `validate-overrides`, and `export-review-workbook`
- [x] 1.3 Add an IDE-friendly Python runner that uses the same build API and typed configuration as the CLI
- [x] 1.4 Add helper CLI entrypoints for manual source searches and diagnostics inspection outside the main build flow
- [x] 1.5 Implement the agreed command behaviors for `build-unified-db`, including IGDB on-demand, refresh-and-replace, and offline/cache-only modes

## 2. Schema And Source Mirrors

- [x] 2.1 Define and create the unified SQLite schema for libraries, platforms, games, releases, ROMs, names, descriptions, genres, companies, regions, languages, series, assets, diagnostics, and build metadata
- [x] 2.2 Define typed name and description storage that preserves canonical, localized, alternate, source-display, short, long, summary, storyline, and overview forms when available
- [x] 2.3 Define and create normalized source mirror tables for Datomatic, IGDB, TGDB, and LaunchBox in the unified database
- [x] 2.4 Define and create source-specific association tables between canonical internal entities and source-native rows

## 3. Datomatic Parsing And Canonical Grouping

- [x] 3.1 Implement Datomatic import into source mirror tables using the existing repository logic as reference without modifying existing files
- [x] 3.2 Implement deterministic Datomatic source-key generation from system, raw title, hashes, and size
- [x] 3.3 Implement readable deterministic internal key generation for games, releases, and series based on the agreed key contents
- [x] 3.4 Implement parsed Datomatic intermediate records that extract normalized titles, release classification, patch taxonomy, regions, languages, revision/version markers, and library classification
- [x] 3.5 Implement deterministic grouping from parsed Datomatic records into canonical games, releases, ROMs, release-language rows, and release-region rows, including separate game handling for competition/special standalone titles and explicit `World` region handling

## 4. Source Enrichment And Matching

- [x] 4.1 Import TGDB dump data into normalized source mirror tables
- [x] 4.2 Import LaunchBox metadata XML into normalized source mirror tables
- [x] 4.3 Reuse the existing IGDB cache pattern in new code so exact cached queries are reused, misses are fetched and stored, refresh replaces exact cached rows, and offline mode records build errors on cache miss
- [x] 4.4 Implement conservative candidate generation plus quality-gated fuzzy matching for IGDB, TGDB, and LaunchBox
- [x] 4.5 Persist accepted, rejected, and ambiguous source match candidates for diagnostics and later review export
- [x] 4.6 Implement source-specific manual search helpers that accept user-provided search terms and platform context for IGDB, TGDB, and LaunchBox investigation

## 5. Overrides, Validation, And Templates

- [x] 5.1 Implement override workbook template generation with all required sheets and headers
- [x] 5.2 Implement override workbook validation for sheet structure, required columns, stable key formats, codes, and external source references
- [x] 5.3 Implement deterministic override application order for ignore, grouping, release, source association, name, and series overrides
- [x] 5.4 Implement input-validation hard stops plus record-level diagnostics rules for parsing, grouping, matching, canonical field selection, and final database integrity
- [x] 5.5 Persist build diagnostics and build-status metadata so partial databases can be published with unresolved records called out explicitly
- [x] 5.6 Include candidate choices, target override sheet names, ready-to-paste override rows, and helper manual-search commands in unresolved-match diagnostics
- [x] 5.7 Include the configured override workbook file path in unresolved-match diagnostics and summaries so corrections point to the right file as well as the right sheet

## 6. Canonical Selection, Series, And Assets

- [x] 6.1 Implement canonical field selection for names, sort names, descriptions, release year, players, coop, genres, companies, and provenance tracking
- [x] 6.2 Implement the fixed per-field source precedence rules for canonical selection, with IGDB-leading semantic metadata and Datomatic-led release identity
- [x] 6.3 Implement the English/American-preferred locale policy for canonical names and default presentation fallbacks while preserving localized values
- [x] 6.4 Implement platform-scoped and cross-platform series generation with conservative auto-generation and override support
- [x] 6.5 Implement asset-candidate aggregation from TGDB and LaunchBox metadata with region/language fields nullable when unknown
- [x] 6.6 Implement deterministic default asset ranking using the agreed source precedence and region fallback order

## 7. Output And Review Workflow

- [x] 7.1 Implement final unified database publication with explicit build-status metadata so valid partial builds can be published and distinguished from clean builds
- [x] 7.2 Implement review workbook export for diagnostics, unresolved matches, grouping conflicts, and metadata inspection
- [x] 7.3 Add build summaries and stage-aware error reporting that identify suggested override sheets, relevant stable keys, candidate options, and ready-to-paste override lines

## 8. Verification

- [x] 8.1 Run the unified build for SNES in cache-first mode and verify the output database contains canonical entities, source mirrors, associations, and diagnostics tables
- [x] 8.2 Run the unified build in IGDB refresh mode and verify exact cached query rows are replaced
- [x] 8.3 Run the unified build in IGDB offline mode with a forced cache miss and verify the build records diagnostics, leaves affected associations unresolved, and still publishes a database with build errors recorded
- [x] 8.4 Validate that the override workbook template, override validation flow, and review workbook export are LibreOffice-compatible
