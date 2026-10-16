from app.services.splitter import split_articles


def test_splits_french_headings_and_keeps_preamble():
    result = split_articles("Préambule\nArticle premier - Un.\nArt. 2 : Deux.\nArticle 2 bis. Deux bis.\nARTICLE 3 Trois.")
    assert result.preamble == "Préambule"
    assert [(a.number, a.text) for a in result.articles] == [
        ("1", "Un."), ("2", "Deux."), ("2 bis", "Deux bis."), ("3", "Trois."),
    ]


def test_print_suffix_variants():
    result = split_articles("Article 13 (Modifié) Un.\nArticle 13 « quinter »(*) (Ajouté) Deux.\nArticle 13 sexis (Ajouté) Trois.")
    assert [a.number for a in result.articles] == ["13", "13 quinter", "13 sexis"]


def test_joins_print_wraps_and_keeps_paragraphs_and_lists():
    raw = (
        "Article 13.- Les sociétés sont tenues de désigner un\ncommissaire aux comptes.\n"
        "Toutefois, elles sont dispensées :\n- au titre du premier exercice,\n- si elles ne remplissent pas deux des\n"
        "limites chiffrées.\nCe commissaire est soumis au\nchapitre trois du présent code.\n"
        "/tatweel/tatweel/tatweel\n"
        "TITRE II\nDES REGLES PARTICULIERES\nCHAPITRE PREMIER\nDu gage\n"
        "Article 14 – Fin."
    )
    result = split_articles(raw)
    assert result.articles[0].text == (
        "Les sociétés sont tenues de désigner un commissaire aux comptes.\n"
        "Toutefois, elles sont dispensées :\n- au titre du premier exercice,\n"
        "- si elles ne remplissent pas deux des limites chiffrées.\n"
        "Ce commissaire est soumis au chapitre trois du présent code."
    )
    assert result.removed_headings == ["TITRE II DES REGLES PARTICULIERES CHAPITRE PREMIER Du gage"]
    assert result.articles[1].text == "Fin."


def test_splits_arabic_headings():
    result = split_articles("الفصل الأول - نص أول.\nالفصل 2 - نص ثان.")
    assert [a.number for a in result.articles] == ["1", "2"]


def test_mid_sentence_reference_is_not_a_heading():
    result = split_articles("Article 1 - Voir l'article 5 de la loi.\nArticle 2 - Fin.")
    assert [a.number for a in result.articles] == ["1", "2"]
    assert "article 5" in result.articles[0].text


def test_no_headings():
    assert split_articles("just text").articles == []
