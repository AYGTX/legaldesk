from datetime import date, datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

from app.models import Category, Jurisdiction, TextStatus

Language = Literal["fr", "en", "ar"]


class LegalTextIn(BaseModel):
    title: str = Field(min_length=1, max_length=300)
    reference: str | None = Field(default=None, max_length=300)
    summary: str = ""
    keywords: str = ""
    language: Language = "fr"
    jurisdiction: Jurisdiction
    category: Category
    effective_date: date | None = None
    source_url: str | None = Field(default=None, max_length=1000)

    @field_validator("source_url")
    @classmethod
    def _http_only(cls, value: str | None) -> str | None:
        if value and not value.lower().startswith(("http://", "https://")):
            raise ValueError("source_url must be an http(s) URL.")
        return value


class ArticleIn(BaseModel):
    number: str = Field(min_length=1, max_length=50)
    text: str = Field(min_length=1)
    order: int | None = None


class ArticleOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    legal_text_id: str
    number: str
    text: str
    order: int


class LegalTextOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    title: str
    reference: str | None
    summary: str
    keywords: str
    language: str
    jurisdiction: Jurisdiction
    category: Category
    status: TextStatus
    effective_date: date | None
    source_url: str | None
    verified_by: str | None
    verified_at: datetime | None
    is_verified: bool
    created_at: datetime
    updated_at: datetime


class LegalTextDetail(LegalTextOut):
    articles: list[ArticleOut]


class BulkArticlesIn(BaseModel):
    raw_text: str = Field(min_length=1)
    replace: bool = False


class VerifyIn(BaseModel):
    verified_by: str = Field(min_length=1, max_length=200)


class StatusIn(BaseModel):
    status: TextStatus


class SearchIn(BaseModel):
    query: str = Field(min_length=1)
    top_k: int = Field(default=5, ge=1, le=50)
    jurisdiction: Jurisdiction | None = None
    category: Category | None = None
    language: Language | None = None
    # Unverified texts are notes someone typed, not law. They are only returned when asked for, and flagged.
    include_unverified: bool = False


class SearchHit(BaseModel):
    article_id: str
    article_number: str
    article_text: str
    excerpt: str
    score: float
    legal_text_id: str
    legal_text_title: str
    reference: str | None
    source_url: str | None
    jurisdiction: Jurisdiction
    category: Category
    language: str
    is_verified: bool
    citation: str


class CitationIn(BaseModel):
    quote: str = Field(min_length=1)
    article_id: str | None = None
    legal_text_id: str | None = None
    article_number: str | None = None


class CitationOut(BaseModel):
    found: bool
    is_verified: bool
    article_id: str | None = None
    article_number: str | None = None
    legal_text_id: str | None = None
    legal_text_title: str | None = None
    citation: str | None = None
    reason: str | None = None


class CitationBatchIn(BaseModel):
    citations: list[CitationIn] = Field(min_length=1, max_length=200)


class IndexStatus(BaseModel):
    embedder: str
    total_articles: int
    indexed_articles: int
    stale_articles: int
