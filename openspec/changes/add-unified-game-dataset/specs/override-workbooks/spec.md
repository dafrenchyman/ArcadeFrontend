## ADDED Requirements

### Requirement: The system SHALL define workbook-driven overrides as first-class build inputs

The build SHALL accept an override workbook that can influence grouping, source associations, names, releases, series memberships, and ignored rows. The workbook SHALL use stable keys such as deterministic Datomatic source keys, deterministic internal build keys, and external source IDs rather than generated numeric row IDs.

#### Scenario: Applying a grouping override

- **WHEN** the override workbook provides a grouping rule for a Datomatic source key
- **THEN** the build applies that grouping decision before automatic grouping finalization

### Requirement: The system SHALL use readable deterministic key contents in override workflows

The override workflow SHALL use readable deterministic keys whose contents include the library slug, platform slug, and normalized identity inputs needed to reproduce the target entity. Game keys SHALL include the canonical grouping identity and game kind. Release keys SHALL include the parent game key plus release identity inputs such as region set, release type, and revision/version markers when applicable.

#### Scenario: Using a generated internal game key in an override

- **WHEN** the build generates a ready-to-paste override row for a game-level source association
- **THEN** the internal game key in that row is readable and reproducible from the game’s library, platform, normalized title identity, and game kind

### Requirement: The system SHALL support a fixed set of override workbook sheets

The override workbook SHALL include sheets for `grouping_override`, `game_source_override`, `release_override`, `name_override`, `series_override`, and `ignore_override`.

#### Scenario: Validating workbook sheet presence

- **WHEN** the build validates an override workbook
- **THEN** the build confirms that all required override sheets are present with the required columns

### Requirement: The system SHALL define required columns for each override workbook sheet

The override workbook SHALL define the following required columns for each supported sheet:

- `grouping_override`: `enabled`, `library_slug`, `platform_slug`, `datomatic_source_key`, `datomatic_raw_title`, `forced_internal_game_key`, `forced_internal_game_name`, `forced_internal_release_key`, `forced_release_title`, `forced_game_kind`, `forced_release_type`, `notes`
- `game_source_override`: `enabled`, `library_slug`, `platform_slug`, `internal_game_key`, `internal_game_name`, `igdb_game_id`, `tgdb_game_id`, `launchbox_game_id`, `preferred_name_source`, `preferred_description_source`, `notes`
- `release_override`: `enabled`, `datomatic_source_key`, `datomatic_raw_title`, `forced_internal_release_key`, `forced_release_title`, `forced_primary_region_codes`, `forced_language_codes`, `forced_release_type`, `forced_revision_label`, `forced_version_label`, `base_release_key`, `notes`
- `name_override`: `enabled`, `target_type`, `internal_key`, `name`, `sort_name`, `name_type`, `language_code`, `region_code`, `is_preferred`, `is_preferred_en_us`, `notes`
- `series_override`: `enabled`, `series_scope`, `series_key`, `series_name`, `platform_slug`, `internal_game_key`, `internal_game_name`, `sort_order`, `notes`
- `ignore_override`: `enabled`, `ignore_scope`, `source_key_or_id`, `related_internal_key`, `reason`, `notes`

The system MAY add non-required informational columns in generated templates, but it MUST preserve the required columns and their semantics.

#### Scenario: Generating a complete override template

- **WHEN** the system generates an override workbook template
- **THEN** each required sheet contains its required columns in the generated workbook

### Requirement: The system SHALL validate override workbooks before main build processing

The system SHALL validate workbook structure, required columns, stable key formats, region and language codes, conflicting enabled rows, and referenced external source IDs before the main build proceeds.

#### Scenario: Rejecting an invalid override workbook

- **WHEN** an enabled override row references an invalid external source ID or contains conflicting values
- **THEN** the validation process fails and the main build does not continue

#### Scenario: Rejecting a workbook with missing required columns

- **WHEN** an override workbook is missing one of the required columns for a supported sheet
- **THEN** the validation process fails and identifies the sheet and missing columns before main build processing begins

### Requirement: The system SHALL apply overrides in a deterministic order

The system SHALL apply ignore overrides before grouping-sensitive work, SHALL apply grouping and release overrides before automatic grouping is finalized, SHALL apply source-association overrides before final source resolution, and SHALL apply name and series overrides before final validation.

#### Scenario: Resolving a source match with an override

- **WHEN** automatic matching produces candidates for a game and a source-association override exists
- **THEN** the build uses the override as the authoritative association decision before final validation

### Requirement: The system SHALL generate override workbook templates

The system SHALL be able to generate a workbook template with all supported override sheets and required columns so users can populate overrides without creating workbook structure manually.

#### Scenario: Creating an override template

- **WHEN** the user requests an override workbook template or the configured workbook is missing and template generation is enabled
- **THEN** the system creates a workbook containing all supported override sheets with the required headers

### Requirement: The system SHALL emit diagnostics that support workbook-based correction

When the build encounters unresolved ambiguity, invalid inputs, or conflicting source data, the system SHALL record diagnostics that identify the affected stage, relevant stable keys, candidate source rows, the configured override workbook file path, and the workbook sheet most likely to resolve the issue. For record-level issues, the system SHALL keep processing and publish the diagnostics with the resulting database.

#### Scenario: Emitting a workbook-oriented diagnostics record

- **WHEN** an IGDB match remains ambiguous during build processing
- **THEN** the diagnostics identify the relevant internal game key, the competing source candidates, the configured override workbook path, and the source-override sheet that can be used to correct the ambiguity

### Requirement: The system SHALL include candidate choices and ready-to-paste override content in unresolved-match diagnostics

For unresolved source matches, the system SHALL record the candidate source rows that were considered, including source IDs, display names, relevant supporting metadata, and match scores. The diagnostics SHALL also identify the override sheet to edit and SHALL include a ready-to-paste row or line formatted for that sheet.

#### Scenario: Suggesting a source override row

- **WHEN** an unresolved TGDB or IGDB match is recorded
- **THEN** the diagnostics include the candidate source options, the target override sheet, and a ready-to-paste override row that references the stable internal key and the selected source ID format

### Requirement: The system SHALL expose helper commands for manual source searches after unresolved matches

The tooling SHALL provide helper commands that allow a user to search a source system manually with custom search terms outside the main build. Unresolved-match diagnostics SHALL include the relevant helper command form so the user can refine the search when the automatic candidates are poor.

#### Scenario: Suggesting a manual search command

- **WHEN** an unresolved source match has poor or obviously unrelated candidate results
- **THEN** the diagnostics include a helper command that identifies the failed source system and shows how to rerun a manual search with custom search terms

### Requirement: The system SHALL support optional review workbook export

The system SHALL support exporting a review workbook from a successful, partial, or input-invalid build so that unresolved matches, grouping conflicts, missing metadata, and other diagnostics can be inspected in LibreOffice-compatible form.

#### Scenario: Exporting a review workbook after a partial build

- **WHEN** the user requests a review workbook and the build completes with recorded diagnostics
- **THEN** the system exports a workbook that includes the recorded diagnostics and review-oriented sheets derived from the preserved build state
