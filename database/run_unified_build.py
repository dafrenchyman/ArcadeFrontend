"""IDE-friendly entrypoint for running the unified dataset build manually."""

import logging
from pathlib import Path

from unified_dataset import BuildConfig, IgdbMode, UnifiedDatasetBuilder

NO_INTRO_ROOT = "/mnt/Bank/SnapArrays/SsdArray01/SnapDisk_4TB_27/Consoles/DatFiles/No-Intro Love Pack (Standard) (2023-04-13)"
PLATFORM_LOOKUP = {
    "amiga": f"{NO_INTRO_ROOT}/No-Intro/Commodore - Amiga (20220712-143036).dat",
    "atari800": f"{NO_INTRO_ROOT}/No-Intro/Atari - 2600 (20230330-104503).dat",
    "atari2600": f"{NO_INTRO_ROOT}/No-Intro/Atari - 2600 (20230330-104503).dat",
    "atari5200": f"{NO_INTRO_ROOT}/No-Intro/Atari - 5200 (20220405-183755).dat",
    "atari7800": f"{NO_INTRO_ROOT}/No-Intro/Atari - 7800 (20220714-205237).dat",
    "atarilynx": f"{NO_INTRO_ROOT}/No-Intro/Atari - Lynx (20230322-221226).dat",
    "atarijaguar": f"{NO_INTRO_ROOT}/No-Intro/Atari - Jaguar (J64) (20230312-215215).dat",
    "colecovision": f"{NO_INTRO_ROOT}/No-Intro/Coleco - ColecoVision (20230204-141322).dat",
    "gameandwatch": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Game & Watch (20211228-000000).dat",
    "gb": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Game Boy (20230413-112139).dat",
    "gba": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Game Boy Advance (20230412-152643).dat",
    "gbc": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Game Boy Color (20230402-224108).dat",
    "genesis": f"{NO_INTRO_ROOT}/No-Intro/Sega - Mega Drive - Genesis (20230413-082302).dat",
    "n64": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Nintendo 64 (BigEndian) (20230410-124148).dat",
    "n64dd": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Nintendo 64DD (20230131-042611).dat",
    "nes": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Nintendo Entertainment System (Headerless) (20230413-090934).dat",
    # "nes": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Nintendo Entertainment System (Headered) (20230413-090934).dat",
    "ngp": f"{NO_INTRO_ROOT}/No-Intro/SNK - NeoGeo Pocket (20230307-173713).dat",
    "ngpc": f"{NO_INTRO_ROOT}/No-Intro/SNK - NeoGeo Pocket Color (20230408-021339).dat",
    "sega32x": f"{NO_INTRO_ROOT}/No-Intro/Sega - 32X (20230308-124118).dat",
    "snes": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Super Nintendo Entertainment System (20230409-114707).dat",
    "virtualboy": f"{NO_INTRO_ROOT}/No-Intro/Nintendo - Virtual Boy (20230405-120113).dat",
    "wonderswan": f"{NO_INTRO_ROOT}/No-Intro/Bandai - WonderSwan (20230317-075216).dat",
    "wonderswancolor": f"{NO_INTRO_ROOT}/No-Intro/Bandai - WonderSwan Color (20230218-062956).dat",
}


def main() -> None:
    """Run a manually configured build from an IDE or local script runner.

    The configuration values in this module are intended for direct editing
    during development so the build can be launched without hand-writing a long
    CLI argument list.
    """
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    platform = "snes"
    datomatic_file = "/mnt/Bank/SnapArrays/SsdArray01/SnapDisk_4TB_27/Consoles/DatFiles/Nintendo - Super Nintendo Entertainment System (20260415-130609).dat"
    # datomatic_file = PLATFORM_LOOKUP.get(platform)

    config = BuildConfig(
        platform_slug=platform,
        datomatic_file=Path(datomatic_file),
        tgdb_db=Path("database/tgdb.db"),
        launchbox_metadata_dir=Path("database/Metadata"),
        igdb_cache_db=Path("database/igdb.db"),
        output_db=Path("database/unified_snes.db"),
        override_workbook=Path("workbooks/unified_overrides.xls"),
        review_workbook=Path("workbooks/unified_review.xls"),
        igdb_mode=IgdbMode.ON_DEMAND,
        verbose=True,
    )
    result = UnifiedDatasetBuilder(config).build()
    logging.getLogger(__name__).info("Build result: %s", result)


if __name__ == "__main__":
    main()
