from dataclasses import dataclass

from app.services.llm import GroqLlm, NoLlm, make_provider

AMENDING = """Loi n° 2099-7 du 1er mars 2099, modifiant la loi test n° 1.
Article premier - L'article 2 de la loi test n° 1 est modifié comme suit :
« Article 2 - Le traitement des données personnelles doit respecter la dignité humaine, la vie privée et la transparence. »
Art. 2 - Il est ajouté à la loi test n° 1 un article 3 ainsi conçu :
« Article 3 - Le responsable du traitement informe la personne concernée de toute violation de ses données. »
Art. 3 - L'article 2 bis de la loi test n° 1 est abrogé."""

NEW_2 = "Le traitement des données personnelles doit respecter la dignité humaine, la vie privée et la transparence."
NEW_3 = "Le responsable du traitement informe la personne concernée de toute violation de ses données."


@dataclass
class FakeLlm:
    answer: dict
    name: str = "fake"

    def complete_json(self, system, user):
        self.last_user = user
        return self.answer


def _use(client, answer):
    fake = FakeLlm(answer)
    client.app.state.llm = fake
    return fake


def test_proposals_are_checked_and_applied_as_versions(client, verified_text):
    fake = _use(client, {"changes": [
        {"article": "2", "action": "replace", "new_text": f"« {NEW_2} »"},
        {"article": "3", "action": "add", "new_text": NEW_3},
        {"article": "2 bis", "action": "repeal", "new_text": ""},
        # Invented wording, a missing article and an existing one: all dropped.
        {"article": "1", "action": "replace", "new_text": "Toute personne a droit à une indemnité forfaitaire de mille dinars."},
        {"article": "9", "action": "repeal"},
        {"article": "1", "action": "add", "new_text": NEW_3},
    ]})
    text_id = verified_text["id"]

    page = client.post(f"/texts/{text_id}/amendments", data={"amending_text": AMENDING, "applies_from": "2099-03-01",
                                                             "note": "Modifié par la loi n° 2099-7"},
                       headers={"accept-language": "fr"}).text
    assert "Loi test n° 1" in fake.last_user and "2 bis" in fake.last_user
    assert "3 modification(s) proposée(s) ont échoué" in page
    assert page.count('name="accept"') == 3

    changes = page.split('name="changes_json" value="')[1].split('"')[0]
    import html
    response = client.post(f"/texts/{text_id}/amendments/apply", data={
        "changes_json": html.unescape(changes), "accept": ["0", "1", "2"], "applies_from": "2099-03-01",
        "note": "Modifié par la loi n° 2099-7"}, follow_redirects=False)
    assert "flash=amendments_applied" in response.headers["location"] and "count=3" in response.headers["location"]

    text = client.get(f"/api/v1/texts/{text_id}").json()
    articles = {a["number"]: a for a in text["articles"]}
    assert articles["2"]["text"] == NEW_2 and articles["2"]["applies_from"] == "2099-03-01"
    assert articles["3"]["text"] == NEW_3 and articles["2 bis"]["text"] == "(Abrogé)"
    assert text["verified_by"] is None
    history = client.get(f"/api/v1/articles/{articles['2']['id']}/versions").json()
    assert history[0]["note"] == "Modifié par la loi n° 2099-7" and "vie privée" in history[0]["text"]
    # Before the amendment took effect, the law still reads as it did.
    before = {a["number"]: a for a in client.get(f"/api/v1/texts/{text_id}", params={"as_of": "2099-02-28"}).json()["articles"]}
    assert "transparence" not in before["2"]["text"] and "3" not in before


def test_only_ticked_changes_are_applied(client, verified_text):
    _use(client, {"changes": [{"article": "2", "action": "replace", "new_text": NEW_2},
                              {"article": "3", "action": "add", "new_text": NEW_3}]})
    text_id = verified_text["id"]
    page = client.post(f"/texts/{text_id}/amendments", data={"amending_text": AMENDING}).text
    import html
    changes = html.unescape(page.split('name="changes_json" value="')[1].split('"')[0])
    client.post(f"/texts/{text_id}/amendments/apply", data={"changes_json": changes, "accept": ["1"]})
    numbers = [a["number"] for a in client.get(f"/api/v1/texts/{text_id}").json()["articles"]]
    assert "3" in numbers
    assert client.get(f"/api/v1/texts/{text_id}").json()["articles"][1]["text"] != NEW_2


def test_without_a_model_the_page_asks_to_retry(client, verified_text):
    client.app.state.llm = NoLlm()
    page = client.post(f"/texts/{verified_text['id']}/amendments", data={"amending_text": AMENDING},
                       headers={"accept-language": "fr"}).text
    assert "pas répondu cette fois" in page


def test_provider_switch():
    class S:
        ai_provider, groq_api_key, groq_model = "groq", "k", "m"
        local_llm_url, local_llm_model = "http://x", "q"
    assert isinstance(make_provider(S), GroqLlm)
    S.groq_api_key = None
    assert isinstance(make_provider(S), NoLlm)
    S.ai_provider = "none"
    assert isinstance(make_provider(S), NoLlm)
