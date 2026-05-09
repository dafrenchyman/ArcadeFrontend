# canonical-game-details Specification

## Purpose

TBD - created by archiving change add-frontend-library-configuration. Update Purpose after archive.

## Requirements

### Requirement: Canonical game detail screen overlays the wheel

Selecting a canonical game from the SNES wheel SHALL open a non-wheel canonical game detail screen that keeps the wheel visible in the background under a dimmed overlay.

#### Scenario: Canonical game opens detail screen

- **WHEN** the user confirms a canonical game in the SNES wheel
- **THEN** the frontend opens a non-wheel canonical game detail screen over the wheel instead of opening another wheel

#### Scenario: Back closes the first detail screen to the wheel

- **WHEN** the user presses cancel from the top level of the first canonical game detail screen
- **THEN** the frontend closes the detail screen and returns focus to the underlying wheel

### Requirement: Section-based detail-screen navigation

The canonical game detail screen SHALL use section-based navigation where up/down moves between sections, left/right acts within the focused section, accept enters or activates the focused section, and cancel backs out of the focused interaction mode.

#### Scenario: Up and down move between sections

- **WHEN** the detail screen is in normal section-navigation mode
- **THEN** pressing up or down changes the focused section instead of moving between owned releases directly

#### Scenario: Left and right act within focused section

- **WHEN** a detail-screen section is focused and supports horizontal navigation
- **THEN** pressing left or right changes the active item within that section

### Requirement: Owned release selector with immediate default persistence

The canonical game detail screen SHALL display all owned releases for the selected canonical game and allow the user to enter a release-selection mode that changes the default checked release and saves it immediately on confirmation.

#### Scenario: Release section highlights as one unit

- **WHEN** the owned release section is focused but not in release-edit mode
- **THEN** the frontend highlights the whole release list instead of a single release row

#### Scenario: Accept enters release-edit mode

- **WHEN** the owned release section is focused and the user presses accept
- **THEN** the frontend enters release-edit mode and allows up/down movement across owned release rows

#### Scenario: Confirm saves new default release

- **WHEN** the user changes the checked owned release and confirms the selection
- **THEN** the frontend saves that release as the canonical game's preferred default in the frontend-local database immediately

#### Scenario: Cancel leaves release-edit mode

- **WHEN** the user is in release-edit mode and presses cancel
- **THEN** the frontend exits release-edit mode and returns to section-based navigation

### Requirement: Play action launches the current preferred owned release

The canonical game detail screen SHALL present a play action that is ready on entry and launches the current preferred owned release using the launch-resolution precedence chain.

#### Scenario: Play is ready on screen entry

- **WHEN** the canonical game detail screen opens
- **THEN** the play action is present and available without requiring the user to open another menu first

#### Scenario: Play launches preferred owned release

- **WHEN** the user activates play from the canonical game detail screen
- **THEN** the frontend launches the currently preferred owned release for that canonical game

### Requirement: Favorite state on canonical game detail screen

The canonical game detail screen SHALL allow the user to mark or unmark the canonical game as a favorite and persist the favorite state in the frontend-local database.

#### Scenario: User marks game as favorite

- **WHEN** the user activates the favorite action for a canonical game that is not currently favorited
- **THEN** the frontend saves the game as a favorite in the frontend-local database

#### Scenario: User removes favorite

- **WHEN** the user activates the favorite action for a canonical game that is already favorited
- **THEN** the frontend removes the favorite flag while preserving favorite history fields needed by the local schema

### Requirement: Master-database metadata on canonical game screen

The canonical game detail screen SHALL display the canonical game's description and metadata from the master unified database, including release year, player count, publisher, and developer when available.

#### Scenario: Description comes from master database

- **WHEN** the canonical game detail screen renders a game that has master-database descriptions
- **THEN** the screen shows the selected description from the master database instead of requiring the description to come from exported JSON

#### Scenario: Metadata fields render when available

- **WHEN** the master database contains release year, player count, publisher, or developer data for the selected canonical game
- **THEN** the detail screen shows those fields on the canonical game screen

### Requirement: Screenshot browsing and fullscreen viewer

The canonical game detail screen SHALL include a screenshots section that allows horizontal browsing and fullscreen viewing.

#### Scenario: Left and right browse screenshots

- **WHEN** the screenshots section is focused in normal detail-screen navigation
- **THEN** pressing left or right changes the active screenshot in that section

#### Scenario: Accept opens fullscreen screenshot viewer

- **WHEN** the screenshots section is focused and the user presses accept
- **THEN** the frontend opens a fullscreen screenshot viewer for the active screenshot

#### Scenario: Fullscreen viewer cycles screenshots and exits on cancel

- **WHEN** the fullscreen screenshot viewer is open
- **THEN** left and right switch screenshots and cancel closes the viewer back to the canonical game detail screen

### Requirement: Related owned-game strips with nested detail-screen stack

The canonical game detail screen SHALL show related-content strips for same-series games and more games from the same publisher or developer, but only for owned canonical games on the same SNES system. Selecting a related game SHALL open another canonical game detail screen on a stack.

#### Scenario: Related strips include only owned SNES games

- **WHEN** the detail screen builds related-content strips
- **THEN** it includes only canonical games that are both owned and part of the SNES runtime library

#### Scenario: Series strip uses series ordering

- **WHEN** the detail screen renders related games from the same series
- **THEN** those games appear in series sort order when the master database provides it

#### Scenario: Publisher and developer strips prefer favorites then newest

- **WHEN** the detail screen renders related games from the same publisher or developer
- **THEN** those strips order games with favorites first and then newer games before older ones

#### Scenario: Selecting related game opens nested detail screen

- **WHEN** the user confirms a related canonical game from a related-content strip
- **THEN** the frontend opens that game as a new canonical game detail screen above the current one

#### Scenario: Back unwinds nested detail screens

- **WHEN** the user presses cancel from a nested canonical game detail screen
- **THEN** the frontend returns to the previous canonical game detail screen instead of jumping directly back to the wheel
