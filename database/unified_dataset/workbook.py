"""Read and write override/review workbooks for the unified dataset.

The primary implementation now targets real Excel 97-2003 ``.xls`` files
through ``xlrd``/``xlwt``. XML workbook reading is kept as a compatibility
fallback for earlier generated files.
"""

from pathlib import Path
from xml.etree import ElementTree as ET

import xlrd
import xlwt

NS = "urn:schemas-microsoft-com:office:spreadsheet"
SS = "{urn:schemas-microsoft-com:office:spreadsheet}"

REQUIRED_SHEETS = {
    "grouping_override": [
        "enabled",
        "library_slug",
        "platform_slug",
        "datomatic_source_key",
        "datomatic_raw_title",
        "forced_internal_game_key",
        "forced_internal_game_name",
        "forced_internal_release_key",
        "forced_release_title",
        "forced_game_kind",
        "forced_release_type",
        "notes",
    ],
    "game_source_override": [
        "enabled",
        "library_slug",
        "platform_slug",
        "internal_game_key",
        "internal_game_name",
        "igdb_game_id",
        "tgdb_game_id",
        "launchbox_game_id",
        "preferred_name_source",
        "preferred_description_source",
        "notes",
    ],
    "release_override": [
        "enabled",
        "datomatic_source_key",
        "datomatic_raw_title",
        "forced_internal_release_key",
        "forced_release_title",
        "forced_primary_region_codes",
        "forced_language_codes",
        "forced_release_type",
        "forced_revision_label",
        "forced_version_label",
        "base_release_key",
        "notes",
    ],
    "name_override": [
        "enabled",
        "target_type",
        "internal_key",
        "name",
        "sort_name",
        "name_type",
        "language_code",
        "region_code",
        "is_preferred",
        "is_preferred_en_us",
        "notes",
    ],
    "series_override": [
        "enabled",
        "series_scope",
        "series_key",
        "series_name",
        "platform_slug",
        "internal_game_key",
        "internal_game_name",
        "sort_order",
        "notes",
    ],
    "ignore_override": [
        "enabled",
        "ignore_scope",
        "source_key_or_id",
        "related_internal_key",
        "reason",
        "notes",
    ],
}


def generate_template(path: Path) -> None:
    """Create a blank override workbook template.

    Args:
        path: Destination workbook path, typically ending in ``.xls``.
    """
    workbook = xlwt.Workbook()
    for sheet_name, columns in REQUIRED_SHEETS.items():
        sheet = workbook.add_sheet(sheet_name[:31])
        for col_idx, value in enumerate(columns):
            sheet.write(0, col_idx, value)
    path.parent.mkdir(parents=True, exist_ok=True)
    workbook.save(str(path))


def load_workbook(path: Path) -> dict[str, list[dict[str, str]]]:
    """Load an override workbook from disk.

    Args:
        path: Path to a workbook file. Real ``.xls`` files are read directly;
            legacy XML workbooks are parsed as a fallback.

    Returns:
        A mapping of worksheet name to row dictionaries keyed by header.
    """
    if _looks_like_xml_workbook(path):
        return _load_xml_workbook(path)
    return _load_xls_workbook(path)


def validate_workbook_dict(workbook: dict[str, list[dict[str, str]]]) -> list[str]:
    """Validate workbook structure against the required override sheets.

    Args:
        workbook: Parsed workbook data keyed by worksheet name.

    Returns:
        A list of human-readable validation errors. Empty means valid.
    """
    errors: list[str] = []
    for sheet_name, columns in REQUIRED_SHEETS.items():
        if sheet_name not in workbook:
            errors.append(f"Missing required sheet: {sheet_name}")
            continue
        headers = set()
        if workbook[sheet_name]:
            headers = set(workbook[sheet_name][0].keys())
        else:
            headers = set(columns)
        missing = [column for column in columns if column not in headers]
        if missing:
            errors.append(f"Sheet {sheet_name} missing columns: {', '.join(missing)}")
    return errors


