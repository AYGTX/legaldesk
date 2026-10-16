import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app

LAW = """LOI ORGANIQUE TEST
Article premier - Toute personne a le droit à la protection des données à caractère personnel.
Art. 2 - Le traitement des données personnelles doit respecter la dignité humaine et la vie privée.
Article 2 bis : Le responsable du traitement prend toutes les précautions utiles pour préserver la sécurité des données.
"""


@pytest.fixture
def client():
    app = create_app(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key=None))
    with TestClient(app) as c:
        yield c


def make_text(client, **overrides):
    body = {
        "title": "Protection des données",
        "reference": "Loi test n° 1",
        "jurisdiction": "Tunisia",
        "category": "DataProtection",
        "language": "fr",
        "source_url": "https://example.org/jort/1",
        **overrides,
    }
    response = client.post("/api/v1/texts", json=body)
    assert response.status_code == 201, response.text
    return response.json()


@pytest.fixture
def verified_text(client):
    text = make_text(client)
    assert client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW}).status_code == 200
    client.put(f"/api/v1/texts/{text['id']}/status", json={"status": "Published"})
    assert client.post(f"/api/v1/texts/{text['id']}/verify", json={"verified_by": "Me Ben Ali"}).status_code == 200
    return client.get(f"/api/v1/texts/{text['id']}").json()
