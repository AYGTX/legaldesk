from tests.conftest import make_text


def test_articles_are_paginated_and_links_land_on_the_right_page(client):
    text = make_text(client)
    raw = "\n".join(f"Article {n}\nTexte de l'article {n}, conforme à l'article {n - 1} de la présente loi." for n in range(1, 61))
    client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": raw})

    first = client.get(f"/texts/{text['id']}", headers={"accept-language": "fr"}).text
    assert "Articles 1 à 50 sur 60" in first and "conforme à l&#39;article 49 de" in first and "conforme à l&#39;article 50 de" not in first
    second = client.get(f"/texts/{text['id']}?page=2", headers={"accept-language": "fr"}).text
    assert "Articles 51 à 60 sur 60" in second and "conforme à l&#39;article 54 de" in second

    article = next(a for a in client.get(f"/api/v1/texts/{text['id']}").json()["articles"] if a["number"] == "55")
    response = client.get(f"/articles/{article['id']}/go", follow_redirects=False)
    assert response.headers["location"] == f"/texts/{text['id']}?page=2#art-{article['id']}"
