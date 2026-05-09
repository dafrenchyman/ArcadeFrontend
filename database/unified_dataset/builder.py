"""Top-level orchestration for building the unified game dataset."""

import datetime
import json
import logging
import os
import shutil
import sqlite3
import tempfile
from pathlib import Path

from tqdm import tqdm
from unified_dataset.config import BuildConfig
from unified_dataset.diagnostics import DiagnosticRecord, DiagnosticsCollector
from unified_dataset.keys import (
    cross_platform_series_key,
    internal_game_key,
    internal_release_key,
    platform_series_key,
)
from unified_dataset.matching import decide_match
from unified_dataset.normalization import normalize_sort_name, slugify
from unified_dataset.schema import create_schema
from unified_dataset.sources import (
    IgdbSource,
    LaunchboxSource,
    NoIntroSource,
    TgdbSource,
)
from unified_dataset.workbook import (
    REQUIRED_SHEETS,
    export_review_workbook,
    generate_template,
    load_workbook,
    validate_workbook_dict,
)

logger = logging.getLogger(__name__)


class UnifiedDatasetBuilder:
    """Run the unified dataset pipeline for one platform/library configuration.

    Args:
        config: Build configuration controlling source locations, output paths,
            and cache behavior.
    """

    def __init__(self, config: BuildConfig) -> None:
        """Initialize a builder for one configured run."""
        self.config = config
        self.diagnostics = DiagnosticsCollector()
        self.temp_db_path: Path | None = None
        self.override_data: dict[str, list[dict[str, str]]] = {
            sheet: [] for sheet in REQUIRED_SHEETS
        }

    def build(self) -> dict:
        """Execute the full unified dataset build.

        Returns:
            A summary dictionary describing the build status, output DB, and key
            row counts.
        """
        logger.info(
            "Starting unified dataset build for platform=%s", self.config.platform_slug
        )
        self._validate_inputs()
        if self.config.override_workbook is not None:
            logger.info("Loading override workbook: %s", self.config.override_workbook)
            self.override_data = load_workbook(self.config.override_workbook)
            validation_errors = validate_workbook_dict(self.override_data)
            if validation_errors:
                raise ValueError("; ".join(validation_errors))

        with tempfile.NamedTemporaryFile(
            prefix="unified-dataset-", suffix=".db", delete=False
        ) as handle:
            self.temp_db_path = Path(handle.name)
        logger.info("Building into temporary database: %s", self.temp_db_path)
        con = sqlite3.connect(self.temp_db_path)
        con.row_factory = sqlite3.Row
        create_schema(con)
        self._seed_reference_data(con)
        self._record_build_status(con, "building")

        no_intro = NoIntroSource(
            self.config.platform_slug,
            self.config.datomatic_file,
            self.config.official_library_slug or self.config.platform_slug,
            self.config.hacks_library_slug or f"{self.config.platform_slug}_hacks",
        )
        tgdb = TgdbSource(self.config.tgdb_db, self.config.platform_slug)
        launchbox = LaunchboxSource(
            self.config.launchbox_metadata_dir, self.config.platform_slug
        )
        igdb = IgdbSource(
            self.config.igdb_cache_db, self.config.platform_slug, self.config.igdb_mode
        )

        logger.info("Loading Datomatic source rows from %s", self.config.datomatic_file)
        raw_rows = no_intro.load_source_rows()
        logger.info("Loaded %s Datomatic source rows", len(raw_rows))
        NoIntroSource.persist_source(con, raw_rows)
        logger.info("Parsing Datomatic rows")
        parsed_rows = [
            no_intro.parse_row(row)
            for row in self._progress(
                raw_rows, "Parsing Datomatic rows", total=len(raw_rows)
            )
        ]
        logger.info("Persisting parsed Datomatic rows")
        for parsed in self._progress(
            parsed_rows, "Persisting parsed rows", total=len(parsed_rows)
        ):
            NoIntroSource.persist_parsed(con, parsed)

        logger.info("Loading TGDB source mirror from %s", self.config.tgdb_db)
        tgdb.load_source(con)
        logger.info(
            "Loading LaunchBox source mirror from %s",
            self.config.launchbox_metadata_dir,
        )
        launchbox.load_source(con)

        logger.info("Building canonical game/release/rom records")
        self._build_canonical_from_parsed(con, parsed_rows)
        logger.info("Applying overrides")
        self._apply_overrides(con)
        logger.info("Matching external sources")
        self._match_sources(con, tgdb, launchbox, igdb)
        logger.info(
            "Merging canonical games that resolved to the same external identity"
        )
        self._merge_games_by_primary_source_identity(con)
        logger.info("Mirroring IGDB franchise metadata")
        self._mirror_igdb_franchises(con, igdb)
        logger.info("Selecting canonical fields")
        self._select_canonical_fields(con)
        logger.info("Generating asset candidates")
        self._generate_assets(con)
        logger.info("Generating series")
        self._generate_series(con)
        logger.info("Persisting %s diagnostics", len(self.diagnostics.records))
        self.diagnostics.persist(con)

        status = "partial" if self.diagnostics.has_errors else "clean"
        self._record_build_status(con, status)
        summary = {
            "games": con.execute("SELECT COUNT(*) FROM games").fetchone()[0],
            "releases": con.execute("SELECT COUNT(*) FROM game_releases").fetchone()[0],
            "roms": con.execute("SELECT COUNT(*) FROM release_roms").fetchone()[0],
            "diagnostics": con.execute("SELECT COUNT(*) FROM diagnostics").fetchone()[
                0
            ],
            "asset_candidates": con.execute(
                "SELECT COUNT(*) FROM asset_candidates"
            ).fetchone()[0],
        }
        con.commit()
        con.close()

        self.config.output_db.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(self.temp_db_path), str(self.config.output_db))
        logger.info("Published unified database to %s", self.config.output_db)
        if self.config.review_workbook is not None:
            logger.info("Exporting review workbook to %s", self.config.review_workbook)
            self.export_review_workbook(
                self.config.output_db, self.config.review_workbook
            )
        logger.info("Build finished with status=%s summary=%s", status, summary)
        return {
            "status": status,
            "output_db": str(self.config.output_db),
            "diagnostic_count": len(self.diagnostics.records),
            "summary": summary,
        }

    def export_review_workbook(self, input_db: Path, output_workbook: Path) -> None:
        """Export a multi-sheet review workbook from a built unified DB.

        Args:
            input_db: Unified SQLite database to read review data from.
            output_workbook: Destination workbook path.
        """
        con = sqlite3.connect(input_db)
        con.row_factory = sqlite3.Row
        diagnostics_rows = [
            dict(row) for row in con.execute("SELECT * FROM diagnostics ORDER BY id")
        ]
        unresolved_games = [
            dict(row)
            for row in con.execute(
                "SELECT game_key, canonical_name FROM games WHERE game_key NOT IN (SELECT game_key FROM game_igdb_associations WHERE is_primary = 1)"
            )
        ]
        sheets: dict[str, list[dict[str, str]]] = {
            "all_diagnostics": [
                {k: str(v) for k, v in row.items()} for row in diagnostics_rows
            ]
            or [{"message": "No diagnostics"}],
            "unresolved_games": [
                {k: str(v) for k, v in row.items()} for row in unresolved_games
            ]
            or [{"message": "No unresolved games"}],
        }

        source_filters = {
            "igdb_review": "igdb",
            "tgdb_review": "tgdb",
            "launchbox_review": "launchbox",
        }
        for sheet_name, source_system in source_filters.items():
            filtered = [
                row
                for row in diagnostics_rows
                if row.get("source_system") == source_system
            ]
            sheets[sheet_name] = [
                {k: str(v) for k, v in row.items()} for row in filtered
            ] or [{"message": f"No {source_system} diagnostics"}]

        override_sheet_groups = {
            "game_src_ovr": "game_source_override",
            "grouping_ovr": "grouping_override",
            "release_ovr": "release_override",
            "name_ovr": "name_override",
            "series_ovr": "series_override",
            "ignore_ovr": "ignore_override",
        }
        for sheet_name, override_sheet in override_sheet_groups.items():
            filtered = [
                row
                for row in diagnostics_rows
                if row.get("override_sheet") == override_sheet
            ]
            sheets[sheet_name] = [
                {k: str(v) for k, v in row.items()} for row in filtered
            ] or [{"message": f"No diagnostics for {override_sheet}"}]

        ready_rows = []
        for row in diagnostics_rows:
            if row.get("ready_to_paste"):
                ready_rows.append(
                    {
                        "source_system": str(row.get("source_system", "")),
                        "override_sheet": str(row.get("override_sheet", "")),
                        "internal_game_key": str(row.get("internal_game_key", "")),
                        "message": str(row.get("message", "")),
                        "ready_to_paste": str(row.get("ready_to_paste", "")),
                        "helper_command": str(row.get("helper_command", "")),
                    }
                )
        sheets["paste_rows"] = ready_rows or [
            {"message": "No ready-to-paste override rows"}
        ]

        export_review_workbook(output_workbook, sheets)
        con.close()

    def validate_overrides(self) -> list[str]:
        """Validate the configured override workbook.

        Returns:
            A list of validation errors. Empty means the workbook structure is
            acceptable.
        """
        if self.config.override_workbook is None:
            return ["No override workbook configured"]
        workbook = load_workbook(self.config.override_workbook)
        return validate_workbook_dict(workbook)

    def manual_search(self, source_system: str, term: str) -> list[dict]:
        """Run an ad-hoc source search outside the main build pipeline.

        Args:
            source_system: Source adapter name such as ``igdb`` or ``tgdb``.
            term: Free-form search term.

        Returns:
            Candidate result rows from the requested source system.
        """
        if source_system == "tgdb":
            return TgdbSource(self.config.tgdb_db, self.config.platform_slug).search(
                term
            )
        if source_system == "launchbox":
            return LaunchboxSource(
                self.config.launchbox_metadata_dir, self.config.platform_slug
            ).search(term)
        if source_system == "igdb":
            return IgdbSource(
                self.config.igdb_cache_db,
                self.config.platform_slug,
                self.config.igdb_mode,
            ).search(term)[0]
        raise ValueError(f"Unsupported source system: {source_system}")

    def _validate_inputs(self) -> None:
        """Ensure all required input files and workbook paths are available."""
        logger.info("Validating build inputs")
        required = [
            self.config.datomatic_file,
            self.config.tgdb_db,
            self.config.metadata_xml,
            self.config.platforms_xml,
            self.config.igdb_cache_db,
        ]
        for path in required:
            if not path.exists():
                raise FileNotFoundError(f"Missing required input: {path}")
        if (
            self.config.override_workbook is not None
            and not self.config.override_workbook.exists()
        ):
            if self.config.generate_override_template_if_missing:
                logger.info(
                    "Override workbook missing; generating template at %s",
                    self.config.override_workbook,
                )
                generate_template(self.config.override_workbook)
            else:
                raise FileNotFoundError(
                    f"Missing override workbook: {self.config.override_workbook}"
                )

    def _seed_reference_data(self, con: sqlite3.Connection) -> None:
        """Insert baseline library, platform, language, and region rows.

        Args:
            con: SQLite connection for the temporary unified build database.
        """
        con.execute(
            "INSERT OR REPLACE INTO libraries (library_slug, name, is_official) VALUES (?, ?, ?)",
            (self.config.official_library_slug, self.config.official_library_slug, 1),
        )
        con.execute(
            "INSERT OR REPLACE INTO libraries (library_slug, name, is_official) VALUES (?, ?, ?)",
            (self.config.hacks_library_slug, self.config.hacks_library_slug, 0),
        )
        con.execute(
            "INSERT OR REPLACE INTO platforms (platform_key, platform_slug, library_slug, name) VALUES (?, ?, ?, ?)",
            (
                f"{self.config.official_library_slug}:{self.config.platform_slug}",
                self.config.platform_slug,
                self.config.official_library_slug,
                self.config.platform_slug.upper(),
            ),
        )
        con.execute(
            "INSERT OR REPLACE INTO platforms (platform_key, platform_slug, library_slug, name) VALUES (?, ?, ?, ?)",
            (
                f"{self.config.hacks_library_slug}:{self.config.platform_slug}",
                self.config.platform_slug,
                self.config.hacks_library_slug,
                f"{self.config.platform_slug.upper()} Hacks",
            ),
        )
        for code, name in {
            "en": "English",
            "en-us": "English (US)",
            "en-gb": "English (GB)",
            "ja": "Japanese",
            "fr": "French",
            "de": "German",
            "es": "Spanish",
            "it": "Italian",
            "pt": "Portuguese",
            "ko": "Korean",
        }.items():
            con.execute(
                "INSERT OR REPLACE INTO languages (language_code, name) VALUES (?, ?)",
                (code, name),
            )
        for code, name in {
            "USA": "USA",
            "EUROPE": "Europe",
            "JAPAN": "Japan",
            "WORLD": "World",
            "NORTH_AMERICA": "North America",
            "ASIA": "Asia",
            "AUSTRALIA": "Australia",
            "KOREA": "Korea",
        }.items():
            con.execute(
                "INSERT OR REPLACE INTO regions (region_code, name) VALUES (?, ?)",
                (code, name),
            )
        con.commit()

    def _record_build_status(self, con: sqlite3.Connection, status: str) -> None:
        """Write the current build status row.

        Args:
            con: SQLite connection for the temporary unified build database.
            status: Status label such as ``building``, ``clean``, or
                ``partial``.
        """
        con.execute("DELETE FROM build_status")
        con.execute(
            "INSERT INTO build_status (status, platform_slug, output_db, override_workbook_path, diagnostic_count) VALUES (?, ?, ?, ?, ?)",
            (
                status,
                self.config.platform_slug,
                str(self.config.output_db),
                (
                    str(self.config.override_workbook)
                    if self.config.override_workbook
                    else None
                ),
                len(self.diagnostics.records),
            ),
        )
        con.commit()

    def _build_canonical_from_parsed(
        self, con: sqlite3.Connection, parsed_rows
    ) -> None:
        """Generate canonical games, releases, ROMs, and No-Intro links.

        Args:
            con: SQLite connection for the temporary unified build database.
            parsed_rows: Parsed Datomatic records used as the canonical backbone.
        """
        grouping_overrides = {
            row["datomatic_source_key"]: row
            for row in self.override_data.get("grouping_override", [])
            if row.get("enabled", "").lower() in {"1", "true", "yes", "y"}
            and row.get("datomatic_source_key")
        }
        logger.info(
            "Applying %s enabled grouping overrides during canonical generation",
            len(grouping_overrides),
        )
        grouped_games: dict[str, dict] = {}
        applied_override_count = 0
        for parsed in self._progress(
            parsed_rows, "Canonical grouping", total=len(parsed_rows)
        ):
            override = grouping_overrides.get(parsed.source_key)
            title_for_group = (
                parsed.base_title
                if parsed.game_kind != "competition"
                else parsed.raw_title
            )
            game_kind = (
                override.get("forced_game_kind", "").strip() or parsed.game_kind
                if override
                else parsed.game_kind
            )
            library_slug = (
                override.get("library_slug", "").strip() or parsed.library_slug
                if override
                else parsed.library_slug
            )
            platform_slug = (
                override.get("platform_slug", "").strip() or self.config.platform_slug
                if override
                else self.config.platform_slug
            )
            forced_game_name = (
                override.get("forced_internal_game_name", "").strip()
                if override
                else ""
            )
            forced_release_title = (
                override.get("forced_release_title", "").strip() if override else ""
            )
            forced_release_type = (
                override.get("forced_release_type", "").strip() if override else ""
            )

            if forced_game_name:
                title_for_group = forced_game_name
            game_key = (
                override.get("forced_internal_game_key", "").strip()
                if override and override.get("forced_internal_game_key", "").strip()
                else internal_game_key(
                    library_slug, platform_slug, title_for_group, game_kind
                )
            )
            if override:
                applied_override_count += 1
                logger.info(
                    "Applied grouping override for %s -> game_key=%s release_key=%s",
                    parsed.source_key,
                    game_key,
                    override.get("forced_internal_release_key", "").strip()
                    or "<derived>",
                )
            platform_key = f"{library_slug}:{platform_slug}"
            if game_key not in grouped_games:
                grouped_games[game_key] = {
                    "platform_key": platform_key,
                    "canonical_name": title_for_group,
                    "slug": slugify(title_for_group),
                    "canonical_sort_name": normalize_sort_name(title_for_group),
                    "game_kind": game_kind,
                }
                # Store a dedicated frontend slug alongside the internal key so
                # routes can use a clean URL fragment without parsing game_key.
                con.execute(
                    "INSERT OR REPLACE INTO games (game_key, platform_key, canonical_name, slug, canonical_sort_name, game_kind) VALUES (?, ?, ?, ?, ?, ?)",
                    (
                        game_key,
                        platform_key,
                        title_for_group,
                        slugify(title_for_group),
                        normalize_sort_name(title_for_group),
                        game_kind,
                    ),
                )
                con.execute(
                    """
                    INSERT INTO game_names
                    (game_key, name, sort_name, name_type, language_code, region_code, source_system, source_row_id, is_preferred, is_preferred_en_us)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        game_key,
                        title_for_group,
                        normalize_sort_name(title_for_group),
                        "canonical",
                        "en",
                        parsed.primary_region_code,
                        "datomatic",
                        parsed.source_game_id,
                        1,
                        1,
                    ),
                )
            release_type = forced_release_type or parsed.release_type
            release_key = (
                override.get("forced_internal_release_key", "").strip()
                if override and override.get("forced_internal_release_key", "").strip()
                else internal_release_key(
                    game_key,
                    release_type,
                    parsed.primary_region_code,
                    parsed.revision_label,
                    parsed.version_label,
                    parsed.patch_kind,
                )
            )
            release_title = forced_release_title or parsed.raw_title
            con.execute(
                """
                INSERT OR REPLACE INTO game_releases
                (release_key, game_key, release_title, release_type, patch_kind, primary_region_code, revision_label, version_label, is_world)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    release_key,
                    game_key,
                    release_title,
                    release_type,
                    parsed.patch_kind,
                    parsed.primary_region_code,
                    parsed.revision_label,
                    parsed.version_label,
                    int(parsed.is_world),
                ),
            )
            con.execute(
                "INSERT INTO release_names (release_key, name, language_code, region_code, source_system, source_row_id, is_preferred) VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    release_key,
                    release_title,
                    None,
                    parsed.primary_region_code,
                    "datomatic",
                    parsed.source_game_id,
                    1,
                ),
            )
            con.execute(
                "INSERT INTO game_no_intro_associations (game_key, source_game_id, is_primary) VALUES (?, ?, ?)",
                (game_key, parsed.source_game_id, 1),
            )
            con.execute(
                "INSERT INTO release_no_intro_associations (release_key, source_game_id, is_primary) VALUES (?, ?, ?)",
                (release_key, parsed.source_game_id, 1),
            )
            for region_code in parsed.regions or (
                [parsed.primary_region_code] if parsed.primary_region_code else []
            ):
                con.execute(
                    "INSERT INTO release_regions (release_key, region_code, is_primary) VALUES (?, ?, ?)",
                    (
                        release_key,
                        region_code,
                        int(region_code == parsed.primary_region_code),
                    ),
                )
            for language_code in parsed.languages:
                con.execute(
                    "INSERT INTO release_languages (release_key, language_code, source_system, is_primary) VALUES (?, ?, ?, ?)",
                    (
                        release_key,
                        language_code,
                        "datomatic",
                        1 if language_code.startswith("en") else 0,
                    ),
                )
            if parsed.is_world and "WORLD" not in parsed.regions:
                con.execute(
                    "INSERT INTO release_regions (release_key, region_code, is_primary) VALUES (?, ?, ?)",
                    (release_key, "WORLD", 1 if not parsed.primary_region_code else 0),
                )
            for rom in parsed.roms:
                rom_key = rom["source_rom_id"]
                con.execute(
                    """
                    INSERT OR REPLACE INTO release_roms
                    (rom_key, release_key, filename, base_filename, file_extension, size_bytes, crc32, md5, sha1, source_system, source_row_id)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        rom_key,
                        release_key,
                        rom["filename"],
                        (
                            os.path.splitext(rom["filename"])[0]
                            if rom["filename"]
                            else None
                        ),
                        (
                            os.path.splitext(rom["filename"])[1].lstrip(".")
                            if rom["filename"]
                            else None
                        ),
                        rom["size_bytes"],
                        rom["crc32"],
                        rom["md5"],
                        rom["sha1"],
                        "datomatic",
                        rom["source_rom_id"],
                    ),
                )
                con.execute(
                    "INSERT INTO rom_no_intro_associations (rom_key, source_rom_id, is_primary) VALUES (?, ?, ?)",
                    (rom_key, rom["source_rom_id"], 1),
                )
        logger.info(
            "Canonical generation applied %s grouping overrides", applied_override_count
        )
        con.commit()

    def _apply_overrides(self, con: sqlite3.Connection) -> None:
        """Apply supported workbook overrides to the temporary build DB.

        Args:
            con: SQLite connection for the temporary unified build database.
        """
        logger.info("Applying name and series overrides")
        for row in self.override_data.get("name_override", []):
            if row.get("enabled", "").lower() not in {"1", "true", "yes", "y"}:
                continue
            target_key = row["internal_key"]
            if row["target_type"] == "game":
                con.execute(
                    """
                    INSERT INTO game_names
                    (game_key, name, sort_name, name_type, language_code, region_code, source_system, source_row_id, is_preferred, is_preferred_en_us)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        target_key,
                        row["name"],
                        row["sort_name"] or normalize_sort_name(row["name"]),
                        row["name_type"],
                        row["language_code"] or None,
                        row["region_code"] or None,
                        "override",
                        target_key,
                        1 if row["is_preferred"].lower() in {"1", "true", "yes"} else 0,
                        (
                            1
                            if row["is_preferred_en_us"].lower() in {"1", "true", "yes"}
                            else 0
                        ),
                    ),
                )
        for row in self.override_data.get("series_override", []):
            if row.get("enabled", "").lower() not in {"1", "true", "yes", "y"}:
                continue
            if row["series_scope"] == "platform":
                platform_key = None
                game_row = con.execute(
                    "SELECT platform_key FROM games WHERE game_key = ?",
                    (row["internal_game_key"],),
                ).fetchone()
                if game_row:
                    platform_key = game_row["platform_key"]
                    con.execute(
                        "INSERT OR REPLACE INTO platform_series (series_key, platform_key, name, series_type, generation_source) VALUES (?, ?, ?, ?, ?)",
                        (
                            row["series_key"],
                            platform_key,
                            row["series_name"],
                            "manual_group",
                            "override",
                        ),
                    )
                    con.execute(
                        "INSERT INTO platform_series_games (series_key, game_key, sort_order, membership_source) VALUES (?, ?, ?, ?)",
                        (
                            row["series_key"],
                            row["internal_game_key"],
                            int(row["sort_order"] or 0),
                            "override",
                        ),
                    )
        con.commit()

    def _match_sources(
        self,
        con: sqlite3.Connection,
        tgdb: TgdbSource,
        launchbox: LaunchboxSource,
        igdb: IgdbSource,
    ) -> None:
        """Match canonical games against external source adapters.

        Args:
            con: SQLite connection for the temporary unified build database.
            tgdb: TGDB source adapter.
            launchbox: LaunchBox source adapter.
            igdb: IGDB source adapter.
        """
        games = con.execute(
            "SELECT game_key, canonical_name FROM games ORDER BY game_key"
        ).fetchall()
        logger.info(
            "Matching %s canonical games against TGDB, LaunchBox, and IGDB", len(games)
        )
        source_overrides = {
            row["internal_game_key"]: row
            for row in self.override_data.get("game_source_override", [])
            if row.get("enabled", "").lower() in {"1", "true", "yes", "y"}
        }
        # Source matching is intentionally single-threaded now. The earlier
        # thread pool added complexity and did not improve wall-clock time
        # once candidate prefiltering and cached search indexes were in place.
        for game in self._progress(games, "Source matching", total=len(games)):
            game_key = game["game_key"]
            canonical_name = game["canonical_name"]
            override = source_overrides.get(game_key)
            preferred_regions = self._load_game_preferred_regions(con, game_key)
            try:
                self._match_one_source(
                    con,
                    game_key,
                    canonical_name,
                    "tgdb",
                    tgdb.search(canonical_name),
                    "game_tgdb_associations",
                    override.get("tgdb_game_id") if override else None,
                )
            except Exception as exc:
                self._record_source_lookup_failure(
                    game_key, canonical_name, "tgdb", exc
                )
            try:
                self._match_one_source(
                    con,
                    game_key,
                    canonical_name,
                    "launchbox",
                    launchbox.search(
                        canonical_name, preferred_regions=preferred_regions
                    ),
                    "game_launchbox_associations",
                    override.get("launchbox_game_id") if override else None,
                )
            except Exception as exc:
                self._record_source_lookup_failure(
                    game_key, canonical_name, "launchbox", exc
                )
            try:
                igdb_candidates, fetched = igdb.search(canonical_name)
                query_text, _ = igdb.build_games_query(canonical_name)
                if query_text is not None:
                    igdb.persist_request_mirror(con, "games", query_text, fetched)
                self._match_one_source(
                    con,
                    game_key,
                    canonical_name,
                    "igdb",
                    igdb_candidates,
                    "game_igdb_associations",
                    override.get("igdb_game_id") if override else None,
                )
            except Exception as exc:
                self._record_source_lookup_failure(
                    game_key, canonical_name, "igdb", exc
                )
        con.commit()

    def _load_game_preferred_regions(
        self, con: sqlite3.Connection, game_key: str
    ) -> set[str]:
        """Load the parsed No-Intro region footprint for one canonical game.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key whose release regions should be read.

        Returns:
            Set of region codes attached to releases under the canonical game.
            These region codes are used as a preference signal during source
            matching, especially when ranking LaunchBox alternate names.
        """
        rows = con.execute(
            """
            SELECT DISTINCT rr.region_code
            FROM game_releases gr
            JOIN release_regions rr ON rr.release_key = gr.release_key
            WHERE gr.game_key = ?
            """,
            (game_key,),
        ).fetchall()
        return {str(row["region_code"]).upper() for row in rows if row["region_code"]}

    def _record_source_lookup_failure(
        self, game_key: str, canonical_name: str, source_system: str, exc: Exception
    ) -> None:
        """Record a non-fatal source lookup failure as a diagnostic.

        Args:
            game_key: Canonical game key being matched.
            canonical_name: Human-readable game name being matched.
            source_system: Source system that failed.
            exc: Raised exception to summarize in diagnostics.
        """
        logger.warning(
            "%s lookup failed for %s: %s", source_system, canonical_name, exc
        )
        workbook_path = (
            str(self.config.override_workbook) if self.config.override_workbook else ""
        )
        helper = f'python -m unified_dataset search-source --source {source_system} --platform {self.config.platform_slug} --term "{canonical_name}"'
        self.diagnostics.add(
            DiagnosticRecord(
                stage="matching",
                severity="error",
                source_system=source_system,
                internal_game_key=game_key,
                override_workbook_path=workbook_path,
                override_sheet="game_source_override",
                message=f"{source_system} lookup failed for {canonical_name}: {exc}",
                candidate_options=[],
                ready_to_paste=f"true\t{self.config.official_library_slug}\t{self.config.platform_slug}\t{game_key}\t{canonical_name}\t",
                helper_command=helper,
                details={
                    "exception_type": type(exc).__name__,
                    "exception_message": str(exc),
                },
            )
        )

    def _match_one_source(
        self,
        con,
        game_key: str,
        canonical_name: str,
        source_system: str,
        candidates: list[dict],
        assoc_table: str,
        forced_id: str | None,
    ) -> None:
        """Resolve one source system for one canonical game.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key being matched.
            canonical_name: Human-readable game name being matched.
            source_system: Source system name.
            candidates: Candidate rows returned by the source adapter.
            assoc_table: Association table that should receive the accepted row.
            forced_id: Optional forced source row ID from the override workbook.
        """
        for candidate in candidates:
            con.execute(
                "INSERT INTO match_candidates (game_key, source_system, candidate_id, candidate_name, candidate_extra, match_score, accepted) VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    game_key,
                    source_system,
                    candidate["source_game_id"],
                    candidate["candidate_name"],
                    candidate.get("candidate_extra", ""),
                    candidate["match_score"],
                    0,
                ),
            )
        if forced_id:
            con.execute(
                f"INSERT INTO {assoc_table} (game_key, source_game_id, match_score, is_primary) VALUES (?, ?, ?, ?)",
                (game_key, forced_id, 100.0, 1),
            )
            return
        decision = decide_match(canonical_name, candidates)
        if decision.accepted:
            con.execute(
                f"INSERT INTO {assoc_table} (game_key, source_game_id, match_score, is_primary) VALUES (?, ?, ?, ?)",
                (
                    game_key,
                    decision.accepted["source_game_id"],
                    decision.accepted["match_score"],
                    1,
                ),
            )
            con.execute(
                "UPDATE match_candidates SET accepted = 1 WHERE game_key = ? AND source_system = ? AND candidate_id = ?",
                (game_key, source_system, decision.accepted["source_game_id"]),
            )
            return
        workbook_path = (
            str(self.config.override_workbook) if self.config.override_workbook else ""
        )
        ready = f"true\t{self.config.official_library_slug}\t{self.config.platform_slug}\t{game_key}\t{canonical_name}\t"
        helper = f'python -m unified_dataset search-source --source {source_system} --platform {self.config.platform_slug} --term "{canonical_name}"'
        self.diagnostics.add(
            DiagnosticRecord(
                stage="matching",
                severity="error",
                source_system=source_system,
                internal_game_key=game_key,
                override_workbook_path=workbook_path,
                override_sheet="game_source_override",
                message=f"Unresolved {source_system} match for {canonical_name}: {decision.unresolved_reason}",
                candidate_options=candidates,
                ready_to_paste=ready,
                helper_command=helper,
                details={"reason": decision.unresolved_reason},
            )
        )

    def _merge_games_by_primary_source_identity(self, con: sqlite3.Connection) -> None:
        """Merge duplicate canonical games that share one accepted source identity.

        Args:
            con: SQLite connection for the temporary unified build database.
        """
        merged_groups = 0
        # External matching can reveal that multiple Datomatic-derived game
        # buckets are really the same title under regional/alternate names.
        # This pass uses accepted primary source IDs as the merge signal so we
        # can collapse those duplicates without asking for manual grouping
        # overrides in every language/localization case.
        for source_system, assoc_table in (
            ("igdb", "game_igdb_associations"),
            ("launchbox", "game_launchbox_associations"),
            ("tgdb", "game_tgdb_associations"),
        ):
            groups = self._load_merge_candidate_groups(con, assoc_table)
            for source_game_id, members in groups.items():
                if len(members) < 2:
                    continue
                # Grouping overrides are treated as the user's final decision
                # about canonical identity. Once a game bucket was created via
                # an explicit grouping override, the later source-driven merge
                # stage must not collapse it back into some other game.
                if self._group_contains_grouping_lock(members):
                    continue
                # IGDB is the strongest merge signal. LaunchBox and TGDB are
                # allowed to drive merges too, but only when the group has
                # enough corroboration to prove it is one title under regional
                # naming differences rather than an accidental bad source match.
                if (
                    source_system != "igdb"
                    and not self._group_supports_secondary_merge(
                        con, members, source_system, source_game_id
                    )
                ):
                    continue
                target_game_key = self._choose_merge_target(
                    con, members, source_system, source_game_id
                )
                loser_game_keys = [
                    member["game_key"]
                    for member in members
                    if member["game_key"] != target_game_key
                ]
                if not loser_game_keys:
                    continue
                self._merge_game_group(con, target_game_key, loser_game_keys)
                self._normalize_primary_associations(
                    con, target_game_key, assoc_table, source_game_id
                )
                for other_assoc_table in (
                    "game_launchbox_associations",
                    "game_tgdb_associations",
                ):
                    self._normalize_primary_associations(
                        con, target_game_key, other_assoc_table
                    )
                merged_groups += 1
                logger.info(
                    "Merged %s games into %s using %s source id %s",
                    len(loser_game_keys) + 1,
                    target_game_key,
                    source_system,
                    source_game_id,
                )
        logger.info("Merged %s duplicate canonical game groups", merged_groups)
        con.commit()

    def _group_contains_grouping_lock(self, members: list[sqlite3.Row]) -> bool:
        """Check whether any merge candidate game is protected by overrides.

        Args:
            members: Canonical game rows currently being considered for merge.

        Returns:
            ``True`` when at least one member game key was explicitly named in
            an enabled ``grouping_override`` row. Those game buckets are treated
            as locked and may not be auto-merged later.
        """
        locked_game_keys = {
            row.get("forced_internal_game_key", "").strip()
            for row in self.override_data.get("grouping_override", [])
            if row.get("enabled", "").lower() in {"1", "true", "yes", "y"}
            and row.get("forced_internal_game_key", "").strip()
        }
        if not locked_game_keys:
            return False
        return any(member["game_key"] in locked_game_keys for member in members)

    def _group_supports_secondary_merge(
        self,
        con: sqlite3.Connection,
        members: list[sqlite3.Row],
        source_system: str,
        source_game_id: str,
    ) -> bool:
        """Decide whether LaunchBox/TGDB identity is strong enough to merge.

        Args:
            con: SQLite connection for the temporary unified build database.
            members: Canonical game rows sharing one non-IGDB source identity.
            source_system: Source system driving the proposed merge.
            source_game_id: Shared external source ID for the candidate group.

        Returns:
            ``True`` when the secondary-source group is corroborated strongly
            enough to merge automatically, otherwise ``False``.
        """
        game_keys = [member["game_key"] for member in members]
        igdb_ids = self._load_primary_source_ids(
            con, "game_igdb_associations", game_keys
        )
        # Secondary-source merges must have at least one IGDB-confirmed anchor.
        # If nobody in the group has an IGDB identity, we do not have enough
        # evidence to distinguish "regional alias" from "bad local-source hit".
        if not igdb_ids:
            return False
        # A shared LaunchBox/TGDB ID is not trustworthy when the candidate
        # group already disagrees about the stronger IGDB identity.
        if len(igdb_ids) > 1:
            return False

        if source_system == "launchbox":
            # LaunchBox gives us a richer proof path than TGDB because each
            # game row can carry an alternate-name list. For regional aliases,
            # every member of the merge group should be explainable by either
            # the primary LaunchBox title or one of those alternate names.
            return all(
                self._member_has_launchbox_name_evidence(
                    con, member["game_key"], source_game_id
                )
                for member in members
            )

        companion_table = "game_launchbox_associations"
        companion_ids = self._load_primary_source_ids(con, companion_table, game_keys)
        # TGDB does not expose alternate-name data locally, so keep the older
        # conservative rule for TGDB-driven merges: if LaunchBox disagrees
        # across the group, do not auto-merge them here.
        if len(companion_ids) > 1:
            return False
        return True

    def _load_primary_source_ids(
        self, con: sqlite3.Connection, assoc_table: str, game_keys: list[str]
    ) -> set[str]:
        """Load distinct primary source IDs for a set of canonical games.

        Args:
            con: SQLite connection for the temporary unified build database.
            assoc_table: Association table to query.
            game_keys: Canonical game keys participating in one merge group.

        Returns:
            Set of distinct primary source IDs present across the group.
        """
        if not game_keys:
            return set()
        placeholders = ",".join("?" for _ in game_keys)
        rows = con.execute(
            f"""
            SELECT DISTINCT source_game_id
            FROM {assoc_table}
            WHERE is_primary = 1
              AND game_key IN ({placeholders})
            """,
            game_keys,
        ).fetchall()
        return {str(row["source_game_id"]) for row in rows if row["source_game_id"]}

    def _member_has_launchbox_name_evidence(
        self, con: sqlite3.Connection, game_key: str, launchbox_source_game_id: str
    ) -> bool:
        """Check whether one game row is explainable by a LaunchBox alias set.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key being evaluated for merge safety.
            launchbox_source_game_id: Shared LaunchBox source row ID.

        Returns:
            ``True`` when the game's current canonical name or any associated
            No-Intro raw title matches the LaunchBox primary/alternate name
            set. This is the core safety valve that lets regional aliases merge
            while blocking unrelated titles that were matched badly.
        """
        launchbox_names = self._load_launchbox_name_set(con, launchbox_source_game_id)
        if not launchbox_names:
            return False
        member_names = self._load_member_name_set(con, game_key)
        return bool(member_names & launchbox_names)

    def _load_launchbox_name_set(
        self, con: sqlite3.Connection, source_game_id: str
    ) -> set[str]:
        """Load normalized LaunchBox primary and alternate names for one row.

        Args:
            con: SQLite connection for the temporary unified build database.
            source_game_id: LaunchBox source row ID.

        Returns:
            Set of normalized LaunchBox names that can be used as alias proof
            during regional-title merge decisions.
        """
        names: set[str] = set()
        primary_row = con.execute(
            "SELECT name FROM src_launchbox_games WHERE source_game_id = ?",
            (source_game_id,),
        ).fetchone()
        if primary_row and primary_row["name"]:
            names.add(normalize_sort_name(primary_row["name"]))
        alternate_rows = con.execute(
            "SELECT alternate_name FROM src_launchbox_game_alternate_names WHERE source_game_id = ?",
            (source_game_id,),
        ).fetchall()
        for row in alternate_rows:
            if row["alternate_name"]:
                names.add(normalize_sort_name(row["alternate_name"]))
        return names

    def _load_member_name_set(self, con: sqlite3.Connection, game_key: str) -> set[str]:
        """Load normalized canonical and Datomatic raw titles for one game.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key whose names should be inspected.

        Returns:
            Set of normalized names that describe the current canonical game
            bucket before merge.
        """
        names: set[str] = set()
        game_row = con.execute(
            "SELECT canonical_name FROM games WHERE game_key = ?", (game_key,)
        ).fetchone()
        if game_row and game_row["canonical_name"]:
            names.add(normalize_sort_name(game_row["canonical_name"]))
        raw_rows = con.execute(
            """
            SELECT p.raw_title
            FROM game_no_intro_associations a
            JOIN parsed_no_intro_records p ON p.source_game_id = a.source_game_id
            WHERE a.game_key = ?
            """,
            (game_key,),
        ).fetchall()
        for row in raw_rows:
            if row["raw_title"]:
                names.add(normalize_sort_name(row["raw_title"]))
        return names

    def _load_merge_candidate_groups(
        self, con: sqlite3.Connection, assoc_table: str
    ) -> dict[str, list[sqlite3.Row]]:
        """Load same-source match groups that are eligible for canonical merges.

        Args:
            con: SQLite connection for the temporary unified build database.
            assoc_table: Association table whose primary IDs define the group.

        Returns:
            Mapping of source game ID to matching canonical game rows.
        """
        rows = con.execute(f"""
            SELECT a.source_game_id, g.game_key, g.platform_key, g.canonical_name, g.game_kind
            FROM {assoc_table} a
            JOIN games g ON g.game_key = a.game_key
            WHERE a.is_primary = 1
            ORDER BY a.source_game_id, g.game_key
            """).fetchall()
        groups: dict[str, list[sqlite3.Row]] = {}
        for row in rows:
            groups.setdefault(row["source_game_id"], []).append(row)
        return {
            source_game_id: members
            for source_game_id, members in groups.items()
            if len({member["platform_key"] for member in members}) == 1
            and len({member["game_kind"] for member in members}) == 1
        }

    def _choose_merge_target(
        self,
        con: sqlite3.Connection,
        members: list[sqlite3.Row],
        source_system: str,
        source_game_id: str,
    ) -> str:
        """Choose which existing game row should survive a duplicate merge.

        Args:
            con: SQLite connection for the temporary unified build database.
            members: Canonical game rows that share one accepted source ID.
            source_system: Source system providing the merge identity.
            source_game_id: Shared source game ID.

        Returns:
            ``game_key`` of the preferred canonical survivor row.
        """
        source_name = self._get_source_display_name(con, source_system, source_game_id)
        source_sort_name = normalize_sort_name(source_name) if source_name else None
        ranked = sorted(
            members,
            key=lambda member: (
                (
                    0
                    if source_sort_name
                    and normalize_sort_name(member["canonical_name"])
                    == source_sort_name
                    else 1
                ),
                len(member["game_key"]),
                member["game_key"],
            ),
        )
        return ranked[0]["game_key"]

    def _get_source_display_name(
        self, con: sqlite3.Connection, source_system: str, source_game_id: str
    ) -> str | None:
        """Look up the source-display name for one accepted external row.

        Args:
            con: SQLite connection for the temporary unified build database.
            source_system: Source system name.
            source_game_id: External source row ID.

        Returns:
            Human-readable source title or ``None`` when unavailable.
        """
        if source_system == "igdb":
            row = con.execute(
                "SELECT name FROM src_igdb_games WHERE source_game_id = ?",
                (source_game_id,),
            ).fetchone()
            return row["name"] if row and row["name"] else None
        if source_system == "launchbox":
            row = con.execute(
                "SELECT name FROM src_launchbox_games WHERE source_game_id = ?",
                (source_game_id,),
            ).fetchone()
            return row["name"] if row and row["name"] else None
        if source_system == "tgdb":
            row = con.execute(
                "SELECT game_title FROM src_tgdb_games WHERE source_game_id = ?",
                (source_game_id,),
            ).fetchone()
            return row["game_title"] if row and row["game_title"] else None
        return None

    def _merge_game_group(
        self, con: sqlite3.Connection, target_game_key: str, loser_game_keys: list[str]
    ) -> None:
        """Move all child rows from loser games onto one surviving game key.

        Args:
            con: SQLite connection for the temporary unified build database.
            target_game_key: Canonical game row that should survive the merge.
            loser_game_keys: Duplicate canonical game rows to fold into the target.
        """
        if not loser_game_keys:
            return
        placeholders = ",".join("?" for _ in loser_game_keys)
        params = (target_game_key, *loser_game_keys)

        # Release, name, description, association, and candidate rows all hang
        # off ``game_key``. Repoint them first, then delete the now-orphaned
        # game rows once every dependent table has been updated.
        for table in (
            "game_releases",
            "game_names",
            "game_descriptions",
            "game_genres",
            "game_companies",
            "asset_candidates",
            "game_igdb_associations",
            "game_tgdb_associations",
            "game_launchbox_associations",
            "game_no_intro_associations",
            "match_candidates",
        ):
            con.execute(
                f"UPDATE {table} SET game_key = ? WHERE game_key IN ({placeholders})",
                params,
            )

        con.execute(
            f"DELETE FROM games WHERE game_key IN ({placeholders})", loser_game_keys
        )

    def _normalize_primary_associations(
        self,
        con: sqlite3.Connection,
        game_key: str,
        assoc_table: str,
        preferred_source_game_id: str | None = None,
    ) -> None:
        """Ensure one canonical primary association per source table after merges.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key being normalized.
            assoc_table: Association table being cleaned up.
            preferred_source_game_id: Optional preferred source row ID to keep
                primary when known from the merge identity.
        """
        rows = con.execute(
            f"SELECT id, source_game_id, COALESCE(match_score, 0) AS match_score FROM {assoc_table} WHERE game_key = ? ORDER BY id",
            (game_key,),
        ).fetchall()
        if not rows:
            return

        best_by_source: dict[str, sqlite3.Row] = {}
        duplicate_ids_to_delete: list[int] = []
        for row in rows:
            existing = best_by_source.get(row["source_game_id"])
            if existing is None:
                best_by_source[row["source_game_id"]] = row
                continue
            if (row["match_score"], -row["id"]) > (
                existing["match_score"],
                -existing["id"],
            ):
                duplicate_ids_to_delete.append(existing["id"])
                best_by_source[row["source_game_id"]] = row
            else:
                duplicate_ids_to_delete.append(row["id"])

        if duplicate_ids_to_delete:
            placeholders = ",".join("?" for _ in duplicate_ids_to_delete)
            con.execute(
                f"DELETE FROM {assoc_table} WHERE id IN ({placeholders})",
                duplicate_ids_to_delete,
            )

        kept_rows = list(best_by_source.values())
        primary_row = None
        if preferred_source_game_id is not None:
            primary_row = next(
                (
                    row
                    for row in kept_rows
                    if row["source_game_id"] == preferred_source_game_id
                ),
                None,
            )
        if primary_row is None:
            primary_row = sorted(
                kept_rows, key=lambda row: (-float(row["match_score"] or 0), row["id"])
            )[0]
        con.execute(
            f"UPDATE {assoc_table} SET is_primary = 0 WHERE game_key = ?", (game_key,)
        )
        con.execute(
            f"UPDATE {assoc_table} SET is_primary = 1 WHERE id = ?",
            (primary_row["id"],),
        )

    def _select_canonical_fields(self, con: sqlite3.Connection) -> None:
        """Populate canonical summary fields on ``games`` from matched sources.

        Args:
            con: SQLite connection for the temporary unified build database.
        """
        games = con.execute("SELECT game_key, canonical_name FROM games").fetchall()
        source_overrides = {
            row["internal_game_key"]: row
            for row in self.override_data.get("game_source_override", [])
            if row.get("enabled", "").lower() in {"1", "true", "yes", "y"}
        }
        logger.info(
            "Selecting canonical names and descriptions for %s games", len(games)
        )
        for game in self._progress(
            games, "Canonical field selection", total=len(games)
        ):
            game_key = game["game_key"]
            override = source_overrides.get(game_key)
            names = con.execute(
                "SELECT * FROM game_names WHERE game_key = ?", (game_key,)
            ).fetchall()

            igdb_assoc = con.execute(
                "SELECT source_game_id FROM game_igdb_associations WHERE game_key = ? AND is_primary = 1",
                (game_key,),
            ).fetchone()
            tgdb_assoc = con.execute(
                "SELECT source_game_id FROM game_tgdb_associations WHERE game_key = ? AND is_primary = 1",
                (game_key,),
            ).fetchone()
            launchbox_assoc = con.execute(
                "SELECT source_game_id FROM game_launchbox_associations WHERE game_key = ? AND is_primary = 1",
                (game_key,),
            ).fetchone()

            igdb_game = (
                con.execute(
                    "SELECT * FROM src_igdb_games WHERE source_game_id = ?",
                    (igdb_assoc["source_game_id"],),
                ).fetchone()
                if igdb_assoc
                else None
            )
            tgdb_game = (
                con.execute(
                    "SELECT * FROM src_tgdb_games WHERE source_game_id = ?",
                    (tgdb_assoc["source_game_id"],),
                ).fetchone()
                if tgdb_assoc
                else None
            )
            launchbox_game = (
                con.execute(
                    "SELECT * FROM src_launchbox_games WHERE source_game_id = ?",
                    (launchbox_assoc["source_game_id"],),
                ).fetchone()
                if launchbox_assoc
                else None
            )

            self._ensure_source_name_rows(
                con,
                game_key,
                igdb_assoc,
                igdb_game,
                tgdb_assoc,
                tgdb_game,
                launchbox_assoc,
                launchbox_game,
            )
            self._ensure_source_description_rows(
                con,
                game_key,
                igdb_assoc,
                igdb_game,
                tgdb_assoc,
                tgdb_game,
                launchbox_assoc,
                launchbox_game,
            )
            names = con.execute(
                "SELECT * FROM game_names WHERE game_key = ?", (game_key,)
            ).fetchall()

            preferred_name_source = (
                override.get("preferred_name_source", "").strip().lower()
                if override
                else ""
            )
            preferred_description_source = (
                override.get("preferred_description_source", "").strip().lower()
                if override
                else ""
            )
            chosen_override_name = self._choose_override_name_row(names)
            if chosen_override_name is not None:
                # Name overrides can intentionally change both the display name
                # and the frontend slug, so keep all three fields in sync here.
                con.execute(
                    "UPDATE games SET canonical_name = ?, slug = ?, canonical_sort_name = ? WHERE game_key = ?",
                    (
                        chosen_override_name["name"],
                        slugify(chosen_override_name["name"]),
                        chosen_override_name["sort_name"]
                        or normalize_sort_name(chosen_override_name["name"]),
                        game_key,
                    ),
                )
            else:
                chosen_name = self._choose_canonical_name_source(
                    preferred_name_source,
                    igdb_assoc,
                    igdb_game,
                    tgdb_assoc,
                    tgdb_game,
                    launchbox_assoc,
                    launchbox_game,
                )
                if chosen_name is not None:
                    # Source-selected canonical names should also drive the
                    # public slug so URLs remain aligned with the chosen title.
                    con.execute(
                        "UPDATE games SET canonical_name = ?, slug = ?, canonical_sort_name = ? WHERE game_key = ?",
                        (
                            chosen_name["name"],
                            slugify(chosen_name["name"]),
                            normalize_sort_name(chosen_name["name"]),
                            game_key,
                        ),
                    )

            release_year = self._select_release_year(
                igdb_game, tgdb_game, launchbox_game
            )
            players_min, players_max = self._select_player_counts(
                tgdb_game, launchbox_game
            )
            is_coop = self._select_coop(tgdb_game, launchbox_game)
            primary_genre = self._select_primary_genre(launchbox_game)
            primary_publisher = self._select_primary_publisher(launchbox_game)
            primary_developer = self._select_primary_developer(launchbox_game)
            (
                preferred_short_id,
                preferred_long_id,
            ) = self._select_preferred_description_ids(
                con, game_key, preferred_description_source
            )

            if primary_genre:
                con.execute(
                    "INSERT OR REPLACE INTO genres (genre_name) VALUES (?)",
                    (primary_genre,),
                )
                con.execute(
                    "INSERT OR REPLACE INTO game_genres (game_key, genre_name, source_system, is_primary) VALUES (?, ?, ?, ?)",
                    (game_key, primary_genre, "launchbox", 1),
                )
            if primary_publisher:
                con.execute(
                    "INSERT OR REPLACE INTO companies (company_name) VALUES (?)",
                    (primary_publisher,),
                )
                con.execute(
                    "INSERT OR REPLACE INTO game_companies (game_key, company_name, role, source_system, region_code, is_primary) VALUES (?, ?, ?, ?, ?, ?)",
                    (game_key, primary_publisher, "publisher", "launchbox", "USA", 1),
                )
            if primary_developer:
                con.execute(
                    "INSERT OR REPLACE INTO companies (company_name) VALUES (?)",
                    (primary_developer,),
                )
                con.execute(
                    "INSERT OR REPLACE INTO game_companies (game_key, company_name, role, source_system, region_code, is_primary) VALUES (?, ?, ?, ?, ?, ?)",
                    (game_key, primary_developer, "developer", "launchbox", "USA", 1),
                )

            con.execute(
                """
                UPDATE games
                SET release_year = ?,
                    players_min = ?,
                    players_max = ?,
                    is_coop = ?,
                    primary_genre_name = ?,
                    primary_publisher_name = ?,
                    primary_developer_name = ?,
                    preferred_short_description_id = ?,
                    preferred_long_description_id = ?
                WHERE game_key = ?
                """,
                (
                    release_year,
                    players_min,
                    players_max,
                    is_coop,
                    primary_genre,
                    primary_publisher,
                    primary_developer,
                    preferred_short_id,
                    preferred_long_id,
                    game_key,
                ),
            )
            con.commit()

    def _choose_override_name_row(self, names) -> sqlite3.Row | None:
        """Choose the highest-priority override name row for a canonical game.

        Args:
            names: Existing ``game_names`` rows for one game.

        Returns:
            The preferred override-backed name row, or ``None`` when no
            override names exist for the game.
        """
        override_names = [row for row in names if row["source_system"] == "override"]
        if not override_names:
            return None
        preferred = [row for row in override_names if row["is_preferred_en_us"]]
        if preferred:
            return preferred[0]
        preferred = [row for row in override_names if row["is_preferred"]]
        if preferred:
            return preferred[0]
        canonical = [row for row in override_names if row["name_type"] == "canonical"]
        if canonical:
            return canonical[0]
        return override_names[0]

    def _ensure_source_name_rows(
        self,
        con,
        game_key,
        igdb_assoc,
        igdb_game,
        tgdb_assoc,
        tgdb_game,
        launchbox_assoc,
        launchbox_game,
    ) -> None:
        """Ensure source-display name rows exist for a matched game.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key receiving name rows.
            igdb_assoc: Primary IGDB association row, if any.
            igdb_game: Mirrored IGDB game row, if any.
            tgdb_assoc: Primary TGDB association row, if any.
            tgdb_game: Mirrored TGDB game row, if any.
            launchbox_assoc: Primary LaunchBox association row, if any.
            launchbox_game: Mirrored LaunchBox game row, if any.
        """
        source_names = []
        if igdb_assoc and igdb_game and igdb_game["name"]:
            source_names.append(
                ("igdb", igdb_assoc["source_game_id"], igdb_game["name"])
            )
        if tgdb_assoc and tgdb_game and tgdb_game["game_title"]:
            source_names.append(
                ("tgdb", tgdb_assoc["source_game_id"], tgdb_game["game_title"])
            )
        if launchbox_assoc and launchbox_game and launchbox_game["name"]:
            source_names.append(
                ("launchbox", launchbox_assoc["source_game_id"], launchbox_game["name"])
            )
        for source_system, source_row_id, name in source_names:
            exists = con.execute(
                "SELECT 1 FROM game_names WHERE game_key = ? AND source_system = ? AND source_row_id = ? AND name_type = 'source-display'",
                (game_key, source_system, str(source_row_id)),
            ).fetchone()
            if exists is not None:
                continue
            con.execute(
                """
                INSERT INTO game_names
                (game_key, name, sort_name, name_type, language_code, region_code, source_system, source_row_id, is_preferred, is_preferred_en_us)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    game_key,
                    name,
                    normalize_sort_name(name),
                    "source-display",
                    "en",
                    "USA",
                    source_system,
                    str(source_row_id),
                    1 if source_system == "igdb" else 0,
                    1 if source_system == "igdb" else 0,
                ),
            )

    def _ensure_source_description_rows(
        self,
        con,
        game_key,
        igdb_assoc,
        igdb_game,
        tgdb_assoc,
        tgdb_game,
        launchbox_assoc,
        launchbox_game,
    ) -> None:
        """Ensure source description rows exist for a matched game.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key receiving description rows.
            igdb_assoc: Primary IGDB association row, if any.
            igdb_game: Mirrored IGDB game row, if any.
            tgdb_assoc: Primary TGDB association row, if any.
            tgdb_game: Mirrored TGDB game row, if any.
            launchbox_assoc: Primary LaunchBox association row, if any.
            launchbox_game: Mirrored LaunchBox game row, if any.
        """
        rows_to_insert = []
        if igdb_assoc and igdb_game:
            if igdb_game["summary"]:
                rows_to_insert.append(
                    (
                        "summary",
                        "igdb",
                        igdb_assoc["source_game_id"],
                        igdb_game["summary"],
                    )
                )
            if igdb_game["storyline"]:
                rows_to_insert.append(
                    (
                        "storyline",
                        "igdb",
                        igdb_assoc["source_game_id"],
                        igdb_game["storyline"],
                    )
                )
        if tgdb_assoc and tgdb_game and tgdb_game["overview"]:
            rows_to_insert.append(
                (
                    "overview",
                    "tgdb",
                    tgdb_assoc["source_game_id"],
                    tgdb_game["overview"],
                )
            )
        if launchbox_assoc and launchbox_game and launchbox_game["overview"]:
            rows_to_insert.append(
                (
                    "overview",
                    "launchbox",
                    launchbox_assoc["source_game_id"],
                    launchbox_game["overview"],
                )
            )
        for (
            description_type,
            source_system,
            source_row_id,
            text_value,
        ) in rows_to_insert:
            exists = con.execute(
                "SELECT 1 FROM game_descriptions WHERE game_key = ? AND description_type = ? AND source_system = ? AND source_row_id = ?",
                (game_key, description_type, source_system, str(source_row_id)),
            ).fetchone()
            if exists is not None:
                continue
            con.execute(
                """
                INSERT INTO game_descriptions
                (game_key, description_type, language_code, region_code, source_system, source_row_id, text_value, is_preferred)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    game_key,
                    description_type,
                    "en",
                    "USA",
                    source_system,
                    str(source_row_id),
                    text_value,
                    0,
                ),
            )

    def _choose_canonical_name_source(
        self,
        preferred_source,
        igdb_assoc,
        igdb_game,
        tgdb_assoc,
        tgdb_game,
        launchbox_assoc,
        launchbox_game,
    ):
        """Choose which matched source should supply the canonical name.

        Args:
            preferred_source: Optional override-selected source name.
            igdb_assoc: Primary IGDB association row, if any.
            igdb_game: Mirrored IGDB game row, if any.
            tgdb_assoc: Primary TGDB association row, if any.
            tgdb_game: Mirrored TGDB game row, if any.
            launchbox_assoc: Primary LaunchBox association row, if any.
            launchbox_game: Mirrored LaunchBox game row, if any.

        Returns:
            Source selection dictionary or ``None`` when no source has a name.
        """
        candidates = {
            "igdb": (
                {
                    "name": igdb_game["name"],
                    "source_row_id": igdb_assoc["source_game_id"],
                }
                if igdb_assoc and igdb_game and igdb_game["name"]
                else None
            ),
            "tgdb": (
                {
                    "name": tgdb_game["game_title"],
                    "source_row_id": tgdb_assoc["source_game_id"],
                }
                if tgdb_assoc and tgdb_game and tgdb_game["game_title"]
                else None
            ),
            "launchbox": (
                {
                    "name": launchbox_game["name"],
                    "source_row_id": launchbox_assoc["source_game_id"],
                }
                if launchbox_assoc and launchbox_game and launchbox_game["name"]
                else None
            ),
        }
        if preferred_source in candidates and candidates[preferred_source] is not None:
            return candidates[preferred_source]
        for source_name in ("igdb", "tgdb", "launchbox"):
            if candidates[source_name] is not None:
                return candidates[source_name]
        return None

    def _select_release_year(self, igdb_game, tgdb_game, launchbox_game) -> int | None:
        """Choose a canonical release year from matched sources.

        Args:
            igdb_game: Mirrored IGDB game row, if any.
            tgdb_game: Mirrored TGDB game row, if any.
            launchbox_game: Mirrored LaunchBox game row, if any.

        Returns:
            Preferred release year or ``None`` when unavailable.
        """
        if igdb_game and igdb_game["first_release_date"]:
            try:
                return (
                    int(str(igdb_game["first_release_date"])[:4])
                    if len(str(igdb_game["first_release_date"])) == 4
                    else datetime.datetime.utcfromtimestamp(
                        int(igdb_game["first_release_date"])
                    ).year
                )
            except (ValueError, OSError, OverflowError):
                pass
        if tgdb_game and tgdb_game["release_date"]:
            year = self._year_from_text(tgdb_game["release_date"])
            if year is not None:
                return year
        if launchbox_game and launchbox_game["release_year"]:
            try:
                return int(launchbox_game["release_year"])
            except (TypeError, ValueError):
                return None
        return None

    def _select_player_counts(
        self, tgdb_game, launchbox_game
    ) -> tuple[int | None, int | None]:
        """Choose canonical minimum and maximum player counts.

        Args:
            tgdb_game: Mirrored TGDB game row, if any.
            launchbox_game: Mirrored LaunchBox game row, if any.

        Returns:
            Tuple of ``(players_min, players_max)``.
        """
        if tgdb_game and tgdb_game["players"]:
            try:
                players = int(tgdb_game["players"])
                return 1, players
            except (TypeError, ValueError):
                pass
        if launchbox_game and launchbox_game["max_players"]:
            try:
                players = int(launchbox_game["max_players"])
                return 1, players
            except (TypeError, ValueError):
                return None, None
        return None, None

    def _select_coop(self, tgdb_game, launchbox_game) -> int:
        """Choose the canonical cooperative-play flag.

        Args:
            tgdb_game: Mirrored TGDB game row, if any.
            launchbox_game: Mirrored LaunchBox game row, if any.

        Returns:
            ``1`` when coop is supported, otherwise ``0``.
        """
        if tgdb_game and tgdb_game["coop"]:
            coop_text = str(tgdb_game["coop"]).strip().lower()
            if coop_text in {"yes", "true", "1"}:
                return 1
            if coop_text in {"no", "false", "0"}:
                return 0
        if launchbox_game and launchbox_game["cooperative"] is not None:
            try:
                return int(launchbox_game["cooperative"])
            except (TypeError, ValueError):
                return 0
        return 0

    def _select_primary_genre(self, launchbox_game) -> str | None:
        """Choose the primary genre from LaunchBox metadata.

        Args:
            launchbox_game: Mirrored LaunchBox game row, if any.

        Returns:
            Primary genre name or ``None`` when unavailable.
        """
        if not launchbox_game or not launchbox_game["genres"]:
            return None
        genres = [
            part.strip()
            for part in str(launchbox_game["genres"]).split(";")
            if part.strip()
        ]
        if not genres:
            genres = [
                part.strip()
                for part in str(launchbox_game["genres"]).split(",")
                if part.strip()
            ]
        return genres[0] if genres else None

    def _select_primary_publisher(self, launchbox_game) -> str | None:
        """Choose the primary publisher from LaunchBox metadata.

        Args:
            launchbox_game: Mirrored LaunchBox game row, if any.

        Returns:
            Publisher name or ``None`` when unavailable.
        """
        if not launchbox_game or not launchbox_game["publisher"]:
            return None
        return str(launchbox_game["publisher"]).strip() or None

    def _select_primary_developer(self, launchbox_game) -> str | None:
        """Choose the primary developer from LaunchBox metadata.

        Args:
            launchbox_game: Mirrored LaunchBox game row, if any.

        Returns:
            Developer name or ``None`` when unavailable.
        """
        if not launchbox_game or not launchbox_game["developer"]:
            return None
        return str(launchbox_game["developer"]).strip() or None

    def _select_preferred_description_ids(
        self, con: sqlite3.Connection, game_key: str, preferred_source: str = ""
    ) -> tuple[int | None, int | None]:
        """Choose preferred short and long description row IDs.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key whose descriptions are being ranked.
            preferred_source: Optional override-selected source name.

        Returns:
            Tuple of ``(preferred_short_description_id,
            preferred_long_description_id)``.
        """
        preferred_source = preferred_source.strip().lower()
        short_row = self._select_description_row(
            con,
            game_key,
            ("summary", "overview", "short"),
            preferred_source,
        )
        long_row = self._select_description_row(
            con,
            game_key,
            ("storyline", "long", "overview", "summary"),
            preferred_source,
        )
        return (
            short_row["id"] if short_row else None,
            long_row["id"] if long_row else None,
        )

    def _select_description_row(
        self,
        con: sqlite3.Connection,
        game_key: str,
        description_types: tuple[str, ...],
        preferred_source: str,
    ):
        """Choose one preferred description row from a set of candidate types.

        Args:
            con: SQLite connection for the temporary unified build database.
            game_key: Canonical game key whose descriptions are being ranked.
            description_types: Ordered description types to consider.
            preferred_source: Optional override-selected source name.

        Returns:
            Selected SQLite row or ``None`` when no row matches.
        """
        rows = con.execute(
            f"""
            SELECT id, description_type, source_system
            FROM game_descriptions
            WHERE game_key = ? AND description_type IN ({",".join("?" for _ in description_types)})
            ORDER BY id
            """,
            (game_key, *description_types),
        ).fetchall()
        if not rows:
            return None
        type_priority = {
            description_type: index
            for index, description_type in enumerate(description_types)
        }
        rows = sorted(
            rows,
            key=lambda row: (
                (
                    0
                    if preferred_source and row["source_system"] == preferred_source
                    else 1
                ),
                type_priority.get(row["description_type"], 99),
                row["id"],
            ),
        )
        return rows[0]

    def _year_from_text(self, value: str | None) -> int | None:
        """Extract a plausible year from free-form text.

        Args:
            value: Free-form source text that may contain a year.

        Returns:
            Four-digit year or ``None`` when not found.
        """
        if not value:
            return None
        text = str(value)
        for idx in range(len(text) - 3):
            chunk = text[idx : idx + 4]  # noqa: E203
            if chunk.isdigit():
                year = int(chunk)
                if 1970 <= year <= 2100:
                    return year
        return None

    def _generate_assets(self, con: sqlite3.Connection) -> None:
        """Populate asset candidate rows from matched source metadata.

        Args:
            con: SQLite connection for the temporary unified build database.
        """
        games = con.execute("SELECT game_key FROM games").fetchall()
        logger.info("Generating asset candidates for %s games", len(games))
        logger.info("Building LaunchBox and TGDB asset indexes")
        launchbox_assets = self._build_launchbox_asset_index(con)
        tgdb_assets = self._build_tgdb_asset_index(con)
        launchbox_assoc_map = {
            row["game_key"]: row["source_game_id"]
            for row in con.execute(
                "SELECT game_key, source_game_id FROM game_launchbox_associations WHERE is_primary = 1"
            )
        }
        tgdb_assoc_map = {
            row["game_key"]: row["source_game_id"]
            for row in con.execute(
                "SELECT game_key, source_game_id FROM game_tgdb_associations WHERE is_primary = 1"
            )
        }

        asset_rows = []
        seen_asset_rows = set()
        for game in self._progress(games, "Asset generation", total=len(games)):
            game_key = game["game_key"]

            # Asset generation is now fully index-driven. We look up the
            # already-matched source IDs once, pull the pre-grouped asset rows
            # directly from memory, and dedupe before writing anything.
            lb_source_game_id = launchbox_assoc_map.get(game_key)
            if lb_source_game_id:
                for row in launchbox_assets.get(lb_source_game_id, []):
                    asset_row = (
                        game_key,
                        None,
                        row["asset_type"],
                        "launchbox",
                        row["source_row_id"],
                        row["region_code"],
                        None,
                        row["path_or_url"],
                        row["priority_rank"],
                    )
                    if asset_row in seen_asset_rows:
                        continue
                    seen_asset_rows.add(asset_row)
                    asset_rows.append(asset_row)

            tgdb_source_game_id = tgdb_assoc_map.get(game_key)
            if tgdb_source_game_id:
                for row in tgdb_assets.get(tgdb_source_game_id, []):
                    asset_row = (
                        game_key,
                        None,
                        row["asset_type"],
                        "tgdb",
                        row["source_row_id"],
                        row["region_code"],
                        None,
                        row["path_or_url"],
                        row["priority_rank"],
                    )
                    if asset_row in seen_asset_rows:
                        continue
                    seen_asset_rows.add(asset_row)
                    asset_rows.append(asset_row)

        # Bulk insert all deduped candidate rows in one pass. SQLite is much
        # faster with ``executemany`` here than with one insert per asset.
        con.executemany(
            """
            INSERT INTO asset_candidates
            (game_key, release_key, asset_type, source_system, source_row_id, region_code, language_code, path_or_url, priority_rank)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            asset_rows,
        )
        con.commit()

    def _build_launchbox_asset_index(
        self, con: sqlite3.Connection
    ) -> dict[str, list[dict]]:
        """Build an in-memory LaunchBox asset index keyed by source game ID.

        Args:
            con: SQLite connection for the temporary unified build database.

        Returns:
            Mapping of LaunchBox ``source_game_id`` to normalized asset rows.
        """
        asset_index: dict[str, list[dict]] = {}
        for row in con.execute("SELECT * FROM src_launchbox_game_images"):
            source_game_id = row["source_game_id"]
            asset_index.setdefault(source_game_id, []).append(
                {
                    "asset_type": row["image_type"],
                    "source_row_id": row["source_image_id"],
                    "region_code": self._normalize_region(row["region"]),
                    "path_or_url": row["file_name"],
                    "priority_rank": self._asset_priority("launchbox", row["region"]),
                }
            )
        return asset_index

    def _build_tgdb_asset_index(self, con: sqlite3.Connection) -> dict[str, list[dict]]:
        """Build an in-memory TGDB asset index keyed by source game ID.

        Args:
            con: SQLite connection for the temporary unified build database.

        Returns:
            Mapping of TGDB ``source_game_id`` to normalized banner rows.
        """
        asset_index: dict[str, list[dict]] = {}
        for row in con.execute("SELECT * FROM src_tgdb_banners"):
            source_game_id = row["source_game_id"]
            asset_index.setdefault(source_game_id, []).append(
                {
                    "asset_type": row["banner_type"],
                    "source_row_id": row["source_banner_id"],
                    "region_code": None,
                    "path_or_url": row["filename"],
                    "priority_rank": self._asset_priority("tgdb", None),
                }
            )
        return asset_index

    def _normalize_region(self, region: str | None) -> str | None:
        """Normalize source region text into canonical region codes.

        Args:
            region: Source region text, if any.

        Returns:
            Canonical region code or ``None``.
        """
        if region is None:
            return None
        region = region.upper().replace(" ", "_")
        if region == "UNITED_STATES":
            return "USA"
        if region == "NORTH_AMERICA":
            return "NORTH_AMERICA"
        return region

    def _asset_priority(self, source_system: str, region: str | None) -> int:
        """Rank an asset candidate by source and region preference.

        Args:
            source_system: Source system supplying the asset.
            region: Optional source region label.

        Returns:
            Integer priority where smaller values are preferred.
        """
        source_base = 0 if source_system == "launchbox" else 100
        region_code = self._normalize_region(region)
        region_priority = {
            "USA": 0,
            "NORTH_AMERICA": 1,
            "WORLD": 2,
            "EUROPE": 3,
        }.get(region_code, 9)
        return source_base + region_priority

    def _generate_series(self, con: sqlite3.Connection) -> None:
        """Populate heuristic and IGDB-backed series rows.

        Args:
            con: SQLite connection for the temporary unified build database.
        """
        # Conservative default: use explicit overrides already applied.
        # Add IGDB franchise-based grouping first, then light numbered-sequel heuristics.
        games = [
            dict(row)
            for row in con.execute(
                "SELECT game_key, platform_key, canonical_name FROM games ORDER BY canonical_name"
            )
        ]
        self._generate_igdb_series(con)
        prefix_groups: dict[tuple[str, str], list[dict]] = {}
        logger.info("Generating heuristic platform series from %s games", len(games))
        for game in self._progress(games, "Series candidate scan", total=len(games)):
            name = game["canonical_name"]
            match = None
            for token in [" 2", " 3", " II", " III"]:
                if token in name:
                    match = name.split(token)[0].strip()
                    break
            if match:
                prefix_groups.setdefault((game["platform_key"], match), []).append(game)
        for (platform_key, prefix), members in prefix_groups.items():
            if len(members) < 2:
                continue
            library_slug, platform_slug = platform_key.split(":", 1)
            series_name = f"{prefix} Series"
            series_key = platform_series_key(library_slug, platform_slug, series_name)
            con.execute(
                "INSERT OR REPLACE INTO platform_series (series_key, platform_key, name, series_type, generation_source) VALUES (?, ?, ?, ?, ?)",
                (series_key, platform_key, series_name, "subseries", "heuristic"),
            )
            con.execute(
                "DELETE FROM platform_series_games WHERE series_key = ? AND membership_source = 'heuristic'",
                (series_key,),
            )
            for index, member in enumerate(members, start=1):
                con.execute(
                    "INSERT INTO platform_series_games (series_key, game_key, sort_order, membership_source) VALUES (?, ?, ?, ?)",
                    (series_key, member["game_key"], index, "heuristic"),
                )
        con.commit()

    def _generate_igdb_series(self, con: sqlite3.Connection) -> None:
        """Populate series rows from mirrored IGDB franchise data.

        Args:
            con: SQLite connection for the temporary unified build database.
        """
        logger.info("Generating IGDB franchise-backed series")
        franchise_members: dict[str, list[dict]] = {}
        rows = con.execute("""
            SELECT g.game_key, g.platform_key, g.canonical_name, g.release_year, s.franchise_json
            FROM games g
            JOIN game_igdb_associations a ON a.game_key = g.game_key AND a.is_primary = 1
            JOIN src_igdb_games s ON s.source_game_id = a.source_game_id
            WHERE s.franchise_json IS NOT NULL AND s.franchise_json != ''
            """).fetchall()
        for row in rows:
            for franchise_id in self._json_int_list(row["franchise_json"]):
                franchise_members.setdefault(str(franchise_id), []).append(dict(row))

        for franchise_id, members in franchise_members.items():
            unique_members = self._dedupe_series_members(members)
            if len(unique_members) < 2:
                continue
            franchise_row = con.execute(
                "SELECT name FROM src_igdb_franchises WHERE source_franchise_id = ?",
                (franchise_id,),
            ).fetchone()
            franchise_name = (
                franchise_row["name"]
                if franchise_row and franchise_row["name"]
                else None
            )
            series_base_name = self._derive_series_base_name(
                [member["canonical_name"] for member in unique_members]
            )
            cross_name = self._format_series_name(
                franchise_name or series_base_name or f"IGDB Franchise {franchise_id}"
            )
            cross_key = cross_platform_series_key(cross_name)
            con.execute(
                "INSERT OR REPLACE INTO cross_platform_series (series_key, name, source_system, source_external_id) VALUES (?, ?, ?, ?)",
                (cross_key, cross_name, "igdb", franchise_id),
            )
            con.execute(
                "DELETE FROM cross_platform_series_games WHERE series_key = ? AND membership_source = 'igdb'",
                (cross_key,),
            )
            for index, member in enumerate(
                self._sort_series_members(unique_members), start=1
            ):
                con.execute(
                    "INSERT INTO cross_platform_series_games (series_key, game_key, sort_order, membership_source) VALUES (?, ?, ?, ?)",
                    (cross_key, member["game_key"], index, "igdb"),
                )

            platform_groups: dict[str, list[dict]] = {}
            for member in unique_members:
                platform_groups.setdefault(member["platform_key"], []).append(member)
            for platform_key, platform_members in platform_groups.items():
                if len(platform_members) < 2:
                    continue
                library_slug, platform_slug = platform_key.split(":", 1)
                platform_base_name = (
                    self._derive_series_base_name(
                        [member["canonical_name"] for member in platform_members]
                    )
                    or franchise_name
                    or cross_name.removesuffix(" Series")
                )
                platform_name = self._format_series_name(platform_base_name)
                platform_key_value = platform_series_key(
                    library_slug, platform_slug, platform_name
                )
                con.execute(
                    "INSERT OR REPLACE INTO platform_series (series_key, platform_key, name, series_type, generation_source) VALUES (?, ?, ?, ?, ?)",
                    (
                        platform_key_value,
                        platform_key,
                        platform_name,
                        "franchise",
                        "igdb",
                    ),
                )
                con.execute(
                    "DELETE FROM platform_series_games WHERE series_key = ? AND membership_source = 'igdb'",
                    (platform_key_value,),
                )
                for index, member in enumerate(
                    self._sort_series_members(platform_members), start=1
                ):
                    con.execute(
                        "INSERT INTO platform_series_games (series_key, game_key, sort_order, membership_source) VALUES (?, ?, ?, ?)",
                        (platform_key_value, member["game_key"], index, "igdb"),
                    )

    def _mirror_igdb_franchises(
        self, con: sqlite3.Connection, igdb: IgdbSource
    ) -> None:
        """Fetch and mirror missing IGDB franchise rows needed for series names.

        Args:
            con: SQLite connection for the temporary unified build database.
            igdb: IGDB source adapter used to resolve franchise rows.
        """
        sql = """
            SELECT s.franchise_json
            FROM game_igdb_associations a
            JOIN src_igdb_games s ON s.source_game_id = a.source_game_id
            WHERE a.is_primary = 1 AND s.franchise_json IS NOT NULL AND s.franchise_json != ''
            """

        franchise_ids: set[str] = set()
        for row in con.execute(sql):
            franchise_ids.update(
                str(franchise_id)
                for franchise_id in self._json_int_list(row["franchise_json"])
            )
        missing_ids = [
            franchise_id
            for franchise_id in sorted(franchise_ids, key=int)
            if con.execute(
                "SELECT 1 FROM src_igdb_franchises WHERE source_franchise_id = ?",
                (franchise_id,),
            ).fetchone()
            is None
        ]
        if not missing_ids:
            return
        logger.info("Resolving %s missing IGDB franchise names", len(missing_ids))
        franchises, _ = igdb.fetch_franchises(missing_ids)
        mirrored_batches, _ = igdb.mirror_franchises(con, missing_ids)
        logger.info(
            "Mirrored %s IGDB franchise rows across %s batch requests",
            len(franchises),
            mirrored_batches,
        )

    def _json_int_list(self, raw_value: str | None) -> list[int]:
        """Parse a JSON list into integer IDs.

        Args:
            raw_value: JSON string representing a list of IDs.

        Returns:
            Parsed integer IDs, or an empty list on invalid input.
        """
        if not raw_value:
            return []
        try:
            parsed = json.loads(raw_value)
        except json.JSONDecodeError:
            return []
        if not isinstance(parsed, list):
            return []
        values: list[int] = []
        for item in parsed:
            try:
                values.append(int(item))
            except (TypeError, ValueError):
                continue
        return values

    def _dedupe_series_members(self, members: list[dict]) -> list[dict]:
        """Deduplicate series members by canonical game key.

        Args:
            members: Candidate member dictionaries.

        Returns:
            Deduplicated member dictionaries.
        """
        deduped: dict[str, dict] = {}
        for member in members:
            deduped[member["game_key"]] = member
        return list(deduped.values())

    def _sort_series_members(self, members: list[dict]) -> list[dict]:
        """Sort series members into stable display order.

        Args:
            members: Candidate member dictionaries.

        Returns:
            Sorted member dictionaries.
        """
        return sorted(
            members,
            key=lambda member: (
                member.get("release_year") is None,
                member.get("release_year") or 0,
                normalize_sort_name(member["canonical_name"]),
            ),
        )

    def _derive_series_base_name(self, names: list[str]) -> str | None:
        """Derive a shared base label from a set of related game names.

        Args:
            names: Canonical game names believed to belong to one series.

        Returns:
            Shared base name or ``None`` when no reliable common label exists.
        """
        clean_names = [name.strip() for name in names if name and name.strip()]
        if len(clean_names) < 2:
            return None
        prefix = os.path.commonprefix(clean_names).strip(" :-!?'\",.")
        if prefix and len(prefix.split()) >= 2:
            return prefix
        token_lists = [name.split() for name in clean_names]
        common_tokens: list[str] = []
        for token_group in zip(*token_lists):
            if len(set(token_group)) == 1:
                common_tokens.append(token_group[0])
            else:
                break
        if len(common_tokens) >= 2:
            return " ".join(common_tokens).strip(" :-!?'\",.")
        return None

    def _format_series_name(self, base_name: str) -> str:
        """Normalize a base label into a user-facing series name.

        Args:
            base_name: Raw base series label.

        Returns:
            Label guaranteed to end with ``" Series"``.
        """
        clean = (base_name or "").strip()
        if clean.endswith(" Series"):
            return clean
        return f"{clean} Series"

    def _progress(self, iterable, description: str, total: int | None = None):
        """Wrap an iterable in ``tqdm`` when verbose progress output is enabled.

        Args:
            iterable: Iterable to wrap.
            description: Human-readable progress label.
            total: Optional total item count.

        Returns:
            Original iterable or a ``tqdm`` wrapper.
        """
        if not self.config.verbose:
            return iterable
        return tqdm(iterable, desc=description, total=total, leave=False)
