import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app

LAW = """LOI ORGANIQUE TEST
Article premier - Toute personne a le droit à la protection des données à caractère personnel.
Art. 2 - Le traitement des données personnelles doit respecter la dignité humaine et la vie privée.
Article 2 bis : Le responsable du traitement prend toutes les précautions utiles pour préserver la sécurité des données.
"""


ADMIN_EMAIL, ADMIN_PASSWORD = "lawyer@test.tn", "Test-Password-123"


def make_settings(**overrides) -> Settings:
    """A test install: in-memory database, one administrator lawyer (bootstrapped from these settings), two-factor
    and the four-eyes rule off unless a test turns them on."""
    values = dict(database_url="sqlite://", embedder="hashing", embedding_model="", api_key=None,
                  legacy_playbook_file=None, seed_risks=False, admin_user="Me Test", admin_email=ADMIN_EMAIL,
                  admin_password=ADMIN_PASSWORD, require_totp=False, four_eyes=False)
    values.update(overrides)
    return Settings(**values)


def sign_in(client, email=ADMIN_EMAIL, password=ADMIN_PASSWORD):
    response = client.post("/login", data={"login": email, "password": password}, follow_redirects=False)
    assert response.status_code == 303, response.text
    return response


@pytest.fixture
def client():
    app = create_app(make_settings())
    with TestClient(app) as c:
        sign_in(c)
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


def make_lawyer(db, email="me@test.tn", name="Me Test"):
    """A lawyer's account, for tests that call the services directly."""
    from app.models import Role
    from app.services import accounts

    return accounts.by_email(db, email) or accounts.create(db, email, name, Role.Lawyer, ADMIN_PASSWORD,
                                                           must_change_password=False)


def serve(client, *keys, severity=None):
    """Puts rules in service as a lawyer's approval would (a version 1 per key), without building their articles:
    for tests of the review, not of approval."""
    from app.models import PlaybookRule, RuleVersion

    with client.app.state.session_factory() as db:
        lawyer = make_lawyer(db)
        for key in keys:
            if db.get(PlaybookRule, key) is None:
                db.add(PlaybookRule(key=key, label=key))
            db.add(RuleVersion(rule_key=key, version=1, payload={"label": key, "severity": severity},
                               approved_by_user_id=lawyer.id, approved_by_name=lawyer.signature))
        db.commit()
