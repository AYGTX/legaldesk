import enum
import uuid
from datetime import date, datetime, timezone

from sqlalchemy import Boolean, Date, DateTime, Enum, ForeignKey, Integer, String, Text, UniqueConstraint
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
    # Set only when a person checked every stored article against the official source. Anything else is a note.
    verified_by: Mapped[str | None] = mapped_column(String(200))
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
