import pytest

from app import mcp_server
from app.db import make_session_factory
from app.models import TextStatus
from app.schemas import ArticleIn, LegalTextIn
from app.services import corpus
from tests.conftest import LAW


@pytest.fixture
def db_factory(monkeypatch):
    factory = make_session_factory("sqlite://")
    monkeypatch.setattr(mcp_server, "_factory", factory)
    with factory() as db:
        verified = corpus.create_text(db, LegalTextIn(title="Protection des données", reference="Loi test n° 1",
                                                      jurisdiction="Tunisia", category="DataProtection",
                                                      source_url="https://example.org/1"))
        corpus.import_articles(db, verified.id, LAW, replace=False)
        db.expire_all()
        corpus.set_status(db, verified.id, TextStatus.Published)
        corpus.verify(db, verified.id, "Me")
        draft = corpus.create_text(db, LegalTextIn(title="Brouillon", reference="Loi test n° 2", jurisdiction="Tunisia",
                                                   category="Other"))
        corpus.add_article(db, draft.id, ArticleIn(number="1", text="Texte non vérifié sur la dignité humaine et la vie privée."))
    return factory


def test_only_verified_law_is_listed_and_found(db_factory):
    laws = mcp_server.list_laws()
    assert [l["reference"] for l in laws] == ["Loi test n° 1"] and laws[0]["articles"] == 3
    hits = mcp_server.search_law("dignité humaine vie privée")
    assert hits and all("Loi test n° 1" in h["citation"] for h in hits)


def test_get_article_and_check_quote(db_factory):
    article = mcp_server.get_article("test n° 1", "2 bis")
    assert article["found"] and article["citation"] == "Loi test n° 1, art. 2 bis"
    assert "sécurité des données" in article["text"]

    ok = mcp_server.check_quote("préserver la sécurité des données", article["article_id"])
    assert ok["found"] and ok["verified_law"]
    invented = mcp_server.check_quote("le responsable paie une amende de mille dinars", article["article_id"])
    assert not invented["found"]


def test_unknown_or_unverified_articles_are_not_returned(db_factory):
    assert not mcp_server.get_article("test n° 2", "1")["found"]
    assert not mcp_server.get_article("test n° 1", "99")["found"]
    assert not mcp_server.explain_article("no-such-id")["found"]


def test_the_server_exposes_the_tools():
    import asyncio

    names = {tool.name for tool in asyncio.run(mcp_server.mcp.list_tools())}
    assert names == {"list_laws", "search_law", "get_article", "check_quote", "explain_article"}
