from types import SimpleNamespace

from app.services import references as R


def _text(id, reference, articles):
    text = SimpleNamespace(id=id, reference=reference, title=reference, articles=[])
    for number, body in articles:
        text.articles.append(SimpleNamespace(id=f"{id}-{number}", number=number, text=body, legal_text_id=id))
    return text


BANK = _text("bank", "Loi n° 2016-48 du 11 juillet 2016", [
    ("43", "Sont considérées personnes ayant des liens avec la banque les actionnaires et les dirigeants."),
    ("62", "Toute convention avec les personnes ayant des liens au sens de l’article 43 de la présente loi est autorisée."),
    ("42", "Sans préjudice de la loi organique n° 2015-26 du 7 août 2015, relative à la lutte contre le terrorisme."),
    ("7", "Conformément à l'article 5 de la loi n° 94-89 du 26 juillet 1994 et aux articles 2 et 3 du code de commerce."),
    ("9", "(Alinéa 2 modifié par la loi n° 2019-47 du 29 mai 2019) La banque informe ses clients."),
])
AML = _text("aml", "Loi organique n° 2015-26 du 7 août 2015", [("1", "La présente loi vise à prévenir le blanchiment.")])
COMMERCE = _text("ccom", "Code de commerce (Imprimerie Officielle, version 2014)", [("2", "Est commerçant…"), ("3", "Sont actes de commerce…")])
CSC = _text("csc", "Code des sociétés commerciales (Loi n° 2000-93 du 3 novembre 2000)", [("1", "Le présent code…")])
ASSUR = _text("assur", "Code des assurances", [("36", "Il est interdit d'imposer un prix de revente minimum.")])
DECREE = _text("decree", "Décret n° 92-2257 du 31 décembre 1992",
               [("1", "Conformément à l'article 36 du code des assurances, les statuts sont fixés comme suit.")])
INDEX = R.Index.build([BANK, AML, COMMERCE, CSC, ASSUR, DECREE])


def _art(text, number):
    return next(a for a in text.articles if a.number == number)


def test_links_to_an_article_of_the_same_law():
    html = str(R.linkify(_art(BANK, "62"), BANK, INDEX))
    assert '<a class="ref" href="/articles/bank-43/go" title="art. 43">article 43 de la présente loi</a>' in html


def test_links_to_another_law_and_to_a_code_article():
    assert 'href="/texts/aml"' in str(R.linkify(_art(BANK, "42"), BANK, INDEX))
    html = str(R.linkify(_art(BANK, "7"), BANK, INDEX, "absent"))
    assert 'href="/articles/ccom-2/go" title="art. 2, 3"' in html
    assert '<span class="ref ref-missing" title="absent">article 5 de la loi n° 94-89 du 26 juillet 1994</span>' in html


def test_links_by_code_name_to_an_annexed_code_with_no_law_number():
    """A décret citing "le code des assurances" by name, not by law number, still resolves — the annexed code's own
    LegalText has no "n°" in its title (it's reproduced as an annex, not promulgated under its own number)."""
    html = str(R.linkify(_art(DECREE, "1"), DECREE, INDEX))
    assert 'href="/articles/assur-36/go" title="art. 36"' in html


def test_text_is_escaped():
    article = SimpleNamespace(id="x", number="1", text="<script>alert(1)</script> article 43", legal_text_id="bank")
    html = str(R.linkify(article, BANK, INDEX))
    assert "<script>" not in html and "&lt;script&gt;" in html


def test_missing_laws_skip_amendment_notes_and_codes_known_by_number():
    missing = R.missing_laws([BANK, AML, COMMERCE, CSC], INDEX)
    assert [m.key for m in missing] == ["94-89"]
    assert missing[0].cited_by == ["Loi n° 2016-48 du 11 juillet 2016, art. 7"]
    assert R.law_keys(CSC.reference) == ["Code des sociétés commerciales", "2000-93"]
