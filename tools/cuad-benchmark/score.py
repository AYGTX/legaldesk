"""Step 3 of the CUAD benchmark: compares the rule keys LegalDesk raised with the categories lawyers labelled.

Contract-level scoring: for a category, a contract is positive when lawyers highlighted at least one clause of that
category, and predicted positive when LegalDesk raised one of the rule keys mapped to it.

    python score.py contracts.jsonl predictions.jsonl [report.md]
"""

import json
import sys

# Only categories whose meaning matches a LegalDesk rule closely enough to compare. The other CUAD categories have no
# rule yet (see Harvey's coverage page) or a rule that asks a different question.
MAPPING = {
    "Uncapped Liability": ["liability"],
    "Renewal Term": ["auto_renewal"],
    "Exclusivity": ["exclusivity"],
    "Non-Compete": ["exclusivity"],
    "Liquidated Damages": ["penalty"],
    "Price Restrictions": ["resale_price"],
    "No-Solicit of Customers": ["market_sharing"],
}


def load(path: str) -> dict[str, dict]:
    return {row["title"]: row for row in (json.loads(line) for line in open(path, encoding="utf-8"))}


def main(contracts_path: str, predictions_path: str, report_path: str | None = None) -> None:
    contracts, predictions = load(contracts_path), load(predictions_path)
    lines = [
        "| CUAD category | LegalDesk rule | Contracts labelled | Raised | Correct | Precision | Recall | F1 |",
        "|---|---|---:|---:|---:|---:|---:|---:|",
    ]
    for category, keys in MAPPING.items():
        tp = fp = fn = 0
        for title, contract in contracts.items():
            # The dataset spells some labels differently from category_descriptions.csv ("No-Solicit Of Customers").
            actual = category.casefold() in {label.casefold() for label in contract["labels"]}
            raised = any(k in predictions.get(title, {}).get("rule_keys", []) for k in keys)
            tp += actual and raised
            fp += raised and not actual
            fn += actual and not raised
        precision = tp / (tp + fp) if tp + fp else 0.0
        recall = tp / (tp + fn) if tp + fn else 0.0
        f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
        lines.append(f"| {category} | {', '.join(keys)} | {tp + fn} | {tp + fp} | {tp} | {precision:.0%} | {recall:.0%} | {f1:.2f} |")
    table = "\n".join(lines)
    print(f"{len(contracts)} contracts\n\n{table}")
    if report_path:
        open(report_path, "w", encoding="utf-8").write(table + "\n")


if __name__ == "__main__":
    main(*sys.argv[1:4])
