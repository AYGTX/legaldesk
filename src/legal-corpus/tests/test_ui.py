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
    assert "Le référentiel juridique vérifié de Harvey" in client.get("/").text
    client.get("/lang/en")
    assert "Harvey&#39;s verified legal reference" in client.get("/").text


def test_cross_origin_post_refused(client):
    response = client.post("/texts/new", data={"title": "x"}, headers={"origin": "https://evil.example"})
    assert response.status_code == 403


def test_unknown_flash_key_not_rendered(client):
    assert "pwned" not in client.get("/?flash=pwned").text


def test_source_can_be_given_when_verifying(client):
    response = client.post("/texts/new", data={"title": "Loi sans source", "jurisdiction": "Tunisia", "category": "Other",
                                               "language": "fr"}, follow_redirects=False)
    path = response.headers["location"].split("?")[0]
    client.post(f"{path}/import", data={"raw_text": LAW})
    page = client.get(path, headers={"accept-language": "fr"}).text
    assert 'id="verify_source"' in page

    bad = client.post(f"{path}/verify", data={"verified_by": "Me", "source_url": "jort.tn"},
                      headers={"referer": f"http://testserver{path}"}, follow_redirects=False)
    assert "flash=err_invalid_source" in bad.headers["location"]

    client.post(f"{path}/verify", data={"verified_by": "Me", "source_url": "https://www.jort.gov.tn/loi"})
    text = client.get(f"/api/v1/texts/{path.split('/')[-1]}").json()
    assert text["source_url"] == "https://www.jort.gov.tn/loi" and text["verified_by"] == "Me"
