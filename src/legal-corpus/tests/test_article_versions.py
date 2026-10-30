from tests.conftest import LAW, make_settings, sign_in
import sqlite3

from app.db import make_session_factory


def _article(text, number):
    return next(a for a in text["articles"] if a["number"] == number)


def _search(client, query):
    return [h["citation"] for h in client.post("/api/v1/search", json={"query": query}).json()]


def test_excluded_article_leaves_search_but_stays_verified_and_quotable(client, verified_text):
    article = _article(verified_text, "2")
    assert any(c.endswith("art. 2") for c in _search(client, "dignité humaine vie privée"))

    client.put(f"/api/v1/articles/{article['id']}/search", json={"excluded": True})

    assert not any(c.endswith("art. 2") for c in _search(client, "dignité humaine vie privée"))
    text = client.get(f"/api/v1/texts/{verified_text['id']}").json()
    assert text["verified_by"] == "Me Test" and _article(text, "2")["excluded_from_search"] is True
    check = client.post("/api/v1/citations/check", json={"article_id": article["id"], "quote": "respecter la dignité humaine et la vie privée"}).json()
    assert check["found"] and check["is_verified"]


def test_editing_an_article_keeps_the_previous_wording_with_its_reason(client, verified_text):
    article = _article(verified_text, "2")
    client.put(f"/api/v1/articles/{article['id']}", json={
        "number": "2", "text": "Le traitement des données doit respecter la dignité, la vie privée et les libertés.",
        "note": "Modifié par la loi n° 2099-1"})

    versions = client.get(f"/api/v1/articles/{article['id']}/versions").json()
    assert len(versions) == 1
    assert versions[0]["text"] == article["text"] and versions[0]["note"] == "Modifié par la loi n° 2099-1"
    assert versions[0]["replaced_by"] == "API"
    assert client.get(f"/api/v1/texts/{verified_text['id']}").json()["verified_by"] is None


def test_saving_without_a_change_records_no_version(client, verified_text):
    article = _article(verified_text, "2")
    client.put(f"/api/v1/articles/{article['id']}", json={"number": "2", "text": article["text"]})
    assert client.get(f"/api/v1/articles/{article['id']}/versions").json() == []


def test_reimporting_an_updated_law_updates_articles_in_place(client, verified_text):
    article = _article(verified_text, "2")
    client.put(f"/api/v1/articles/{article['id']}/search", json={"excluded": True})
    updated = LAW.replace("doit respecter la dignité humaine", "doit respecter strictement la dignité humaine")

    client.post(f"/api/v1/texts/{verified_text['id']}/articles/import", json={"raw_text": updated, "replace": True})

    text = client.get(f"/api/v1/texts/{verified_text['id']}").json()
    same = _article(text, "2")
    assert same["id"] == article["id"] and same["excluded_from_search"] is True and "strictement" in same["text"]
    versions = client.get(f"/api/v1/articles/{article['id']}/versions").json()
    assert [v["text"] for v in versions] == [article["text"]]
    # Unchanged articles get no version.
    assert client.get(f"/api/v1/articles/{_article(text, '1')['id']}/versions").json() == []


def test_restoring_a_version_from_the_text_page(client, verified_text):
    article = _article(verified_text, "2")
    client.put(f"/api/v1/articles/{article['id']}", json={"number": "2", "text": "Nouvelle rédaction de l'article deux."})
    version_id = client.get(f"/texts/{verified_text['id']}").text.split(f"/articles/{article['id']}/versions/")[1].split("/")[0]

    response = client.post(f"/articles/{article['id']}/versions/{version_id}/restore", follow_redirects=False)

    assert "flash=restored_ok" in response.headers["location"]
    assert client.get(f"/api/v1/articles/{article['id']}").json()["text"] == article["text"]
    versions = client.get(f"/api/v1/articles/{article['id']}/versions").json()
    assert versions[0]["text"] == "Nouvelle rédaction de l'article deux." and versions[0]["replaced_by"] == "Me Test"
    page = client.get(f"/texts/{verified_text['id']}", headers={"accept-language": "fr"}).text
    assert "Rédactions antérieures (2)" in page


