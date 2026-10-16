import enum
import uuid
from datetime import date, datetime, timezone

from sqlalchemy import Date, DateTime, Enum, ForeignKey, Integer, String, Text
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
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now, onupdate=_now)

    legal_text: Mapped[LegalText] = relationship(back_populates="articles")
