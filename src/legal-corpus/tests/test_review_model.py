"""Harvey's review with the real multilingual model, on contracts written for LegalDesk's former keyword-rule tests.
Skipped when the model is not installed (it is a ~470 MB download)."""

import pytest

from app.services import review
from app.services.matching import ExampleMatcher
from app.services.risk_catalog import seeded_grid

MODEL = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"


@pytest.fixture(scope="module")
def matcher():
    try:
        from huggingface_hub import try_to_load_from_cache

        if not try_to_load_from_cache(MODEL, "config.json"):
            pytest.skip("semantic model not downloaded")
        from app.services.embeddings import SentenceTransformerEmbedder

        return ExampleMatcher(SentenceTransformerEmbedder(MODEL), threshold=0.60, margin=0.03)
    except ImportError:
        pytest.skip("sentence-transformers not installed")


CASES = [
    # (clause, expected risk or None)
    ("Le Prestataire sera tenu de réparer tous les préjudices subis par le Client, quel qu'en soit le montant, y compris les pertes d'exploitation.", "liability"),
    ("Le contrat est conclu pour deux ans et se prolongera ensuite d'année en année faute de dénonciation.", "auto_renewal"),
    ("Pendant trois ans, le Prestataire ne pourra proposer ses services à aucune entreprise concurrente du Client en Tunisie.", "exclusivity"),
    ("Le distributeur s'engage à ne pas revendre les produits à un prix inférieur au prix public fixé par le fournisseur.", "resale_price"),
    ("Le territoire du Nord est réservé à la société Alpha et celui du Sud à la société Gamma ; chacune des parties s'interdit de démarcher les clients situés sur le territoire de l'autre.", "market_sharing"),
    ("Le Prestataire peut résilier le présent Contrat à tout moment, sans préavis ni indemnité. La Banque n'a aucun droit de résiliation.", "termination"),
    ("During the term, the Customer shall purchase all its requirements of such services exclusively from the Supplier.", "exclusivity"),
    ("This Agreement shall be automatically extended for additional periods of one year unless either party gives written notice of its intention not to renew.", "auto_renewal"),
    ("En cas de manquement grave, le contrat peut être résilié après mise en demeure restée sans effet pendant 30 jours.", None),
    ("Les livrables restent la propriété exclusive du Prestataire, qui concède au Client une licence à titre non exclusif.", None),
    ("All deliverables remain the sole and exclusive property of the Supplier.", None),
    ("Sauf en cas de faute lourde ou de dol, la responsabilité globale de chaque partie ne saurait excéder le montant des sommes versées au cours des douze derniers mois.", None),
]


@pytest.mark.parametrize("text,expected", CASES)
def test_clause(matcher, text, expected):
    findings, _ = review.review([review.Clause("Article 1 — Test", text)], seeded_grid(), matcher)
    found = {f.rule_key for f in findings if f.contract_reference == "Article 1 — Test"}
    assert expected in found if expected else not found
