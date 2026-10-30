import json

from app.services import playbook
from tests.conftest import LAW, make_text


RULE = {"key": "personal_data", "label": "Données",
        "articles": [{"reference": "Loi test n° 1", "number": "2 bis", "why": "Sécurité"},
                     {"reference": "Loi test n° 1", "number": "99", "why": "Absent"}]}


def _write(db, approved: bool):
    """The rule in the working copy and, when approved, as version 1 (written directly: approve() would refuse the
    missing article 99, which is what this test is about)."""
    from app.models import RuleVersion
    from tests.conftest import make_lawyer

    playbook.save(db, {"rules": [dict(RULE)]})
    if approved:
        lawyer = make_lawyer(db)
        db.add(RuleVersion(rule_key="personal_data", version=1, payload={k: v for k, v in RULE.items() if k != "key"},
                           approved_by_user_id=lawyer.id, approved_by_name=lawyer.signature))
        db.commit()
    return db


def _db(client):
    return next(iter([client.app.state.session_factory()]))


def test_serves_only_confirmed_rules_with_verified_article_text(client, verified_text, tmp_path):
    db = _db(client)
    assert playbook.resolve(_write(db, approved=False))["rules"] == {}

    result = playbook.resolve(_write(db, approved=True))
    assert result["rules"]["personal_data"]["version"] == 1
    articles = result["rules"]["personal_data"]["articles"]
    assert [a["citation"] for a in articles] == ["Loi test n° 1, art. 2 bis"]
    assert articles[0]["text"].startswith("Le responsable du traitement")
    assert result["unresolved"] == ["personal_data: Loi test n° 1, art. 99"]
    assert result["regulations"] == {}  # kept only so LegalDesk's client can still read the field


def test_unverified_law_is_not_served(client, tmp_path):
    text = make_text(client)
    client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})

    result = playbook.resolve(_write(_db(client), approved=True))
    assert result["rules"]["personal_data"]["articles"] == []


def test_grid_starts_with_one_empty_risk_per_detected_risk(client):
    db = _db(client)
    playbook.bootstrap(db)
    rules = playbook.load(db)["rules"]
    assert [r["key"] for r in rules] == list(playbook.DETECTED_KEYS)
    assert all(r["articles"] == [] and r["confirmed_by"] is None for r in rules)
    playbook.bootstrap(db)  # a grid that has rules is never touched again
    assert len(playbook.load(db)["rules"]) == len(playbook.DETECTED_KEYS)


def test_old_playbook_file_is_imported_once(client, tmp_path):
    old = tmp_path / "playbook.json"
    old.write_text(json.dumps({"rules": [{"key": "payment", "label": "Prix", "articles": [
        {"reference": "Loi test n° 1", "number": "2 bis", "why": "Sécurité"}], "phrases": ["paiement à 90 jours"]}],
        "regulations": {"x": "y"}}), encoding="utf-8")
    db = _db(client)
    playbook.bootstrap(db, old)
    rule = playbook.load(db)["rules"][0]
    assert rule["key"] == "payment" and rule["articles"][0]["number"] == "2 bis" and rule["phrases"] == ["paiement à 90 jours"]
    assert not old.exists() and (tmp_path / "playbook.json.imported").exists()
