"""Baseline: Harvey's schema as it stood before migrations (October 2026).

A database created earlier by create_all is stamped at this revision instead of running it (app.migrations.runner).

Revision ID: 0001_baseline
Revises:
"""
import sqlalchemy as sa
from alembic import op

revision = "0001_baseline"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:

    op.create_table('legal_texts',
    sa.Column('id', sa.String(length=36), nullable=False),
    sa.Column('title', sa.String(length=300), nullable=False),
    sa.Column('reference', sa.String(length=300), nullable=True),
    sa.Column('summary', sa.Text(), nullable=False),
    sa.Column('keywords', sa.Text(), nullable=False),
    sa.Column('language', sa.String(length=5), nullable=False),
    sa.Column('jurisdiction', sa.Enum('Tunisia', 'EU', 'Sectoral', name='jurisdiction', native_enum=False), nullable=False),
    sa.Column('category', sa.Enum('DataProtection', 'Labor', 'Consumer', 'Commercial', 'Tax', 'Telecom', 'Other', name='category', native_enum=False), nullable=False),
    sa.Column('status', sa.Enum('Draft', 'Published', 'Archived', name='textstatus', native_enum=False), nullable=False),
    sa.Column('effective_date', sa.Date(), nullable=True),
    sa.Column('source_url', sa.String(length=1000), nullable=True),
    sa.Column('verified_by', sa.String(length=200), nullable=True),
    sa.Column('verified_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), nullable=False),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_table('playbook_rules',
    sa.Column('key', sa.String(length=80), nullable=False),
    sa.Column('position', sa.Integer(), nullable=False),
    sa.Column('label', sa.String(length=300), nullable=False),
    sa.Column('description', sa.Text(), nullable=True),
    sa.Column('confirmed_by', sa.String(length=200), nullable=True),
    sa.Column('confirmed_at', sa.String(length=40), nullable=True),
    sa.Column('phrases', sa.JSON(), nullable=False),
    sa.Column('examples', sa.JSON(), nullable=False),
    sa.Column('counter_examples', sa.JSON(), nullable=False),
    sa.Column('model_clause', sa.JSON(), nullable=True),
    sa.Column('catalog_seeded', sa.Boolean(), nullable=False),
    sa.Column('checks', sa.JSON(), nullable=True),
    sa.PrimaryKeyConstraint('key')
    )
    op.create_table('articles',
    sa.Column('id', sa.String(length=36), nullable=False),
    sa.Column('legal_text_id', sa.String(length=36), nullable=False),
    sa.Column('number', sa.String(length=50), nullable=False),
    sa.Column('text', sa.Text(), nullable=False),
    sa.Column('order', sa.Integer(), nullable=False),
    sa.Column('embedding', sa.Text(), nullable=True),
    sa.Column('embedding_model', sa.String(length=200), nullable=True),
    sa.Column('excluded_from_search', sa.Boolean(), server_default='0', nullable=False),
    sa.Column('applies_from', sa.Date(), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), nullable=False),
    sa.ForeignKeyConstraint(['legal_text_id'], ['legal_texts.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id')
    )
    with op.batch_alter_table('articles', schema=None) as batch_op:
        batch_op.create_index(batch_op.f('ix_articles_legal_text_id'), ['legal_text_id'], unique=False)

    op.create_table('playbook_rule_articles',
    sa.Column('id', sa.Integer(), autoincrement=True, nullable=False),
    sa.Column('rule_key', sa.String(length=80), nullable=False),
    sa.Column('position', sa.Integer(), nullable=False),
    sa.Column('reference', sa.String(length=300), nullable=False),
    sa.Column('number', sa.String(length=40), nullable=False),
    sa.Column('why', sa.Text(), nullable=True),
    sa.ForeignKeyConstraint(['rule_key'], ['playbook_rules.key'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id')
    )
    with op.batch_alter_table('playbook_rule_articles', schema=None) as batch_op:
        batch_op.create_index(batch_op.f('ix_playbook_rule_articles_rule_key'), ['rule_key'], unique=False)

    op.create_table('article_explanations',
    sa.Column('id', sa.String(length=36), nullable=False),
    sa.Column('article_id', sa.String(length=36), nullable=False),
    sa.Column('language', sa.String(length=5), nullable=False),
    sa.Column('summary', sa.Text(), nullable=False),
    sa.Column('in_practice', sa.Text(), nullable=True),
    sa.Column('text_hash', sa.String(length=64), nullable=False),
    sa.Column('model', sa.String(length=100), nullable=True),
    sa.Column('written_by', sa.String(length=200), nullable=True),
    sa.Column('approved_by', sa.String(length=200), nullable=True),
    sa.Column('approved_at', sa.DateTime(timezone=True), nullable=True),
    sa.Column('updated_at', sa.DateTime(timezone=True), nullable=False),
    sa.ForeignKeyConstraint(['article_id'], ['articles.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('article_id', 'language')
    )
    with op.batch_alter_table('article_explanations', schema=None) as batch_op:
        batch_op.create_index(batch_op.f('ix_article_explanations_article_id'), ['article_id'], unique=False)

    op.create_table('article_versions',
    sa.Column('id', sa.String(length=36), nullable=False),
    sa.Column('article_id', sa.String(length=36), nullable=False),
    sa.Column('number', sa.String(length=50), nullable=False),
    sa.Column('text', sa.Text(), nullable=False),
    sa.Column('replaced_at', sa.DateTime(timezone=True), nullable=False),
    sa.Column('replaced_by', sa.String(length=200), nullable=True),
    sa.Column('note', sa.String(length=300), nullable=True),
    sa.Column('applies_from', sa.Date(), nullable=True),
    sa.ForeignKeyConstraint(['article_id'], ['articles.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id')
    )
    with op.batch_alter_table('article_versions', schema=None) as batch_op:
        batch_op.create_index(batch_op.f('ix_article_versions_article_id'), ['article_id'], unique=False)


def downgrade() -> None:

    with op.batch_alter_table('article_versions', schema=None) as batch_op:
        batch_op.drop_index(batch_op.f('ix_article_versions_article_id'))

    op.drop_table('article_versions')
    with op.batch_alter_table('article_explanations', schema=None) as batch_op:
        batch_op.drop_index(batch_op.f('ix_article_explanations_article_id'))

    op.drop_table('article_explanations')
    with op.batch_alter_table('playbook_rule_articles', schema=None) as batch_op:
        batch_op.drop_index(batch_op.f('ix_playbook_rule_articles_rule_key'))

    op.drop_table('playbook_rule_articles')
    with op.batch_alter_table('articles', schema=None) as batch_op:
        batch_op.drop_index(batch_op.f('ix_articles_legal_text_id'))

    op.drop_table('articles')
    op.drop_table('playbook_rules')
    op.drop_table('legal_texts')
