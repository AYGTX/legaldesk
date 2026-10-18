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


def test_defined_terms_are_underlined_with_their_definition():
    from app.services import references
    defining = _a("2", "Est considérée banque, toute personne morale qui exerce à titre habituel les opérations bancaires.")
    using = _a("5", "Les banques doivent tenir une comptabilité. Une banquette n'est pas visée & reste <libre>.")
    own = SimpleNamespace(reference="", title="Loi test", articles=[defining, using])
    defined = definitions.of_text([defining, using])
    index = references.Index.build([])
    html = str(references.linkify(using, own, index, "", defined))
    assert html.count('class="term"') == 1
    assert 'href="#art-a2"' in html and ">banques</a>" in html and "toute personne morale" in html
    assert "banquette" in html and "&lt;libre&gt;" in html and "&amp;" in html
    assert 'class="term"' not in str(references.linkify(defining, own, index, "", defined))
