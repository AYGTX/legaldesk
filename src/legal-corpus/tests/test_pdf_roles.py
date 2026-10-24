from app.services import pdf

DECREE = pdf.Candidate("Décret du 15 décembre 1906, portant promulgation du code tunisien des obligations et des contrats", "", "Article unique. Le code est promulgué.", 1)
AMEND = pdf.Candidate("Loi n° 2005-87 du 15 août 2005, portant approbation de la réorganisation de certaines dispositions du code", "Loi n° 2005-87", "Article premier. Est approuvée…", 2)
CODE = pdf.Candidate("Code des obligations et des contrats", "", "Article 1. Les obligations dérivent… Article 2. …", 1200)


def test_roles_are_guessed_from_titles():
    assert pdf.guess_roles([DECREE, AMEND, CODE]) == ["promulgation", "amendment", "main"]


def test_combined_text_takes_its_reference_from_the_promulgation_act():
    main, reference, summary = pdf.combine([DECREE, AMEND, CODE], ["promulgation", "amendment", "main"])
    assert main is CODE
    assert reference == "Code des obligations et des contrats (Décret du 15 décembre 1906)"
    assert summary.startswith("Version consolidée, intégrant : Loi n° 2005-87")


def test_roles_form_needs_exactly_one_main_text(client):
    data = {"count": "2", "filename": "coc.pdf"}
    for i, c in enumerate([DECREE, CODE]):
        data |= {f"title_{i}": c.title, f"reference_{i}": c.reference, f"raw_text_{i}": c.raw_text,
                 f"articles_{i}": str(c.article_count), f"role_{i}": "promulgation" if i == 0 else "ignore"}
    page = client.post("/import-pdf/roles", data=data, headers={"accept-language": "fr"}).text
    assert "Choisissez un seul texte principal" in page

    data["role_1"] = "main"
    page = client.post("/import-pdf/roles", data=data, headers={"accept-language": "fr"}).text
    assert "Code des obligations et des contrats (Décret du 15 décembre 1906)" in page
