"""Datomatic/No-Intro source adapter and parser."""

import json
import re
from dataclasses import dataclass
from pathlib import Path

import xmltodict
from unified_dataset.keys import datomatic_source_key
from unified_dataset.normalization import normalize_sort_name

REGION_MAP = {
    "USA": "USA",
    "Europe": "EUROPE",
    "Japan": "JAPAN",
    "World": "WORLD",
    "Asia": "ASIA",
    "Australia": "AUSTRALIA",
    "Canada": "CANADA",
    "Korea": "KOREA",
    "France": "FRANCE",
    "Germany": "GERMANY",
    "Italy": "ITALY",
    "Spain": "SPAIN",
    "UK": "UK",
    "United Kingdom": "UK",
    "North America": "NORTH_AMERICA",
}

LANGUAGE_CODES = {
    "En": "en",
    "En-US": "en-us",
    "En-GB": "en-gb",
    "Ja": "ja",
    "Fr": "fr",
    "De": "de",
    "Es": "es",
    "It": "it",
    "Pt": "pt",
    "Nl": "nl",
    "Ru": "ru",
    "Ko": "ko",
    "Zh": "zh",
}

RELEASE_PATTERNS = {
    "beta": re.compile(r"\bBeta\b", re.I),
    "prototype": re.compile(r"\bProto\b|\bPrototype\b", re.I),
    "demo": re.compile(r"\bDemo\b", re.I),
    "sample": re.compile(r"\bSample\b", re.I),
    "competition": re.compile(r"\bCompetition\b", re.I),
    "translation_patch": re.compile(r"\bTranslation\b", re.I),
    "bugfix_patch": re.compile(r"\bBugfix\b|\bBug Fix\b", re.I),
    "randomizer": re.compile(r"\bRandomizer\b", re.I),
    "total_conversion": re.compile(r"\bTotal Conversion\b", re.I),
    "hack": re.compile(r"\bHack\b", re.I),
}

TITLE_CLEAN_PATTERNS = [
    re.compile(r"\s+\(([^)]*)\)"),
    re.compile(r"\s+\[([^\]]*)\]"),
]


@dataclass(slots=True)
class ParsedNoIntroRecord:
    """Normalized representation of one parsed Datomatic game row."""

    source_game_id: str
    source_key: str
    raw_title: str
    base_title: str
    normalized_title: str
    categories: list[str]
    library_slug: str
    game_kind: str
    release_type: str
    patch_kind: str | None
    primary_region_code: str | None
    regions: list[str]
    languages: list[str]
    revision_label: str | None
    version_label: str | None
    is_world: bool
    roms: list[dict]


