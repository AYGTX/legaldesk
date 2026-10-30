from tests.conftest import LAW, sign_in, make_settings
import html
import json
from dataclasses import dataclass, field
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app


@dataclass
class FakeLlm:
    answers: list[dict]
    name: str = "fake"
    prompts: list[str] = field(default_factory=list)

    def complete_json(self, system, user):
        self.prompts.append(user)
        return self.answers.pop(0)


def test_import_suggestion_fills_only_empty_fields_and_drops_an_invented_reference(client):
    raw = "Loi n° 2099-1 du 2 janvier 2099, relative aux données.\n" + LAW
    client.app.state.llm = FakeLlm([{"title": "Loi relative aux données", "reference": "Loi n° 1999-9 du 9 septembre 1999",
                                     "category": "DataProtection", "keywords": "données, vie privée", "summary": "Protège les données."}])
    page = client.post("/import-pdf/suggest", data={"raw_text": raw, "title": "Mon titre", "jurisdiction": "Tunisia",
                                                    "category": "Other", "language": "fr"}).text
    assert 'value="Mon titre"' in page
    assert "1999-9" not in page
    assert 'value="DataProtection" selected' in page
    assert "données, vie privée" in page and "Protège les données." in page


def test_import_suggestion_keeps_a_reference_found_in_the_text(client):
    raw = "Loi n° 2099-1 du 2 janvier 2099, relative aux données.\n" + LAW
    client.app.state.llm = FakeLlm([{"reference": "Loi n° 2099-1 du 2 janvier 2099", "category": "Nope"}])
    page = client.post("/import-pdf/suggest", data={"raw_text": raw, "jurisdiction": "Tunisia", "language": "fr"}).text
    assert 'value="Loi n° 2099-1 du 2 janvier 2099"' in html.unescape(page)


def _pb(app):
    from app.services import playbook
    with app.state.session_factory() as db:
        return playbook.load(db)


@pytest.fixture
def rule_client(tmp_path):
    app = create_app(make_settings(api_key=None, legacy_playbook_file=None, seed_risks=False))
    with TestClient(app) as c:
        sign_in(c)
        text = c.post("/api/v1/texts", json={"title": "Données", "reference": "Loi test n° 1", "jurisdiction": "Tunisia",
                                             "category": "DataProtection", "source_url": "https://example.org/1"}).json()
        c.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
        c.put(f"/api/v1/texts/{text['id']}/status", json={"status": "Published"})
        c.post(f"/api/v1/texts/{text['id']}/verify", json={"verified_by": "Me"})
        c.post("/playbook/rules", data={"key": "personal_data", "label": "Données personnelles"})
        yield c, text["id"], app


def test_article_suggestions_keep_only_real_articles(rule_client):
    c, text_id, _ = rule_client
    fake = FakeLlm([{"articles": [{"number": "2 bis", "why": "Sécurité des données."}, {"number": "99", "why": "Inventé."},
                                  {"number": "2", "why": "Dignité et vie privée."}]}])
    c.app.state.llm = fake
    page = c.post("/playbook/rules/personal_data/suggest-articles", data={"text_id": text_id}).text

    assert "Article 2 bis" in page and "Article 99" not in page and "Dignité et vie privée." in page
    assert "Article 2 bis:" in fake.prompts[0] and "Données personnelles" in fake.prompts[0]
    # Accepting goes through the normal form, so the rule stays unconfirmed until a person confirms it.
    c.post("/playbook/rules/personal_data/articles", data={"text_id": text_id, "number": "2 bis", "why": "Sécurité des données."})
    rule = _pb(rule_client[2])["rules"][0]
    assert [a["number"] for a in rule["articles"]] == ["2 bis"] and rule["confirmed_by"] is None


def test_phrase_suggestions_skip_known_and_bad_phrases(rule_client):
    c, _, _ = rule_client
    c.post("/playbook/rules/personal_data/phrases", data={"phrase": "données à caractère personnel"})
    c.app.state.llm = FakeLlm([{"phrases": ["Données à caractère PERSONNEL", "ab", "transfert de données hors de Tunisie",
                                            "« fichiers clients »"]}])
    page = c.post("/playbook/rules/personal_data/suggest-phrases").text
    assert "transfert de données hors de Tunisie" in page and "« fichiers clients »" in html.unescape(page)
    assert html.unescape(page).count("« Données à caractère PERSONNEL »") == 0


