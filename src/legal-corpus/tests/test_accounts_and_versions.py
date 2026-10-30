"""Rework spec Sprint 3 (M11): accounts and roles, approvals tied to an account, the four-eyes rule, versioned rules
served only once approved, Critical severity, a mandatory API key, and migrations that keep every row."""

import sqlite3
import time

import pytest
from fastapi.testclient import TestClient

from app.config import Settings, check_service_settings
from app.main import create_app
from app.models import HarveyUser, Role
from app.services import accounts, playbook, review
from app.services.matching import ExampleMatcher
from tests.conftest import ADMIN_PASSWORD, LAW, make_settings, make_text, serve, sign_in
from tests.test_matching import FakeEmbedder

PASSWORD = "Another-Pass-42"


def _app(**overrides):
    return TestClient(create_app(make_settings(**overrides)))


def _add_user(client, email, role, **extra):
    with client.app.state.session_factory() as db:
        accounts.create(db, email, email.split("@")[0], role, PASSWORD, must_change_password=False, **extra)


def _verified_text(client):
    text = make_text(client)
    client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})
    client.put(f"/api/v1/texts/{text['id']}/status", json={"status": "Published"})
    client.post(f"/api/v1/texts/{text['id']}/verify", json={})
    return text


# ------------------------------------------------------------------ start-up

def test_harvey_refuses_to_start_without_an_api_key():
    with pytest.raises(RuntimeError, match="CORPUS_API_KEY"):
        check_service_settings(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key=None))
    with pytest.raises(RuntimeError):
        check_service_settings(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key="short"))
    check_service_settings(Settings(database_url="sqlite://", embedder="hashing", embedding_model="", api_key="k" * 32))


def test_the_default_admin_password_must_be_changed_at_first_sign_in():
    with _app(admin_password="admin") as c:
        c.post("/login", data={"login": "lawyer@test.tn", "password": "admin"})
        assert c.get("/playbook", follow_redirects=False).headers["location"] == "/account"
        c.post("/account/password", data={"current": "admin", "new": PASSWORD, "confirm": PASSWORD})
        assert c.get("/playbook", follow_redirects=False).status_code == 200


# ------------------------------------------------------------------ sign-in and roles

def test_five_wrong_passwords_lock_the_account():
    with _app() as c:
        for _ in range(5):
            c.post("/login", data={"login": "lawyer@test.tn", "password": "wrong-Pass-1"})
        c.post("/login", data={"login": "lawyer@test.tn", "password": ADMIN_PASSWORD})
        assert c.get("/", follow_redirects=False).status_code == 303  # still signed out


def test_a_reader_looks_and_changes_nothing(client):
    _add_user(client, "reader@test.tn", Role.Reader)
    with TestClient(client.app) as reader:
        sign_in(reader, "reader@test.tn", PASSWORD)
        assert reader.get("/playbook").status_code == 200
        assert reader.post("/playbook/rules", data={"key": "x", "label": "X"}).status_code == 403


def test_an_editor_edits_but_cannot_approve(client):
    text = _verified_text(client)
    _add_user(client, "editor@test.tn", Role.Editor)
    with TestClient(client.app) as editor:
        sign_in(editor, "editor@test.tn", PASSWORD)
        editor.post("/playbook/rules", data={"key": "liability", "label": "Resp."})
        editor.post("/playbook/rules/liability/articles", data={"text_id": text["id"], "number": "1", "why": "x"})
        r = editor.post("/playbook/rules/liability/confirm", headers={"referer": "http://testserver/playbook/rules/liability"},
                        follow_redirects=False)
        assert "flash=err_not_lawyer" in r.headers["location"]
        assert editor.post(f"/api/v1/texts/{text['id']}/verify", json={}).status_code == 403
    assert client.get("/api/v1/playbook").json()["rules"] == {}


