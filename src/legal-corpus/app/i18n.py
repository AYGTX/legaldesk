LANGUAGES = ("en", "fr")

_T: dict[str, tuple[str, str]] = {
    "app_name": ("Legal corpus", "Corpus juridique"),
    "nav_texts": ("Texts", "Textes"),
    "nav_search": ("Search & citations", "Recherche et citations"),
    "nav_api": ("API docs", "Documentation API"),
    "nav_playbook": ("Playbook", "Playbook"),
    "playbook_intro": (
        "Which verified articles back each risk LegalDesk detects in a contract. A rule is used in reviews only once a person confirms it; any change to it withdraws the confirmation.",
        "Les articles vérifiés qui fondent chaque risque détecté par LegalDesk dans un contrat. Une règle n'est utilisée dans les revues qu'une fois confirmée par une personne ; toute modification retire cette confirmation.",
    ),
    "pb_rule": ("Rule", "Règle"),
    "pb_detects": ("Detects", "Détecte"),
    "pb_risk": ("Risk detected by LegalDesk", "Risque détecté par LegalDesk"),
    "pb_label": ("Label", "Libellé"),
    "pb_not_detected": ("Not detected by LegalDesk", "Non détecté par LegalDesk"),
    "pb_confirmed_by": ("Confirmed by {who}", "Confirmée par {who}"),
    "pb_not_confirmed": ("Not confirmed", "Non confirmée"),
    "pb_not_ready": (
        "Every article must exist and be verified law before the rule can be confirmed.",
        "Chaque article doit exister et être une loi vérifiée avant que la règle puisse être confirmée.",
    ),
    "pb_empty": ("No rules yet.", "Aucune règle pour l'instant."),
    "pb_new_rule": ("New rule", "Nouvelle règle"),
    "pb_all_covered": ("Every risk LegalDesk detects already has a rule.", "Chaque risque détecté par LegalDesk a déjà une règle."),
    "pb_mappings": ("Regulations linked to rules", "Réglementations liées aux règles"),
    "pb_mappings_intro": (
        "When a LegalDesk regulation's reference or title contains this text, its compliance finding quotes the rule's first article.",
        "Lorsque la référence ou le titre d'une réglementation de LegalDesk contient ce texte, sa constatation de conformité cite le premier article de la règle.",
    ),
    "pb_fragment": ("Reference contains", "La référence contient"),
    "pb_add_mapping": ("Link", "Lier"),
    "pb_back": ("Back to the playbook", "Retour au playbook"),
    "pb_confirm": ("Confirm this rule", "Confirmer cette règle"),
    "pb_confirm_explain": (
        "Confirm only after checking that each article really applies to this risk. Adding, removing, reordering or rewording an article withdraws the confirmation.",
        "Ne confirmez qu'après avoir vérifié que chaque article s'applique bien à ce risque. Ajouter, retirer, réordonner ou reformuler un article retire la confirmation.",
    ),
    "pb_unconfirmed_explain": (
        "Until confirmed, LegalDesk does not quote these articles in its reviews.",
        "Tant qu'elle n'est pas confirmée, LegalDesk ne cite pas ces articles dans ses revues.",
    ),
    "pb_article_missing": ("Article not found", "Article introuvable"),
    "pb_open_text": ("Open the text", "Ouvrir le texte"),
    "pb_up": ("Move up", "Monter"),
    "pb_down": ("Move down", "Descendre"),
    "pb_remove": ("Remove", "Retirer"),
    "pb_why": ("Why it applies (shown to the reader)", "Pourquoi il s'applique (affiché au lecteur)"),
    "pb_add_article": ("Add an article", "Ajouter un article"),
    "pb_add_article_hint": (
        "Only verified texts are listed. The article is stored by the law's reference and number, so it survives re-imports.",
        "Seuls les textes vérifiés sont proposés. L'article est enregistré par la référence de la loi et son numéro, il résiste donc aux réimportations.",
    ),
    "pb_delete_rule": ("Delete this rule", "Supprimer cette règle"),
    "rule_confirmed": ("Rule confirmed.", "Règle confirmée."),
    "err_unknown_rule": ("That rule no longer exists.", "Cette règle n'existe plus."),
    "err_duplicate_rule": ("A rule with that key already exists.", "Une règle avec cette clé existe déjà."),
    "err_no_reference": ("Choose a legal text that has a reference.", "Choisissez un texte juridique qui a une référence."),
    "err_duplicate_article": ("This article is already in the rule.", "Cet article figure déjà dans la règle."),
    "err_unverified_article": (
        "An article of this rule is not verified law. Verify its text first.",
        "Un article de cette règle n'est pas une loi vérifiée. Vérifiez d'abord son texte.",
    ),
    "new_text": ("New text", "Nouveau texte"),
    "filter": ("Filter", "Filtrer"),
    "search_placeholder": ("Title, reference or keyword", "Titre, référence ou mot-clé"),
    "all": ("All", "Tous"),
    "no_texts": ("No texts yet. Add the first law.", "Aucun texte pour l'instant. Ajoutez la première loi."),
    "title": ("Title", "Titre"),
    "reference": ("Reference", "Référence"),
    "reference_hint": ("e.g. Loi organique n° 2004-63", "ex. Loi organique n° 2004-63"),
    "summary": ("Summary", "Résumé"),
    "keywords": ("Keywords (comma-separated)", "Mots-clés (séparés par des virgules)"),
    "language": ("Language", "Langue"),
    "jurisdiction": ("Jurisdiction", "Juridiction"),
    "category": ("Category", "Catégorie"),
    "status": ("Status", "Statut"),
    "effective_date": ("Effective date", "Date d'entrée en vigueur"),
    "source_url": ("Official source (URL)", "Source officielle (URL)"),
    "articles": ("Articles", "Articles"),
    "article": ("Article", "Article"),
    "number": ("Number", "Numéro"),
    "text": ("Text", "Texte"),
    "order": ("Order", "Ordre"),
    "save": ("Save", "Enregistrer"),
    "create": ("Create", "Créer"),
    "cancel": ("Cancel", "Annuler"),
    "delete": ("Delete", "Supprimer"),
    "edit": ("Edit", "Modifier"),
    "add_article": ("Add an article", "Ajouter un article"),
    "import_articles": ("Import articles from pasted text", "Importer des articles depuis un texte collé"),
    "import_hint": (
        "Paste the law as published. Articles are split on headings like “Article premier”, “Art. 12 bis” or “الفصل 3”.",
        "Collez la loi telle que publiée. Les articles sont découpés sur les intitulés « Article premier », « Art. 12 bis » ou « الفصل 3 ».",
    ),
    "replace_existing": ("Replace existing articles", "Remplacer les articles existants"),
    "import": ("Import", "Importer"),
    "no_articles": ("No articles yet.", "Aucun article pour l'instant."),
    "verified": ("Verified", "Vérifié"),
    "not_verified": ("Not verified", "Non vérifié"),
    "verified_by_on": ("Verified by {who} on {when}", "Vérifié par {who} le {when}"),
    "verification": ("Verification", "Vérification"),
    "verify_explain": (
        "Sign off only after checking every article word for word against the official source. Any later change to the articles, the reference or the source clears this sign-off.",
        "Ne validez qu'après avoir comparé chaque article mot à mot avec la source officielle. Toute modification ultérieure des articles, de la référence ou de la source annule cette validation.",
    ),
    "unverified_explain": (
        "Until verified, this text is a note: it is not returned as law to LegalDesk.",
        "Tant qu'il n'est pas vérifié, ce texte est une note : il n'est pas transmis comme loi à LegalDesk.",
    ),
    "your_name": ("Your name", "Votre nom"),
    "verify": ("Mark as verified", "Marquer comme vérifié"),
    "unverify": ("Withdraw sign-off", "Retirer la validation"),
    "set_status": ("Change status", "Changer le statut"),
    "danger_zone": ("Delete this text", "Supprimer ce texte"),
    "confirm_delete": ("Delete permanently?", "Supprimer définitivement ?"),
    "back": ("Back to texts", "Retour aux textes"),
    "search": ("Search", "Rechercher"),
    "search_title": ("Search the law", "Rechercher dans la loi"),
    "search_query": ("What are you looking for?", "Que cherchez-vous ?"),
    "include_unverified": ("Include unverified texts (notes)", "Inclure les textes non vérifiés (notes)"),
    "no_results": ("No matching article.", "Aucun article correspondant."),
    "score": ("Score", "Score"),
    "check_citation": ("Check a citation", "Vérifier une citation"),
    "check_explain": (
        "Checks that a quote appears word for word in the article. Typography (quotes, dashes, spaces) is tolerated, wording is not. Use “...” for omitted passages.",
        "Vérifie qu'une citation figure mot à mot dans l'article. La typographie (guillemets, tirets, espaces) est tolérée, pas la formulation. Utilisez « ... » pour les passages omis.",
    ),
    "quote": ("Quote", "Citation"),
    "legal_text": ("Legal text", "Texte juridique"),
    "article_number_optional": ("Article number (optional)", "Numéro d'article (facultatif)"),
    "check": ("Check", "Vérifier"),
    "citation_found": ("Found word for word in {where}.", "Trouvée mot à mot dans {where}."),
    "citation_found_unverified": (
        "Found in {where}, but this text is not verified: cite it as a note, not as law.",
        "Trouvée dans {where}, mais ce texte n'est pas vérifié : citez-le comme une note, pas comme la loi.",
    ),
    "index_status": ("Index: {indexed}/{total} articles indexed with {embedder}", "Index : {indexed}/{total} articles indexés avec {embedder}"),
    "rebuild_index": ("Rebuild index", "Reconstruire l'index"),
    "updated": ("Updated", "Mis à jour"),
    # flash messages and rule codes
    "saved": ("Saved.", "Enregistré."),
    "deleted": ("Deleted.", "Supprimé."),
    "imported": ("{count} article(s) imported.", "{count} article(s) importé(s)."),
    "verified_ok": ("Text marked as verified.", "Texte marqué comme vérifié."),
    "unverified_ok": ("Sign-off withdrawn.", "Validation retirée."),
    "index_rebuilt": ("Index rebuilt.", "Index reconstruit."),
    "err_no_articles": ("A text without articles has nothing to verify.", "Un texte sans articles n'a rien à vérifier."),
    "err_no_source": ("Add the official source before verifying.", "Ajoutez la source officielle avant de vérifier."),
    "err_duplicate_number": ("That article number already exists in this text.", "Ce numéro d'article existe déjà dans ce texte."),
    "err_no_articles_found": (
        "No article headings found (expected “Article 1”, “Art. 2”, “الفصل 3”).",
        "Aucun intitulé d'article trouvé (attendu : « Article 1 », « Art. 2 », « الفصل 3 »).",
    ),
    "err_invalid": ("Some fields are missing or invalid.", "Certains champs sont manquants ou invalides."),
    "err_quote_too_short": ("The quote is too short to prove anything.", "La citation est trop courte pour prouver quoi que ce soit."),
    "err_no_target": ("Choose the legal text the quote comes from.", "Choisissez le texte juridique dont provient la citation."),
    "err_article_not_found": ("No such article in that text.", "Aucun article correspondant dans ce texte."),
    "err_quote_not_in_article": ("Not found word for word. Do not cite it as law.", "Introuvable mot à mot. Ne la citez pas comme la loi."),
    # enum labels
    "Tunisia": ("Tunisia", "Tunisie"),
    "EU": ("European Union", "Union européenne"),
    "Sectoral": ("Sectoral", "Sectoriel"),
    "DataProtection": ("Data protection", "Protection des données"),
    "Labor": ("Labor", "Travail"),
    "Consumer": ("Consumer", "Consommation"),
    "Commercial": ("Commercial", "Commercial"),
    "Tax": ("Tax", "Fiscal"),
    "Telecom": ("Telecom", "Télécommunications"),
    "Other": ("Other", "Autre"),
    "Draft": ("Draft", "Brouillon"),
    "Published": ("Published", "Publié"),
    "Archived": ("Archived", "Archivé"),
    "lang_fr": ("French", "Français"),
    "lang_en": ("English", "Anglais"),
    "lang_ar": ("Arabic", "Arabe"),
}


def translator(lang: str):
    index = 1 if lang == "fr" else 0

    def t(key: str, **kwargs) -> str:
        entry = _T.get(key)
        value = entry[index] if entry else key
        return value.format(**kwargs) if kwargs else value

    return t


def is_known(key: str | None) -> bool:
    return key in _T


def pick_language(cookie: str | None, accept_language: str | None) -> str:
    if cookie in LANGUAGES:
        return cookie
    if accept_language and accept_language.lower().startswith("fr"):
        return "fr"
    return "en"
