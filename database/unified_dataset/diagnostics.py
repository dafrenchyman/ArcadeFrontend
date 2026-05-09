"""Diagnostics structures for partial builds and manual review workflows."""

import json
import sqlite3
from dataclasses import dataclass


@dataclass(slots=True)
class DiagnosticRecord:
    """Single structured build diagnostic.

    Attributes:
        stage: Pipeline stage that produced the diagnostic.
        severity: Diagnostic severity, typically ``error`` or ``info``.
        source_system: Source system involved in the issue.
        message: Human-readable diagnostic message.
        internal_game_key: Optional canonical game key related to the issue.
        internal_release_key: Optional canonical release key related to the
            issue.
        datomatic_source_key: Optional Datomatic source key related to the
            issue.
        override_workbook_path: Optional path to the override workbook that the
            user should edit.
        override_sheet: Optional override sheet name suggested for resolution.
        candidate_options: Optional candidate rows considered during matching.
        ready_to_paste: Optional tab-delimited row the user can paste into a
            workbook sheet.
        helper_command: Optional manual investigation command.
        details: Optional structured detail payload for debugging.
    """

    stage: str
    severity: str
    source_system: str
    message: str
    internal_game_key: str | None = None
    internal_release_key: str | None = None
    datomatic_source_key: str | None = None
    override_workbook_path: str | None = None
    override_sheet: str | None = None
    candidate_options: list[dict] | None = None
    ready_to_paste: str | None = None
    helper_command: str | None = None
    details: dict | None = None


class DiagnosticsCollector:
    """Collects diagnostics before persisting them into the unified DB."""

    def __init__(self) -> None:
        """Initialize an empty in-memory diagnostic collection."""
        self.records: list[DiagnosticRecord] = []

    def add(self, record: DiagnosticRecord) -> None:
        """Append a diagnostic record to the in-memory collection.

        Args:
            record: Structured diagnostic to add.
        """
        self.records.append(record)

    def persist(self, con: sqlite3.Connection) -> None:
        """Write all collected diagnostics into the ``diagnostics`` table.

        Args:
            con: Open SQLite connection for the unified build database.
        """
        for record in self.records:
            con.execute(
                """
                INSERT INTO diagnostics (
                    stage, severity, source_system, internal_game_key, internal_release_key,
                    datomatic_source_key, override_workbook_path, override_sheet, message,
                    candidate_options_json, ready_to_paste, helper_command, details_json
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    record.stage,
                    record.severity,
                    record.source_system,
                    record.internal_game_key,
                    record.internal_release_key,
                    record.datomatic_source_key,
                    record.override_workbook_path,
                    record.override_sheet,
                    record.message,
                    json.dumps(record.candidate_options or []),
                    record.ready_to_paste,
                    record.helper_command,
                    json.dumps(record.details or {}),
                ),
            )

    @property
    def has_errors(self) -> bool:
        """Return whether any collected diagnostics are errors."""
        return any(record.severity == "error" for record in self.records)
