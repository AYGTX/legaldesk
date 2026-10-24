# Harvey & LegalDesk — status and backlog (updated 24 October 2026)

## Start here (next session)

1. **Commit first.** About 212 files of 21–24 October work are uncommitted on `feature/legaldesk-rebrand`.
2. **Overhaul step 1: Clients (plan A)** — full plan in `docs/plan-clients.md` (about 6 days; steps 1–4 give the demo).
3. Decisions still open (see "Decisions" below): risk source, regulatory watch, field-detection licence.

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
- **Risk grid** (« Grille des risques », formerly "playbook rules"), stored in Harvey's database (no more `playbook.json`; an old file is imported once). It starts with one empty risk per risk LegalDesk detects:
  - lawyer-confirmed rules linked to articles;
  - model clauses (the corrected wording) approved by a lawyer.
- **Groq helpers:** draft amendments, explanations, rule suggestions and examples. Only law text is sent, never contracts. Every proposal is checked word for word against the source.
- **MCP server** for AI agents (Claude Desktop etc.):
  - tools: `list_laws`, `search_law`, `get_article`, `find_definition`, `check_quote`, `explain_article`;
  - install script included;
  - full write-up in `docs/architecture/harvey-mcp.md`.
- **Interface:**
  - in-page updates with htmx (no full page reload) and the scroll fix;
  - EN/FR throughout;
  - a session secret that persists, plus `data/.env` for local settings.

### Harvey reviews contracts (21 October 2026)
- **Detection moved from LegalDesk's C# keyword rules to Harvey** (`POST /api/v1/review`, `app/services/review.py`):
  - **by meaning:** each clause is compared with example clauses per risk (`app/services/risk_catalog.py`, written for
    Harvey, used as they stand, not yet reviewed by a lawyer) plus examples and phrases from the risk grid. Shared
    "standard clauses" act as counter-examples for every risk; a clause can carry several risks.
  - **checks in code:** contradictory payment periods, references to missing articles, party names, missing
    termination / force majeure clauses, personal data safeguards, consumer withdrawal, bank checks (outsourcing,
    secrecy, related parties, supervisor exception, records kept under ten years, account agreement).
- LegalDesk sends the clauses and attaches law and corrected clauses from the risk grid (unconfirmed rules included:
  no lawyer approval required for now). If Harvey is down, the analysis says so instead of showing "no risk".
- `LegalCorpus:Enabled` is gone: Harvey is required. The C# rules sit in `ContractRuleEngine.cs` under `#if LEGACY_RULES`
  (not compiled), with their tests.
- Checked against the old C# test contracts: same risks found, plus paraphrases the keyword rules missed.

### Contract lifecycle AI (21 October 2026)
- **Key data:** « Extraire les données clés » on a contract reads parties, dates, duration, renewal, notice, amount,
  payment terms, law and jurisdiction; each value is kept only if its sentence is found word for word in the contract.
  « Appliquer à la durée du contrat » fills the term in one click (`POST documents/{id}/key-terms`).
- **Renewal note:** « Note de renouvellement » recommends Renew / Renegotiate / Terminate / Review from the recorded
  dates, the notice deadline, the open risks and the contract's clauses, with verified quotes (`POST documents/{id}/renewal-note`).
- Both run on Groq (contract text is sent, as for the contract chat), nothing is saved, each call is audited.

### LegalDesk (contract review)
- **New risks:**
  - bank: related parties, supervisor access, customer complaints, anti-money laundering;
  - competition: imposed resale price, market sharing.
