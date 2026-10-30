"""Alembic environment. Migrations run from the application (app.migrations.runner) on the engine it already has, or
from the command line: `python -m alembic -c app/migrations/alembic.ini upgrade head` with CORPUS_DATABASE_URL set."""

from alembic import context
from sqlalchemy import engine_from_config, pool

from app.db import Base
from app import models  # noqa: F401  registers tables

config = context.config
target_metadata = Base.metadata


def run() -> None:
    connection = config.attributes.get("connection")
    if connection is not None:
        _migrate(connection)
        return

    from app.config import load_settings

    url = config.get_main_option("sqlalchemy.url") or load_settings().database_url
    engine = engine_from_config({"sqlalchemy.url": url}, prefix="sqlalchemy.", poolclass=pool.NullPool)
    with engine.connect() as conn:
        _migrate(conn)


def _migrate(connection) -> None:
    # Batch mode rewrites tables where SQLite cannot ALTER them; on SQL Server and PostgreSQL it is a plain ALTER.
    context.configure(connection=connection, target_metadata=target_metadata,
                      render_as_batch=connection.dialect.name == "sqlite", compare_type=True)
    with context.begin_transaction():
        context.run_migrations()


run()
