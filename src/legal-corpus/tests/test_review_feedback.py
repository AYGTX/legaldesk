"""Rework spec Sprint 4 (R-M8-4): a LegalDesk user who marks a finding "not applicable" sends it to Harvey with a
reason; it waits on the risk's page until a lawyer has looked at it. And the served grid says when each rule version
was approved, so LegalDesk can show it beside every finding (R-M8-11)."""

from tests.conftest import serve

REPORT = {"source_ref": "item-1", "rule_key": "liability", "rule_version": 1, "clause_reference": "Article 7",
          "quote": "Le prestataire n'est responsable d'aucun dommage, quelle qu'en soit la cause ou la nature.",
          "reason": "Clause négociée avec le client, plafond dans l'annexe."}


def test_a_report_is_stored_once_and_shown_on_the_risk_page_until_handled(client):
    serve(client, "liability")
    assert client.post("/api/v1/review-feedback", json=REPORT).status_code == 202
    assert client.post("/api/v1/review-feedback", json=REPORT).status_code == 202  # a retried delivery

    page = client.get("/playbook/rules/liability").text
    assert page.count("Clause négociée avec le client") == 1 and "Article 7" in page

    from app.services import feedback
    with client.app.state.session_factory() as db:
        [item] = feedback.open_for(db, "liability")
    client.post(f"/playbook/rules/liability/feedback/{item.id}/handled")
    assert "Clause négociée avec le client" not in client.get("/playbook/rules/liability").text


def test_a_report_needs_a_reason():
    from fastapi.testclient import TestClient

    from app.main import create_app
    from tests.conftest import make_settings

    with TestClient(create_app(make_settings())) as c:
        assert c.post("/api/v1/review-feedback", json={**REPORT, "reason": ""}).status_code == 422


def test_the_served_grid_says_who_approved_each_rule_and_when(client):
    serve(client, "liability")
    rule = client.get("/api/v1/playbook").json()["rules"]["liability"]
    assert rule["version"] == 1 and rule["approved_by"] and rule["approved_at"]
