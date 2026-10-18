# LegalDesk × Harvey — une IA juridique qui ne peut pas inventer la loi

**Pour :** directions juridiques, banques et entreprises tunisiennes · **Par :** H. Aymen, Consultim IT

## Le problème

- Les juristes revoient à la main des centaines de contrats, face à des lois qui changent.
- Les IA généralistes **inventent des articles de loi** : selon une étude de Stanford, les outils juridiques du marché se trompent sur **17 à 33 % des requêtes**.
- Une banque tunisienne **ne peut pas envoyer les contrats de ses clients** à une IA hébergée à l'étranger (protection des données, secret bancaire).

## Notre réponse : une IA « ancrée » dans la loi vérifiée

> **Les modèles d'IA trouvent le risque, la loi vérifiée le prouve, le juriste décide.**

| Composant | Rôle | Technique d'IA |
|---|---|---|
| **LegalDesk** | Cycle de vie du contrat : rédaction, validation, signature, revue des risques | Analyse du langage (découpage en clauses, langue), **25+ règles juridiques** |
| **Harvey** | Référentiel de la loi tunisienne vérifiée, article par article | Recherche juridique (BM25), **modèle sémantique local** (MiniLM) |
| **Garde-fous** | Chaque citation contrôlée **mot à mot** dans la loi vérifiée | Vérification automatique + validation humaine |
| **IA générative** (Groq) | Lit les lois modificatives, explique les articles, propose des exemples | LLM, **uniquement sur des textes de loi publics**, jamais sur un contrat |

## Ce qui est en place aujourd'hui

- **1 109 articles** de droit tunisien importés depuis les PDF officiels ; **13 textes vérifiés et publiés** par un juriste (Code des obligations et des contrats, Code de commerce, Code des sociétés commerciales, lois 2004-63, 2015-36, 2016-48, 2000-83, 2005-51, et la loi organique 2015-26).
- **Grille des risques : 29 risques**, dont 23 validés, chacun relié aux articles qui le fondent (responsabilité illimitée, reconduction tacite, données transférées à l'étranger, externalisation bancaire, secret bancaire, conventions réglementées, prix de revente imposé, lutte contre le blanchiment…).
- **Versions et dates d'entrée en vigueur** : on lit la loi telle qu'elle était au jour de la signature du contrat.
- **Renvois entre lois** reliés automatiquement ; Harvey liste les **lois citées mais absentes** du corpus.
- **Explications en termes simples**, rédigées par l'IA et **publiées seulement après validation** d'un juriste.
- **Serveur MCP** : n'importe quel agent IA (Claude, agents d'entreprise) peut interroger la loi tunisienne vérifiée et contrôler ses citations.
- **Tout tourne sur les serveurs du client** ; le contrat ne quitte jamais l'infrastructure.
- **335 tests automatisés** (249 côté LegalDesk, 86 côté Harvey).

## Pourquoi c'est différent

| IA juridique classique | LegalDesk × Harvey |
|---|---|
| Rédige l'analyse et cite la loi « de mémoire » | Cite **uniquement** des articles vérifiés, contrôlés mot à mot |
| Contrat envoyé dans le cloud | Contrat analysé **sur place** |
| Une boîte noire | Chaque constat montre **sa clause, son article, sa source officielle et qui l'a validé** |
| Réponses figées | Les juristes **enseignent** à Harvey : lois, risques, formulations, exemples |

## Démonstration (7 minutes)

1. **Harvey** : la loi vérifiée, sa source officielle, la carte des liens risques → lois.
2. **LegalDesk** : revue d'un contrat bancaire — 9 constats, chacun avec l'article cité et le badge « Loi vérifiée ».
3. **Le sens, pas les mots** : un risque formulé autrement repéré par le modèle sémantique (« À valider »).
4. **IA générative sur la loi** : une loi modificative lue et appliquée en versions, validées par le juriste.
5. **Agent IA** : Claude interroge Harvey via MCP et vérifie sa citation en direct.

## Feuille de route

Circulaires de la Banque centrale de Tunisie · protection du consommateur · Code du travail · taux d'intérêt excessifs · proposition de rédaction corrigée pour chaque clause à risque · import Word (DOCX).

---

*« Une banque ne peut pas prendre conseil auprès d'une IA qui improvise. Harvey ne cite que la loi que vos juristes ont vérifiée. »*
