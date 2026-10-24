import json

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from tests.conftest import LAW


@pytest.fixture
def setup(tmp_path):
    app = create_app(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key=None, legacy_playbook_file=None, seed_risks=False))
    with TestClient(app) as c:
        c.post("/login", data={"login": "admin", "password": "admin"})
        text = c.post("/api/v1/texts", json={
            "title": "Données", "reference": "Loi test n° 1", "jurisdiction": "Tunisia",
            "category": "DataProtection", "source_url": "https://example.org/1"}).json()
        c.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
        yield c, app, text["id"]


def _verify(c, text_id):
    c.post(f"/api/v1/texts/{text_id}/status", json={"status": "Published"})
    c.post(f"/api/v1/texts/{text_id}/verify", json={"verified_by": "H. Aymen"})


def _pb(app):
    from app.services import playbook
    with app.state.session_factory() as db:
        return playbook.load(db)


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
    assert "Loi test n° 1, art. 2 bis" in page


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


def test_rule_deletion(setup):
    c, path, _ = setup
    c.post("/playbook/rules", data={"key": "payment", "label": "Paiement"})
    assert [r["key"] for r in _pb(path)["rules"]] == ["payment"]

    c.post("/playbook/rules/payment/delete")
    assert _pb(path)["rules"] == []


def test_list_page_renders(setup):
    c, _, _ = setup
    c.post("/playbook/rules", data={"key": "custom_thing", "label": "Custom"})
    page = c.get("/playbook").text
    assert "Custom" in page


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


def test_model_clause_is_approved_on_its_own_and_served_once_approved(setup):
    c, path, text_id = setup
    _verify(c, text_id)
    c.post("/playbook/rules", data={"key": "liability", "label": "Responsabilité"})
    c.post("/playbook/rules/liability/articles", data={"text_id": text_id, "number": "1", "why": "x"})
    c.post("/playbook/rules/liability/confirm", data={"confirmed_by": "H. Aymen"})
    clause = "La responsabilité de chaque partie est limitée au montant des sommes versées au cours des douze derniers mois."

    c.post("/playbook/rules/liability/model-clause", data={"fr": clause, "en": ""})
    assert c.get("/api/v1/playbook").json()["rules"]["liability"]["model_clause"] is None
    assert _pb(path)["rules"][0]["confirmed_by"] == "H. Aymen"  # the risk keeps its approval

    c.post("/playbook/rules/liability/model-clause/approve", data={"approved_by": "Me Trabelsi"})
    assert c.get("/api/v1/playbook").json()["rules"]["liability"]["model_clause"] == {"fr": clause, "en": None}

    c.post("/playbook/rules/liability/model-clause", data={"fr": clause + " Sauf faute lourde ou dol.", "en": ""})
    assert c.get("/api/v1/playbook").json()["rules"]["liability"]["model_clause"] is None
    assert _pb(path)["rules"][0]["confirmed_by"] == "H. Aymen"


def test_model_clause_drafted_by_the_model_is_only_a_proposal(setup):
    c, path, text_id = setup
    _verify(c, text_id)
    c.post("/playbook/rules", data={"key": "liability", "label": "Responsabilité"})
    c.post("/playbook/rules/liability/articles", data={"text_id": text_id, "number": "1", "why": "x"})

    class Fake:
        name = "fake"
        def complete_json(self, system, user):
            assert "Loi test n° 1" in user and "Responsabilité" in user
            return {"fr": "Chaque partie ne répond que des dommages directs, dans la limite du prix annuel du contrat.",
                    "en": "Each party is liable only for direct damage, up to the annual contract price."}

    c.app.state.llm = Fake()
    page = c.post("/playbook/rules/liability/model-clause/draft").text
    assert "dans la limite du prix annuel" in page
    assert "model_clause" not in _pb(path)["rules"][0]


def test_add_all_drafted_examples_at_once(setup):
    c, app, _ = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Clause pénale"})
    ex = "En cas de retard, le Prestataire paiera une pénalité de 2 % du prix par jour, sans plafond."
    counter = "Les pénalités de retard sont plafonnées à 10 % du prix total du contrat."
    c.post("/playbook/rules/penalty/examples-bulk", data={"examples": [ex, ex, "trop court"], "counter_examples": [counter]})
    rule = _pb(app)["rules"][0]
    assert [e["text"] for e in rule["examples"]] == [ex]
    assert [e["text"] for e in rule["counter_examples"]] == [counter]


