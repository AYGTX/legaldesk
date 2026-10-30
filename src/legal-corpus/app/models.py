import enum
import uuid
from datetime import date, datetime, timezone

from sqlalchemy import JSON, Boolean, Date, DateTime, Enum, ForeignKey, Integer, String, Text, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db import Base


def _now() -> datetime:
    return datetime.now(timezone.utc)


def _id() -> str:
    return str(uuid.uuid4())


# Names and order match LegalDesk's enums so the two sides can exchange values as-is.
class Jurisdiction(str, enum.Enum):
    Tunisia = "Tunisia"
    EU = "EU"
    Sectoral = "Sectoral"


class Category(str, enum.Enum):
    DataProtection = "DataProtection"
    Labor = "Labor"
    Consumer = "Consumer"
    Commercial = "Commercial"
    Tax = "Tax"
    Telecom = "Telecom"
    Other = "Other"


class TextStatus(str, enum.Enum):
    Draft = "Draft"
    Published = "Published"
    Archived = "Archived"


class Role(str, enum.Enum):
    """What a Harvey account may do (R-M11-1). Reader: look. Editor: import and edit, never approve. Lawyer: also
    verify texts and approve risks, model clauses and explanations, which the legal claims of LegalDesk rest on."""
    Reader = "Reader"
    Editor = "Editor"
    Lawyer = "Lawyer"


class Severity(str, enum.Enum):
    """Order matters: Critical blocks a contract in LegalDesk (R-M11-4)."""
    Critical = "Critical"
    High = "High"
    Medium = "Medium"
    Low = "Low"


class HarveyUser(Base):
    """A person who signs in to Harvey. Approvals record this account, never a typed name (R-M11-2)."""

    __tablename__ = "harvey_users"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_id)
    email: Mapped[str] = mapped_column(String(256), unique=True)
    name: Mapped[str] = mapped_column(String(200))
    role: Mapped[Role] = mapped_column(Enum(Role, native_enum=False), default=Role.Reader)
    # Manages accounts; independent of the role (an administrator who is not a lawyer cannot approve).
    is_admin: Mapped[bool] = mapped_column(Boolean, default=False)
    # Bar membership or firm, shown beside every approval this person gives.
    bar_reference: Mapped[str | None] = mapped_column(String(200))
    password_hash: Mapped[str] = mapped_column(String(300))
    must_change_password: Mapped[bool] = mapped_column(Boolean, default=False)
    totp_secret: Mapped[str | None] = mapped_column(String(64))
    totp_enabled: Mapped[bool] = mapped_column(Boolean, default=False)
    active: Mapped[bool] = mapped_column(Boolean, default=True)
    failed_logins: Mapped[int] = mapped_column(Integer, default=0)
    locked_until: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    last_login_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))

    @property
    def can_edit(self) -> bool:
        return self.active and self.role in (Role.Editor, Role.Lawyer)

    @property
    def can_approve(self) -> bool:
        return self.active and self.role == Role.Lawyer

    @property
    def signature(self) -> str:
        """How an approval is shown: "Me Leila Ben Ali (Barreau de Tunis)"."""
        return f"{self.name} ({self.bar_reference})" if self.bar_reference else self.name


class LegalText(Base):
    __tablename__ = "legal_texts"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_id)
    title: Mapped[str] = mapped_column(String(300))
    reference: Mapped[str | None] = mapped_column(String(300))
    summary: Mapped[str] = mapped_column(Text, default="")
    keywords: Mapped[str] = mapped_column(Text, default="")
    language: Mapped[str] = mapped_column(String(5), default="fr")
    jurisdiction: Mapped[Jurisdiction] = mapped_column(Enum(Jurisdiction, native_enum=False))
    category: Mapped[Category] = mapped_column(Enum(Category, native_enum=False))
    status: Mapped[TextStatus] = mapped_column(Enum(TextStatus, native_enum=False), default=TextStatus.Draft)
    effective_date: Mapped[date | None] = mapped_column(Date)
    source_url: Mapped[str | None] = mapped_column(String(1000))
    # Set only when a lawyer checked every stored article against the official source. verified_by is the display
    # name kept for history; verified_by_user_id is the account (R-M11-2).
    verified_by: Mapped[str | None] = mapped_column(String(200))
    verified_by_user_id: Mapped[str | None] = mapped_column(ForeignKey("harvey_users.id"))
    verified_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, onupdate=_now)

    articles: Mapped[list["Article"]] = relationship(
        back_populates="legal_text", cascade="all, delete-orphan", order_by="Article.order"
    )

    @property
    def is_verified(self) -> bool:
        return self.verified_at is not None

    def clear_verification(self) -> None:
        self.verified_by = None
        self.verified_by_user_id = None
        self.verified_at = None


