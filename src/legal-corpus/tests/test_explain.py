import json
from dataclasses import dataclass, field

from app.services.llm import NoLlm

DRAFT = {"summary": "Le traitement de vos données doit respecter votre dignité et votre vie privée.",
         "in_practice": "Une entreprise qui traite des données doit protéger la vie privée des personnes."}


@dataclass
class FakeLlm:
    answer: dict
    name: str = "fake"
    calls: list = field(default_factory=list)

    def complete_json(self, system, user):
        self.calls.append((system, user))
        return self.answer


def _article(text, number):
    return next(a for a in text["articles"] if a["number"] == number)


def _page(client, text_id):
    return client.get(f"/texts/{text_id}", headers={"accept-language": "fr"}).text


def test_draft_is_stored_seen_by_lawyers_only_until_approved(client, verified_text):
    fake = FakeLlm(DRAFT)
    client.app.state.llm = fake
    text_id, article = verified_text["id"], _article(verified_text, "2")

    r = client.post(f"/articles/{article['id']}/explanation/generate", data={"lang": "fr"}, follow_redirects=False)
    assert "flash=explanation_drafted" in r.headers["location"]
    system, user = fake.calls[0]
    assert "plain French" in system and "Loi test n° 1, article 2" in user

    page = _page(client, text_id)
    assert "Brouillon IA" in page and "vie privée" in page
    assert client.get(f"/api/v1/articles/{article['id']}/explanation").status_code == 404

    client.post(f"/articles/{article['id']}/explanation/approve", data={"lang": "fr", "approved_by": "Me Trabelsi"})
    served = client.get(f"/api/v1/articles/{article['id']}/explanation").json()
    assert served["approved_by"] == "Me Trabelsi" and served["model"] == "fake" and "dignité" in served["summary"]
    assert "Validée par Me Trabelsi" in _page(client, text_id)
    # Stored: no further model call to show it again.
    assert len(fake.calls) == 1


def test_a_changed_article_puts_its_explanation_out_of_date(client, verified_text):
    client.app.state.llm = FakeLlm(DRAFT)
    article = _article(verified_text, "2")
    client.post(f"/articles/{article['id']}/explanation/generate", data={"lang": "fr"})
    client.post(f"/articles/{article['id']}/explanation/approve", data={"lang": "fr", "approved_by": "Me"})

    client.put(f"/api/v1/articles/{article['id']}", json={"number": "2", "text": "Nouvelle rédaction de l'article deux sur les données."})

    assert client.get(f"/api/v1/articles/{article['id']}/explanation").status_code == 404
    assert "Le texte a changé" in _page(client, verified_text["id"])
    r = client.post(f"/articles/{article['id']}/explanation/approve", data={"lang": "fr", "approved_by": "Me"},
                    headers={"referer": f"http://testserver/texts/{verified_text['id']}"}, follow_redirects=False)
    assert "flash=err_explanation_out_of_date" in r.headers["location"]


def test_a_lawyer_can_correct_and_approve_in_one_step(client, verified_text):
    client.app.state.llm = NoLlm()
    article = _article(verified_text, "2")
    client.post(f"/articles/{article['id']}/explanation", data={
        "lang": "fr", "summary": "Toute personne a droit au respect de sa vie privée quand ses données sont traitées.",
        "in_practice": "", "approve_as": "H. Aymen"})
    served = client.get(f"/api/v1/articles/{article['id']}/explanation", params={"lang": "fr"}).json()
    assert served["approved_by"] == "H. Aymen" and served["model"] is None
    # Editing the approved text withdraws the approval.
    client.post(f"/articles/{article['id']}/explanation", data={
        "lang": "fr", "summary": "Une autre formulation de l'explication, plus courte mais complète.", "in_practice": ""})
    assert client.get(f"/api/v1/articles/{article['id']}/explanation").status_code == 404


def test_without_a_model_no_draft_button_and_a_clear_error(client, verified_text):
    client.app.state.llm = NoLlm()
    article = _article(verified_text, "2")
    assert "Expliquer simplement" not in _page(client, verified_text["id"])
    r = client.post(f"/articles/{article['id']}/explanation/generate", data={"lang": "fr"}, follow_redirects=False)
    assert "flash=explain_error" in r.headers["location"]


def test_approved_explanations_travel_with_the_playbook(tmp_path):
    from fastapi.testclient import TestClient

    from app.config import Settings
    from app.main import create_app
    from tests.conftest import LAW

    app = create_app(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key=None, legacy_playbook_file=None, seed_risks=False))
    with TestClient(app) as c:
        c.post("/login", data={"login": "admin", "password": "admin"})
        text = c.post("/api/v1/texts", json={"title": "Données", "reference": "Loi test n° 1", "jurisdiction": "Tunisia",
                                             "category": "DataProtection", "source_url": "https://example.org/1"}).json()
        c.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
        c.put(f"/api/v1/texts/{text['id']}/status", json={"status": "Published"})
        c.post(f"/api/v1/texts/{text['id']}/verify", json={"verified_by": "Me"})
        c.post("/playbook/rules", data={"key": "personal_data", "label": "Données"})
        c.post("/playbook/rules/personal_data/articles", data={"text_id": text["id"], "number": "2", "why": "x"})
        c.post("/playbook/rules/personal_data/confirm", data={"confirmed_by": "Me"})
        article = _article(c.get(f"/api/v1/texts/{text['id']}").json(), "2")
        c.post(f"/articles/{article['id']}/explanation", data={
            "lang": "fr", "summary": "Vos données doivent être traitées avec respect pour votre vie privée.", "approve_as": "Me"})

        served = c.get("/api/v1/playbook").json()["rules"]["personal_data"]["articles"][0]
        assert served["explanations"] == {"fr": "Vos données doivent être traitées avec respect pour votre vie privée."}
