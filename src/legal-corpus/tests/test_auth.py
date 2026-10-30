from tests.conftest import ADMIN_EMAIL, ADMIN_PASSWORD, sign_in, make_settings
from fastapi.testclient import TestClient

from app.auth import make_session, read_session
from app.main import create_app


def _anonymous(**overrides):
    return TestClient(create_app(make_settings(**overrides)))


def test_pages_need_sign_in_but_api_does_not():
    with _anonymous() as c:
        response = c.get("/playbook", follow_redirects=False)
        assert response.status_code == 303 and response.headers["location"] == "/login?next=/playbook"
        assert c.get("/api/v1/texts").status_code == 200


def test_sign_in_with_email_and_return_to_page():
    with _anonymous() as c:
        assert "login-card" in c.post("/login", data={"login": ADMIN_EMAIL, "password": "nope"}).text
        response = c.post("/login", data={"login": ADMIN_EMAIL.upper(), "password": ADMIN_PASSWORD, "next": "/playbook"},
                          follow_redirects=False)
        assert response.headers["location"] == "/playbook"
        assert "Quick access" in c.get("/").text

        c.post("/logout")
        assert c.get("/", follow_redirects=False).status_code == 303


def test_next_cannot_leave_the_site():
    with _anonymous() as c:
        response = c.post("/login", data={"login": ADMIN_EMAIL, "password": ADMIN_PASSWORD, "next": "//evil.example"},
                          follow_redirects=False)
        assert response.headers["location"] == "/"


def test_session_token_is_signed_and_expires():
    token = make_session("k", "user-1", now=1000)
    assert read_session("k", token, now=1000) == ("user-1", "full")
    assert read_session("other", token, now=1000) is None
    assert read_session("k", token, now=1000 + 13 * 3600) is None
    assert read_session("k", token.replace("full", "password"), now=1000) is None


def test_dashboard_lists_what_needs_attention(client):
    client.post("/texts/new", data={"title": "Loi brouillon", "jurisdiction": "Tunisia", "category": "Other", "language": "fr"})
    page = client.get("/", headers={"accept-language": "fr"}).text
    assert "Accès rapide" in page and "À vérifier" in page and "Loi brouillon" in page
