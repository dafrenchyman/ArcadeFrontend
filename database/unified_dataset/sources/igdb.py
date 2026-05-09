"""IGDB adapter with cache-aware lookup, throttling, and mirroring helpers."""

import datetime
import json
import os
import sqlite3
import time
from pathlib import Path

import requests
from igdb.wrapper import IGDBWrapper
from rapidfuzz import fuzz
from unified_dataset.config import IgdbMode
from unified_dataset.normalization import (
    best_match_score,
    meaningful_match_tokens,
    move_trailing_article_to_front,
    should_consider_candidate,
)

PLATFORM_TO_PLATFORM_WHERE_CLAUSE = {
    "snes": 'where name = ("Super Nintendo Entertainment System", "Super Famicom");',
    "snes_hacks": 'where name = ("Super Nintendo Entertainment System", "Super Famicom");',
}


class IgdbSource:
    """Read from the IGDB cache and fetch remote IGDB data when needed.

    Args:
        cache_db: SQLite database that stores exact IGDB request results.
        platform_slug: Platform slug being processed.
        mode: Cache mode controlling whether requests are reused, refreshed, or
            served offline.
    """

    def __init__(self, cache_db: Path, platform_slug: str, mode: IgdbMode) -> None:
        self.platform_slug = platform_slug
        self.mode = mode
        self.cache = sqlite3.connect(cache_db)
        self.cache.row_factory = sqlite3.Row
        self._ensure_cache_tables()
        self.client_id = os.getenv("IGDB_CLIENT_ID")
        self.api_key = os.getenv("IGDB_API_KEY")
        self._wrapper = None
        self.token = None
        self.expires_at = datetime.datetime.min
        self.last_request_at = datetime.datetime.min
        self._platform_ids: list[str] | None = None

    def _ensure_cache_tables(self) -> None:
        """Ensure the local IGDB request cache tables exist."""
        self.cache.execute("""
            CREATE TABLE IF NOT EXISTS requests (
                id INTEGER PRIMARY KEY,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                endpoint TEXT NOT NULL,
                query TEXT NOT NULL,
                result TEXT NOT NULL
            )
            """)
        self.cache.execute(
            "CREATE UNIQUE INDEX IF NOT EXISTS requests_endpoint_query_uidx ON requests (endpoint, query)"
        )
        self.cache.commit()

    def _get_wrapper(self):
        """Create or reuse an authenticated IGDB API wrapper.

        Returns:
            An authenticated ``IGDBWrapper`` instance.
        """
        now = datetime.datetime.now()
        if self._wrapper is not None and now < self.expires_at:
            return self._wrapper
        if not self.client_id or not self.api_key:
            raise RuntimeError(
                "IGDB_CLIENT_ID and IGDB_API_KEY must be configured for remote IGDB access"
            )
        token_url = f"https://id.twitch.tv/oauth2/token?client_id={self.client_id}&client_secret={self.api_key}&grant_type=client_credentials"
        response = requests.post(token_url, timeout=30)
        response.raise_for_status()
        payload = response.json()
        self.token = payload["access_token"]
        expires_in = int(payload.get("expires_in", 0))
        # Refresh a bit early so long runs do not hit an expired token mid-request.
        self.expires_at = now + datetime.timedelta(seconds=max(expires_in - 60, 0))
        self._wrapper = IGDBWrapper(self.client_id, self.token)
        return self._wrapper

    def _throttle(self) -> None:
        """Enforce the legacy 4-requests-per-second pacing rule."""
        now = datetime.datetime.now()
        earliest_next_request = self.last_request_at + datetime.timedelta(seconds=0.25)
        if now < earliest_next_request:
            time.sleep((earliest_next_request - now).total_seconds())

    def _remote_request(self, endpoint: str, query: str) -> str:
        """Execute a remote IGDB request with retry/backoff behavior.

        Args:
            endpoint: IGDB endpoint name such as ``games`` or ``franchises``.
            query: IGDB APICALYPSE query text.

        Returns:
            Raw JSON response string from IGDB.
        """
        last_error: Exception | None = None
        for attempt in range(4):
            self._throttle()
            wrapper = self._get_wrapper()
            try:
                result = wrapper.api_request(endpoint, query).decode("utf-8")
                self.last_request_at = datetime.datetime.now()
                return result
            except requests.exceptions.HTTPError as exc:
                self.last_request_at = datetime.datetime.now()
                last_error = exc
                response = exc.response
                if response is None or response.status_code != 429:
                    raise
                retry_after = response.headers.get("Retry-After")
                if retry_after is not None:
                    try:
                        sleep_seconds = float(retry_after)
                    except ValueError:
                        sleep_seconds = 2.0
                else:
                    sleep_seconds = min(2**attempt, 8)
                time.sleep(max(sleep_seconds, 0.5))
                continue
        if last_error is not None:
            raise last_error
        raise RuntimeError(f"Failed to fetch IGDB {endpoint} request")

    def _run_request(self, endpoint: str, query: str) -> tuple[list[dict], bool]:
        """Run an IGDB request using cache-first behavior.

        Args:
            endpoint: IGDB endpoint name.
            query: Exact APICALYPSE query text.

        Returns:
            A tuple of ``(parsed_rows, fetched_from_remote)``.
        """
        existing = self.cache.execute(
            "SELECT result FROM requests WHERE endpoint = ? AND query = ?",
            (endpoint, query),
        ).fetchone()
        fetched = False
        if existing is not None and self.mode != IgdbMode.REFRESH:
            return json.loads(existing["result"]), fetched
        if self.mode == IgdbMode.OFFLINE and existing is None:
            return [], fetched
        result = self._remote_request(endpoint, query)
        fetched = True
        if existing is None:
            self.cache.execute(
                "INSERT INTO requests (endpoint, query, result) VALUES (?, ?, ?)",
                (endpoint, query, result),
            )
        else:
            self.cache.execute(
                "UPDATE requests SET result = ?, created_at = CURRENT_TIMESTAMP WHERE endpoint = ? AND query = ?",
                (result, endpoint, query),
            )
        self.cache.commit()
        return json.loads(result), fetched

    def get_platform_ids(self) -> tuple[list[str], bool]:
        """Resolve IGDB platform IDs for the configured platform slug.

        Returns:
            A tuple of ``(platform_ids, fetched_from_remote)``.
        """
        if self._platform_ids is not None:
            return self._platform_ids, False
        platform_query = PLATFORM_TO_PLATFORM_WHERE_CLAUSE.get(
            self.platform_slug,
            PLATFORM_TO_PLATFORM_WHERE_CLAUSE["snes"],
        )
        results, fetched = self._run_request("platforms", f"fields *; {platform_query}")
        self._platform_ids = [
            str(row["id"]) for row in results if row.get("id") is not None
        ]
        return self._platform_ids, fetched

    def build_games_query(self, term: str) -> tuple[str | None, bool]:
        """Build the IGDB ``games`` query for a search term.

        Args:
            term: Title text to search for.

        Returns:
            A tuple of ``(query_text_or_none, platform_lookup_fetched)``.
        """
        platform_ids, platform_fetched = self.get_platform_ids()
        if not platform_ids:
            return None, platform_fetched
        platform_str = ",".join(platform_ids)
        search_term = move_trailing_article_to_front(term)
        query = (
            f'search "{search_term.replace("\"", "\'")}"; '
            "fields name,summary,storyline,first_release_date,genres,involved_companies,collections,franchises,platforms; "
            f"where platforms = ({platform_str});"
        )
        return query, platform_fetched

    def build_franchises_query(self, franchise_ids: list[str]) -> str | None:
        """Build a batched IGDB ``franchises`` query.

        Args:
            franchise_ids: Franchise IDs to request.

        Returns:
            Query text or ``None`` when no valid IDs were supplied.
        """
        cleaned = []
        for franchise_id in franchise_ids:
            try:
                cleaned.append(str(int(franchise_id)))
            except (TypeError, ValueError):
                continue
        if not cleaned:
            return None
        unique_ids = sorted(set(cleaned), key=int)
        return f"fields name,slug; where id = ({','.join(unique_ids)}); limit {len(unique_ids)};"

    def search(self, term: str, limit: int = 10) -> tuple[list[dict], bool]:
        """Search IGDB ``games`` for fuzzy match candidates.

        Args:
            term: Canonical game title to search.
            limit: Maximum number of candidates to return.

        Returns:
            A tuple of ``(candidate_rows, fetched_from_remote)``.
        """
        query, platform_fetched = self.build_games_query(term)
        if query is None:
            return [], platform_fetched
        search_term = move_trailing_article_to_front(term)
        search_tokens = meaningful_match_tokens(search_term)
        results, fetched = self._run_request("games", query)
        candidates = []
        for row in results:
            # IGDB already returns a much smaller server-side candidate set, so
            # the token gate here is just a consistency and noise-reduction
            # step rather than the main performance lever.
            candidate_name = row.get("name", "") or ""
            candidate_tokens = meaningful_match_tokens(candidate_name)
            if not should_consider_candidate(
                search_term, candidate_name, search_tokens, candidate_tokens
            ):
                continue
            # Keep IGDB scoring aligned with the local DB sources so a title
            # that normalizes cleanly in one adapter behaves the same here.
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
                        "source_game_id": str(row.get("id")),
                        "candidate_name": candidate_name,
                        "candidate_extra": row.get("first_release_date", ""),
                        "match_score": score,
                        "release_sort": row.get("first_release_date", ""),
                        "overview_length": len(
                            (row.get("summary") or "") + (row.get("storyline") or "")
                        ),
                        "raw_json": row,
                    }
                )
        candidates.sort(key=lambda item: item["match_score"], reverse=True)
        return candidates[:limit], fetched or platform_fetched

    def fetch_franchises(
        self, franchise_ids: list[str]
    ) -> tuple[dict[str, dict], bool]:
        """Fetch franchise rows from IGDB in small batches.

        Args:
            franchise_ids: Franchise IDs to resolve.

        Returns:
            A tuple of ``(franchise_row_map, any_fetched_from_remote)`` keyed by
            franchise ID string.
        """
        mapping: dict[str, dict] = {}
        any_fetched = False
        for batch in self._chunked_ids(franchise_ids, 10):
            query = self.build_franchises_query(batch)
            if query is None:
                continue
            results, fetched = self._run_request("franchises", query)
            any_fetched = any_fetched or fetched
            for row in results:
                if row.get("id") is None:
                    continue
                mapping[str(row["id"])] = row
        return mapping, any_fetched

    def mirror_franchises(
        self, target_con, franchise_ids: list[str]
    ) -> tuple[int, bool]:
        """Mirror franchise request batches into the unified DB.

        Args:
            target_con: SQLite connection for the unified build database.
            franchise_ids: Franchise IDs to mirror.

        Returns:
            A tuple of ``(mirrored_batch_count, any_fetched_from_remote)``.
        """
        mirrored = 0
        any_fetched = False
        for batch in self._chunked_ids(franchise_ids, 10):
            query = self.build_franchises_query(batch)
            if query is None:
                continue
            _, fetched = self._run_request("franchises", query)
            any_fetched = any_fetched or fetched
            self.persist_request_mirror(target_con, "franchises", query, fetched)
            mirrored += 1
        return mirrored, any_fetched

    def _chunked_ids(self, values: list[str], chunk_size: int) -> list[list[str]]:
        """Normalize and split integer-like IDs into fixed-size batches.

        Args:
            values: Raw ID values.
            chunk_size: Maximum number of IDs per batch.

        Returns:
            List of normalized ID batches.
        """
        cleaned = []
        seen = set()
        for value in values:
            try:
                normalized = str(int(value))
            except (TypeError, ValueError):
                continue
            if normalized in seen:
                continue
            seen.add(normalized)
            cleaned.append(normalized)
        return [
            cleaned[idx : idx + chunk_size]  # noqa: E203
            for idx in range(0, len(cleaned), chunk_size)
        ]

    def persist_request_mirror(
        self, target_con, endpoint: str, query: str, fetched: bool
    ) -> None:
        """Persist a cached IGDB request result into unified mirror tables.

        Args:
            target_con: SQLite connection for the unified build database.
            endpoint: IGDB endpoint name whose cached result should be mirrored.
            query: Exact APICALYPSE query text used as the cache key.
            fetched: Whether the request was fetched remotely during this run.
        """
        existing = self.cache.execute(
            "SELECT result FROM requests WHERE endpoint = ? AND query = ?",
            (endpoint, query),
        ).fetchone()
        if existing is None:
            return
        target_con.execute(
            "INSERT OR REPLACE INTO src_igdb_requests (request_key, endpoint, query_text, result_json, fetched_from_remote) VALUES (?, ?, ?, ?, ?)",
            (f"{endpoint}:{query}", endpoint, query, existing["result"], int(fetched)),
        )
        # IGDB IDs are only unique within an endpoint namespace. A franchise
        # can legitimately reuse the same numeric ID as a game, so we must not
        # mirror non-``games`` payloads into ``src_igdb_games`` or they will
        # overwrite real game rows.
        if endpoint == "games":
            for game in json.loads(existing["result"]):
                if "id" not in game:
                    continue
                target_con.execute(
                    """
                    INSERT OR REPLACE INTO src_igdb_games
                    (source_game_id, name, summary, storyline, first_release_date, genres_json, involved_companies_json, collections_json, franchise_json, raw_json)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        str(game.get("id")),
                        game.get("name"),
                        game.get("summary"),
                        game.get("storyline"),
                        str(game.get("first_release_date") or ""),
                        json.dumps(game.get("genres", [])),
                        json.dumps(game.get("involved_companies", [])),
                        json.dumps(game.get("collections", [])),
                        json.dumps(game.get("franchises", [])),
                        json.dumps(game),
                    ),
                )
        if endpoint == "franchises":
            for franchise in json.loads(existing["result"]):
                if "id" not in franchise:
                    continue
                target_con.execute(
                    """
                    INSERT OR REPLACE INTO src_igdb_franchises
                    (source_franchise_id, name, slug, raw_json)
                    VALUES (?, ?, ?, ?)
                    """,
                    (
                        str(franchise.get("id")),
                        franchise.get("name"),
                        franchise.get("slug"),
                        json.dumps(franchise),
                    ),
                )
