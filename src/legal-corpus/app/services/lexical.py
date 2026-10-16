"""BM25 keyword ranking: no model, no download. Rare legal terms ("majeure", "résiliation") weigh far more than
the filler words every article shares, which is what a plain bag-of-words similarity gets wrong."""

import math
import re
from collections import Counter

from app.services.textnorm import fold_for_search

K1 = 1.5
B = 0.75

_STOPWORDS = set("""
a au aux avec ce ces cet cette dans de des du elle elles en entre est et etre il ils je la le les leur leurs lui ma
mais me meme mes ne ni nos notre nous on ou par pas pour qu que qui sa se ses son sont sur ta te tes toi ton tu un une
vos votre vous y ont ete sera seront peut peuvent doit doivent ainsi lorsque lorsqu dont tout tous toute toutes autre
autres sans sous selon apres avant cas si non plus moins article articles art present presente loi alinea
the of and to in for on by with as is are be or an at from that this which any all its it not shall may
""".split())


def tokenize(text: str) -> list[str]:
    tokens = []
    for word in re.findall(r"\w+", fold_for_search(text)):
        if len(word) < 3 or word.isdigit() or word in _STOPWORDS:
            continue
        # Light plural folding so "données"/"donnée" and "contrats"/"contrat" meet.
        if len(word) > 4 and word[-1] in "sx":
            word = word[:-1]
        tokens.append(word)
    return tokens


TITLE_WEIGHT = 3.0
_TITLE = re.compile(r"^\s*(?:article|art\.|clause|section)?\s*[\w.]*\s*[-–—:.)]\s*([^.:\n]{3,80})", re.IGNORECASE)


def query_weights(query: str) -> dict[str, float]:
    """A contract clause's title ("Article 8 - Force majeure") says what it is about better than its body does."""
    weights = {t: 1.0 for t in tokenize(query)}
    title = _TITLE.match(query)
    if title:
        for t in tokenize(title.group(1)):
            weights[t] = TITLE_WEIGHT
    return weights


class Bm25Index:
    def __init__(self, documents: dict[str, str]) -> None:
        self.terms = {doc_id: Counter(tokenize(text)) for doc_id, text in documents.items()}
        self.lengths = {doc_id: sum(c.values()) for doc_id, c in self.terms.items()}
        self.avg_length = (sum(self.lengths.values()) / len(self.lengths)) if self.lengths else 1.0
        df: Counter[str] = Counter()
        for counts in self.terms.values():
            df.update(counts.keys())
        n = len(self.terms)
        self.idf = {t: math.log(1 + (n - f + 0.5) / (f + 0.5)) for t, f in df.items()}

    def rank(self, query: str) -> list[tuple[str, float]]:
        """(doc_id, score in 0..1). The score is the share of the query's weight the article matches fully, so one
        threshold works whatever the query length."""
        weights = query_weights(query)
        query_terms = [t for t in weights if t in self.idf]
        if not query_terms:
            return []
        ceiling = sum(weights[t] * self.idf[t] * (K1 + 1) for t in query_terms)

        scores = []
        for doc_id, counts in self.terms.items():
            norm = K1 * (1 - B + B * self.lengths[doc_id] / self.avg_length)
            score = sum(
                weights[t] * self.idf[t] * counts[t] * (K1 + 1) / (counts[t] + norm)
                for t in query_terms if counts[t]
            )
            if score > 0:
                scores.append((doc_id, score / ceiling))
        scores.sort(key=lambda item: item[1], reverse=True)
        return scores


_cache: dict[tuple, Bm25Index] = {}


def index_for(articles) -> Bm25Index:
    key = tuple(sorted((a.id, a.updated_at.isoformat() if a.updated_at else "") for a in articles))
    index = _cache.get(key)
    if index is None:
        _cache.clear()
        index = _cache[key] = Bm25Index({a.id: a.text for a in articles})
    return index
