from app.config import Settings
from app.main import create_app
from fastapi.testclient import TestClient

from tests.conftest import LAW, make_text


def test_create_and_import(client):
    text = make_text(client)
    response = client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
    assert response.json()["imported"] == 3
    assert response.json()["preamble"] == "LOI ORGANIQUE TEST"
    detail = client.get(f"/api/v1/texts/{text['id']}").json()
    assert [a["number"] for a in detail["articles"]] == ["1", "2", "2 bis"]


def test_import_refuses_clashing_numbers_unless_replace(client):
    text = make_text(client)
    client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
    clash = client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
    assert clash.status_code == 409 and clash.json()["code"] == "duplicate_number"
    replaced = client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW, "replace": True})
    assert replaced.json()["imported"] == 3
    assert len(client.get(f"/api/v1/texts/{text['id']}/articles").json()) == 3


def test_verify_requires_articles_and_source(client):
    text = make_text(client, source_url=None)
    assert client.post(f"/api/v1/texts/{text['id']}/verify", json={"verified_by": "x"}).json()["code"] == "no_articles"
    client.post(f"/api/v1/texts/{text['id']}/articles", json={"number": "1", "text": "Texte."})
    assert client.post(f"/api/v1/texts/{text['id']}/verify", json={"verified_by": "x"}).json()["code"] == "no_source"


def test_editing_article_clears_verification(client, verified_text):
    assert verified_text["is_verified"]
    article = verified_text["articles"][0]
    client.put(f"/api/v1/articles/{article['id']}", json={"number": "1", "text": "Texte modifié."})
    assert not client.get(f"/api/v1/texts/{verified_text['id']}").json()["is_verified"]


def test_changing_reference_clears_verification_but_summary_does_not(client, verified_text):
    base = {k: verified_text[k] for k in
            ("title", "reference", "summary", "keywords", "language", "jurisdiction", "category", "source_url")}
    assert client.put(f"/api/v1/texts/{verified_text['id']}", json={**base, "summary": "new"}).json()["is_verified"]
    assert not client.put(f"/api/v1/texts/{verified_text['id']}", json={**base, "reference": "Autre"}).json()["is_verified"]


def test_deleting_article_clears_verification(client, verified_text):
    client.delete(f"/api/v1/articles/{verified_text['articles'][0]['id']}")
    assert not client.get(f"/api/v1/texts/{verified_text['id']}").json()["is_verified"]


def test_rejects_non_http_source(client):
    response = client.post("/api/v1/texts", json={
        "title": "x", "jurisdiction": "EU", "category": "Other", "source_url": "javascript:alert(1)"})
    assert response.status_code == 422


def test_search_returns_only_verified_published_by_default(client, verified_text):
    make_text(client, title="Brouillon", reference="Note")
    draft = client.get("/api/v1/texts?verified=false").json()[0]
    client.post(f"/api/v1/texts/{draft['id']}/articles", json={"number": "1", "text": "sécurité des données brouillon"})

    hits = client.post("/api/v1/search", json={"query": "sécurité des données"}).json()
    assert hits and all(h["is_verified"] for h in hits)
    assert hits[0]["article_number"] == "2 bis"
    assert hits[0]["citation"] == "Loi test n° 1, art. 2 bis"

    with_notes = client.post("/api/v1/search", json={"query": "sécurité des données", "include_unverified": True}).json()
    assert any(not h["is_verified"] for h in with_notes)


def test_search_reindexes_edited_article(client, verified_text):
    article = verified_text["articles"][0]
    client.put(f"/api/v1/articles/{article['id']}", json={"number": "1", "text": "Le licenciement abusif ouvre droit à réparation."})
    client.post(f"/api/v1/texts/{verified_text['id']}/verify", json={"verified_by": "x"})
    hits = client.post("/api/v1/search", json={"query": "licenciement abusif"}).json()
    assert hits[0]["article_id"] == article["id"]


def test_citation_check(client, verified_text):
    tid = verified_text["id"]
    ok = client.post("/api/v1/citations/check", json={
        "legal_text_id": tid, "article_number": "2",
        "quote": "« doit respecter la dignité   humaine et la vie privée »"}).json()
    assert ok["found"] and ok["is_verified"] and ok["citation"] == "Loi test n° 1, art. 2"

    elided = client.post("/api/v1/citations/check", json={
        "legal_text_id": tid, "quote": "Le traitement ... respecter la dignité humaine"}).json()
    assert elided["found"] and elided["article_number"] == "2"

    reworded = client.post("/api/v1/citations/check", json={
        "legal_text_id": tid, "quote": "doit respecter la dignité et la vie privée"}).json()
    assert not reworded["found"] and reworded["reason"] == "quote_not_in_article"

    out_of_order = client.post("/api/v1/citations/check", json={
        "legal_text_id": tid, "quote": "dignité humaine ... Le traitement des données"}).json()
    assert not out_of_order["found"]

    short = client.post("/api/v1/citations/check", json={"legal_text_id": tid, "quote": "la loi"}).json()
    assert short["reason"] == "quote_too_short"


def test_citation_in_unverified_text_is_flagged(client):
    text = make_text(client)
    client.post(f"/api/v1/texts/{text['id']}/articles", json={"number": "1", "text": "Une phrase suffisamment longue."})
    result = client.post("/api/v1/citations/check", json={
        "legal_text_id": text["id"], "quote": "phrase suffisamment longue"}).json()
    assert result["found"] and not result["is_verified"] and result["reason"] == "text_not_verified"


def test_api_key_is_enforced_when_configured():
    app = create_app(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key="s3cret"))
    with TestClient(app) as c:
        assert c.get("/api/v1/texts").status_code == 401
        assert c.get("/api/v1/texts", headers={"X-API-Key": "s3cret"}).status_code == 200
        assert c.get("/health").status_code == 200


def test_index_status_and_rebuild(client, verified_text):
    status = client.post("/api/v1/index/rebuild?force=true").json()
    assert status["total_articles"] == 3 and status["stale_articles"] == 0