def test_go_to_article_by_number(setup):
    c, _, text_id = setup
    response = c.get(f"/texts/{text_id}/goto?number=1", follow_redirects=False)
    assert response.headers["location"].startswith(f"/texts/{text_id}?page=1#art-")
    assert "flash=goto_missing" in c.get(f"/texts/{text_id}/goto?number=999", follow_redirects=False).headers["location"]


def test_accepting_one_suggestion_keeps_the_others(setup):
    c, app, _ = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Clause pénale"})
    a = "En cas de retard, le Prestataire paiera une pénalité de 2 % du prix par jour, sans plafond."
    b = "Tout retard donnera lieu à des pénalités calculées par jour calendaire, sans limite de montant."
    page = c.post("/playbook/rules/penalty/examples-accept",
                  data={"kind": "examples", "text": a, "examples": [a, b]}, headers={"accept-language": "fr"}).text
    assert [e["text"] for e in _pb(app)["rules"][0]["examples"]] == [a]
    assert "1 clause(s) ajoutée(s)" in page and 'value="' + b in page.replace("&#39;", "'")


def test_accepting_one_phrase_keeps_the_other_suggestions(setup):
    c, app, _ = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Clause pénale"})
    page = c.post("/playbook/rules/penalty/phrases-accept",
                  data={"phrase": "pénalité par jour", "suggested": ["pénalité par jour", "sans plafond de pénalités"]}).text
    assert _pb(app)["rules"][0]["phrases"] == ["pénalité par jour"]
    assert 'value="sans plafond de pénalités"' in page


def test_text_page_shows_which_risks_an_article_backs(setup):
    c, _, text_id = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Clause pénale"})
    c.post("/playbook/rules/penalty/articles", data={"text_id": text_id, "number": "1", "why": "x"})
    page = c.get(f"/texts/{text_id}").text
    assert 'href="/playbook/rules/penalty"' in page and "⚑ Clause pénale" in page


def test_risks_have_an_editable_description(setup):
    c, app, _ = setup
    c.post("/playbook/rules", data={"key": "penalty", "label": "Clause pénale",
                                    "description": "Somme forfaitaire due en cas de retard, parfois sans plafond."})
    assert _pb(app)["rules"][0]["description"] == "Somme forfaitaire due en cas de retard, parfois sans plafond."
    c.post("/playbook/rules/penalty/label", data={"label": "Pénalités", "description": "Nouvelle description du risque."})
    rule = _pb(app)["rules"][0]
    assert (rule["label"], rule["description"]) == ("Pénalités", "Nouvelle description du risque.")
    assert "Nouvelle description du risque." in c.get("/playbook/rules/penalty").text


def test_seeded_risks_get_default_descriptions():
    from app.services import playbook

    assert set(playbook.DETECTED_KEYS) <= set(playbook.DEFAULT_DESCRIPTIONS)


def test_builtin_checks_can_be_switched_off_and_regraded(setup):
    from app.services import review
    from app.services.risk_catalog import seeded_grid

    c, app, _ = setup
    c.post("/playbook/rules", data={"key": "force_majeure", "label": "Force majeure"})
    c.post("/playbook/rules/force_majeure/checks/missing_force_majeure", data={"enabled": "1", "severity": "High"})
    clause = [review.Clause("Article 1 — Objet", "Le présent contrat a pour objet la fourniture de services.")]
    grid = _pb(app)["rules"]
    found = {f.check: f.severity for f in review.review(clause, grid, None)[0]}
    assert found["missing_force_majeure"] == "High"

    c.post("/playbook/rules/force_majeure/checks/missing_force_majeure", data={"severity": "High"})  # unchecked box
    grid = _pb(app)["rules"]
    assert "missing_force_majeure" not in {f.check for f in review.review(clause, grid, None)[0]}
    page = c.get("/playbook/rules/force_majeure", headers={"accept-language": "fr"}).text
    assert "Contrôles intégrés" in page and "Aucune clause de force majeure" in page
    assert "Contrôles intégrés actifs" in c.get("/playbook", headers={"accept-language": "fr"}).text