- **Possible risks:** found by meaning-based matching (`/classify`), in addition to the rules.
- **Lawyer-approved corrected clause** proposed for each risk found.
- **Tests (18 Oct):** 262 LegalDesk unit tests and the full Harvey suite passed. On 24 Oct: 208/208 unit tests (the
  legacy C# rule tests are now excluded with their code) and 102/104 integration tests; the 2 failures are
  `DemoContractSeedingTests`, which still expect 9 demo contracts while the seeder now creates 10 (stale test).
- **Documents:** jury presentation (`docs/LegalDesk-presentation-jury.md`) and architecture diagram (`docs/architecture/legaldesk-architecture.svg`).

### LegalDesk UX, presets and hardening (22–24 October 2026, uncommitted)
- **Approval circuit presets** (« Circuits d'approbation »): admin page with title, search and live preview; steps by
  user, role or department, conditional steps, stages for mixed circuits.
- **One review per contract:** the « Revue juridique » tab is merged into Summary. The checklist is the only list of
  risks, with full detail in place and the clause reference; the contract profile sits above it. Tabs: Summary,
  Harvey, Signature. The Risk ring shows « Risque calculé sur le texte analysé le … » and asks for a re-run when
  points were handled since.
- **Fields card:** progress counts only signature, initials and stamp fields; each field shows label, translated
  type, page and signer; long labels truncated; « +N » expands.
- **i18n:** missing keys added (`review.send.*`, 9 audit actions, 6 field types); the capability map is hidden from
  the menu (route kept).
- **Field detection** in the designer: tested end to end, works.
- **Security (24 Oct):** Hangfire dashboard closed (local requests only in development, Administrator otherwise);
  JWT secret and Groq key moved out of `appsettings*.json` into .NET user secrets (the API refuses to start without a
  32+ character `Jwt:Secret`); `docker-compose.yml` reads `JWT_SECRET` / `GROQ_API_KEY` from a git-ignored `.env`;
  vulnerable packages patched (`System.Security.Cryptography.Pkcs` 10.0.13, `Microsoft.OpenApi` 2.13.0).
- **Documents:** technical specification by module (13 modules, sprint mapping, gaps) as a Claude doc:
  https://claude.ai/artifact/9zkyeroMp2LwhRoCSevvim

### Found in review (24 October), not fixed yet
- **Risk source:** the pre-flight review runs the general model (Groq) first; Harvey's grid review only runs when
  the model fails or finds nothing. So most checklist risks have no rule key, no linked law, and model wording.
- **Readiness score:** 35 % of it is compliance, which is switched off and counts as a constant 50.
- **Backend (medium/high):** rate limits are defined but never applied; no HTTPS redirect / HSTS; signing-link expiry
  is not checked on reject, request changes, acknowledge, comments and checklist; the virus scanner is a stub;
  `InvalidOperationException` messages reach the client.
- **Enterprise gaps:** no SSO, MFA flag not enforced, no multi-tenancy, no certified signatures (PAdES /
  timestamping), no webhooks, contract text sent to Groq by default.
- The fallback warning still says « analyse par mots-clés » though the fallback is Harvey's grid.

## Decisions
- **Risk source:** make Harvey's grid produce checklist risks; keep Groq for summary, profile, chat, key data.
- **Regulatory watch:** retire it or turn it into a view of Harvey's corpus (two law registries today).
- **Readiness score:** drop compliance from the formula until a real compliance source exists.
- **Field-detection licence:** FFDNet-L states no licence; ultralytics is AGPL-3.0. Check before selling.
- **Vacations:** connect absences to automatic step delegation, or remove the screen.
- **Dead code to delete:** `#if LEGACY_RULES` rules and tests, the unused findings view in
  `contract-analysis-panel`, the capabilities page, leftover SignFlow names, `Document.FolderId`.

## Waiting on you
- Approve the 6 new risks (4 bank, 2 competition) in Harvey.
- Publish the AML law.
- Revoke the old Groq key in the Groq console and set the new one with
  `dotnet user-secrets set Groq:ApiKey <key> --project src/backend/SignFlow.Api`. The old key stays in 3 local
  commits (96a316a, 7a20a8a, f6df328) by choice; never push them anywhere public.

## Backlog

### Before the competition
1. **Send the contract signing date from LegalDesk to Harvey,** so contracts are checked against the law as it stood when they were signed.
2. **Rehearse the demo:** the script, the seeded contracts and the MCP demo in Claude Desktop.

3a. **Import: keep « bis / ter / quater » articles.** Import currently stops with « Numéros d'articles répétés :
    42, 50, 54, 9. Corrigez le texte avant l'import. » These articles aren't duplicates: « Article 42 bis » is a separate
    article inserted after article 42. The splitter should read the suffix as part of the number (42, 42 bis,
    42 ter…), order and cite them correctly, and flag only true duplicates. Existing laws and LegalDesk must not change.

3b. **Language model setup that stays configured.** Harvey sometimes shows « Aucun modèle de langage n'est disponible.
    Réglez CORPUS_AI_PROVIDER… ». Harvey reads `src/legal-corpus/data/.env` at startup, but a value already set in the
    shell takes priority, and the provider is only checked at startup. Fix: a settings page in Harvey where you choose
    the provider and paste the key. It is saved in `data/.env`, applied without a restart, and tested with a "Test
    connection" button. If Groq has no key, fall back to the local model. Show the active provider in the header.

### Accuracy
3. A **non-compete** rule of its own, separate from exclusivity.
4. **Penalty:** tell contractual penalties apart from tax or regulatory penalties.

### Coverage
5. **New risks:** change of control, assignment, insurance, audit rights.
6. **Arabic versions** of the laws, verified alongside the French.

### Harvey features
7. **Meaning-based search** in Harvey's search page, next to keyword search.
8. **Audit log** of which AI agent asked for which article, and when.
9. **More ways laws define terms** (« désigne », « s'entend de »), and an option to underline terms defined in other laws.
10. **Stable article IDs** in the Akoma Ntoso style (the standard for legal documents).
11. **Import laws from DOCX files.**

### Product (from 24 October)
15. **Clients (counterparties)** — `docs/plan-clients.md`. Next up.
16. **User onboarding for large companies:** bulk CSV import with invitation e-mails first, then SSO (Entra ID /
    Google / Okta) with group-to-role mapping, then SCIM sync.
17. **Process AI:** smart routing (contract type → preset), negotiation diff explainer on version redlines, signature
    and stamp detection on scanned contracts, missing-annex checker.

### Platform
12. **Automatic legal watch:** check JORT and CMF for new laws, then prepare amendments for a lawyer to approve.
13. **Harvey's MCP server over the network** (HTTP with an API key), so agents on other machines can use it.
14. **Docker packaging and automatic tests (CI).**
