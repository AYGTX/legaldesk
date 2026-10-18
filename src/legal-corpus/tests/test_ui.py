from tests.conftest import LAW


def _create(client):
    response = client.post("/texts/new", data={
        "title": "Code du travail", "reference": "Loi 66-27", "jurisdiction": "Tunisia", "category": "Labor",
        "language": "fr", "source_url": "https://example.org/ct",
    }, follow_redirects=False)
    assert response.status_code == 303
    return response.headers["location"].split("?")[0]


def test_full_ui_flow(client):
    path = _create(client)
    client.post(f"{path}/import", data={"raw_text": LAW})
    page = client.get(path, headers={"accept-language": "fr"}).text
    assert "Article 2 bis" in page and "Non vérifié" in page

    client.post(f"{path}/verify", data={"verified_by": "Me Trabelsi"})
    client.post(f"{path}/status", data={"status": "Published"})
    assert "Vérifié par Me Trabelsi" in client.get(path, headers={"accept-language": "fr"}).text

    results = client.get("/search", params={"q": "sécurité des données"}).text
    assert "Loi 66-27, art. 2 bis" in results

    check = client.get("/search", params={"quote": "la dignité humaine et la vie privée", "text_id": path.split("/")[-1]}).text
    assert "Found word for word" in check


def test_rule_error_redirects_with_message(client):
    path = _create(client)
    response = client.post(f"{path}/verify", data={"verified_by": "x"}, headers={"referer": f"http://testserver{path}"},
                           follow_redirects=False)
    assert response.status_code == 303 and "flash=err_no_articles" in response.headers["location"]


def test_language_switch(client):
    client.get("/lang/fr")
    assert "Référentiel juridique vérifié" in client.get("/").text
    client.get("/lang/en")
    assert "Verified legal reference" in client.get("/").text


def test_cross_origin_post_refused(client):
    response = client.post("/texts/new", data={"title": "x"}, headers={"origin": "https://evil.example"})
    assert response.status_code == 403


def test_unknown_flash_key_not_rendered(client):
    assert "pwned" not in client.get("/?flash=pwned").text
