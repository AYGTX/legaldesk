"""Accounts, approvals by account, versioned rules, Critical severity (rework spec R-M11-1 to R-M11-4).

Earlier rule confirmations were typed names and are not carried over: a rule is served again once a lawyer approves
it under their own account. Earlier text verifications stay (verified_by keeps the name; no account is linked).

Revision ID: 0002_accounts_rule_versions
Revises: 0001_baseline
"""
import sqlalchemy as sa
from alembic import op

revision = "0002_accounts_rule_versions"
down_revision = "0001_baseline"
branch_labels = None
depends_on = None


def _columns(table: str) -> set[str]:
    return {c["name"] for c in sa.inspect(op.get_bind()).get_columns(table)}


def upgrade() -> None:
    op.create_table(
        "harvey_users",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("email", sa.String(length=256), nullable=False),
        sa.Column("name", sa.String(length=200), nullable=False),
        sa.Column("role", sa.Enum("Reader", "Editor", "Lawyer", name="role", native_enum=False), nullable=False),
        sa.Column("is_admin", sa.Boolean(), nullable=False),
        sa.Column("bar_reference", sa.String(length=200), nullable=True),
        sa.Column("password_hash", sa.String(length=300), nullable=False),
        sa.Column("must_change_password", sa.Boolean(), nullable=False),
        sa.Column("totp_secret", sa.String(length=64), nullable=True),
        sa.Column("totp_enabled", sa.Boolean(), nullable=False),
        sa.Column("active", sa.Boolean(), nullable=False),
        sa.Column("failed_logins", sa.Integer(), nullable=False),
        sa.Column("locked_until", sa.DateTime(timezone=True), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("last_login_at", sa.DateTime(timezone=True), nullable=True),
        sa.PrimaryKeyConstraint("id", name="pk_harvey_users"),
        sa.UniqueConstraint("email", name="uq_harvey_users_email"),
    )
    op.create_table(
        "rule_versions",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("rule_key", sa.String(length=80), nullable=False),
        sa.Column("version", sa.Integer(), nullable=False),
        sa.Column("payload", sa.JSON(), nullable=False),
        sa.Column("approved_by_user_id", sa.String(length=36), nullable=False),
        sa.Column("approved_by_name", sa.String(length=300), nullable=False),
        sa.Column("approved_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("edited_by_user_id", sa.String(length=36), nullable=True),
        sa.Column("withdrawn_at", sa.DateTime(timezone=True), nullable=True),
        sa.ForeignKeyConstraint(["approved_by_user_id"], ["harvey_users.id"], name="fk_rule_versions_approver"),
        sa.PrimaryKeyConstraint("id", name="pk_rule_versions"),
        sa.UniqueConstraint("rule_key", "version", name="uq_rule_versions_key_version"),
    )
    op.create_index("ix_rule_versions_rule_key", "rule_versions", ["rule_key"])

    if "approved_by_user_id" not in _columns("article_explanations"):
        with op.batch_alter_table("article_explanations") as batch:
            batch.add_column(sa.Column("approved_by_user_id", sa.String(length=36), nullable=True))
            batch.create_foreign_key("fk_article_explanations_approver", "harvey_users", ["approved_by_user_id"], ["id"])

    if "verified_by_user_id" not in _columns("legal_texts"):
        with op.batch_alter_table("legal_texts") as batch:
            batch.add_column(sa.Column("verified_by_user_id", sa.String(length=36), nullable=True))
            batch.create_foreign_key("fk_legal_texts_verifier", "harvey_users", ["verified_by_user_id"], ["id"])

    existing = _columns("playbook_rules")
    with op.batch_alter_table("playbook_rules") as batch:
        for name, column in (("severity", sa.Column("severity", sa.String(length=10), nullable=True)),
                             ("edited_by_user_id", sa.Column("edited_by_user_id", sa.String(length=36), nullable=True)),
                             ("edited_at", sa.Column("edited_at", sa.DateTime(timezone=True), nullable=True))):
            if name not in existing:
                batch.add_column(column)

    # Typed-name confirmations prove nothing about who approved: every rule waits for a lawyer's account.
    op.execute(sa.text("UPDATE playbook_rules SET confirmed_by = NULL, confirmed_at = NULL"))


def downgrade() -> None:
    with op.batch_alter_table("playbook_rules") as batch:
        batch.drop_column("edited_at")
        batch.drop_column("edited_by_user_id")
        batch.drop_column("severity")
    with op.batch_alter_table("legal_texts") as batch:
        batch.drop_constraint("fk_legal_texts_verifier", type_="foreignkey")
        batch.drop_column("verified_by_user_id")
    with op.batch_alter_table("article_explanations") as batch:
        batch.drop_constraint("fk_article_explanations_approver", type_="foreignkey")
        batch.drop_column("approved_by_user_id")
    op.drop_index("ix_rule_versions_rule_key", table_name="rule_versions")
    op.drop_table("rule_versions")
    op.drop_table("harvey_users")
