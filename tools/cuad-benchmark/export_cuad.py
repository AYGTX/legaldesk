"""Step 1 of the CUAD benchmark: turns CUADv1.json (SQuAD format) into one line per contract with the categories
lawyers labelled in it.

    python export_cuad.py <path/to/CUADv1.json> contracts.jsonl
"""

import json
import sys


def main(source: str, target: str) -> None:
    data = json.load(open(source, encoding="utf-8"))["data"]
    with open(target, "w", encoding="utf-8") as out:
        for contract in data:
            paragraph = contract["paragraphs"][0]
            labels = sorted({qa["id"].split("__", 1)[1] for qa in paragraph["qas"] if qa["answers"]})
            out.write(json.dumps({"title": contract["title"], "text": paragraph["context"], "labels": labels},
                                 ensure_ascii=False) + "\n")
    print(f"{len(data)} contracts written to {target}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
