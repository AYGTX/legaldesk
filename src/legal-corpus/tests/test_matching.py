from tests.conftest import LAW, sign_in, make_settings
import json

import numpy as np
import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app
from app.services.matching import ExampleMatcher, RuleExamples, sentences

# Each axis stands for one meaning; a text points along the axes of the words it contains.
AXES = {
    "indemnity": ("conséquences", "intégralité", "supportera", "indemnis", "aucun plafond"),
    "data": ("données", "étranger", "fichiers"),
    "capped": ("limitée", "plafonnée", "dans la limite"),
}


class FakeEmbedder:
    name = "fake-axes"

    def embed(self, texts):
        rows = []
        for text in texts:
            low = text.lower()
            v = np.array([sum(w in low for w in words) for words in AXES.values()] + [0.2], dtype=np.float32)
            rows.append(v / np.linalg.norm(v))
        return np.vstack(rows)


RULES = [
    RuleExamples("indemnification", "Indemnisation",
                 ["Le Client supportera seul l'intégralité des conséquences de tout sinistre."],
                 ["La garantie est limitée et plafonnée au montant annuel du contrat."]),
    RuleExamples("personal_data", "Données personnelles",
                 ["Le Prestataire peut transférer les données et fichiers des clients à l'étranger."], []),
]


def test_sentences_split_on_punctuation_and_drop_fragments():
    assert sentences("ARTICLE 5. Le Client supportera seul les conséquences du sinistre ; il renonce à tout recours.") == [
        "Le Client supportera seul les conséquences du sinistre ;", "il renonce à tout recours."]


def test_a_paraphrase_matches_its_rule_on_the_right_sentence():
    matcher = ExampleMatcher(FakeEmbedder(), threshold=0.6)
    clause = "Les parties conviennent de ce qui suit. La Banque prendra en charge l'intégralité des conséquences financières."
    [match] = matcher.classify([("Article 5 — Charges", clause)], RULES)
    assert match.rule_key == "indemnification" and match.reference == "Article 5 — Charges"
    assert "intégralité des conséquences" in match.sentence and match.score >= 0.6


def test_closer_to_a_counter_example_means_no_match():
    matcher = ExampleMatcher(FakeEmbedder(), threshold=0.5)
    clause = "Les conséquences sont limitées et plafonnées dans la limite du prix annuel."
    assert matcher.classify([("Article 6", clause)], RULES) == []


def test_below_threshold_or_ambiguous_means_no_match():
    matcher = ExampleMatcher(FakeEmbedder(), threshold=0.95)
    assert matcher.classify([("Article 1", "Le Prestataire livre le matériel au siège du Client.")], RULES) == []
    # Equally close to both rules: no margin, no match.
    ambiguous = ExampleMatcher(FakeEmbedder(), threshold=0.3, margin=0.05)
    clause = "Il supportera les conséquences liées aux données envoyées à l'étranger."
    assert ambiguous.classify([("Article 2", clause)], RULES) == []


def test_example_vectors_are_embedded_once():
    class Counting(FakeEmbedder):
        calls = 0

        def embed(self, texts):
            Counting.calls += len(texts)
            return super().embed(texts)

    matcher = ExampleMatcher(Counting(), threshold=0.6)
    for _ in range(3):
        matcher.classify([("A", "Le Client supportera l'intégralité des conséquences du sinistre.")], RULES)
    # 3 example/counter texts once, plus one sentence per call.
    assert Counting.calls == 3 + 3


@pytest.fixture
def api(tmp_path):
    app = create_app(make_settings(api_key=None,
                              legacy_playbook_file=None, seed_risks=False, match_threshold=0.6))
    with TestClient(app) as c:
        sign_in(c)
        text = c.post("/api/v1/texts", json={"title": "Données", "reference": "Loi test n° 1", "jurisdiction": "Tunisia",
                                             "category": "DataProtection", "source_url": "https://example.org/1"}).json()
        c.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
        c.put(f"/api/v1/texts/{text['id']}/status", json={"status": "Published"})
        c.post(f"/api/v1/texts/{text['id']}/verify", json={"verified_by": "Me"})
        c.post("/playbook/rules", data={"key": "indemnification", "label": "Indemnisation"})
        c.post("/playbook/rules/indemnification/articles", data={"text_id": text["id"], "number": "1", "why": "x"})
        c.post("/playbook/rules/indemnification/examples/examples",
               data={"text": "Le Client supportera seul l'intégralité des conséquences de tout sinistre."})
        yield c


def test_classify_uses_confirmed_rules_only(api):
    api.app.state.matcher_embedder = FakeEmbedder()
    body = {"clauses": [{"reference": "Article 5", "text": "La Banque prendra en charge l'intégralité des conséquences."}]}

    assert api.post("/api/v1/classify", json=body).json()["matches"] == []

    api.post("/playbook/rules/indemnification/confirm", data={})
    result = api.post("/api/v1/classify", json=body).json()
    assert result["enabled"] and result["model"] == "fake-axes"
    assert [m["rule_key"] for m in result["matches"]] == ["indemnification"]
    assert api.get("/api/v1/playbook").json()["rules"]["indemnification"]["examples"] == [
        "Le Client supportera seul l'intégralité des conséquences de tout sinistre."]


def test_classify_reports_disabled_without_a_semantic_model(api, monkeypatch):
    monkeypatch.setattr("app.services.matching.load_semantic", lambda name: None)
    result = api.post("/api/v1/classify", json={"clauses": [{"reference": "A", "text": "x" * 40}]}).json()
    assert result == {"enabled": False, "model": None, "matches": []}


def test_editing_examples_needs_a_new_approval_and_rejects_bad_input(api):
    api.post("/playbook/rules/indemnification/confirm", data={})
    api.post("/playbook/rules/indemnification/examples/counter_examples",
             data={"text": "La garantie est plafonnée au montant annuel du contrat signé."})
    # The new counter-example waits for an approval; version 1, as approved, stays in service.
    served = api.get("/api/v1/playbook").json()["rules"]["indemnification"]
    assert served["version"] == 1
    assert "La garantie est plafonnée au montant annuel du contrat signé." not in served["counter_examples"]
    r = api.post("/playbook/rules/indemnification/examples/examples", data={"text": "trop court"},
                 headers={"referer": "http://testserver/playbook/rules/indemnification"}, follow_redirects=False)
    assert "flash=err_example_length" in r.headers["location"]