def test_existing_database_gets_the_new_column(tmp_path):
    path = tmp_path / "old.db"
    with sqlite3.connect(path) as con:
        con.execute("CREATE TABLE legal_texts (id VARCHAR(36) PRIMARY KEY)")
        con.execute("CREATE TABLE articles (id VARCHAR(36) PRIMARY KEY, legal_text_id VARCHAR(36), number VARCHAR(50), "
                    "text TEXT, \"order\" INTEGER, embedding TEXT, embedding_model VARCHAR(200), created_at DATETIME, updated_at DATETIME)")
        con.execute("INSERT INTO articles (id, legal_text_id, number, text) VALUES ('a', 't', '1', 'x')")

    make_session_factory(f"sqlite:///{path}")

    with sqlite3.connect(path) as con:
        columns = [row[1] for row in con.execute("PRAGMA table_info(articles)")]
        assert "excluded_from_search" in columns
        assert con.execute("SELECT excluded_from_search FROM articles").fetchone() == (0,)
        assert con.execute("SELECT name FROM sqlite_master WHERE name = 'article_versions'").fetchone()


def test_law_as_it_stood_on_a_date(client, verified_text):
    article = _article(verified_text, "2")
    client.put(f"/api/v1/articles/{article['id']}", json={"number": "2", "text": article["text"], "applies_from": "2004-07-27"})
    client.put(f"/api/v1/articles/{article['id']}", json={
        "number": "2", "text": "Le traitement des données doit respecter la dignité et la transparence.", "applies_from": "2020-01-01"})

    def article_on(day):
        text = client.get(f"/api/v1/texts/{verified_text['id']}", params={"as_of": day}).json()
        return next((a for a in text["articles"] if a["id"] == article["id"]), None)

    assert article_on("2010-06-01")["text"] == article["text"]
    assert "transparence" in article_on("2021-06-01")["text"]
    assert article_on("2000-01-01") is None

    old = client.post("/api/v1/search", json={"query": "dignité humaine vie privée", "as_of": "2010-06-01", "include_unverified": True}).json()
    assert any(h["article_id"] == article["id"] and "vie privée" in h["article_text"] for h in old)
    new = client.post("/api/v1/search", json={"query": "transparence", "as_of": "2021-06-01", "include_unverified": True}).json()
    assert any(h["article_id"] == article["id"] for h in new)

    page = client.get(f"/texts/{verified_text['id']}", params={"as_of": "2010-06-01"}, headers={"accept-language": "fr"}).text
    assert "Rédaction antérieure" in page and "vie privée" in page


def test_new_wording_cannot_start_before_the_current_one(client, verified_text):
    article = _article(verified_text, "2")
    client.put(f"/api/v1/articles/{article['id']}", json={"number": "2", "text": article["text"], "applies_from": "2020-01-01"})
    r = client.put(f"/api/v1/articles/{article['id']}", json={"number": "2", "text": "Autre rédaction de l'article.", "applies_from": "2019-01-01"})
    assert r.status_code == 409 and r.json()["code"] == "applies_before_current"


def test_session_key_is_kept_across_restarts(tmp_path):
    from fastapi.testclient import TestClient
    from app.config import Settings
    from app.main import create_app

    # A file database: the session names an account, which must still exist after the restart.
    settings = make_settings(database_url=f"sqlite:///{tmp_path / 'harvey.db'}", session_secret_file=tmp_path / ".session_secret")
    with TestClient(create_app(settings)) as first:
        sign_in(first)
        cookie = first.cookies.get("harvey_session")
    with TestClient(create_app(settings)) as second:
        second.cookies.set("harvey_session", cookie)
        assert second.get("/", follow_redirects=False).status_code == 200
