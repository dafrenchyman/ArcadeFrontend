"""Shared text-normalization helpers for canonical keys and sorting.

The unified dataset uses human-readable display names alongside normalized
machine identifiers. These helpers keep those two concerns separate by
providing deterministic normalization for sort keys and slugs.
"""

import re
import unicodedata

ROMAN_NUMERAL_MAP = {
    " i ": " 1 ",
    " ii ": " 2 ",
    " iii ": " 3 ",
    " iv ": " 4 ",
    " v ": " 5 ",
    " vi ": " 6 ",
    " vii ": " 7 ",
    " viii ": " 8 ",
    " ix ": " 9 ",
    " x ": " 10 ",
}

TRAILING_ARTICLE_RE = re.compile(
    r"^(?P<body>.+?),\s*(?P<article>The|A|An)$", re.IGNORECASE
)
LEADING_ARTICLE_RE = re.compile(
    r"^(?P<article>The|A|An)\s+(?P<body>.+)$", re.IGNORECASE
)
INVALID_STANDALONE_TITLES = {"the", "a", "an"}
MEANINGFUL_TOKEN_STOPWORDS = {
    "the",
    "a",
    "an",
    "and",
    "of",
    "for",
    "to",
    "in",
    "on",
    "at",
    "by",
    "with",
}
SHORT_TITLE_PREFIX_ALLOWLIST = {
    "disney",
    "disneys",
    "the",
    "le",
    "les",
}
ROMAN_NUMERAL_TOKEN_MAP = {
    "i": "1",
    "ii": "2",
    "iii": "3",
    "iv": "4",
    "v": "5",
    "vi": "6",
    "vii": "7",
    "viii": "8",
    "ix": "9",
    "x": "10",
}


def ascii_fold(value: str) -> str:
    """Fold a Unicode string down to ASCII.

    Args:
        value: Input text that may contain accents or other Unicode characters.

    Returns:
        A best-effort ASCII-only representation of ``value``.
    """
    normalized = unicodedata.normalize("NFKD", value)
    return normalized.encode("ascii", "ignore").decode("ascii")


def collapse_spaces(value: str) -> str:
    """Collapse repeated whitespace into single spaces.

    Args:
        value: Raw text that may contain repeated or irregular spacing.

    Returns:
        The input text with internal whitespace normalized to single spaces and
        leading/trailing whitespace stripped.
    """
    return re.sub(r"\s+", " ", value).strip()


def normalize_sort_name(name: str) -> str:
    """Build a deterministic sort key from a display name.

    Args:
        name: Human-readable title or series name.

    Returns:
        A lower-case normalized sort string with punctuation cleaned up, common
        trailing English articles moved to the front, and simple Roman numerals
        converted to Arabic numbers.
    """
    # Sorting wants the "display" article form normalized first, then stripped.
    # For example:
    #   "Addams Family, The" -> "The Addams Family" -> "Addams Family"
    result = ascii_fold(
        strip_leading_article(move_trailing_article_to_front(name))
    ).strip()
    lowered = f" {result.lower()} "
    # Roman numeral normalization is intentionally simple and title-oriented.
    # It is not a general parser, but it helps sequel names sort consistently.
    for roman, number in ROMAN_NUMERAL_MAP.items():
        lowered = lowered.replace(roman, number)
    result = collapse_spaces(lowered)
    result = re.sub(r"[^a-z0-9\s]+", " ", result.lower())
    return collapse_spaces(result)


def move_trailing_article_to_front(name: str) -> str:
    """Convert titles like ``"Addams Family, The"`` to ``"The Addams Family"``.

    Args:
        name: Human-readable title that may use trailing English articles.

    Returns:
        The title with a trailing English article moved to the front when the
        pattern matches. Titles without trailing articles are returned
        unchanged except for surrounding whitespace cleanup.
    """
    stripped = collapse_spaces(name)
    match = TRAILING_ARTICLE_RE.match(stripped)
    if match is None:
        return stripped
    article = match.group("article").strip()
    body = match.group("body").strip()
    return collapse_spaces(f"{article} {body}")


