using System.Collections.Generic;

namespace ArcadeFrontend;

public static class FrontendRuntimeSchema
{
    public const int CurrentSchemaVersion = 2;

    public static IReadOnlyList<string> Statements { get; } = new[]
    {
        """
        CREATE TABLE IF NOT EXISTS schema_info (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            schema_version INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS app_settings (
            setting_key TEXT PRIMARY KEY,
            setting_value TEXT,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS systems (
            system_id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            is_enabled INTEGER NOT NULL DEFAULT 0,
            rom_root_path TEXT,
            default_emulator_command TEXT,
            preferred_region_code TEXT,
            preferred_language_code TEXT,
            last_scanned_at TEXT,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS scan_sessions (
            scan_id TEXT PRIMARY KEY,
            system_id TEXT NOT NULL,
            status TEXT NOT NULL,
            total_candidates INTEGER NOT NULL DEFAULT 0,
            hashed_candidates INTEGER NOT NULL DEFAULT 0,
            matched_candidates INTEGER NOT NULL DEFAULT 0,
            asset_candidates INTEGER NOT NULL DEFAULT 0,
            started_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            completed_at TEXT,
            error_message TEXT,
            FOREIGN KEY(system_id) REFERENCES systems(system_id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS discovered_files (
            file_id TEXT PRIMARY KEY,
            system_id TEXT NOT NULL,
            scan_id TEXT,
            absolute_path TEXT NOT NULL UNIQUE,
            file_name TEXT NOT NULL,
            file_extension TEXT NOT NULL,
            file_size_bytes INTEGER NOT NULL DEFAULT 0,
            file_modified_utc TEXT,
            relative_path TEXT,
            archive_path TEXT,
            archive_entry_name TEXT,
            sha1 TEXT,
            md5 TEXT,
            match_status TEXT NOT NULL DEFAULT 'pending',
            matched_release_key TEXT,
            discovered_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(system_id) REFERENCES systems(system_id),
            FOREIGN KEY(scan_id) REFERENCES scan_sessions(scan_id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS owned_releases (
            owned_release_id TEXT PRIMARY KEY,
            system_id TEXT NOT NULL,
            game_key TEXT NOT NULL,
            release_key TEXT NOT NULL,
            primary_file_id TEXT NOT NULL,
            selected_by_default INTEGER NOT NULL DEFAULT 0,
            discovered_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(system_id, release_key, primary_file_id),
            FOREIGN KEY(system_id) REFERENCES systems(system_id),
            FOREIGN KEY(primary_file_id) REFERENCES discovered_files(file_id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS game_preferences (
            system_id TEXT NOT NULL,
            game_key TEXT NOT NULL,
            preferred_release_key TEXT,
            is_favorite INTEGER NOT NULL DEFAULT 0,
            favorite_marked_at TEXT,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY(system_id, game_key),
            FOREIGN KEY(system_id) REFERENCES systems(system_id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS launch_overrides (
            override_id TEXT PRIMARY KEY,
            system_id TEXT NOT NULL,
            scope_type TEXT NOT NULL,
            game_key TEXT,
            release_key TEXT,
            command_template TEXT NOT NULL,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(system_id) REFERENCES systems(system_id)
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS cached_assets (
            cached_asset_id TEXT PRIMARY KEY,
            system_id TEXT NOT NULL,
            game_key TEXT NOT NULL,
            release_key TEXT,
            asset_role TEXT NOT NULL,
            source_system TEXT,
            source_reference TEXT,
            region_code TEXT,
            language_code TEXT,
            cache_path TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            selected_by_default INTEGER NOT NULL DEFAULT 0,
            cached_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(system_id) REFERENCES systems(system_id)
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS idx_discovered_files_system_status
        ON discovered_files(system_id, match_status);
        """,
        """
        CREATE INDEX IF NOT EXISTS idx_owned_releases_system_game
        ON owned_releases(system_id, game_key);
        """,
        """
        CREATE INDEX IF NOT EXISTS idx_cached_assets_system_game_role
        ON cached_assets(system_id, game_key, asset_role, sort_order);
        """
    };
}
