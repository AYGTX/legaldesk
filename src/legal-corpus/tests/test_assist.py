import html
import json
from dataclasses import dataclass, field
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from tests.conftest import LAW


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


@pytest.fixture
def rule_client(tmp_path):
    path = tmp_path / "playbook.json"
    path.write_text(json.dumps({"rules": [], "regulations": {}}), encoding="utf-8")
    app = create_app(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key=None, playbook_path=path))
    with TestClient(app) as c:
        c.post("/login", data={"login": "admin", "password": "admin"})
        text = c.post("/api/v1/texts", json={"title": "Données", "reference": "Loi test n° 1", "jurisdiction": "Tunisia",
                                             "category": "DataProtection", "source_url": "https://example.org/1"}).json()
        c.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
        c.put(f"/api/v1/texts/{text['id']}/status", json={"status": "Published"})
        c.post(f"/api/v1/texts/{text['id']}/verify", json={"verified_by": "Me"})
        c.post("/playbook/rules", data={"key": "personal_data", "label": "Données personnelles"})
        yield c, text["id"], path


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
    rule = json.loads(Path(rule_client[2]).read_text(encoding="utf-8"))["rules"][0]
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
