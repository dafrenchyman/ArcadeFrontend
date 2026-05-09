"""SQLite schema definition for the unified dataset database."""

import sqlite3

SCHEMA_SQL = """
CREATE TABLE IF NOT EXISTS build_status (
    id INTEGER PRIMARY KEY,
    status TEXT NOT NULL,
    platform_slug TEXT NOT NULL,
    output_db TEXT NOT NULL,
    override_workbook_path TEXT,
    diagnostic_count INTEGER DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS diagnostics (
    id INTEGER PRIMARY KEY,
    stage TEXT NOT NULL,
    severity TEXT NOT NULL,
    source_system TEXT NOT NULL,
    internal_game_key TEXT,
    internal_release_key TEXT,
    datomatic_source_key TEXT,
    override_workbook_path TEXT,
    override_sheet TEXT,
    message TEXT NOT NULL,
    candidate_options_json TEXT,
    ready_to_paste TEXT,
    helper_command TEXT,
    details_json TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS libraries (
    library_slug TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    is_official INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS platforms (
    platform_key TEXT PRIMARY KEY,
    platform_slug TEXT NOT NULL,
    library_slug TEXT NOT NULL,
    name TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS games (
    game_key TEXT PRIMARY KEY,
    platform_key TEXT NOT NULL,
    canonical_name TEXT NOT NULL,
    slug TEXT NOT NULL,
    canonical_sort_name TEXT NOT NULL,
    game_kind TEXT NOT NULL,
    release_year INTEGER,
    players_min INTEGER,
    players_max INTEGER,
    is_coop INTEGER DEFAULT 0,
    primary_genre_name TEXT,
    primary_publisher_name TEXT,
    primary_developer_name TEXT,
    preferred_short_description_id INTEGER,
    preferred_long_description_id INTEGER
);
CREATE TABLE IF NOT EXISTS game_names (
    id INTEGER PRIMARY KEY,
    game_key TEXT NOT NULL,
    name TEXT NOT NULL,
    sort_name TEXT NOT NULL,
    name_type TEXT NOT NULL,
    language_code TEXT,
    region_code TEXT,
    source_system TEXT,
    source_row_id TEXT,
    is_preferred INTEGER DEFAULT 0,
    is_preferred_en_us INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS game_descriptions (
    id INTEGER PRIMARY KEY,
    game_key TEXT NOT NULL,
    description_type TEXT NOT NULL,
    language_code TEXT,
    region_code TEXT,
    source_system TEXT,
    source_row_id TEXT,
    text_value TEXT NOT NULL,
    is_preferred INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS game_releases (
    release_key TEXT PRIMARY KEY,
    game_key TEXT NOT NULL,
    release_title TEXT NOT NULL,
    release_type TEXT NOT NULL,
    patch_kind TEXT,
    primary_region_code TEXT,
    revision_label TEXT,
    version_label TEXT,
    base_release_key TEXT,
    is_world INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS release_names (
    id INTEGER PRIMARY KEY,
    release_key TEXT NOT NULL,
    name TEXT NOT NULL,
    language_code TEXT,
    region_code TEXT,
    source_system TEXT,
    source_row_id TEXT,
    is_preferred INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS release_roms (
    rom_key TEXT PRIMARY KEY,
    release_key TEXT NOT NULL,
    filename TEXT NOT NULL,
    base_filename TEXT,
    file_extension TEXT,
    size_bytes INTEGER,
    crc32 TEXT,
    md5 TEXT,
    sha1 TEXT,
    source_system TEXT,
    source_row_id TEXT
);
CREATE TABLE IF NOT EXISTS languages (
    language_code TEXT PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS regions (
    region_code TEXT PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS genres (
    genre_name TEXT PRIMARY KEY
);
CREATE TABLE IF NOT EXISTS companies (
    company_name TEXT PRIMARY KEY
);
CREATE TABLE IF NOT EXISTS game_genres (
    game_key TEXT NOT NULL,
    genre_name TEXT NOT NULL,
    source_system TEXT,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS game_companies (
    game_key TEXT NOT NULL,
    company_name TEXT NOT NULL,
    role TEXT NOT NULL,
    source_system TEXT,
    region_code TEXT,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS release_languages (
    release_key TEXT NOT NULL,
    language_code TEXT NOT NULL,
    source_system TEXT,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS release_regions (
    release_key TEXT NOT NULL,
    region_code TEXT NOT NULL,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS platform_series (
    series_key TEXT PRIMARY KEY,
    platform_key TEXT NOT NULL,
    name TEXT NOT NULL,
    series_type TEXT NOT NULL,
    generation_source TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS platform_series_games (
    series_key TEXT NOT NULL,
    game_key TEXT NOT NULL,
    sort_order INTEGER,
    membership_source TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS cross_platform_series (
    series_key TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    source_system TEXT,
    source_external_id TEXT
);
CREATE TABLE IF NOT EXISTS cross_platform_series_games (
    series_key TEXT NOT NULL,
    game_key TEXT NOT NULL,
    sort_order INTEGER,
    membership_source TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS asset_candidates (
    id INTEGER PRIMARY KEY,
    game_key TEXT,
    release_key TEXT,
    asset_type TEXT NOT NULL,
    source_system TEXT NOT NULL,
    source_row_id TEXT,
    region_code TEXT,
    language_code TEXT,
    path_or_url TEXT NOT NULL,
    priority_rank INTEGER DEFAULT 999
);

CREATE TABLE IF NOT EXISTS game_igdb_associations (
    id INTEGER PRIMARY KEY,
    game_key TEXT NOT NULL,
    source_game_id TEXT NOT NULL,
    match_score REAL,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS game_tgdb_associations (
    id INTEGER PRIMARY KEY,
    game_key TEXT NOT NULL,
    source_game_id TEXT NOT NULL,
    match_score REAL,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS game_launchbox_associations (
    id INTEGER PRIMARY KEY,
    game_key TEXT NOT NULL,
    source_game_id TEXT NOT NULL,
    match_score REAL,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS game_no_intro_associations (
    id INTEGER PRIMARY KEY,
    game_key TEXT NOT NULL,
    source_game_id TEXT NOT NULL,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS release_no_intro_associations (
    id INTEGER PRIMARY KEY,
    release_key TEXT NOT NULL,
    source_game_id TEXT NOT NULL,
    is_primary INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS rom_no_intro_associations (
    id INTEGER PRIMARY KEY,
    rom_key TEXT NOT NULL,
    source_rom_id TEXT NOT NULL,
    is_primary INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS src_no_intro_games (
    source_game_id TEXT PRIMARY KEY,
    source_key TEXT NOT NULL,
    platform_slug TEXT NOT NULL,
    raw_title TEXT NOT NULL,
    description TEXT,
    categories_json TEXT NOT NULL DEFAULT '[]'
);
CREATE TABLE IF NOT EXISTS src_no_intro_roms (
    source_rom_id TEXT PRIMARY KEY,
    source_game_id TEXT NOT NULL,
    filename TEXT NOT NULL,
    size_bytes INTEGER,
    crc32 TEXT,
    md5 TEXT,
    sha1 TEXT
);
CREATE TABLE IF NOT EXISTS src_tgdb_games (
    source_game_id TEXT PRIMARY KEY,
    platform_alias TEXT,
    game_title TEXT,
    release_date TEXT,
    overview TEXT,
    players INTEGER,
    coop TEXT,
    youtube TEXT
);
CREATE TABLE IF NOT EXISTS src_tgdb_banners (
    source_banner_id TEXT PRIMARY KEY,
    source_game_id TEXT NOT NULL,
    banner_type TEXT,
    side TEXT,
    filename TEXT
);
CREATE TABLE IF NOT EXISTS src_launchbox_games (
    source_game_id TEXT PRIMARY KEY,
    platform_name TEXT,
    name TEXT,
    release_year INTEGER,
    overview TEXT,
    max_players INTEGER,
    release_type TEXT,
    cooperative INTEGER,
    genres TEXT,
    developer TEXT,
    publisher TEXT
);
CREATE TABLE IF NOT EXISTS src_launchbox_game_images (
    source_image_id TEXT PRIMARY KEY,
    source_game_id TEXT NOT NULL,
    image_type TEXT,
    region TEXT,
    file_name TEXT
);
CREATE TABLE IF NOT EXISTS src_launchbox_game_alternate_names (
    id INTEGER PRIMARY KEY,
    source_game_id TEXT NOT NULL,
    alternate_name TEXT NOT NULL,
    region TEXT
);
CREATE TABLE IF NOT EXISTS src_igdb_requests (
    request_key TEXT PRIMARY KEY,
    endpoint TEXT NOT NULL,
    query_text TEXT NOT NULL,
    result_json TEXT NOT NULL,
    fetched_from_remote INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS src_igdb_games (
    source_game_id TEXT PRIMARY KEY,
    name TEXT,
    summary TEXT,
    storyline TEXT,
    first_release_date TEXT,
    genres_json TEXT,
    involved_companies_json TEXT,
    collections_json TEXT,
    franchise_json TEXT,
    raw_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS src_igdb_franchises (
    source_franchise_id TEXT PRIMARY KEY,
    name TEXT,
    slug TEXT,
    raw_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS parsed_no_intro_records (
    source_key TEXT PRIMARY KEY,
    source_game_id TEXT NOT NULL,
    platform_slug TEXT NOT NULL,
    library_slug TEXT NOT NULL,
    raw_title TEXT NOT NULL,
    base_title TEXT NOT NULL,
    normalized_title TEXT NOT NULL,
    categories_json TEXT NOT NULL DEFAULT '[]',
    game_kind TEXT NOT NULL,
    release_type TEXT NOT NULL,
    patch_kind TEXT,
    primary_region_code TEXT,
    regions_json TEXT NOT NULL,
    languages_json TEXT NOT NULL,
    revision_label TEXT,
    version_label TEXT,
    is_world INTEGER DEFAULT 0,
    rom_count INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS match_candidates (
    id INTEGER PRIMARY KEY,
    game_key TEXT NOT NULL,
    source_system TEXT NOT NULL,
    candidate_id TEXT NOT NULL,
    candidate_name TEXT NOT NULL,
    candidate_extra TEXT,
    match_score REAL NOT NULL,
    accepted INTEGER DEFAULT 0
);
"""


def create_schema(con: sqlite3.Connection) -> None:
    """Create or update the unified dataset schema in a SQLite database.

    Args:
        con: Open SQLite connection that should receive the schema objects.
    """
    con.executescript(SCHEMA_SQL)
    con.commit()