class Article(Base):
    __tablename__ = "articles"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_id)
    legal_text_id: Mapped[str] = mapped_column(ForeignKey("legal_texts.id", ondelete="CASCADE"), index=True)
    # A string: laws number articles "1", "12 bis", "premier".
    number: Mapped[str] = mapped_column(String(50))
    text: Mapped[str] = mapped_column(Text)
    order: Mapped[int] = mapped_column(Integer, default=0)
    # JSON list of chunk vectors; null until indexed or after the text changes.
    embedding: Mapped[str | None] = mapped_column(Text)
    embedding_model: Mapped[str | None] = mapped_column(String(200))
    # Kept in the verified text and quotable by the playbook, but left out of "applicable law" search results: for
    # articles of a law that are irrelevant to contracts (a banking licence procedure, transitional provisions).
    excluded_from_search: Mapped[bool] = mapped_column(Boolean, default=False, server_default="0")
    # When the current wording took effect; null when unknown (then it is taken to have always applied).
    applies_from: Mapped[date | None] = mapped_column(Date)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, onupdate=_now)

    legal_text: Mapped[LegalText] = relationship(back_populates="articles")
    explanations: Mapped[list["ArticleExplanation"]] = relationship(cascade="all, delete-orphan")
    versions: Mapped[list["ArticleVersion"]] = relationship(
        back_populates="article", cascade="all, delete-orphan", order_by="ArticleVersion.replaced_at.desc()"
    )


class ArticleVersion(Base):
    """A wording an article used to have. Written whenever the number or text changes, by hand or by re-importing an
    updated (consolidated) law, so an amended article keeps its history: what it said, until when, and why."""

    __tablename__ = "article_versions"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_id)
    article_id: Mapped[str] = mapped_column(ForeignKey("articles.id", ondelete="CASCADE"), index=True)
    number: Mapped[str] = mapped_column(String(50))
    text: Mapped[str] = mapped_column(Text)
    replaced_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    replaced_by: Mapped[str | None] = mapped_column(String(200))
    # Why it changed, e.g. "Modifié par la loi n° 2019-47 du 29 mai 2019".
    note: Mapped[str | None] = mapped_column(String(300))
    # When this wording had taken effect; it applied until the next wording's applies_from.
    applies_from: Mapped[date | None] = mapped_column(Date)

    article: Mapped[Article] = relationship(back_populates="versions")


class ArticleExplanation(Base):
    """A plain-language explanation of one article in one language: drafted by the model or written by a person, and
    shown to everyone only once a lawyer approves it for the article's current wording (text_hash)."""

    __tablename__ = "article_explanations"
    __table_args__ = (UniqueConstraint("article_id", "language"),)

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=_id)
    article_id: Mapped[str] = mapped_column(ForeignKey("articles.id", ondelete="CASCADE"), index=True)
    language: Mapped[str] = mapped_column(String(5))
    summary: Mapped[str] = mapped_column(Text)
    in_practice: Mapped[str | None] = mapped_column(Text)
    text_hash: Mapped[str] = mapped_column(String(64))
    model: Mapped[str | None] = mapped_column(String(100))
    written_by: Mapped[str | None] = mapped_column(String(200))
    approved_by: Mapped[str | None] = mapped_column(String(200))
    approved_by_user_id: Mapped[str | None] = mapped_column(ForeignKey("harvey_users.id"))
    approved_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, onupdate=_now)


def wording_on(article: Article, day: date) -> tuple[str, str] | None:
    """(number, text) of the wording that applied on that day, or None if the article did not exist yet. A wording
    with no known start date is taken to have always applied."""
    if article.applies_from is None or article.applies_from <= day:
        return article.number, article.text
    for version in article.versions:  # newest first
        if version.applies_from is None or version.applies_from <= day:
            return version.number, version.text
    return None


