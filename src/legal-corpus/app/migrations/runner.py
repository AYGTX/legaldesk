"""Brings Harvey's database to the latest schema at start-up (rework spec R-M11-8).

A database made before migrations existed (by create_all, plus a few hand-added columns) is first given those columns,
then stamped at the baseline, so its data is kept and only later revisions run on it.
"""

from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import inspect, text
from sqlalchemy.engine import Engine

MIGRATIONS = Path(__file__).resolve().parent
BASELINE = "0001_baseline"

# Columns older databases may lack: they were added by hand before migrations, and the baseline expects them.
_PRE_MIGRATION_COLUMNS = {
    "articles": {"excluded_from_search": "BOOLEAN NOT NULL DEFAULT 0", "applies_from": "DATE"},
    "article_versions": {"applies_from": "DATE"},
    "playbook_rules": {"description": "TEXT", "catalog_seeded": "BOOLEAN NOT NULL DEFAULT 0", "checks": "JSON"},
}


def _config() -> Config:
    config = Config()
    config.set_main_option("script_location", str(MIGRATIONS))
    return config


def upgrade(engine: Engine) -> None:
    with engine.begin() as connection:
        tables = set(inspect(connection).get_table_names())
        config = _config()
        config.attributes["connection"] = connection
        if "alembic_version" not in tables and "legal_texts" in tables:
            _create_missing_baseline_tables(connection, tables)
            _add_pre_migration_columns(connection)
            command.stamp(config, BASELINE)
        command.upgrade(config, "head")


# The tables of the baseline revision. A database from before article versions or the risk grid may lack some of them;
# create_all used to add them, so they are added here, from today's models. Later revisions therefore check whether a
# column already exists before adding it.
BASELINE_TABLES = ("legal_texts", "articles", "article_versions", "article_explanations", "playbook_rules",
                   "playbook_rule_articles")


def _create_missing_baseline_tables(connection, existing: set[str]) -> None:
    from app.db import Base
    from app import models  # noqa: F401

    missing = [Base.metadata.tables[name] for name in BASELINE_TABLES if name not in existing]
    if missing:
        Base.metadata.create_all(connection, tables=missing)


def _add_pre_migration_columns(connection) -> None:
    inspector = inspect(connection)
    for table, columns in _PRE_MIGRATION_COLUMNS.items():
        existing = {c["name"] for c in inspector.get_columns(table)}
        for name, ddl in columns.items():
            if name not in existing:
                connection.execute(text(f"ALTER TABLE {table} ADD COLUMN {name} {ddl}"))