class NoIntroSource:
    """Read and parse one Datomatic/No-Intro DAT file.

    Args:
        platform_slug: Platform slug being processed.
        dat_file: Path to the DAT/XML file.
        official_library: Library slug for official content.
        hacks_library: Library slug for hacks/mods content.
    """

    def __init__(
        self,
        platform_slug: str,
        dat_file: Path,
        official_library: str,
        hacks_library: str,
    ) -> None:
        self.platform_slug = platform_slug
        self.dat_file = dat_file
        self.official_library = official_library
        self.hacks_library = hacks_library

    def load_source_rows(self) -> list[dict]:
        """Load raw game/ROM rows from the DAT file.

        Returns:
            A list of dictionaries containing Datomatic game titles,
            descriptions, and ROM metadata.
        """
        with self.dat_file.open("r", encoding="utf-8") as handle:
            parsed = xmltodict.parse(handle.read())
        games = parsed["datafile"]["game"]
        if not isinstance(games, list):
            games = [games]
        rows = []
        for game_index, game in enumerate(games):
            roms = game.get("rom", [])
            if isinstance(roms, dict):
                roms = [roms]
            categories = game.get("category", [])
            if isinstance(categories, str):
                categories = [categories]
            elif categories is None:
                categories = []
            source_game_id = f"{self.platform_slug}:{game_index}"
            rows.append(
                {
                    "source_game_id": source_game_id,
                    "raw_title": game["@name"],
                    "description": game.get("description", ""),
                    "categories": [
                        str(category).strip()
                        for category in categories
                        if str(category).strip()
                    ],
                    "roms": [
                        {
                            "source_rom_id": f"{source_game_id}:{rom_index}",
                            "filename": rom.get("@name"),
                            "size_bytes": int(rom.get("@size", "0") or 0),
                            "crc32": rom.get("@crc"),
                            "md5": rom.get("@md5"),
                            "sha1": rom.get("@sha1"),
                        }
                        for rom_index, rom in enumerate(roms)
                    ],
                }
            )
        return rows

    def parse_row(self, row: dict) -> ParsedNoIntroRecord:
        """Parse one raw Datomatic row into normalized canonical input data.

        Args:
            row: Raw Datomatic row from ``load_source_rows``.

        Returns:
            A ``ParsedNoIntroRecord`` containing extracted title, region,
            language, release, and grouping information.
        """
        raw_title = row["raw_title"]
        roms = row["roms"]
        categories = row.get("categories", [])
        first_hash = next(
            (
                rom.get("sha1") or rom.get("md5") or rom.get("crc32")
                for rom in roms
                if rom
            ),
            None,
        )
        first_size = next((rom.get("size_bytes") for rom in roms if rom), None)
        source_key = datomatic_source_key(
            self.platform_slug, raw_title, first_hash, first_size
        )
        groups = re.findall(r"\(([^)]*)\)", raw_title)
        regions = []
        languages = []
        release_type = "retail"
        patch_kind = None
        game_kind = "main"
        revision_label = None
        version_label = None

        for token in groups:
            if token in REGION_MAP:
                mapped = REGION_MAP[token]
                if mapped not in regions:
                    regions.append(mapped)
            for language_token in [part.strip() for part in token.split(",")]:
                if language_token in LANGUAGE_CODES:
                    code = LANGUAGE_CODES[language_token]
                    if code not in languages:
                        languages.append(code)
            if token.lower().startswith("rev"):
                revision_label = token
            if token.lower().startswith("v"):
                version_label = token

        lowered = raw_title.lower()
        for candidate_type, pattern in RELEASE_PATTERNS.items():
            if pattern.search(raw_title):
                if candidate_type in {
                    "translation_patch",
                    "bugfix_patch",
                    "randomizer",
                    "total_conversion",
                    "hack",
                }:
                    patch_kind = candidate_type
                if candidate_type == "competition":
                    game_kind = "competition"
                elif candidate_type in {"randomizer", "total_conversion", "hack"}:
                    game_kind = "hack"
                if candidate_type in {"beta", "prototype", "demo", "sample"}:
                    release_type = candidate_type
                elif candidate_type in {"translation_patch", "bugfix_patch"}:
                    release_type = candidate_type
                elif candidate_type in {"randomizer", "total_conversion", "hack"}:
                    release_type = candidate_type

        if "competition" in lowered:
            game_kind = "competition"
        if patch_kind in {"randomizer", "total_conversion", "hack"}:
            library_slug = self.hacks_library
        else:
            library_slug = self.official_library

        base_title = raw_title
        for pattern in TITLE_CLEAN_PATTERNS:
            base_title = pattern.sub("", base_title)
        base_title = base_title.strip()
        normalized_title = normalize_sort_name(base_title)
        primary_region_code = regions[0] if regions else None
        is_world = "WORLD" in regions
        if is_world and "WORLD" not in regions:
            regions.append("WORLD")
        if not regions and "world" in lowered:
            regions.append("WORLD")
            primary_region_code = "WORLD"
            is_world = True
        return ParsedNoIntroRecord(
            source_game_id=row["source_game_id"],
            source_key=source_key,
            raw_title=raw_title,
            base_title=base_title,
            normalized_title=normalized_title,
            categories=categories,
            library_slug=library_slug,
            game_kind=game_kind,
            release_type=release_type,
            patch_kind=patch_kind,
            primary_region_code=primary_region_code,
            regions=regions,
            languages=languages,
            revision_label=revision_label,
            version_label=version_label,
            is_world=is_world,
            roms=roms,
        )

    @staticmethod
    def persist_source(con, rows: list[dict]) -> None:
        """Persist raw Datomatic rows into source mirror tables.

        Args:
            con: SQLite connection for the unified build database.
            rows: Raw Datomatic rows produced by ``load_source_rows``.
        """
        for row in rows:
            source_key = datomatic_source_key(
                row["source_game_id"].split(":")[0],
                row["raw_title"],
                next((rom.get("sha1") or rom.get("md5") for rom in row["roms"]), None),
                next((rom.get("size_bytes") for rom in row["roms"]), None),
            )
            con.execute(
                "INSERT INTO src_no_intro_games (source_game_id, source_key, platform_slug, raw_title, description, categories_json) VALUES (?, ?, ?, ?, ?, ?)",
                (
                    row["source_game_id"],
                    source_key,
                    row["source_game_id"].split(":")[0],
                    row["raw_title"],
                    row["description"],
                    json.dumps(row.get("categories", [])),
                ),
            )
            for rom in row["roms"]:
                con.execute(
                    "INSERT INTO src_no_intro_roms (source_rom_id, source_game_id, filename, size_bytes, crc32, md5, sha1) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    (
                        rom["source_rom_id"],
                        row["source_game_id"],
                        rom["filename"],
                        rom["size_bytes"],
                        rom["crc32"],
                        rom["md5"],
                        rom["sha1"],
                    ),
                )

    @staticmethod
    def persist_parsed(con, parsed: ParsedNoIntroRecord) -> None:
        """Persist a parsed Datomatic record into the parsed staging table.

        Args:
            con: SQLite connection for the unified build database.
            parsed: Parsed Datomatic record to store.
        """
        con.execute(
            """
            INSERT INTO parsed_no_intro_records (
                source_key, source_game_id, platform_slug, library_slug, raw_title, base_title, normalized_title, categories_json,
                game_kind, release_type, patch_kind, primary_region_code, regions_json, languages_json,
                revision_label, version_label, is_world, rom_count
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                parsed.source_key,
                parsed.source_game_id,
                parsed.source_game_id.split(":")[0],
                parsed.library_slug,
                parsed.raw_title,
                parsed.base_title,
                parsed.normalized_title,
                json.dumps(parsed.categories),
                parsed.game_kind,
                parsed.release_type,
                parsed.patch_kind,
                parsed.primary_region_code,
                json.dumps(parsed.regions),
                json.dumps(parsed.languages),
                parsed.revision_label,
                parsed.version_label,
                int(parsed.is_world),
                len(parsed.roms),
            ),
        )
