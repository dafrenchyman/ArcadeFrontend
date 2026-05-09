"""Small rule-based helpers for deciding whether a source match is acceptable."""

import datetime
from dataclasses import dataclass

from unified_dataset.normalization import normalize_sort_name


@dataclass(slots=True)
class MatchDecision:
    """Result of evaluating a source candidate list for one game.

    Attributes:
        accepted: The accepted candidate row when auto-matching succeeds.
        candidates: Full candidate list that was evaluated.
        unresolved_reason: Explanation for why the candidates were not accepted.
    """

    accepted: dict | None
    candidates: list[dict]
    unresolved_reason: str | None = None


def decide_match(
    target_name: str,
    candidates: list[dict],
    accepted_threshold: int = 88,
    min_margin: int = 5,
) -> MatchDecision:
    """Decide whether a candidate list is strong enough to auto-accept.

    Args:
        target_name: Canonical game name being matched.
        candidates: Candidate rows ordered by descending confidence.
        accepted_threshold: Minimum score required for automatic acceptance.
        min_margin: Minimum lead over the second-best candidate required to
            break ambiguity.

    Returns:
        A ``MatchDecision`` containing the accepted candidate or the reason the
        match should remain unresolved.
    """
    if not candidates:
        return MatchDecision(None, [], "no_candidates")

    # Fuzzy scorers can legitimately give multiple candidates the same top
    # score, especially when sequel numbers or subtitles are short. Before we
    # fall back to generic score/margin rules, prefer the one candidate whose
    # normalized title exactly matches the canonical target name.
    target_normalized = normalize_sort_name(target_name)
    exact_normalized_candidates = [
        candidate
        for candidate in candidates
        if normalize_sort_name(candidate["candidate_name"]) == target_normalized
    ]
    # A unique normalized-exact title match should win immediately. Fuzzy
    # thresholds exist to protect approximate matches, not to block the one
    # candidate whose normalized title is exactly the same game name.
    if len(exact_normalized_candidates) == 1:
        return MatchDecision(exact_normalized_candidates[0], candidates, None)
    if len(exact_normalized_candidates) > 1:
        # Some source dumps contain duplicate rows for the same semantic game.
        # When multiple candidates normalize to the exact same title, prefer
        # the one with better supporting metadata instead of forcing a manual
        # override for every duplicate.
        best_exact = _pick_duplicate_candidate(exact_normalized_candidates)
        return MatchDecision(best_exact, candidates, None)

    # If there is no exact normalized tie-break, use the classic threshold +
    # margin rule. This keeps ambiguous high-score packs unresolved so the
    # workbook override flow can decide them explicitly.
    top = candidates[0]
    if len(candidates) == 1 and top["match_score"] >= accepted_threshold:
        return MatchDecision(top, candidates, None)
    top_pack = [
        candidate
        for candidate in candidates
        if candidate["match_score"] == top["match_score"]
    ]
    if top["match_score"] >= accepted_threshold and len(top_pack) > 1:
        top_pack_normalized = {
            normalize_sort_name(candidate["candidate_name"]) for candidate in top_pack
        }
        # When the top score pack is the same title repeated multiple times,
        # choose deterministically instead of leaving the match unresolved.
        if len(top_pack_normalized) == 1:
            return MatchDecision(_pick_duplicate_candidate(top_pack), candidates, None)
    second_score = candidates[1]["match_score"] if len(candidates) > 1 else 0
    if (
        top["match_score"] >= accepted_threshold
        and (top["match_score"] - second_score) >= min_margin
    ):
        return MatchDecision(top, candidates, None)
    if top["match_score"] < accepted_threshold:
        return MatchDecision(None, candidates, "below_threshold")
    return MatchDecision(None, candidates, "ambiguous")


def _pick_duplicate_candidate(candidates: list[dict]) -> dict:
    """Choose one candidate from an otherwise equivalent duplicate set.

    Args:
        candidates: Candidates that already represent the same normalized title.

    Returns:
        The preferred duplicate candidate.
    """
    return sorted(candidates, key=_duplicate_tiebreak_key)[0]


def _duplicate_tiebreak_key(candidate: dict) -> tuple:
    """Rank duplicate candidates by metadata quality and stable identity.

    Args:
        candidate: Candidate row from one source adapter.

    Returns:
        Sort key where smaller values are better. The ranking prefers:
        1. longer overview/description text
        2. newer release dates
        3. lower stable source IDs for deterministic fallback
    """
    release_date = _normalize_release_value(candidate.get("release_sort"))
    overview_length = -(int(candidate.get("overview_length") or 0))
    source_id = _normalize_source_id(candidate.get("source_game_id"))
    has_release_date = 0 if release_date is not None else 1
    newer_release_sort = _reverse_sortable_date(release_date)
    return (overview_length, has_release_date, newer_release_sort, source_id)


def _normalize_release_value(value) -> str | None:
    """Convert mixed source release values into one sortable text format.

    Args:
        value: Source-specific release value such as ``YYYY-MM-DD``, year-only
            text, or a Unix timestamp.

    Returns:
        ISO-like sortable text or ``None`` when the value is missing.
    """
    if value in {None, ""}:
        return None
    text = str(value).strip()
    if not text:
        return None
    if text.isdigit():
        if len(text) == 4:
            return f"{text}-01-01"
        try:
            return datetime.datetime.utcfromtimestamp(int(text)).strftime("%Y-%m-%d")
        except (ValueError, OSError, OverflowError):
            return text
    return text


def _normalize_source_id(value) -> tuple[int, str]:
    """Normalize source IDs so final tie-break selection stays deterministic.

    Args:
        value: Candidate source identifier.

    Returns:
        Tuple that sorts numeric IDs before non-numeric IDs while preserving a
        deterministic lexical fallback.
    """
    text = str(value or "")
    try:
        return (0, f"{int(text):020d}")
    except (TypeError, ValueError):
        return (1, text)


def _reverse_sortable_date(value: str | None) -> str:
    """Invert a sortable date string so newer dates sort first.

    Args:
        value: ISO-like sortable date text or ``None``.

    Returns:
        String that sorts lexically in the opposite order of ``value``. Missing
        dates sort last.
    """
    if value is None:
        return "9999-99-99"
    translated = []
    for char in value:
        if char.isdigit():
            translated.append(str(9 - int(char)))
        else:
            translated.append(char)
    return "".join(translated)