def export_review_workbook(path: Path, sheets: dict[str, list[dict[str, str]]]) -> None:
    """Write a multi-sheet review workbook.

    Args:
        path: Destination workbook path, typically ending in ``.xls``.
        sheets: Mapping of worksheet name to rows to export.
    """
    workbook = xlwt.Workbook()
    for sheet_name, rows in sheets.items():
        sheet = workbook.add_sheet(sheet_name[:31])
        headers = list(rows[0].keys()) if rows else ["message"]
        for col_idx, header in enumerate(headers):
            sheet.write(0, col_idx, header)
        for row_idx, row_dict in enumerate(rows, start=1):
            for col_idx, header in enumerate(headers):
                sheet.write(row_idx, col_idx, str(row_dict.get(header, "")))
    path.parent.mkdir(parents=True, exist_ok=True)
    workbook.save(str(path))


def _looks_like_xml_workbook(path: Path) -> bool:
    """Detect whether a workbook file is XML-based instead of binary ``.xls``.

    Args:
        path: Path to the workbook on disk.

    Returns:
        ``True`` when the file appears to start with XML markup.
    """
    with path.open("rb") as handle:
        prefix = handle.read(16).lstrip()
    return prefix.startswith(b"<") or prefix.startswith(b"<?xml")


def _load_xls_workbook(path: Path) -> dict[str, list[dict[str, str]]]:
    """Load a binary Excel 97-2003 workbook.

    Args:
        path: Path to a binary ``.xls`` workbook.

    Returns:
        Parsed workbook data keyed by worksheet name.
    """
    book = xlrd.open_workbook(str(path))
    workbook: dict[str, list[dict[str, str]]] = {}
    for sheet in book.sheets():
        rows: list[dict[str, str]] = []
        if sheet.nrows == 0:
            workbook[sheet.name] = rows
            continue
        headers = [
            _cell_to_string(sheet.cell_value(0, col_idx))
            for col_idx in range(sheet.ncols)
        ]
        for row_idx in range(1, sheet.nrows):
            values = [
                _cell_to_string(sheet.cell_value(row_idx, col_idx))
                for col_idx in range(sheet.ncols)
            ]
            padded = values + [""] * (len(headers) - len(values))
            rows.append(dict(zip(headers, padded)))
        workbook[sheet.name] = rows
    return workbook


def _load_xml_workbook(path: Path) -> dict[str, list[dict[str, str]]]:
    """Load a legacy XML workbook file.

    Args:
        path: Path to an XML workbook file.

    Returns:
        Parsed workbook data keyed by worksheet name.
    """
    tree = ET.parse(path)
    root = tree.getroot()
    workbook: dict[str, list[dict[str, str]]] = {}
    for worksheet in root.findall(f"{SS}Worksheet"):
        name = worksheet.attrib.get(f"{SS}Name", "Sheet1")
        rows = []
        all_rows = worksheet.findall(f".//{SS}Row")
        if not all_rows:
            workbook[name] = rows
            continue
        headers = [_xml_cell_text(cell) for cell in all_rows[0].findall(f"{SS}Cell")]
        for row in all_rows[1:]:
            values = [_xml_cell_text(cell) for cell in row.findall(f"{SS}Cell")]
            padded = values + [""] * (len(headers) - len(values))
            rows.append(dict(zip(headers, padded)))
        workbook[name] = rows
    return workbook


def _xml_cell_text(cell: ET.Element) -> str:
    """Read cell text from an XML workbook cell element.

    Args:
        cell: SpreadsheetML ``Cell`` element.

    Returns:
        Extracted string value, or an empty string if absent.
    """
    data = cell.find(f"{SS}Data")
    return data.text if data is not None and data.text is not None else ""


def _cell_to_string(value) -> str:
    """Convert an ``xlrd`` cell value into a stable string representation.

    Args:
        value: Raw cell value returned by ``xlrd``.

    Returns:
        Stringified cell content with integer-like floats normalized.
    """
    if value is None:
        return ""
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    return str(value)
