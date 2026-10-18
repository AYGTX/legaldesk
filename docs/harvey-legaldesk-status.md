# Harvey & LegalDesk — status and backlog (18 October 2026)

## Done

### Harvey (verified Tunisian law corpus)
- **Corpus:** laws imported article by article, each text verified by a lawyer against its official source.
- **Article versions:** article versions with "law as of a date"; articles that aren't needed can be excluded.
- **Amending laws:** an amending law is turned into proposed changes, which a lawyer approves.
- **Cross-references:** links between articles, and a list of laws the corpus cites but doesn't hold yet.
- **Defined terms:**
  - found automatically: « on entend par », « est considérée… »;
  - shown in a panel on each law's page;
  - underlined in article text, with the definition shown in place on hover or click.
- **Plain-language explanations:** written with AI help, approved by a lawyer, and tied to the article's exact wording. An explanation no longer counts as approved once the article changes.
- **Risk grid** (« Grille des risques », formerly "playbook rules"):
  - lawyer-confirmed rules linked to articles;
  - model clauses (the corrected wording) approved by a lawyer.
- **Groq helpers:** draft amendments, explanations, rule suggestions and examples. Only law text is sent, never contracts. Every proposal is checked word for word against the source.
- **CUAD coverage page:** shows which of CUAD's 41 clause categories (a public dataset of lawyer-labelled contracts) the risk grid covers, with each category's review question in EN and FR.
- **MCP server** for AI agents (Claude Desktop etc.):
  - tools: `list_laws`, `search_law`, `get_article`, `find_definition`, `check_quote`, `explain_article`;
  - install script included;
  - full write-up in `docs/architecture/harvey-mcp.md`.
- **Interface:**
  - in-page updates with htmx (no full page reload) and the scroll fix;
  - EN/FR throughout;
  - a session secret that persists, plus `data/.env` for local settings.

### LegalDesk (contract review)
- **New risks:**
  - bank: related parties, supervisor access, customer complaints, anti-money laundering;
  - competition: imposed resale price, market sharing.
- **Possible risks:** found by meaning-based matching (`/classify`), in addition to the rules.
- **Lawyer-approved corrected clause** proposed for each risk found.
- **Validated on a real dataset (CUAD, 510 contracts),** F1 before → after:
  - uncapped liability: 0 → 0.38;
  - automatic renewal: 0.58 → 0.77;
  - exclusivity: 0.61 → 0.71.

  Details are in `tools/cuad-benchmark/README.md`.
- **Tests:** 262 LegalDesk unit tests and the full Harvey suite pass.
- **Documents:** jury presentation (`docs/LegalDesk-presentation-jury.md`) and architecture diagram (`docs/architecture/legaldesk-architecture.svg`).

## Waiting on you
- Approve the 6 new risks (4 bank, 2 competition) in Harvey.
- Publish the AML law.
- Replace the Groq key. `appsettings.json` holds it and stays uncommitted.

## Backlog

### Before the competition
1. **Send the contract signing date from LegalDesk to Harvey,** so contracts are checked against the law as it stood when they were signed.
2. **Rehearse the demo:** the script, the seeded contracts and the MCP demo in Claude Desktop.

3a. **Import: keep « bis / ter / quater » articles.** Import currently stops with « Numéros d'articles répétés :
    42, 50, 54, 9. Corrigez le texte avant l'import. » These articles aren't duplicates: « Article 42 bis » is a separate
    article inserted after article 42. The splitter should read the suffix as part of the number (42, 42 bis,
    42 ter…), order and cite them correctly, and flag only true duplicates. Existing laws and LegalDesk must not change.

### Accuracy (benchmark follow-ups)
3. A **non-compete** rule of its own, separate from exclusivity.
4. **Penalty:** tell contractual penalties apart from tax or regulatory penalties.
5. **Rerun the CUAD benchmark** after each rule change and keep its README table up to date.

### Coverage
6. **New risks from the coverage page's "not covered" list:** change of control, assignment, insurance, audit rights.
7. **Arabic versions** of the laws, verified alongside the French.
8. **Example clauses imported from CUAD** to calibrate meaning-based matching, once the dataset's license is confirmed.

### Harvey features
9. **Meaning-based search** in Harvey's search page, next to keyword search.
10. **Audit log** of which AI agent asked for which article, and when.
11. **More ways laws define terms** (« désigne », « s'entend de »), and an option to underline terms defined in other laws.
12. **Stable article IDs** in the Akoma Ntoso style (the standard for legal documents).
13. **Import laws from DOCX files.**

### Platform
14. **Automatic legal watch:** check JORT and CMF for new laws, then prepare amendments for a lawyer to approve.
15. **Harvey's MCP server over the network** (HTTP with an API key), so agents on other machines can use it.
16. **Docker packaging and automatic tests (CI).**
