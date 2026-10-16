import re
import unicodedata

_QUOTES = str.maketrans({
    "‘": "'", "’": "'", "‚": "'", "′": "'", "`": "'",
    "“": '"', "”": '"', "„": '"', "«": '"', "»": '"',
    "–": "-", "—": "-", "‑": "-", " ": " ", " ": " ",
})


def normalize_for_match(text: str) -> str:
    """Tolerates typography (quotes, dashes, spacing, line breaks), never wording."""
    text = unicodedata.normalize("NFKC", text).translate(_QUOTES)
    text = re.sub(r'\s*"\s*', '"', text)
    return re.sub(r"\s+", " ", text).strip()


def fold_for_search(text: str) -> str:
    text = unicodedata.normalize("NFKD", text.casefold())
    return "".join(c for c in text if not unicodedata.combining(c))
