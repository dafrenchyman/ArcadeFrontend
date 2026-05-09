"""TGDB source adapter for mirroring and fuzzy searching local dump data."""

import html
import sqlite3
from pathlib import Path

import pandas
from rapidfuzz import fuzz
from unified_dataset.normalization import (
    best_match_score,
    meaningful_match_tokens,
    move_trailing_article_to_front,
    should_consider_candidate,
)

PLATFORM_TO_DB_NAME = {
    "snes": "super-nintendo-snes",
    "snes_hacks": "super-nintendo-snes",
}


class TgdbSource:
    """Access TGDB source data for one platform.

    Args:
        db_path: Path to the local TGDB SQLite dump.
        platform_slug: Platform slug being processed.
    """

    def __init__(self, db_path: Path, platform_slug: str) -> None:
        self.db_path = db_path
        self.platform_slug = platform_slug
        self.con = sqlite3.connect(db_path)
        self.con.row_factory = sqlite3.Row
        self._search_rows: list[dict] | None = None

    def load_source(self, target_con: sqlite3.Connection) -> None:
        """Mirror TGDB rows for the configured platform into the unified DB.

        Args:
            target_con: SQLite connection for the unified build database.
        """
        platform_alias = PLATFORM_TO_DB_NAME.get(
            self.platform_slug, PLATFORM_TO_DB_NAME.get("snes")
        )
        platform_id_row = self.con.execute(
            "SELECT id FROM platforms WHERE alias = ?", (platform_alias,)
        ).fetchone()
        if not platform_id_row:
            return
        platform_id = platform_id_row["id"]
        games_df = pandas.read_sql(
            """
            SELECT id, game_title, release_date, overview, COALESCE(players, 1) AS players, COALESCE(coop, 'no') AS coop, youtube
            FROM games
            WHERE platform = ?
            """,
            self.con,
            params=(platform_id,),
        )
        for _, row in games_df.iterrows():
            game_title = self._clean_text(row["game_title"])
            overview = self._clean_text(row["overview"])
            target_con.execute(
                "INSERT OR REPLACE INTO src_tgdb_games (source_game_id, platform_alias, game_title, release_date, overview, players, coop, youtube) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    str(row["id"]),
                    platform_alias,
                    game_title,
                    row["release_date"],
                    overview,
                    int(row["players"]),
                    row["coop"],
                    row["youtube"],
                ),
            )
        banners_df = pandas.read_sql(
            "SELECT id, games_id, type, side, filename FROM banners", self.con
        )
        for _, row in banners_df.iterrows():
            target_con.execute(
                "INSERT OR REPLACE INTO src_tgdb_banners (source_banner_id, source_game_id, banner_type, side, filename) VALUES (?, ?, ?, ?, ?)",
                (
                    str(row["id"]),
                    str(row["games_id"]),
                    row["type"],
                    row["side"],
                    row["filename"],
                ),
            )

    def search(self, term: str, limit: int = 10) -> list[dict]:
        """Search TGDB titles for fuzzy match candidates.

        Args:
            term: Search term derived from the canonical game name.
            limit: Maximum number of candidates to return.

        Returns:
            Candidate dictionaries sorted by descending fuzzy score.
        """
        search_term = move_trailing_article_to_front(term)
        search_tokens = meaningful_match_tokens(search_term)
        candidates = []
        for row in self._load_search_rows():
            # Cheap token overlap is the first gate. If the candidate fails the
            # coarse token/number checks, it is almost never worth paying the
            # fuzzy-scoring cost for it.
            if not should_consider_candidate(
                search_term, row["candidate_name"], search_tokens, row["match_tokens"]
            ):
                continue
            candidate_name = row["candidate_name"]
            # Search scoring intentionally combines article-front display names
            # and normalized sort forms. That makes regional punctuation and
            # ", The" title variants far less brittle to match automatically.
            score = best_match_score(
                search_term,
                candidate_name,
                fuzz.ratio,
                fuzz.token_set_ratio,
                fuzz.partial_ratio,
            )
            if score >= 55:
                candidates.append(
                    {
                        "source_game_id": row["source_game_id"],
                        "candidate_name": candidate_name,
                        "candidate_extra": row["release_sort"] or "",
                        "match_score": score,
                        "release_sort": row["release_sort"] or "",
                        "overview_length": row["overview_length"],
                    }
                )
        candidates.sort(key=lambda item: item["match_score"], reverse=True)
        return candidates[:limit]

    def _load_search_rows(self) -> list[dict]:
        """Load and cache the TGDB rows used by repeated fuzzy searches.

        Args:
            None.

        Returns:
            Pre-cleaned TGDB rows for the configured platform.
        """
        if self._search_rows is not None:
            return self._search_rows
        platform_alias = PLATFORM_TO_DB_NAME.get(
            self.platform_slug, PLATFORM_TO_DB_NAME.get("snes")
        )
        platform_id_row = self.con.execute(
            "SELECT id FROM platforms WHERE alias = ?", (platform_alias,)
        ).fetchone()
        if not platform_id_row:
            self._search_rows = []
            return self._search_rows
        platform_id = platform_id_row["id"]
        rows = self.con.execute(
            "SELECT id, game_title, release_date, overview FROM games WHERE platform = ?",
            (platform_id,),
        ).fetchall()
        search_rows = []
        for row in rows:
            candidate_name = self._clean_text(row["game_title"])
            search_rows.append(
                {
                    "source_game_id": str(row["id"]),
                    "candidate_name": candidate_name,
                    "release_sort": row["release_date"] or "",
                    "overview_length": len(row["overview"] or ""),
                    "match_tokens": meaningful_match_tokens(candidate_name or ""),
                }
            )
        self._search_rows = search_rows
        return self._search_rows

    def _clean_text(self, value):
        """Decode TGDB HTML-escaped text values.

        Args:
            value: Raw value from the TGDB dump.

        Returns:
            A cleaned string with HTML entities decoded, or ``None`` if the
            input value is missing.
        """
        if value is None:
            return None
        return html.unescape(str(value))
