from app.services import coverage


def test_the_41_cuad_categories_are_listed_once():
    names = [c.name for c in coverage.CATEGORIES]
    assert len(names) == 41 and len(set(names)) == 41
    assert names[0] == "Document Name" and names[-1] == "Third Party Beneficiary"


def test_status_follows_the_grid():
    rules = [{"key": "liability", "label": "Responsabilité", "confirmed_by": "Me"},
             {"key": "penalty", "label": "Pénalité", "confirmed_by": None}]
    rows = {r["name"]: r["status"] for r in coverage.report(rules)["rows"]}
    assert rows["Cap on Liability"] == "covered"
    assert rows["Liquidated Damages"] == "pending"
    assert rows["Agreement Date"] == "extracted"
    assert rows["Change of Control"] == "missing"


def test_page_renders(client):
    page = client.get("/coverage", headers={"accept-language": "fr"}).text
    assert "Couverture des types de clauses" in page and "Changement de contrôle" in page


def test_every_category_has_its_question_in_both_languages():
    assert set(coverage.QUESTIONS) == {c.name for c in coverage.CATEGORIES}
    assert all(en and fr for en, fr in coverage.QUESTIONS.values())
