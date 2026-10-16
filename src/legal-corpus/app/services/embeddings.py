import re
import zlib
from typing import Protocol

import numpy as np

from app.services.textnorm import fold_for_search

CHUNK_CHARS = 700
CHUNK_OVERLAP = 150


class Embedder(Protocol):
    name: str

    def embed(self, texts: list[str]) -> np.ndarray:
        """One L2-normalised row per text."""


class HashingEmbedder:
    """Dependency-free lexical embedder: hashed words and word pairs, accent- and case-insensitive."""

    def __init__(self, dims: int = 2048) -> None:
        self.dims = dims
        self.name = f"hashing-{dims}"

    def _vector(self, text: str) -> np.ndarray:
        words = [w for w in re.findall(r"\w+", fold_for_search(text)) if len(w) > 1]
        features = words + [f"{a} {b}" for a, b in zip(words, words[1:])]
        vector = np.zeros(self.dims, dtype=np.float32)
        for feature in features:
            h = zlib.crc32(feature.encode("utf-8"))
            vector[h % self.dims] += 1.0 if (h >> 31) & 1 else -1.0
        norm = np.linalg.norm(vector)
        return vector / norm if norm else vector

    def embed(self, texts: list[str]) -> np.ndarray:
        if not texts:
            return np.zeros((0, self.dims), dtype=np.float32)
        return np.vstack([self._vector(t) for t in texts])


class SentenceTransformerEmbedder:
    def __init__(self, model_name: str) -> None:
        from sentence_transformers import SentenceTransformer

        self._model = SentenceTransformer(model_name)
        self.name = model_name

    def embed(self, texts: list[str]) -> np.ndarray:
        return np.asarray(self._model.encode(texts, normalize_embeddings=True), dtype=np.float32)


def build_embedder(kind: str, model_name: str) -> Embedder:
    if kind == "hashing":
        return HashingEmbedder()
    try:
        return SentenceTransformerEmbedder(model_name)
    except ImportError:
        if kind == "semantic":
            raise
        return HashingEmbedder()


def chunk_text(text: str, size: int = CHUNK_CHARS, overlap: int = CHUNK_OVERLAP) -> list[str]:
    text = re.sub(r"\s+", " ", text).strip()
    if len(text) <= size:
        return [text] if text else []
    chunks, start = [], 0
    while start < len(text):
        end = min(start + size, len(text))
        if end < len(text):
            space = text.rfind(" ", start + size // 2, end)
            end = space if space > 0 else end
        chunks.append(text[start:end].strip())
        if end >= len(text):
            break
        start = max(end - overlap, start + 1)
    return chunks