def strip_leading_article(name: str) -> str:
    """Remove a leading English article from a display title for sorting.

    Args:
        name: Human-readable title that may begin with ``The``, ``A``, or
            ``An``.

    Returns:
        The title without a leading English article when present. Internal
        article words elsewhere in the title are preserved.
    """
    stripped = collapse_spaces(name)
    match = LEADING_ARTICLE_RE.match(stripped)
    if match is None:
        return stripped
    return collapse_spaces(match.group("body"))


def best_match_score(search_term: str, candidate_name: str, *scorers) -> int:
    """Compute a robust fuzzy score for display titles and normalized forms.

    Args:
        search_term: Canonical search term being matched.
        candidate_name: Candidate title from a source system.
        scorers: One or more callable fuzzy scorers such as ``fuzz.ratio`` or
            ``fuzz.token_set_ratio``.

    Returns:
        A weighted blended score across article-front display matching and
        normalized sort-name matching. The first scorer is treated as the most
        conservative/safest signal and therefore receives the highest weight.
    """
    if not scorers:
        raise ValueError("At least one scorer must be provided")

    # Matching is done against two parallel views of the title:
    # 1. display-friendly titles with trailing articles moved to the front
    # 2. normalized sort names with punctuation/articles cleaned up
    #
    # This lets us handle cases like:
    #   "Addams Family, The" <-> "The Addams Family"
    # while still comparing more aggressively normalized forms for fuzzy search.
    display_search = move_trailing_article_to_front(search_term)
    display_candidate = move_trailing_article_to_front(candidate_name)
    normalized_search = normalize_sort_name(search_term)
    normalized_candidate = normalize_sort_name(candidate_name)

    # Reject obviously bad source rows up front. Updated external datasets can
    # contain garbage titles like "'the" that would otherwise look like a
    # perfect partial/token-set match for many unrelated games.
    if normalized_candidate in INVALID_STANDALONE_TITLES:
        return 0
    # Do not let one generous scorer dominate the whole decision. Instead,
    # blend the scorer outputs and weight the first scorer highest. The source
    # adapters intentionally pass ``ratio`` first because it is the safest
    # whole-string signal, while token/partial scorers are more permissive.
    view_pairs = (
        (display_search.lower(), display_candidate.lower()),
        (normalized_search, normalized_candidate),
    )
    weights = [max(len(scorers) - index, 1) for index in range(len(scorers))]

    best_view_score = 0.0
    for left_value, right_value in view_pairs:
        weighted_total = 0.0
        total_weight = 0.0
        for scorer, weight in zip(scorers, weights, strict=True):
            weighted_total += float(scorer(left_value, right_value)) * weight
            total_weight += weight
        best_view_score = max(
            best_view_score, weighted_total / total_weight if total_weight else 0.0
        )
    return int(round(best_view_score))


def meaningful_match_tokens(name: str) -> set[str]:
    """Extract title tokens useful for coarse candidate filtering.

    Args:
        name: Human-readable title to tokenize.

    Returns:
        Set of normalized title tokens with low-information stopwords removed.
        Numeric tokens are preserved because sequel numbers and years are often
        strong discriminators during candidate generation.
    """
    normalized = normalize_sort_name(name)
    tokens = set()
    for token in normalized.split():
        # Coarse candidate filtering should treat sequel markers consistently
        # regardless of whether a source writes "II" or "2". The broader
        # sort-name normalizer only catches Roman numerals when punctuation
        # happens to line up, so we normalize individual tokens here too.
        token = ROMAN_NUMERAL_TOKEN_MAP.get(token, token)
        # Keep numeric tokens such as "2" or "2020" because they are often
        # the easiest way to separate sequels and sports-year releases.
        if token.isdigit():
            tokens.add(token)
            continue
        # Very short alpha fragments and common glue words do not help narrow
        # the search space and can create a large amount of accidental overlap.
        if len(token) <= 1 or token in MEANINGFUL_TOKEN_STOPWORDS:
            continue
        tokens.add(token)
    return tokens


