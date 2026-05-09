# scanned-system-library Specification

## Purpose

TBD - created by archiving change add-frontend-library-configuration. Update Purpose after archive.

## Requirements

### Requirement: Scanned SNES library generation

The frontend SHALL generate the SNES runtime library by combining the fixed master unified database at `database/database/unified_snes.db` with frontend-local scan state. The generated library SHALL group owned ROMs by canonical game and then by owned releases under each game.

#### Scenario: Owned release matches appear under one canonical game

- **WHEN** the frontend has multiple owned release matches for the same canonical SNES game
- **THEN** the runtime library presents one canonical game entry that contains those owned releases as selectable variants

#### Scenario: Canonical grouping follows the master database

- **WHEN** the master database groups multiple releases under one canonical game
- **THEN** the frontend runtime library uses that canonical grouping instead of creating separate top-level wheel entries for each owned release

### Requirement: Hash-based release ownership matching

The frontend SHALL identify owned SNES ROMs by hashing scanned files and matching them to master-database release ROM rows using SHA-1 first and MD5 second.

#### Scenario: SHA-1 match identifies owned release

- **WHEN** a scanned file SHA-1 matches a master-database `release_roms` row
- **THEN** the frontend records ownership for the associated release

#### Scenario: MD5 fallback identifies owned release

- **WHEN** a scanned file has no SHA-1 match but its MD5 matches a master-database `release_roms` row
- **THEN** the frontend records ownership for the associated release

#### Scenario: Unmatched file remains unidentified

- **WHEN** a scanned file matches neither SHA-1 nor MD5 in the master database
- **THEN** the frontend records the file as unidentified instead of assigning it to a canonical game

### Requirement: Unidentified SNES wheel

The runtime root library SHALL include an `Unidentified SNES` wheel that lists unmatched SNES files individually by filename.

#### Scenario: Unidentified entries appear as separate items

- **WHEN** a scan produces unmatched SNES files
- **THEN** the runtime root library includes an `Unidentified SNES` wheel with one entry per unmatched filename

#### Scenario: Identified files do not appear in unidentified wheel

- **WHEN** a scanned file is matched to an owned release
- **THEN** that file is excluded from the `Unidentified SNES` wheel

### Requirement: Launch-resolution precedence

The frontend SHALL resolve launch commands through a layered override chain with owned-release overrides taking precedence over canonical-game overrides, canonical-game overrides taking precedence over system defaults, and system defaults taking precedence over exported or compatibility fallback configuration.

#### Scenario: Owned-release override wins

- **WHEN** an owned release has a specific launch override and the canonical game or system also has launch defaults
- **THEN** the frontend launches the owned release using the owned-release override

#### Scenario: Canonical-game override wins over system default

- **WHEN** a canonical game has a launch override and the selected owned release has no specific override
- **THEN** the frontend launches the game using the canonical-game override instead of the system default

#### Scenario: System default applies when no narrower override exists

- **WHEN** neither the selected owned release nor the canonical game has an override
- **THEN** the frontend launches using the configured SNES system default
