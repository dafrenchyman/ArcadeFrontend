"""Command-line entrypoints for unified dataset build and support tools."""

import argparse
import json
from pathlib import Path

from unified_dataset.builder import UnifiedDatasetBuilder
from unified_dataset.config import BuildConfig, IgdbMode


def build_parser() -> argparse.ArgumentParser:
    """Build the top-level CLI parser.

    Returns:
        An ``argparse.ArgumentParser`` configured with all supported subcommands
        and arguments.
    """
    parser = argparse.ArgumentParser(prog="unified_dataset")
    subparsers = parser.add_subparsers(dest="command", required=True)

    build_parser_ = subparsers.add_parser("build-unified-db")
    _add_common_build_args(build_parser_)

    validate_parser = subparsers.add_parser("validate-overrides")
    validate_parser.add_argument("--override-workbook", required=True)

    export_parser = subparsers.add_parser("export-review-workbook")
    export_parser.add_argument("--input-db", required=True)
    export_parser.add_argument("--output-workbook", required=True)

    search_parser = subparsers.add_parser("search-source")
    search_parser.add_argument(
        "--source", choices=["igdb", "tgdb", "launchbox"], required=True
    )
    search_parser.add_argument("--platform", required=True)
    search_parser.add_argument("--term", required=True)
    search_parser.add_argument("--tgdb-db", default="database/tgdb.db")
    search_parser.add_argument("--launchbox-metadata-dir", default="database/Metadata")
    search_parser.add_argument("--igdb-cache-db", default="database/igdb.db")
    search_parser.add_argument(
        "--igdb-mode",
        choices=[mode.value for mode in IgdbMode],
        default=IgdbMode.ON_DEMAND.value,
    )

    diagnostics_parser = subparsers.add_parser("inspect-diagnostics")
    diagnostics_parser.add_argument("--input-db", required=True)
    return parser


def _add_common_build_args(parser: argparse.ArgumentParser) -> None:
    """Attach the shared build arguments to a parser.

    Args:
        parser: Parser instance for the main build subcommand.
    """
    parser.add_argument("--platform", required=True)
    parser.add_argument("--datomatic-file", required=True)
    parser.add_argument("--tgdb-db", required=True)
    parser.add_argument("--launchbox-metadata-dir", required=True)
    parser.add_argument("--igdb-cache-db", required=True)
    parser.add_argument("--output-db", required=True)
    parser.add_argument("--override-workbook")
    parser.add_argument("--review-workbook")
    parser.add_argument("--official-library")
    parser.add_argument("--hacks-library")
    parser.add_argument(
        "--igdb-mode",
        choices=[mode.value for mode in IgdbMode],
        default=IgdbMode.ON_DEMAND.value,
    )


def build_config_from_args(args: argparse.Namespace) -> BuildConfig:
    """Translate parsed CLI arguments into a ``BuildConfig``.

    Args:
        args: Namespace returned by ``argparse`` for the build command.

    Returns:
        A populated ``BuildConfig`` instance.
    """
    return BuildConfig(
        platform_slug=args.platform,
        datomatic_file=Path(args.datomatic_file),
        tgdb_db=Path(args.tgdb_db),
        launchbox_metadata_dir=Path(args.launchbox_metadata_dir),
        igdb_cache_db=Path(args.igdb_cache_db),
        output_db=Path(args.output_db),
        override_workbook=(
            Path(args.override_workbook)
            if getattr(args, "override_workbook", None)
            else None
        ),
        review_workbook=(
            Path(args.review_workbook)
            if getattr(args, "review_workbook", None)
            else None
        ),
        official_library_slug=getattr(args, "official_library", None),
        hacks_library_slug=getattr(args, "hacks_library", None),
        igdb_mode=IgdbMode(args.igdb_mode),
    )


def main(argv: list[str] | None = None) -> int:
    """Run the unified dataset CLI.

    Args:
        argv: Optional explicit argument list. When ``None``, arguments are read
            from ``sys.argv``.

    Returns:
        Process exit code where ``0`` indicates success.
    """
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "build-unified-db":
        config = build_config_from_args(args)
        result = UnifiedDatasetBuilder(config).build()
        print(json.dumps(result, indent=2))
        return 0
    if args.command == "validate-overrides":
        config = BuildConfig(
            platform_slug="snes",
            datomatic_file=Path("unused.dat"),
            tgdb_db=Path("database/tgdb.db"),
            launchbox_metadata_dir=Path("database/Metadata"),
            igdb_cache_db=Path("database/igdb.db"),
            output_db=Path("database/unified_unused.db"),
            override_workbook=Path(args.override_workbook),
        )
        errors = UnifiedDatasetBuilder(config).validate_overrides()
        print(json.dumps(errors, indent=2))
        return 0 if not errors else 1
    if args.command == "export-review-workbook":
        config = BuildConfig(
            platform_slug="snes",
            datomatic_file=Path("unused.dat"),
            tgdb_db=Path("database/tgdb.db"),
            launchbox_metadata_dir=Path("database/Metadata"),
            igdb_cache_db=Path("database/igdb.db"),
            output_db=Path(args.input_db),
        )
        UnifiedDatasetBuilder(config).export_review_workbook(
            Path(args.input_db), Path(args.output_workbook)
        )
        return 0
    if args.command == "search-source":
        config = BuildConfig(
            platform_slug=args.platform,
            datomatic_file=Path("unused.dat"),
            tgdb_db=Path(args.tgdb_db),
            launchbox_metadata_dir=Path(args.launchbox_metadata_dir),
            igdb_cache_db=Path(args.igdb_cache_db),
            output_db=Path("database/unified_unused.db"),
            igdb_mode=IgdbMode(args.igdb_mode),
        )
        results = UnifiedDatasetBuilder(config).manual_search(args.source, args.term)
        print(json.dumps(results, indent=2))
        return 0
    if args.command == "inspect-diagnostics":
        import sqlite3

        con = sqlite3.connect(args.input_db)
        con.row_factory = sqlite3.Row
        rows = [
            dict(row) for row in con.execute("SELECT * FROM diagnostics ORDER BY id")
        ]
        print(json.dumps(rows, indent=2))
        return 0
    return 1
