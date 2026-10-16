import json

from app.services import playbook
from tests.conftest import LAW, make_text


def _write(tmp_path, confirmed_by):
    path = tmp_path / "playbook.json"
    path.write_text(json.dumps({
        "rules": [{"key": "personal_data", "label": "Données", "confirmed_by": confirmed_by,
                   "articles": [{"reference": "Loi test n° 1", "number": "2 bis", "why": "Sécurité"},
                                {"reference": "Loi test n° 1", "number": "99", "why": "Absent"}]}],
        "regulations": {"2004-63": "personal_data"},
    }), encoding="utf-8")
    return path


def _db(client):
    return next(iter([client.app.state.session_factory()]))


def test_serves_only_confirmed_rules_with_verified_article_text(client, verified_text, tmp_path):
    db = _db(client)
    assert playbook.resolve(db, _write(tmp_path, None))["rules"] == {}

    result = playbook.resolve(db, _write(tmp_path, "H. Aymen"))
    articles = result["rules"]["personal_data"]["articles"]
    assert [a["citation"] for a in articles] == ["Loi test n° 1, art. 2 bis"]
    assert articles[0]["text"].startswith("Le responsable du traitement")
    assert result["unresolved"] == ["personal_data: Loi test n° 1, art. 99"]


def test_unverified_law_is_not_served(client, tmp_path):
    text = make_text(client)
    client.post(f"/api/v1/texts/{text['id']}/articles/import", json={"raw_text": LAW})

    result = playbook.resolve(_db(client), _write(tmp_path, "H. Aymen"))
    assert result["rules"]["personal_data"]["articles"] == []
