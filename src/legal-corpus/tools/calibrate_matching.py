"""Chooses the matching threshold on real wording, with the real model.

Positives are clauses that carry a rule's risk in wording the keyword rules miss; negatives are clauses that must not
match anything. For each threshold the script prints precision (share of matches that are right) and recall (share of
positives found). Pick the lowest threshold that keeps precision at or above the target, and set it with
CORPUS_MATCH_THRESHOLD. Add every clause LegalDesk missed in a real review to the positives.

    python tools/calibrate_matching.py [cases.json] [--rules exported.json] [--target 0.9]
"""

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from app.config import load_settings  # noqa: E402
from app.services.matching import ExampleMatcher, RuleExamples, load_semantic  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("cases", nargs="?", default=str(ROOT / "tools" / "matching_cases.json"))
    parser.add_argument("--rules", default=None, help="a JSON export of the grid; by default it is read from Harvey's database")
    parser.add_argument("--target", type=float, default=0.9)
    args = parser.parse_args()

    embedder = load_semantic(load_settings().embedding_model)
    if embedder is None:
        sys.exit("sentence-transformers is not installed: pip install -r requirements-semantic.txt")
    cases = json.loads(Path(args.cases).read_text(encoding="utf-8"))
    if args.rules:
        book = json.loads(Path(args.rules).read_text(encoding="utf-8"))
    else:
        from app.db import make_session_factory
        from app.services import playbook

        with make_session_factory(load_settings().database_url)() as db:
            book = playbook.load(db)
    rules = [RuleExamples(r["key"], r.get("label") or r["key"], [e["text"] for e in r.get("examples", [])],
                          [e["text"] for e in r.get("counter_examples", [])])
             for r in book["rules"] if r.get("examples")]
    # Cases may bring their own examples, so the script also works before the playbook has any.
    for key, extra in cases.get("examples", {}).items():
        rule = next((r for r in rules if r.key == key), None)
        if rule is None:
            rules.append(RuleExamples(key, key, extra.get("examples", []), extra.get("counter_examples", [])))
        else:
            rule.examples.extend(extra.get("examples", []))
            rule.counter_examples.extend(extra.get("counter_examples", []))
    if not rules:
        sys.exit("No rule has example clauses yet.")

    matcher = ExampleMatcher(embedder, threshold=0.0, margin=load_settings().match_margin)
    positives = [(c["rule"], c["text"]) for c in cases.get("positives", [])]
    negatives = [c if isinstance(c, str) else c["text"] for c in cases.get("negatives", [])]
    clauses = [(f"p{i}", t) for i, (_, t) in enumerate(positives)] + [(f"n{i}", t) for i, t in enumerate(negatives)]
    found = {m.reference: m for m in matcher.classify(clauses, rules)}

    print(f"model {embedder.name} · {len(rules)} rules · {len(positives)} positives · {len(negatives)} negatives\n")
    for i, (rule, text) in enumerate(positives):
        m = found.get(f"p{i}")
        status = "ok " if m and m.rule_key == rule else "MISS" if not m else "WRONG"
        print(f"  {status} {m.score if m else 0:.3f} {rule:<18} {m.rule_key if m else '-':<18} {text[:70]}")
    for i, text in enumerate(negatives):
        m = found.get(f"n{i}")
        print(f"  neg  {m.score if m else 0:.3f} {'-':<18} {m.rule_key if m else '-':<18} {text[:70]}")

    print(f"\n  threshold  precision  recall")
    best = None
    for step in range(40, 91, 2):
        t = step / 100
        hits = {r: m for r, m in found.items() if m.score >= t}
        right = sum(1 for i, (rule, _) in enumerate(positives) if (m := hits.get(f"p{i}")) and m.rule_key == rule)
        precision = right / len(hits) if hits else 1.0
        recall = right / len(positives) if positives else 0.0
        if best is None and precision >= args.target:
            best = t
        print(f"  {t:9.2f}  {precision:9.2f}  {recall:6.2f}")
    print(f"\nLowest threshold with precision >= {args.target:.0%}: {best}")


if __name__ == "__main__":
    main()
