## 1. Frontend Runtime Storage

- [x] 1.1 Define the frontend-local SQLite schema for settings, systems, scans, discovered files, owned release matches, favorites, preferred releases, launch overrides, and cached asset metadata.
- [x] 1.2 Add frontend data-access code for creating, opening, and migrating the frontend-local database at startup.
- [x] 1.3 Add runtime configuration loading that preserves `config.json` compatibility while making the frontend-local database the primary runtime state store.

## 2. Root Menus And Configuration Flow

- [x] 2.1 Replace immediate root-wheel exit with a dimmed non-wheel escape menu that offers exit and configuration actions.
- [x] 2.2 Add a non-wheel configuration menu flow that can open over the wheel background and return cleanly to the wheel.
- [x] 2.3 Add an SNES setup screen with editable ROM root text input, system-level emulator defaults, and a scan action.
- [x] 2.4 Add locale/default-preference settings for USA/EN presentation behavior in the configuration flow.
- [x] 2.5 Add full configuration export that writes a generated snapshot from the runtime library and saved settings.

## 3. Scan And Match Pipeline

- [x] 3.1 Implement recursive SNES file discovery for `.sfc` and `.smc` files with a pre-hash candidate count.
- [x] 3.2 Implement file hashing for discovered ROMs using SHA-1 and MD5 and persist scan/file state in the frontend-local database.
- [x] 3.3 Implement release matching against `database/database/unified_snes.db` using SHA-1 first and MD5 fallback.
- [x] 3.4 Persist owned release matches and unmatched files explicitly so the runtime can rebuild the library without rescanning.
- [x] 3.5 Add scan progress reporting that distinguishes discovery, hashing, matching, and asset-fetch phases.
- [x] 3.6 Reserve local-schema fields needed for future archive-aware scan support without implementing zip scanning yet.

## 4. Runtime Library Generation

- [x] 4.1 Build runtime SNES wheel data from owned canonical games using master-database grouping plus frontend-local ownership state.
- [x] 4.2 Build a sibling `Unidentified SNES` wheel from unmatched scanned files using filename-only entries.
- [x] 4.3 Replace direct startup deserialization of the static menu tree with runtime library generation while preserving compatibility paths needed for export or fallback.
- [x] 4.4 Implement launch-command resolution across system, canonical-game, and owned-release override scopes.

## 5. Canonical Game Detail Screen

- [x] 5.1 Replace the current version-popup style overlay path with a canonical game detail screen that keeps the wheel visible behind a dimmed overlay.
- [x] 5.2 Implement section-based navigation for the detail screen, including hero/actions, owned releases, screenshots, and related-content sections.
- [x] 5.3 Implement owned release edit mode with whole-section highlight, up/down release movement, confirm-to-save default, and cancel-to-exit behavior.
- [x] 5.4 Implement play behavior that launches the currently preferred owned release through the launch-resolution chain.
- [x] 5.5 Implement favorite toggling and persistence with boolean favorite state plus timestamp fields.
- [x] 5.6 Render master-database metadata on the detail screen, including description, release year, player count, publisher, and developer.
- [x] 5.7 Add vertical scrolling behavior for the detail screen so additional sections remain accessible.

## 6. Assets And Media

- [x] 6.1 Implement asset selection logic for clear logos with LaunchBox-first precedence and the agreed USA/NORTH_AMERICA/WORLD/EUROPE/OCEANIA/JAPAN fallback order.
- [x] 6.2 Implement poster selection logic with `Fanart - Box - Front` preferred over `Box - Front`, using LaunchBox first and TGDB fallback.
- [x] 6.3 Implement screenshot aggregation that collects all supported screenshots for each canonical game in deterministic order.
- [x] 6.4 Add asset download and cache storage using semantic filenames that do not depend on unstable master-database row IDs.
- [x] 6.5 Persist asset-cache metadata in the frontend-local database and wire cached clear logos into wheel rendering with text fallback when none exist.
- [x] 6.6 Render cached posters, logos, and screenshots on the canonical game detail screen.
- [x] 6.7 Add fullscreen screenshot viewing with left/right navigation and cancel-to-close behavior.

## 7. Related Content Navigation

- [x] 7.1 Build same-series related strips from owned SNES canonical games using master-database series memberships and sort order.
- [x] 7.2 Build publisher and developer related strips from owned SNES canonical games with favorites first and newer games before older ones.
- [x] 7.3 Implement horizontal card-strip navigation for related content using poster image plus text presentation.
- [x] 7.4 Add canonical game detail-screen stacking so selecting a related game opens a nested detail screen and cancel unwinds to the previous detail screen.

## 8. Verification

- [x] 8.1 Verify that root cancel opens the escape/configuration menu instead of quitting immediately.
- [x] 8.2 Verify that an SNES scan produces owned canonical games, owned releases, and unmatched entries in the correct wheels.
- [x] 8.3 Verify that release-default changes persist across detail-screen re-entry and full frontend restart.
- [x] 8.4 Verify that favorites persist and affect related-content ordering.
- [x] 8.5 Verify that wheel clear logos, detail posters, and screenshot sets resolve from cached assets using the agreed precedence rules.
- [x] 8.6 Verify that nested related-game navigation returns to the previous detail screen before returning to the wheel.
- [x] 8.7 Verify that full configuration export includes generated library content and artwork references from the runtime library model.
