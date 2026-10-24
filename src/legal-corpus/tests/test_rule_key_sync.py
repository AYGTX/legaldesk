"""Guards the one thing that actually links a SignFlow risk finding to its law: the bare string key. The C# rule
engine tags a finding with e.g. RuleKey: "indemnification"; this playbook attaches law to it only if a rule with
that same key exists in DETECTED_KEYS. Nothing checks the two stay in sync except this test — a typo on either
side would silently break the link with no error anywhere, so this reads the C# source directly rather than
trusting a second hand-copied list."""

import re
from pathlib import Path

import pytest

from app.services.playbook import DETECTED_KEYS

RULE_ENGINE_PATH = (
    Path(__file__).resolve().parents[2]
    / "backend" / "SignFlow.Application" / "Common" / "Services" / "ContractRuleEngine.cs"
)

# Every place ContractRuleEngine.cs tags a finding with a risk key: Finding(clause, "key", ...) or the RiskFinding
# record's RuleKey: "key" named argument.
_KEY_PATTERN = re.compile(r'Finding\(\w+,\s*"([a-z_]+)"|RuleKey:\s*"([a-z_]+)"')


def _keys_in_rule_engine() -> set[str]:
    text = RULE_ENGINE_PATH.read_text(encoding="utf-8")
    return {a or b for a, b in _KEY_PATTERN.findall(text)}


def test_every_key_the_rule_engine_emits_is_a_detected_key():
    if not RULE_ENGINE_PATH.exists():
        pytest.skip("SignFlow backend not checked out alongside legal-corpus.")
    engine_keys = _keys_in_rule_engine()
    assert engine_keys, "Found no risk keys in ContractRuleEngine.cs — the pattern above no longer matches its code."

    missing = engine_keys - DETECTED_KEYS.keys()
    assert not missing, (
        f"ContractRuleEngine.cs tags findings with {sorted(missing)}, but DETECTED_KEYS in playbook.py does not "
        "list them: a playbook rule with that key would never be offered as 'detected by LegalDesk', and the "
        "editor's warning for it would be silently wrong."
    )


def test_every_detected_key_is_still_emitted_by_the_rule_engine():
    """The reverse drift: a key DETECTED_KEYS still claims is wired up, but the rule engine no longer emits (renamed
    or removed) — a rule confirmed under that key would quietly stop attaching to anything."""
    if not RULE_ENGINE_PATH.exists():
        pytest.skip("SignFlow backend not checked out alongside legal-corpus.")
    engine_keys = _keys_in_rule_engine()

    stale = DETECTED_KEYS.keys() - engine_keys
    assert not stale, (
        f"DETECTED_KEYS claims {sorted(stale)} are detected by LegalDesk, but ContractRuleEngine.cs no longer "
        "tags any finding with them."
    )
