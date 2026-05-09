## ADDED Requirements

### Requirement: Clear-logo selection for wheel presentation

The frontend SHALL select one clear logo per canonical game for wheel presentation and SHALL fall back to text when no suitable clear logo can be resolved.

#### Scenario: Wheel uses resolved clear logo

- **WHEN** the asset cache has a selected clear logo for a canonical game
- **THEN** the wheel renders that clear logo for the canonical game's wheel item

#### Scenario: Wheel falls back to text when no clear logo exists

- **WHEN** the frontend cannot resolve a clear logo for a canonical game
- **THEN** the wheel renders the canonical game's text name instead of another artwork type

### Requirement: LaunchBox-first clear-logo precedence with regional fallback

The frontend SHALL prefer LaunchBox clear logos over TGDB clear logos and SHALL select the default clear logo for the USA/EN policy using this region fallback order: `USA`, `NORTH_AMERICA`, `WORLD`, `EUROPE`, `OCEANIA`, `JAPAN`, then any remaining region.

#### Scenario: USA logo beats broader fallback

- **WHEN** both `USA` and `WORLD` clear logos are available for a canonical game
- **THEN** the frontend selects the `USA` clear logo as the default wheel logo

#### Scenario: World logo beats Europe fallback

- **WHEN** `WORLD` and `EUROPE` clear logos are available but no `USA` or `NORTH_AMERICA` logo exists
- **THEN** the frontend selects the `WORLD` clear logo

#### Scenario: Japan logo is used for Japan-only title

- **WHEN** a canonical game has no better regional fallback than `JAPAN`
- **THEN** the frontend selects the `JAPAN` clear logo instead of falling back to text

### Requirement: Poster selection precedence

The frontend SHALL select one poster asset for the canonical game detail screen using LaunchBox first and TGDB as fallback, with `Fanart - Box - Front` preferred over `Box - Front`.

#### Scenario: Fanart box front beats box front

- **WHEN** both `Fanart - Box - Front` and `Box - Front` poster candidates are available for a canonical game
- **THEN** the frontend selects `Fanart - Box - Front` as the canonical game poster

#### Scenario: TGDB poster fills LaunchBox gap

- **WHEN** no qualifying LaunchBox poster candidate exists and a qualifying TGDB poster candidate is available
- **THEN** the frontend selects the TGDB poster candidate

### Requirement: Screenshot-set aggregation

The frontend SHALL gather all available screenshot assets for a canonical game and cache them as a set for the canonical game detail screen instead of selecting only one screenshot.

#### Scenario: Multiple screenshots are cached

- **WHEN** multiple screenshot candidates are available for a canonical game
- **THEN** the frontend caches all of them for the canonical game detail screen

#### Scenario: Screenshots remain browsable in stable order

- **WHEN** the frontend builds the screenshot set for a canonical game
- **THEN** it preserves a deterministic order for screenshot browsing across launches

### Requirement: Semantic cache naming and metadata persistence

The frontend SHALL cache downloaded assets using semantic file naming that does not depend on unstable master-database row IDs and SHALL persist cache metadata in the frontend-local database.

#### Scenario: Cached asset path avoids numeric row IDs

- **WHEN** the frontend writes a downloaded logo, poster, or screenshot to the local asset cache
- **THEN** the cache filename is derived from semantic values such as system, canonical game identity, asset role, and source fingerprint rather than a numeric master-database row ID

#### Scenario: Cache metadata supports reuse

- **WHEN** the frontend has previously cached selected artwork for a canonical game
- **THEN** the frontend-local database contains the metadata needed to reuse those cached files without reselecting them from scratch