def test_a_lawyer_must_set_up_two_factor_then_signs_in_with_a_code():
    with _app(require_totp=True) as c:
        sign_in(c)
        assert c.get("/playbook", follow_redirects=False).headers["location"] == "/account"
        c.post("/account/totp/start")
        with c.app.state.session_factory() as db:
            secret = accounts.by_email(db, "lawyer@test.tn").totp_secret
        c.post("/account/totp/confirm", data={"code": accounts.totp_code(secret, int(time.time() // 30))})
        assert c.get("/playbook", follow_redirects=False).status_code == 200

        c.post("/logout")
        page = c.post("/login", data={"login": "lawyer@test.tn", "password": ADMIN_PASSWORD}).text
        assert 'action="/login/code"' in page
        assert c.get("/playbook", follow_redirects=False).status_code == 303  # not in yet
        c.post("/login/code", data={"code": "000000" if accounts.totp_code(secret, int(time.time() // 30)) != "000000" else "111111"})
        assert c.get("/playbook", follow_redirects=False).status_code == 303
        c.post("/login/code", data={"code": accounts.totp_code(secret, int(time.time() // 30))})
        assert c.get("/playbook", follow_redirects=False).status_code == 200


def test_passwords_are_hashed_and_checked():
    stored = accounts.hash_password(PASSWORD)
    assert PASSWORD not in stored and stored.startswith("scrypt$")
    assert accounts.verify_password(PASSWORD, stored) and not accounts.verify_password("nope", stored)


# ------------------------------------------------------------------ approvals by account, versions, four-eyes

def test_verification_and_rule_approval_record_the_lawyers_account(client):
    text = _verified_text(client)
    with client.app.state.session_factory() as db:
        me = accounts.by_email(db, "lawyer@test.tn")
    assert client.get(f"/api/v1/texts/{text['id']}").json()["verified_by_user_id"] == me.id

    client.post("/playbook/rules", data={"key": "liability", "label": "Resp."})
    client.post("/playbook/rules/liability/articles", data={"text_id": text["id"], "number": "1", "why": "x"})
    client.post("/playbook/rules/liability/confirm")
    with client.app.state.session_factory() as db:
        [version] = playbook.versions_of(db, "liability")
    assert version.version == 1 and version.approved_by_user_id == me.id and version.approved_by_name == "Me Test"


def test_four_eyes_the_last_editor_cannot_approve():
    with _app(four_eyes=True) as c:
        sign_in(c)
        text = _verified_text(c)
        c.post("/playbook/rules", data={"key": "liability", "label": "Resp."})
        c.post("/playbook/rules/liability/articles", data={"text_id": text["id"], "number": "1", "why": "x"})
        r = c.post("/playbook/rules/liability/confirm", headers={"referer": "http://testserver/playbook/rules/liability"},
                   follow_redirects=False)
        assert "flash=err_four_eyes" in r.headers["location"]
        assert c.get("/api/v1/playbook").json()["rules"] == {}

        _add_user(c, "second@test.tn", Role.Lawyer)
        with TestClient(c.app) as second:
            sign_in(second, "second@test.tn", PASSWORD)
            second.post("/playbook/rules/liability/confirm")
        assert c.get("/api/v1/playbook").json()["rules"]["liability"]["approved_by"] == "second"


def test_withdrawing_takes_a_rule_out_of_reviews_until_approved_again(client):
    text = _verified_text(client)
    client.post("/playbook/rules", data={"key": "liability", "label": "Resp."})
    client.post("/playbook/rules/liability/articles", data={"text_id": text["id"], "number": "1", "why": "x"})
    client.post("/playbook/rules/liability/confirm")
    client.post("/playbook/rules/liability/unconfirm")
    assert client.get("/api/v1/playbook").json()["rules"] == {}
    client.post("/playbook/rules/liability/confirm")
    assert client.get("/api/v1/playbook").json()["rules"]["liability"]["version"] == 2


def test_the_rule_page_offers_approval_to_a_lawyer(client):
    client.post("/playbook/rules", data={"key": "liability", "label": "Resp."})
    page = client.get("/playbook/rules/liability", headers={"accept-language": "fr"}).text
    assert "Hors service" in page and "Gravité des constats" in page and "Critique" in page


# ------------------------------------------------------------------ reviews use approved versions only

CONTRACT = {"clauses": [{"reference": "Article 1 — Objet",
                         "text": "Le présent contrat a pour objet la fourniture de services par le Prestataire."}]}


def test_review_ignores_rules_without_an_approved_version_and_says_which_versions_it_used(client):
    client.app.state.matcher = ExampleMatcher(FakeEmbedder(), threshold=0.6)
    assert client.post("/api/v1/review", json=CONTRACT).json()["findings"] == []

    serve(client, "termination")
    body = client.post("/api/v1/review", json=CONTRACT).json()
    [finding] = [f for f in body["findings"] if f["rule_key"] == "termination"]
    assert finding["rule_version"] == 1 and finding["detection"] == "CodeCheck"
    assert body["grid_version"] and body["grid_version"] == playbook.grid_version([{"key": "termination", "version": 1}])


def test_a_lawyers_critical_severity_and_an_organisations_overrides_apply(client):
    client.app.state.matcher = ExampleMatcher(FakeEmbedder(), threshold=0.6)
    serve(client, "termination", "force_majeure")
    with client.app.state.session_factory() as db:
        playbook.set_check(db, "termination", "missing_termination", True, "Critical")
    # set_check edits the working copy; approve it as version 2 for the review to use it.
    with client.app.state.session_factory() as db:
        rule = next(r for r in playbook.load(db)["rules"] if r["key"] == "termination")
        from app.models import RuleVersion
        lawyer = db.query(HarveyUser).first()
        db.add(RuleVersion(rule_key="termination", version=2, payload={"label": "termination", "checks": rule["checks"]},
                           approved_by_user_id=lawyer.id, approved_by_name=lawyer.signature))
        db.commit()

    findings = client.post("/api/v1/review", json=CONTRACT).json()["findings"]
    assert findings[0]["rule_key"] == "termination" and findings[0]["severity"] == "Critical"  # Critical sorts first

    overridden = client.post("/api/v1/review", json={**CONTRACT, "disabled_rules": ["force_majeure"],
                                                     "severity_overrides": {"termination": "Low"}}).json()["findings"]
    assert {f["rule_key"] for f in overridden} == {"termination"} and overridden[0]["severity"] == "Low"


def test_meaning_findings_carry_the_quote_and_the_confidence():
    grid = [{"key": "data_transfer", "label": "Transfert", "version": 3, "severity": "High",
             "examples": [{"text": "Le Prestataire peut transférer les données des clients à l'étranger sans accord."}]}]
    clauses = [review.Clause("Article 9", "Le Prestataire peut transférer les données des clients à l'étranger sans accord.")]
    findings, _ = review.review(clauses, grid, ExampleMatcher(FakeEmbedder(), threshold=0.6))
    [f] = [f for f in findings if f.rule_key == "data_transfer"]
    assert f.detection == "MeaningMatch" and f.rule_version == 3 and f.confidence and f.quote and f.severity == "High"


# ------------------------------------------------------------------ migrations keep every row

def test_migrating_an_old_database_keeps_every_article(tmp_path):
    """Regression: SQLite rebuilds legal_texts to add a foreign key; with foreign keys on, dropping the old table
    cascaded and deleted every article. The runner turns them off during migrations and checks integrity after."""
    from app.db import make_session_factory

    path = tmp_path / "old.db"
    with sqlite3.connect(path) as con:
        con.execute("CREATE TABLE legal_texts (id VARCHAR(36) PRIMARY KEY, title VARCHAR(300) NOT NULL, reference VARCHAR(300), "
                    "summary TEXT NOT NULL DEFAULT '', keywords TEXT NOT NULL DEFAULT '', language VARCHAR(5) NOT NULL DEFAULT 'fr', "
                    "jurisdiction VARCHAR(8) NOT NULL, category VARCHAR(14) NOT NULL, status VARCHAR(9) NOT NULL, "
                    "effective_date DATE, source_url VARCHAR(1000), verified_by VARCHAR(200), verified_at DATETIME, "
                    "created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL)")
        con.execute("CREATE TABLE articles (id VARCHAR(36) PRIMARY KEY, legal_text_id VARCHAR(36) REFERENCES legal_texts(id) ON DELETE CASCADE, "
                    "number VARCHAR(50), text TEXT, \"order\" INTEGER, embedding TEXT, embedding_model VARCHAR(200), "
                    "created_at DATETIME, updated_at DATETIME)")
        con.execute("INSERT INTO legal_texts VALUES ('t', 'Loi', 'Loi 1', '', '', 'fr', 'Tunisia', 'Other', 'Published', "
                    "NULL, NULL, 'Me Ancien', '2026-01-01', '2026-01-01', '2026-01-01')")
        con.executemany("INSERT INTO articles (id, legal_text_id, number, text) VALUES (?, 't', ?, 'x')",
                        [(f"a{i}", str(i)) for i in range(50)])

    make_session_factory(f"sqlite:///{path}")

    with sqlite3.connect(path) as con:
        assert con.execute("SELECT COUNT(*) FROM articles").fetchone() == (50,)
        assert con.execute("SELECT verified_by, verified_by_user_id FROM legal_texts").fetchone() == ("Me Ancien", None)
        assert con.execute("SELECT version_num FROM alembic_version").fetchone() == ("0002_accounts_rule_versions",)
        assert con.execute("PRAGMA foreign_key_check").fetchall() == []