def test_buttons_hidden_without_a_model(rule_client):
    c, _, _ = rule_client
    assert "Suggest other wordings" not in c.get("/playbook/rules/personal_data").text


def test_rule_suggestions_for_an_article_keep_only_known_keys(rule_client):
    c, text_id, _ = rule_client
    c.post("/playbook/rules", data={"key": "liability", "label": "Responsabilité"})
    article_id = next(a["id"] for a in c.get(f"/api/v1/texts/{text_id}").json()["articles"] if a["number"] == "2 bis")

    # One answer for the suggestion, then one focused check of all suggested keys.
    fake = FakeLlm([{"keys": ["personal_data", "made_up", "liability"]},
                    {"cite": {"personal_data": "yes", "liability": "yes"}}])
    c.app.state.llm = fake
    page = c.post(f"/articles/{article_id}/suggest-rules").text

    assert "Données personnelles" in page and "Responsabilité" in page
    assert "made_up" not in page
    assert "Article 2 bis" in fake.prompts[0] and "personal_data — Données personnelles" in fake.prompts[0]

    # Accepting a suggestion goes through the normal link form, so the rule still needs a person's confirmation.
    c.post(f"/articles/{article_id}/link-to-rule", data={"key": "liability"})
    rules = _pb(rule_client[2])["rules"]
    liability = next(r for r in rules if r["key"] == "liability")
    assert liability["articles"][0]["number"] == "2 bis" and liability["confirmed_by"] is None


def test_rule_suggestions_exclude_already_linked_rules(rule_client):
    c, text_id, _ = rule_client
    article_id = next(a["id"] for a in c.get(f"/api/v1/texts/{text_id}").json()["articles"] if a["number"] == "2 bis")
    c.post(f"/articles/{article_id}/link-to-rule", data={"key": "personal_data"})

    fake = FakeLlm([{"keys": ["personal_data"]}])
    c.app.state.llm = fake
    c.post(f"/articles/{article_id}/suggest-rules")
    assert "personal_data — Données personnelles" not in fake.prompts[0]


def test_rule_suggestions_send_each_risk_description():
    from app.services import assist

    class Spy:
        name = "spy"

        def complete_json(self, system, user):
            self.user = user
            return {"keys": []}

    spy = Spy()
    assist.suggest_rules(spy, "COC", "3", "Sont incapables de contracter les mineurs.",
                         [{"key": "contradiction", "label": "Contradictions", "description": "Erreurs internes au contrat."}])
    assert "contradiction — Contradictions : Erreurs internes au contrat." in spy.user


def test_rule_suggestions_carry_a_reason_and_the_review_context():
    from app.services import assist

    class Fake:
        name = "fake"

        def complete_json(self, system, user):
            if "You check proposed links" in system:
                return {"cite": {"penalty": "yes"}}
            self.system = system
            return {"risks": [{"key": "penalty", "why": "L'article fixe le régime de la clause pénale."}, {"key": "nope"}]}

    fake = Fake()
    out = assist.suggest_rules(fake, "COC", "1", "texte", [{"key": "penalty", "label": "Clause pénale", "description": "x"}])
    assert [(s.key, s.why) for s in out] == [("penalty", "L'article fixe le régime de la clause pénale.")]
    assert "LegalDesk" in fake.system and "capacity to contract" in fake.system


def test_a_suggestion_the_focused_check_rejects_is_dropped():
    from app.services import assist

    class Fake:
        name = "fake"

        def complete_json(self, system, user):
            return {"cite": {"liability": "no"}} if "You check proposed links" in system else {"risks": [{"key": "liability", "fit": "direct", "why": "x"}]}

    assert assist.suggest_rules(Fake(), "COC", "11", "Le mineur autorisé…", [{"key": "liability", "label": "Responsabilité"}]) == []
