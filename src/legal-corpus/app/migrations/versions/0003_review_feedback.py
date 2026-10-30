"""Findings LegalDesk users marked "not applicable", kept for the lawyers who maintain the grid (R-M8-4).

Revision ID: 0003_review_feedback
Revises: 0002_accounts_rule_versions
"""
import sqlalchemy as sa
from alembic import op

revision = "0003_review_feedback"
down_revision = "0002_accounts_rule_versions"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "review_feedback",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("source_ref", sa.String(length=64), nullable=False),
        sa.Column("rule_key", sa.String(length=80), nullable=False),
        sa.Column("rule_version", sa.Integer(), nullable=True),
        sa.Column("clause_reference", sa.String(length=300), nullable=True),
        sa.Column("quote", sa.Text(), nullable=True),
        sa.Column("reason", sa.Text(), nullable=False),
        sa.Column("received_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("handled_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("handled_by_user_id", sa.String(length=36), nullable=True),
        sa.PrimaryKeyConstraint("id", name="pk_review_feedback"),
        sa.UniqueConstraint("source_ref", name="uq_review_feedback_source_ref"),
    )
    op.create_index("ix_review_feedback_rule_key", "review_feedback", ["rule_key"])


def downgrade() -> None:
    op.drop_index("ix_review_feedback_rule_key", table_name="review_feedback")
    op.drop_table("review_feedback")
