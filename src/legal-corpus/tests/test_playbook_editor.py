import json

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from tests.conftest import LAW


@pytest.fixture
def setup(tmp_path):
    path = tmp_path / "playbook.json"
    path.write_text(json.dumps({"rules": [], "regulations": {}}), encoding="utf-8")
    app = create_app(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key=None, playbook_path=path))
    with TestClient(app) as c:
        c.post("/login", data={"login": "admin", "password": "admin"})
        text = c.post("/api/v1/texts", json={
            "title": "Données", "reference": "Loi test n° 1", "jurisdiction": "Tunisia",
            "category": "DataProtection", "source_url": "https://example.org/1"}).json()
        c.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
        yield c, path, text["id"]


def _verify(c, text_id):
    c.post(f"/api/v1/texts/{text_id}/status", json={"status": "Published"})
    c.post(f"/api/v1/texts/{text_id}/verify", json={"verified_by": "H. Aymen"})


def _pb(path):
    return json.loads(path.read_text(encoding="utf-8"))


def test_build_and_confirm_a_rule(setup):
    c, path, text_id = setup
    _verify(c, text_id)

    c.post("/playbook/rules", data={"key": "personal_data", "label": "Données personnelles"})
    c.post("/playbook/rules/personal_data/articles", data={"text_id": text_id, "number": "2 bis", "why": "Sécurité"})
    c.post("/playbook/rules/personal_data/articles", data={"text_id": text_id, "number": "1", "why": "Droit"})
    c.post("/playbook/rules/personal_data/articles/1/move", data={"offset": "-1"})
    r = c.post("/playbook/rules/personal_data/confirm", data={"confirmed_by": "H. Aymen"}, follow_redirects=False)
    assert "flash=rule_confirmed" in r.headers["location"]

    rule = _pb(path)["rules"][0]
    assert [a["number"] for a in rule["articles"]] == ["1", "2 bis"]
    assert rule["confirmed_by"] == "H. Aymen" and rule["confirmed_at"]

    served = c.get("/api/v1/playbook").json()["rules"]["personal_data"]["articles"]
    assert [a["citation"] for a in served] == ["Loi test n° 1, art. 1", "Loi test n° 1, art. 2 bis"]

    page = c.get("/playbook/rules/personal_data", headers={"accept-language": "fr"}).text
    assert "Confirmée par H. Aymen" in page and "Loi test n° 1, art. 2 bis" in page


def test_any_edit_withdraws_the_confirmation(setup):
    c, path, text_id = setup
    _verify(c, text_id)
    c.post("/playbook/rules", data={"key": "liability", "label": "Resp."})
    c.post("/playbook/rules/liability/articles", data={"text_id": text_id, "number": "1", "why": "x"})
    c.post("/playbook/rules/liability/confirm", data={"confirmed_by": "H. Aymen"})

    c.post("/playbook/rules/liability/articles/0", data={"why": "reworded"})

    assert _pb(path)["rules"][0]["confirmed_by"] is None
    assert c.get("/api/v1/playbook").json()["rules"] == {}


def test_cannot_confirm_a_rule_on_unverified_law(setup):
    c, path, text_id = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Pénalité"})
    c.post("/playbook/rules/penalty/articles", data={"text_id": text_id, "number": "1", "why": "x"})

    r = c.post("/playbook/rules/penalty/confirm", data={"confirmed_by": "H. Aymen"},
               headers={"referer": "http://testserver/playbook/rules/penalty"}, follow_redirects=False)

    assert "flash=err_unverified_article" in r.headers["location"]
    assert _pb(path)["rules"][0]["confirmed_by"] is None


def test_rejects_an_article_that_does_not_exist(setup):
    c, path, text_id = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Pénalité"})
    r = c.post("/playbook/rules/penalty/articles", data={"text_id": text_id, "number": "99"},
               headers={"referer": "http://testserver/playbook/rules/penalty"}, follow_redirects=False)
    assert "flash=err_article_not_found" in r.headers["location"]
    assert _pb(path)["rules"][0]["articles"] == []


def test_regulation_mapping_and_rule_deletion(setup):
    c, path, _ = setup
    c.post("/playbook/rules", data={"key": "payment", "label": "Paiement"})
    c.post("/playbook/mappings", data={"fragment": "Code de commerce", "key": "payment"})
    assert _pb(path)["regulations"] == {"Code de commerce": "payment"}

    c.post("/playbook/rules/payment/delete")
    assert _pb(path) == {"rules": [], "regulations": {}}


def test_list_page_renders(setup):
    c, _, _ = setup
    c.post("/playbook/rules", data={"key": "custom_thing", "label": "Custom"})
    page = c.get("/playbook").text
    assert "Custom" in page and "Not detected by LegalDesk" in page


def test_phrases_are_served_only_once_the_rule_is_confirmed_again(setup):
    c, path, text_id = setup
    _verify(c, text_id)
    c.post("/playbook/rules", data={"key": "indemnification", "label": "Indemnisation"})
    c.post("/playbook/rules/indemnification/articles", data={"text_id": text_id, "number": "1", "why": "x"})
    c.post("/playbook/rules/indemnification/confirm", data={"confirmed_by": "H. Aymen"})

    c.post("/playbook/rules/indemnification/phrases", data={"phrase": "  prendra en charge   l'intégralité des conséquences "})
    assert _pb(path)["rules"][0]["phrases"] == ["prendra en charge l'intégralité des conséquences"]
    assert c.get("/api/v1/playbook").json()["rules"] == {}

    c.post("/playbook/rules/indemnification/confirm", data={"confirmed_by": "H. Aymen"})
    served = c.get("/api/v1/playbook").json()["rules"]["indemnification"]
    assert served["phrases"] == ["prendra en charge l'intégralité des conséquences"] and served["label"] == "Indemnisation"
    assert "Formulations à rechercher" in c.get("/playbook/rules/indemnification", headers={"accept-language": "fr"}).text

    c.post("/playbook/rules/indemnification/phrases/0/delete")
    assert _pb(path)["rules"][0]["phrases"] == [] and _pb(path)["rules"][0]["confirmed_by"] is None


def test_rejects_short_and_duplicate_phrases(setup):
    c, path, _ = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Pénalité"})
    referer = {"referer": "http://testserver/playbook/rules/penalty"}
    r = c.post("/playbook/rules/penalty/phrases", data={"phrase": "ab"}, headers=referer, follow_redirects=False)
    assert "flash=err_phrase_too_short" in r.headers["location"]
    c.post("/playbook/rules/penalty/phrases", data={"phrase": "Astreinte journalière"})
    r = c.post("/playbook/rules/penalty/phrases", data={"phrase": "astreinte  journalière"}, headers=referer, follow_redirects=False)
    assert "flash=err_duplicate_phrase" in r.headers["location"]
