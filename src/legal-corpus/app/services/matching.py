"""Meaning-based matching of contract clauses against the playbook's example clauses.

LegalDesk sends the clauses it has already split (this service runs on the client's own servers, like the rest of
Harvey); each sentence is compared with every example of every confirmed rule using a local sentence-embedding model.
A rule matches a clause when one of its sentences is close enough to one of the rule's examples, closer to that
example than to any of the rule's counter-examples, and clearly closer to this rule than to any other. The result is a
lead for a person to check, never a verdict.

With no semantic model installed the matcher is off and says so: hashed word vectors cannot measure meaning, and a
matcher that only pretends to would be worse than none."""

import hashlib
import re
import threading
from dataclasses import dataclass

import numpy as np

from app.services.embeddings import Embedder

MIN_SENTENCE_CHARS = 25
_SENTENCE_END = re.compile(r"(?<=[.;:!?])\s+")


@dataclass(frozen=True)
class RuleExamples:
    key: str
    label: str
    examples: list[str]
    counter_examples: list[str]


@dataclass(frozen=True)
class Match:
    reference: str
    rule_key: str
    label: str
    score: float
    sentence: str
    example: str


def sentences(text: str) -> list[str]:
    parts = [" ".join(p.split()) for p in _SENTENCE_END.split(text)]
    kept = [p for p in parts if len(p) >= MIN_SENTENCE_CHARS]
    return kept or ([" ".join(text.split())] if text.strip() else [])


class ExampleMatcher:
    def __init__(self, embedder: Embedder, threshold: float = 0.60, margin: float = 0.05) -> None:
        self.embedder = embedder
        self.threshold = threshold
        self.margin = margin
        self._cache: dict[str, np.ndarray] = {}
        self._lock = threading.Lock()

    def _vectors(self, texts: list[str]) -> np.ndarray:
        """Example vectors are cached by model and text, so a confirmed playbook is embedded once."""
        if not texts:
            return np.zeros((0, 1), dtype=np.float32)
        keys = [hashlib.sha256(f"{self.embedder.name}\n{t}".encode("utf-8")).hexdigest() for t in texts]
        with self._lock:
            missing = [(k, t) for k, t in zip(keys, texts) if k not in self._cache]
            if missing:
                for (k, _), vector in zip(missing, self.embedder.embed([t for _, t in missing])):
                    self._cache[k] = vector
            return np.vstack([self._cache[k] for k in keys])

    def classify(self, clauses: list[tuple[str, str]], rules: list[RuleExamples],
                 threshold: float | None = None) -> list[Match]:
        threshold = self.threshold if threshold is None else threshold
        rules = [r for r in rules if r.examples]
        if not rules or not clauses:
            return []
        prepared = [(r, self._vectors(r.examples), self._vectors(r.counter_examples)) for r in rules]

        matches = []
        for reference, text in clauses:
            parts = sentences(text)
            if not parts:
                continue
            vectors = self.embedder.embed(parts)
            scored = []
            for rule, examples, counters in prepared:
                similarity = vectors @ examples.T  # sentences × examples
                best_example = similarity.max(axis=1)
                # A sentence closer to a safe wording of the risk than to the risk itself does not count.
                if counters.shape[0] and counters.shape[1] == vectors.shape[1]:
                    best_counter = (vectors @ counters.T).max(axis=1)
                    best_example = np.where(best_example > best_counter, best_example, -1.0)
                i = int(best_example.argmax())
                score = float(best_example[i])
                j = int(similarity[i].argmax())
                scored.append((score, rule, parts[i], rule.examples[j]))
            scored.sort(key=lambda s: s[0], reverse=True)
            best = scored[0]
            runner_up = scored[1][0] if len(scored) > 1 else -1.0
            if best[0] >= threshold and best[0] - runner_up >= self.margin:
                matches.append(Match(reference, best[1].key, best[1].label, round(best[0], 4), best[2], best[3]))
        return matches


def load_semantic(model_name: str) -> Embedder | None:
    """The real model, or None when sentence-transformers is not installed."""
    try:
        from app.services.embeddings import SentenceTransformerEmbedder

        return SentenceTransformerEmbedder(model_name)
    except ImportError:
        return None
