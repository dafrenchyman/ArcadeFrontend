"""LaunchBox metadata adapter for mirroring and fuzzy searching local XML."""

from pathlib import Path

import xmltodict
from rapidfuzz import fuzz
from unified_dataset.normalization import (
    best_match_score,
    meaningful_match_tokens,
    move_trailing_article_to_front,
    should_consider_candidate,
)

PLATFORM_LOOKUP = {
    "snes": "Super Nintendo Entertainment System",
    "snes_hacks": "Super Nintendo Entertainment System",
}


ENGLISH_FRIENDLY_REGIONS = {"USA", "WORLD", "EUROPE", "NORTH_AMERICA"}


class LaunchboxSource:
    """Access LaunchBox metadata for one platform.

    Args:
        metadata_dir: Directory containing LaunchBox XML metadata files.
        platform_slug: Platform slug being processed.
    """

    def __init__(self, metadata_dir: Path, platform_slug: str) -> None:
        self.metadata_dir = metadata_dir
        self.platform_slug = platform_slug
        self._metadata_root: dict | None = None
        self._games: list[dict] | None = None
        self._alternate_names: dict[str, list[dict]] | None = None
        self._search_rows: list[dict] | None = None

    def _load_root(self) -> dict:
        """Load and cache the LaunchBox XML root object.

        Returns:
            Parsed ``LaunchBox`` root dictionary from ``Metadata.xml``.
        """
        if self._metadata_root is not None:
            return self._metadata_root
        metadata_file = self.metadata_dir / "Metadata.xml"
        with metadata_file.open("r", encoding="utf-8") as handle:
            self._metadata_root = xmltodict.parse(handle.read()).get("LaunchBox", {})
        return self._metadata_root

    def _load_metadata(self) -> list[dict]:
        """Load and cache LaunchBox game metadata from XML.

        Returns:
            A list of raw LaunchBox ``Game`` dictionaries.
        """
        if self._games is not None:
            return self._games
        metadata_root = self._load_root()
        games = metadata_root.get("Game", [])
        if isinstance(games, dict):
            games = [games]
        self._games = games
        return games

    def _load_alternate_names(self) -> dict[str, list[dict]]:
        """Load and cache LaunchBox game alternate names keyed by game ID.

        Returns:
            Mapping of LaunchBox ``DatabaseID`` to alternate-name rows.
        """
        if self._alternate_names is not None:
            return self._alternate_names
        metadata_root = self._load_root()
        alternates = metadata_root.get("GameAlternateName", [])
        if isinstance(alternates, dict):
            alternates = [alternates]
        alternate_map: dict[str, list[dict]] = {}
        for alternate in alternates:
            database_id = str(alternate.get("DatabaseID", "")).strip()
            alternate_name = str(alternate.get("AlternateName", "")).strip()
            if not database_id or not alternate_name:
                continue
            alternate_map.setdefault(database_id, []).append(
                {
                    "alternate_name": alternate_name,
                    "region": str(alternate.get("Region", "")).strip() or None,
                }
            )
        self._alternate_names = alternate_map
        return alternate_map

    def load_source(self, target_con) -> None:
        """Mirror LaunchBox games, images, and alternate names into the DB.

        Args:
            target_con: SQLite connection for the unified build database.
        """
        alternate_names = self._load_alternate_names()
        for game in self._load_metadata():
            if game.get("Platform") != PLATFORM_LOOKUP.get(
                self.platform_slug, PLATFORM_LOOKUP["snes"]
            ):
                continue
            source_game_id = str(game.get("DatabaseID"))
            target_con.execute(
                """
                INSERT OR REPLACE INTO src_launchbox_games
                (source_game_id, platform_name, name, release_year, overview, max_players, release_type, cooperative, genres, developer, publisher)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    source_game_id,
                    game.get("Platform"),
                    game.get("Name"),
                    int(game.get("ReleaseYear", 0) or 0) or None,
                    game.get("Overview"),
                    int(game.get("MaxPlayers", 1) or 1),
                    game.get("ReleaseType"),
                    1 if str(game.get("Cooperative", "False")).lower() == "true" else 0,
                    game.get("Genres"),
                    game.get("Developer"),
                    game.get("Publisher"),
                ),
            )
            # Alternate names are only mirrored for games already accepted into
            # the current platform. This keeps region/localized aliases from
            # other platforms from contaminating platform-scoped matching.
            for alternate in alternate_names.get(source_game_id, []):
                target_con.execute(
                    """
                    INSERT INTO src_launchbox_game_alternate_names (source_game_id, alternate_name, region)
                    VALUES (?, ?, ?)
                    """,
                    (
                        source_game_id,
                        alternate["alternate_name"],
                        alternate["region"],
                    ),
                )

        metadata_root = self._load_root()
        images = metadata_root.get("GameImage", [])
        if isinstance(images, dict):
            images = [images]
        for idx, image in enumerate(images):
            target_con.execute(
                "INSERT OR REPLACE INTO src_launchbox_game_images (source_image_id, source_game_id, image_type, region, file_name) VALUES (?, ?, ?, ?, ?)",
                (
                    str(idx),
                    str(image.get("DatabaseID")),
                    image.get("Type"),
                    image.get("Region"),
                    image.get("FileName"),
                ),
            )

    def search(
        self, term: str, limit: int = 10, preferred_regions: set[str] | None = None
    ) -> list[dict]:
        """Search LaunchBox titles for fuzzy match candidates.

        Args:
            term: Search term derived from the canonical game name.
            limit: Maximum number of candidates to return.
            preferred_regions: Preferred region codes derived from No-Intro
                release metadata for the current canonical game.

        Returns:
            Candidate dictionaries sorted by descending fuzzy score.
        """
        search_term = move_trailing_article_to_front(term)
        search_tokens = meaningful_match_tokens(search_term)
        preferred_regions = {
            region.upper() for region in (preferred_regions or set()) if region
        }
        prefers_english_friendly_name = bool(
            preferred_regions & ENGLISH_FRIENDLY_REGIONS
        )
        candidates = []
        for row in self._load_search_rows():
            # Apply the same coarse token gate as TGDB so we do not fuzzy-score
            # obviously unrelated LaunchBox titles or alternate names.
            if not should_consider_candidate(
                search_term, row["candidate_name"], search_tokens, row["match_tokens"]
            ):
                continue
            # Use the shared multi-view scorer so LaunchBox titles follow
            # the same article and normalization behavior as TGDB/IGDB.
            score = best_match_score(
                search_term,
                row["candidate_name"],
                fuzz.ratio,
                fuzz.token_set_ratio,
                fuzz.partial_ratio,
            )
            # Alternate names are useful, but for games that clearly have an
            # English-friendly region footprint in No-Intro we should not let
            # a Japan-only alias compete on equal footing with a primary or
            # English-region LaunchBox title.
            if (
                row["is_alternate_name"]
                and prefers_english_friendly_name
                and row["alternate_region"]
                and row["alternate_region"].upper() not in ENGLISH_FRIENDLY_REGIONS
            ):
                score -= 8
            if score < 55:
                continue
            candidates.append(
                {
                    "source_game_id": row["source_game_id"],
                    "candidate_name": row["candidate_name"],
                    "candidate_extra": row["candidate_extra"],
                    "match_score": score,
                    "release_sort": row["release_sort"],
                    "overview_length": row["overview_length"],
                    "is_alternate_name": row["is_alternate_name"],
                    "alternate_region": row["alternate_region"],
                }
            )
        candidates.sort(key=lambda item: item["match_score"], reverse=True)
        return candidates[:limit]

    def _load_search_rows(self) -> list[dict]:
        """Load and cache the flattened LaunchBox search index.

        Args:
            None.

        Returns:
            Flat list of platform-scoped primary and alternate names that can
            be scored without reparsing XML on every search.
        """
        if self._search_rows is not None:
            return self._search_rows

        alternate_names = self._load_alternate_names()
        search_rows = []
        for game in self._load_metadata():
            if game.get("Platform") != PLATFORM_LOOKUP.get(
                self.platform_slug, PLATFORM_LOOKUP["snes"]
            ):
                continue
            release_type = str(game.get("ReleaseType", "") or "").strip().lower()
            # The official-library build should not pull LaunchBox ROM hacks
            # into its auto-match candidate pool. They are still useful source
            # data elsewhere, but they cause very noisy false positives here.
            if release_type == "rom hack":
                continue
            source_game_id = str(game.get("DatabaseID"))
            release_sort = str(game.get("ReleaseYear", "") or "")
            overview_length = len(str(game.get("Overview", "") or ""))
            search_rows.append(
                {
                    "source_game_id": source_game_id,
                    "candidate_name": str(game.get("Name", "") or ""),
                    "candidate_extra": release_sort,
                    "release_sort": release_sort,
                    "overview_length": overview_length,
                    "match_tokens": meaningful_match_tokens(
                        str(game.get("Name", "") or "")
                    ),
                    "is_alternate_name": False,
                    "alternate_region": None,
                }
            )
            # Flatten alternate names into the same search index so lookups do
            # not need to rebuild per-game name lists on every search.
            for alternate in alternate_names.get(source_game_id, []):
                search_rows.append(
                    {
                        "source_game_id": source_game_id,
                        "candidate_name": alternate["alternate_name"],
                        "candidate_extra": alternate["region"] or release_sort,
                        "release_sort": release_sort,
                        "overview_length": overview_length,
                        "match_tokens": meaningful_match_tokens(
                            alternate["alternate_name"]
                        ),
                        "is_alternate_name": True,
                        "alternate_region": alternate["region"],
                    }
                )

        self._search_rows = search_rows
        return self._search_rows
