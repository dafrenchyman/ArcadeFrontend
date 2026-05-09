"""Typed configuration objects for the unified dataset build."""

from dataclasses import dataclass
from enum import Enum
from pathlib import Path


class IgdbMode(str, Enum):
    """Controls how IGDB requests interact with the local cache."""

    ON_DEMAND = "on-demand"
    REFRESH = "refresh"
    OFFLINE = "offline"


@dataclass(slots=True)
class BuildConfig:
    """Runtime configuration for a unified dataset build or helper command.

    Attributes:
        platform_slug: Platform being processed, such as ``snes``.
        datomatic_file: Path to the Datomatic/No-Intro DAT file.
        tgdb_db: Path to the local TGDB SQLite dump.
        launchbox_metadata_dir: Directory containing LaunchBox metadata XML.
        igdb_cache_db: SQLite database used as the IGDB request cache.
        output_db: Final unified SQLite output path.
        override_workbook: Optional override workbook path.
        review_workbook: Optional review workbook output path.
        official_library_slug: Library slug for official content.
        hacks_library_slug: Library slug for hacks/mods content.
        igdb_mode: Cache behavior for IGDB requests.
        keep_temp_db: Whether temporary build files should be retained on
            failure-oriented workflows.
        generate_override_template_if_missing: Whether to create a blank
            override workbook when one is missing.
        stop_after_stage: Optional debug hook to stop after a named stage.
        verbose: Whether to emit verbose logs/progress.
    """

    platform_slug: str
    datomatic_file: Path
    tgdb_db: Path
    launchbox_metadata_dir: Path
    igdb_cache_db: Path
    output_db: Path
    override_workbook: Path | None = None
    review_workbook: Path | None = None
    official_library_slug: str | None = None
    hacks_library_slug: str | None = None
    igdb_mode: IgdbMode = IgdbMode.ON_DEMAND
    keep_temp_db: bool = True
    generate_override_template_if_missing: bool = True
    stop_after_stage: str | None = None
    verbose: bool = True

    def __post_init__(self) -> None:
        """Fill in derived library slugs when they are omitted."""
        if self.official_library_slug is None:
            self.official_library_slug = self.platform_slug
        if self.hacks_library_slug is None:
            self.hacks_library_slug = f"{self.platform_slug}_hacks"

    @property
    def metadata_xml(self) -> Path:
        """Return the LaunchBox ``Metadata.xml`` path for this build."""
        return self.launchbox_metadata_dir / "Metadata.xml"

    @property
    def platforms_xml(self) -> Path:
        """Return the LaunchBox ``Platforms.xml`` path for this build."""
        return self.launchbox_metadata_dir / "Platforms.xml"

    @property
    def tasks_file(self) -> Path:
        """Return the OpenSpec task list for this change."""
        return Path("openspec/changes/add-unified-game-dataset/tasks.md")
