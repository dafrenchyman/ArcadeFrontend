"""Deterministic key builders for canonical and source-derived entities."""

import hashlib

from unified_dataset.normalization import slugify


def datomatic_source_key(
    platform_slug: str, raw_title: str, hash_value: str | None, size_bytes: int | None
) -> str:
    """Build a stable key for a Datomatic source row.

    Args:
        platform_slug: Platform slug the row belongs to.
        raw_title: Raw Datomatic game title.
        hash_value: Preferred ROM hash for the row, if available.
        size_bytes: Preferred ROM size for the row, if available.

    Returns:
        A deterministic source key combining the platform, slugified title, and
        a short SHA1 digest of the source identity components.
    """
    identity = f"{platform_slug}|{raw_title}|{hash_value or ''}|{size_bytes or ''}"
    digest = hashlib.sha1(identity.encode("utf-8")).hexdigest()[:16]
    return f"{platform_slug}:{slugify(raw_title)}:{digest}"


def internal_game_key(
    library_slug: str, platform_slug: str, grouped_title: str, game_kind: str
) -> str:
    """Build a stable canonical game key.

    Args:
        library_slug: Library slug such as ``snes`` or ``snes_hacks``.
        platform_slug: Platform slug for the game.
        grouped_title: Canonical grouped title used for the game bucket.
        game_kind: Internal game classification such as ``main`` or
            ``competition``.

    Returns:
        A readable deterministic canonical game key.
    """
    return (
        f"{library_slug}:{platform_slug}:{slugify(grouped_title)}:{slugify(game_kind)}"
    )


def internal_release_key(
    game_key: str,
    release_type: str,
    primary_region: str | None,
    revision_label: str | None,
    version_label: str | None,
    patch_kind: str | None,
) -> str:
    """Build a stable canonical release key.

    Args:
        game_key: Parent canonical game key.
        release_type: Release classification such as ``retail`` or
            ``translation_patch``.
        primary_region: Primary region code for the release, if known.
        revision_label: Revision label such as ``Rev 1``, if any.
        version_label: Version label such as ``v1.1``, if any.
        patch_kind: Patch taxonomy value, if the release is patch-derived.

    Returns:
        A deterministic canonical release key derived from the parent game and
        the release identity fields.
    """
    parts = [game_key, slugify(release_type or "retail")]
    if primary_region:
        parts.append(slugify(primary_region))
    if revision_label:
        parts.append(slugify(revision_label))
    if version_label:
        parts.append(slugify(version_label))
    if patch_kind:
        parts.append(slugify(patch_kind))
    return ":".join(parts)


def platform_series_key(library_slug: str, platform_slug: str, series_name: str) -> str:
    """Build a stable key for a platform-scoped series.

    Args:
        library_slug: Library slug that owns the series.
        platform_slug: Platform slug that owns the series.
        series_name: Human-readable series name.

    Returns:
        A deterministic key for ``platform_series`` rows.
    """
    return f"{library_slug}:{platform_slug}:series:{slugify(series_name)}"


def cross_platform_series_key(series_name: str) -> str:
    """Build a stable key for a cross-platform series.

    Args:
        series_name: Human-readable franchise or collection name.

    Returns:
        A deterministic key for ``cross_platform_series`` rows.
    """
    return f"cross-series:{slugify(series_name)}"