class PlaybookRule(Base):
    """One risk of the risk grid (« Grille des risques »). Linked articles are stored by law reference and article
    number, so a link survives re-importing the law; the short lists (phrases, examples, model clause) are JSON."""

    __tablename__ = "playbook_rules"

    key: Mapped[str] = mapped_column(String(80), primary_key=True)
    position: Mapped[int] = mapped_column(Integer, default=0)
    label: Mapped[str] = mapped_column(String(300))
    # What the risk is about, in plain words: shown to reviewers and sent to the AI assistant so it can tell risks apart.
    description: Mapped[str | None] = mapped_column(Text)
    confirmed_by: Mapped[str | None] = mapped_column(String(200))
    confirmed_at: Mapped[str | None] = mapped_column(String(40))
    phrases: Mapped[list] = mapped_column(JSON, default=list)
    examples: Mapped[list] = mapped_column(JSON, default=list)
    counter_examples: Mapped[list] = mapped_column(JSON, default=list)
    model_clause: Mapped[dict | None] = mapped_column(JSON)
    # Harvey's own example clauses were copied in once; never again, so a clause someone removed stays removed.
    catalog_seeded: Mapped[bool] = mapped_column(Boolean, default=False)
    # Built-in checks of this risk: {check id: {"enabled": bool, "severity": Critical|High|Medium|Low}}; absent = default.
    checks: Mapped[dict | None] = mapped_column(JSON)
    # Severity of a finding by meaning or phrase for this risk; null keeps the catalogue's (R-M11-4).
    severity: Mapped[str | None] = mapped_column(String(10))
    # Who last changed the working copy: the four-eyes rule keeps them from approving it (R-M11-2).
    edited_by_user_id: Mapped[str | None] = mapped_column(String(36))
    edited_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))

    articles: Mapped[list["PlaybookRuleArticle"]] = relationship(
        back_populates="rule", cascade="all, delete-orphan", order_by="PlaybookRuleArticle.position")


class PlaybookRuleArticle(Base):
    __tablename__ = "playbook_rule_articles"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    rule_key: Mapped[str] = mapped_column(ForeignKey("playbook_rules.key", ondelete="CASCADE"), index=True)
    position: Mapped[int] = mapped_column(Integer, default=0)
    reference: Mapped[str] = mapped_column(String(300))
    number: Mapped[str] = mapped_column(String(40))
    why: Mapped[str | None] = mapped_column(Text)

    rule: Mapped[PlaybookRule] = relationship(back_populates="articles")


class RuleVersion(Base):
    """An approved, frozen state of one risk (R-M11-3). The grid page edits a working copy; approving it adds a version
    here, and reviews use each risk's latest version, so a finding always names the exact rule it came from."""

    __tablename__ = "rule_versions"
    __table_args__ = (UniqueConstraint("rule_key", "version"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    rule_key: Mapped[str] = mapped_column(String(80), index=True)
    version: Mapped[int] = mapped_column(Integer)
    # The rule as served: label, description, severity, articles, phrases, examples, checks, approved model clause.
    payload: Mapped[dict] = mapped_column(JSON)
    approved_by_user_id: Mapped[str] = mapped_column(ForeignKey("harvey_users.id"))
    approved_by_name: Mapped[str] = mapped_column(String(300))
    approved_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    edited_by_user_id: Mapped[str | None] = mapped_column(String(36))
    # Set when a lawyer withdraws the rule from reviews; a withdrawn rule is not served until approved again.
    withdrawn_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class ReviewFeedback(Base):
    """A finding a LegalDesk user marked "not applicable" (rework spec R-M8-4): a possible false positive for the
    lawyers who keep the grid. Harvey stores no contract, only the rule, the clause reference, the quoted words and the
    reason given; it is reviewed on the risk's page and closed once a lawyer has acted on it."""

    __tablename__ = "review_feedback"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    # LegalDesk's id for the finding, so a retried delivery is stored once.
    source_ref: Mapped[str] = mapped_column(String(64), unique=True)
    rule_key: Mapped[str] = mapped_column(String(80), index=True)
    rule_version: Mapped[int | None] = mapped_column(Integer)
    clause_reference: Mapped[str | None] = mapped_column(String(300))
    quote: Mapped[str | None] = mapped_column(Text)
    reason: Mapped[str] = mapped_column(Text)
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    handled_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    handled_by_user_id: Mapped[str | None] = mapped_column(String(36))
