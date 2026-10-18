from types import SimpleNamespace

from app.services import definitions


def _a(number, text):
    return SimpleNamespace(id=f"a{number}", number=number, text=text)


def test_list_single_and_deemed_definitions():
    found = definitions.of_text([
        _a("2", "Au sens de la présente loi, on entend par : - Echanges électroniques : les échanges qui s'effectuent "
                "en utilisant des documents électroniques. - Commerce électronique : les opérations commerciales qui "
                "s'effectuent à travers les échanges électroniques."),
        _a("4", "Au sens de la présente loi, on entend par données à caractère personnel toutes les informations qui "
                "permettent d'identifier une personne physique."),
        _a("7", "Est considérée opération de leasing au sens de la présente loi, l'opération de location assortie "
                "d'une option d'achat."),
        _a("9", "Est considérée nulle toute clause exonératoire de responsabilité contraire au présent article."),
    ])
    terms = {d.term: (d.article_number, d.definition) for d in found}
    assert set(terms) == {"Echanges électroniques", "Commerce électronique", "Données à caractère personnel",
                          "Opération de leasing"}
    assert terms["Opération de leasing"][1].startswith("l'opération de location")
    assert terms["Données à caractère personnel"][0] == "4"


def test_definitions_panel_and_mcp_tool(client, verified_text, monkeypatch):
    page = client.get(f"/texts/{verified_text['id']}", headers={"accept-language": "fr"}).text
    assert "Définitions" not in page  # the sample law defines nothing
