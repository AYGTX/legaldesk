from tests.conftest import serve
from app.services import review
from app.services.matching import ExampleMatcher
from tests.test_matching import FakeEmbedder


# Every rule that owns a built-in check, approved (version 1): checks only run for rules in service.
IN_SERVICE = [{"key": k, "version": 1} for k in sorted({i.rule_key for i in review.CHECK_INFO.values()})]


def _keys(clauses, grid=None, matcher=None):
    findings, _ = review.review([review.Clause(r, t) for r, t in clauses], grid or IN_SERVICE, matcher)
    return [(f.rule_key, f.contract_reference) for f in findings], findings


BASE = [("Article 1 — Objet", "Le présent contrat a pour objet la fourniture de services par le Prestataire à la Société."),
        ("Article 2 — Résiliation", "Chacune des parties peut résilier le contrat moyennant un préavis de trois mois."),
        ("Article 3 — Force majeure", "Aucune partie n'est responsable d'un cas de force majeure.")]


def test_contradictory_payment_days_and_missing_reference():
    keys, findings = _keys(BASE + [
        ("Article 4 — Paiement", "Les factures sont payables à 30 jours. Le paiement intervient à 60 jours fin de mois."),
        ("Article 5 — Divers", "Les conditions de l'article 14 du présent contrat s'appliquent."),
    ])
    assert ("contradiction", "Article 4 — Paiement") in keys
    assert ("contradiction", "Article 5 — Divers") in keys
    assert "30 jours / 60 jours" in next(f.explanation for f in findings if f.contract_reference == "Article 4 — Paiement")


def test_missing_termination_and_force_majeure():
    keys, _ = _keys([("Article 1 — Objet", "Le présent contrat a pour objet la fourniture de services par le Prestataire.")])
    assert ("termination", None) in keys and ("force_majeure", None) in keys


def test_bank_records_kept_less_than_ten_years():
    keys, _ = _keys(BASE + [("Article 4 — Archives", "Le Prestataire conservera les dossiers des clients de la Banque pendant cinq (5) ans.")])
    assert ("bank_aml", "Article 4 — Archives") in keys


def test_english_contract_gets_english_findings():
    _, findings = _keys([("Article 1 — Scope", "This Agreement sets out the services the Supplier shall provide to the Customer.")])
    assert any(f.clause_type == "Termination rights" for f in findings)


def test_meaning_uses_risk_grid_examples_and_phrases():
    grid = [{"key": "data_transfer", "label": "Transfert de données", "phrases": ["prise en charge intégrale"],
             "examples": [{"text": "Le Prestataire peut transférer les données et fichiers des clients à l'étranger."}],
             "counter_examples": []}]
    keys, findings = _keys(BASE + [
        ("Article 4 — Hébergement", "Les fichiers et données des clients seront hébergés à l'étranger."),
        ("Article 5 — Frais", "Les frais font l'objet d'une prise en charge intégrale par la Société."),
    ], grid, ExampleMatcher(FakeEmbedder(), threshold=0.6))
    assert ("data_transfer", "Article 4 — Hébergement") in keys
    assert ("data_transfer", "Article 5 — Frais") in keys
    assert next(f for f in findings if f.contract_reference == "Article 4 — Hébergement").clause_type == "Transfert de données"


def test_review_endpoint_needs_a_semantic_model(client):
    client.app.state.matcher = None
    response = client.post("/api/v1/review", json={"clauses": [{"reference": "Article 1", "text": "Objet du contrat."}]})
    assert response.status_code == 503


def test_review_endpoint(client):
    serve(client, "termination", "force_majeure")
    client.app.state.matcher = ExampleMatcher(FakeEmbedder(), threshold=0.6)
    response = client.post("/api/v1/review", json={"clauses": [
        {"reference": "Article 1 — Objet", "text": "Le présent contrat a pour objet la fourniture de services par le Prestataire."}]})
    body = response.json()
    assert response.status_code == 200 and body["language"] == "fr" and body["model"] == "fake-axes"
    assert {f["rule_key"] for f in body["findings"]} >= {"termination", "force_majeure"}


def test_contradiction_is_never_matched_by_meaning():
    grid = [{"key": "contradiction", "label": "Contradictions",
             "examples": [{"text": "Le Prestataire peut transférer les données et fichiers des clients à l'étranger."}]}]
    assert "contradiction" not in {r.key for r in review.meaning_rules(grid)}