def should_consider_candidate(
    search_term: str,
    candidate_name: str,
    search_tokens: set[str] | None = None,
    candidate_tokens: set[str] | None = None,
) -> bool:
    """Decide whether a candidate should even reach fuzzy scoring.

    Args:
        search_term: Human-readable search term being matched.
        candidate_name: Candidate title from one source system.
        search_tokens: Optional precomputed meaningful tokens for
            ``search_term``.
        candidate_tokens: Optional precomputed meaningful tokens for
            ``candidate_name``.

    Returns:
        ``True`` when the candidate is worth fuzzy scoring, otherwise
        ``False``.
    """
    normalized_search = normalize_sort_name(search_term)
    normalized_candidate = normalize_sort_name(candidate_name)

    # Exact normalized-title equivalence should always be allowed through,
    # even if the token heuristics below would be conservative.
    if normalized_search == normalized_candidate:
        return True

    search_tokens = (
        search_tokens
        if search_tokens is not None
        else meaningful_match_tokens(search_term)
    )
    candidate_tokens = (
        candidate_tokens
        if candidate_tokens is not None
        else meaningful_match_tokens(candidate_name)
    )

    if not search_tokens or not candidate_tokens:
        return False

    shared_tokens = search_tokens & candidate_tokens
    if not shared_tokens:
        return False

    # Very short titles are the most dangerous fuzzy-match cases because one
    # strong token can appear in many unrelated sequel/subtitle names. Keep
    # branded variants such as "Disney's Aladdin", but block candidates that
    # add sequel numbers or large subtitle tails to a one-token query.
    if _is_short_title_query(normalized_search, search_tokens):
        if any(
            token.isdigit() for token in candidate_tokens if token not in search_tokens
        ):
            return False
        extra_candidate_tokens = candidate_tokens - search_tokens
        if (
            extra_candidate_tokens
            and not extra_candidate_tokens <= SHORT_TITLE_PREFIX_ALLOWLIST
        ):
            return False

    search_numeric_tokens = {token for token in search_tokens if token.isdigit()}
    candidate_numeric_tokens = {token for token in candidate_tokens if token.isdigit()}

    # If the search title includes a meaningful numeric discriminator, require
    # the candidate to agree on at least one numeric token too. This prevents
    # cases like broad "... III ..." matches collapsing onto unrelated games
    # that only share a theme word such as "Alien".
    if search_numeric_tokens and not (search_numeric_tokens & candidate_numeric_tokens):
        return False

    required_overlap = 2 if len(search_tokens) >= 2 else 1
    return len(shared_tokens) >= required_overlap


def _is_short_title_query(normalized_search: str, search_tokens: set[str]) -> bool:
    """Detect short/generic title queries that need stricter candidate gating.

    Args:
        normalized_search: Normalized sort-form of the search title.
        search_tokens: Meaningful tokens extracted from the search title.

    Returns:
        ``True`` when the query is short enough that fuzzy overlap alone is
        risky, such as one-token titles like ``Contra`` or ``Batman``.
    """
    normalized_words = [word for word in normalized_search.split() if word]
    return len(search_tokens) <= 1 and len(normalized_words) <= 2


def slugify(value: str) -> str:
    """Convert display text into a stable slug.

    Args:
        value: Human-readable text to convert.

    Returns:
        Lower-case, ASCII-only, hyphen-separated text suitable for stable keys.
    """
    result = ascii_fold(value).lower()
    result = re.sub(r"['\"]", "", result)
    result = re.sub(r"[^a-z0-9]+", "-", result)
    return result.strip("-")


def english_preferred_sort_key(
    language_code: str | None, region_code: str | None
) -> tuple[int, int]:
    """Rank language and region values using the English/US-preferred policy.

    Args:
        language_code: Optional language code attached to a name or description.
        region_code: Optional region code attached to a name or description.

    Returns:
        A tuple where smaller values represent higher preference for the
        canonical English/American presentation defaults.
    """
    language_priority = 0
    region_priority = 9
    if language_code in {"en", "en-us", "en-gb"}:
        language_priority = 0
    elif language_code:
        language_priority = 5
    else:
        language_priority = 3

    if region_code == "USA":
        region_priority = 0
    elif region_code == "NORTH_AMERICA":
        region_priority = 1
    elif region_code == "WORLD":
        region_priority = 2
    elif region_code == "EUROPE":
        region_priority = 3
    elif region_code:
        region_priority = 5
    return (language_priority, region_priority)
