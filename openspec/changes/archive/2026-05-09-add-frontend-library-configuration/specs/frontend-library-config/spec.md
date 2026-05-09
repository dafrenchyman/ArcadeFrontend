## ADDED Requirements

### Requirement: Root escape menu and configuration flow

The frontend SHALL open a non-wheel root escape menu when the user presses cancel at the root wheel instead of quitting immediately. The root escape menu SHALL dim the background, keep the wheel visible behind it, and offer exit and configuration actions.

#### Scenario: Root cancel opens escape menu

- **WHEN** the user presses cancel while focused on the root wheel
- **THEN** the frontend opens a dimmed non-wheel menu over the wheel with exit and configuration choices

#### Scenario: Escape menu exits the application

- **WHEN** the root escape menu is open and the user confirms the exit action
- **THEN** the frontend quits the application

#### Scenario: Escape menu opens configuration

- **WHEN** the root escape menu is open and the user confirms the configuration action
- **THEN** the frontend opens a non-wheel configuration menu over the dimmed wheel background

### Requirement: Frontend local runtime database

The frontend SHALL maintain its own SQLite database for runtime state instead of storing all live library state only in `config.json`. The frontend-local database SHALL persist settings, scan state, owned release associations, favorites, preferred releases, launch overrides, and cached asset metadata.

#### Scenario: Frontend initializes local runtime storage

- **WHEN** the frontend starts and the frontend-local database does not exist
- **THEN** the frontend creates the database with the required runtime tables before library generation or configuration actions proceed

#### Scenario: Runtime state survives restart

- **WHEN** a user restarts the frontend after changing settings, scanning ROMs, or marking favorites
- **THEN** the previously saved settings, owned-library state, and preferences remain available from the frontend-local database

### Requirement: SNES system setup

The configuration flow SHALL provide a setup path for the SNES system that allows users to enter a ROM root path as plain text, set system-level emulator defaults, and trigger a library scan.

#### Scenario: User configures SNES ROM root path

- **WHEN** the user opens configuration, enters the SNES setup screen, and saves a ROM root path
- **THEN** the frontend persists the ROM root path for SNES in the frontend-local database

#### Scenario: User configures system-level launch defaults

- **WHEN** the user saves SNES emulator or launch-template defaults in the setup screen
- **THEN** the frontend persists those launch defaults for the SNES system

### Requirement: Recursive scan with progress

The SNES setup flow SHALL scan the configured ROM root recursively for supported files, compute progress from a discovered file total, and present progress through discovery, hashing, matching, and asset-fetch phases.

#### Scenario: Scan counts candidate files before hashing

- **WHEN** the user starts an SNES scan
- **THEN** the frontend discovers all candidate files recursively before hashing begins so the scan has a known total file count

#### Scenario: Scan reports stage-aware progress

- **WHEN** the scan is running
- **THEN** the frontend reports progress that distinguishes discovery, hashing, matching, and asset-fetch work

#### Scenario: Scan filters to supported SNES extensions

- **WHEN** the frontend discovers files under the configured SNES ROM root
- **THEN** it only treats `.sfc` and `.smc` files as scan candidates in the first implementation

### Requirement: Full configuration export with compatibility retention

The frontend SHALL retain `config.json` compatibility while also supporting export of a full generated configuration snapshot that includes generated library content and artwork references.

#### Scenario: User exports generated configuration

- **WHEN** the user triggers configuration export
- **THEN** the frontend writes a full configuration snapshot derived from the current runtime library and saved settings

#### Scenario: Export preserves artwork references

- **WHEN** the frontend exports configuration after assets have been cached
- **THEN** the exported configuration includes artwork references needed to reconstruct the generated library presentation
