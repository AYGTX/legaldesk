<!-- Copied from the Claude doc https://claude.ai/artifact/AaUWp2zVCKe5LtFjxH6M13 on 2026-11-01. The doc is the source; this copy lets sessions without the doc connector read it. -->

# LegalDesk — Rework Specification

2026-10-29 · Aymen

This spec rebuilds LegalDesk to a sellable standard. Every statement is checked against the code on branch feature/legaldesk-rebrand (commit 845d5bc). Where the 24 October spec and the code disagree, the code wins and the gap is listed.

## 1. Decisions

The rework makes LegalDesk an honest, lawyer-controlled contract desk that several client organisations can use at once. Each call below is final for this rework; changing one means editing this table first.

| # | Decision | Why | Replaces |
|---|---|---|---|
| D1 | Checklist risks come only from Harvey's /review, and /review runs only rules a lawyer has approved. The language model never creates a checklist item. | Today the model writes most risks, and Harvey's review also runs unapproved rules (`review.py` uses `playbook.load`, "confirmed or not"). The sales claim "rules your lawyers validate" is false on both paths. | Model-first review with grid fallback |
| D2 | Every finding carries its origin: rule key, rule version, approver, detection kind (code check or meaning match) and confidence. A finding without a rule key cannot exist. | A lawyer must be able to trace any flag to a rule and its law. | Free-text model findings |
| D3 | The language model is optional and off by default per organisation. Turning it on is an admin setting that names the provider (Groq or local) and is shown to users. | Contract text leaves the server whenever Groq is set. Clients in banking will refuse that by default. | Groq on whenever a key exists |
| D4 | Model features keep only what can be checked: summary, chat, key data and renewal note show quotes verified word for word; unverified output is labelled as a lead. | This is already the rule for chat and key data; it becomes the rule for all model output. | Summary and profile unchecked |
| D5 | Readiness score drops compliance: 100 − 25 per open Critical − 10 per open High − 3 per open Medium − 1 per open Low, floored at 0. Risk score is shown separately and comes from Harvey, not from a model estimate. | 35 % of today's score is a constant 50 because compliance is off. A score must move only for reasons a user can see. | 65/35 formula with model risk score |
| D6 | The regulatory watch is retired. Harvey's corpus is the only law registry; "required mentions per contract family" become code checks in Harvey. | Two law registries with different verification rules confuse users and lawyers. | RegulatoryText, RegulatoryArticle, keyword compliance |
| D7 | Multi-tenant SaaS: an Organisation entity, an OrganisationId on every business row, enforced by an EF Core global query filter and tested. One database, one deployment. | Several clients on one install is the business model. Today any Administrator sees every contract in the database. | Single-organisation install |
| D8 | Counterparties become a first-class entity (Plan A in docs/plan-clients.md), linked to contracts and signers. | Portfolio reporting by client is the first thing a legal team asks for. | Free-text party names |
| D9 | Signature evidence is sealed: SHA-256 of every version and of the final PDF, a PAdES seal with an RFC 3161 timestamp on the final PDF, and an audit log chained by hash. | `Signature.Hash` and `Signature.Certificate` exist but are never set; nothing proves the signed file is the file that was read. | Unsealed final PDF |
| D10 | Signing links: 256-bit random tokens, stored hashed, expiry checked on every signing endpoint, one-time e-mail code before the first action. The owner never sees another person's token. | Today tokens are stored in clear, shown to the owner, and expiry is checked only when the session opens. | GUID tokens in clear |
| D11 | Accounts by invitation only, OIDC single sign-on (Microsoft Entra ID first), TOTP multi-factor required for Administrators. | Self-registration is open and makes anyone an Employee; MFA fields exist but are not used. | Open register endpoint |
| D12 | Obligations become a list per contract (many dates, each with its quote and owner). No date is ever invented. | Upload today stores a made-up "review in 1 year" date when the text has none. | One obligation field on Document |
| D13 | One storage abstraction with two real providers: local disk (on-premise) and S3-compatible (AWS S3, MinIO, OVH). Azure Blob is dropped from the enum until a client needs it. | The enum lists Azure and S3, packages are referenced, only local disk is implemented. | LocalStorageService only |
| D14 | Code namespaces stay SignFlow; everything a user, client or log reader sees says LegalDesk and Harvey. Arabic UI is out of scope for this rework. | A namespace rename costs a sprint and sells nothing. | Mixed names in logs and files |

## 2. Errata to the 24 October spec

39 statements in the [24 October spec](https://claude.ai/artifact/9zkyeroMp2LwhRoCSevvim) do not match the code; 18 are High: they change what can be sold or promised. E1–E32 are in the table; E33–E39 were found while writing the module specs and follow it. Checked file by file on 29 October; paths are relative to `src/`.

| # | The spec says | The code does | Where | Impact |
|---|---|---|---|---|
| E1 | Template and grid findings are lawyer-validated when rules are confirmed | Harvey's /review matches against every rule, confirmed or not; LegalDesk also fetches the grid with `include_unconfirmed=true`, so unapproved articles and model clauses reach the checklist | `legal-corpus/app/services/review.py` meaning_rules, `LegalCorpusClient.GetPlaybookAsync` | High |
| E2 | Files go to local disk, Azure Blob or S3 | Only `LocalStorageService` exists and is registered; the Azure and S3 packages are referenced but unused | `InfrastructureServiceExtensions.cs:171` | High |
| E3 | Only a draft can be deleted | Any contract not InProgress can be deleted by its owner, including a Completed, signed one (soft delete) | `DeleteDocumentCommand.cs` | High |
| E4 | A recipient can delegate their step, with a note | No endpoint, command or screen; `Recipient.DelegatedToId` and `DelegationNote` are never written | Signing, Recipients features | High |
| E5 | Steps carry a due date, reminders every N days, an escalation e-mail and a condition | Fields exist on `WorkflowStep`; no job or handler reads `SendReminders`, `ReminderDays`, `EscalationEmail`, `EscalationDays` or `ConditionalExpression` | `WorkflowStep.cs` | High |
| E6 | A signing token works only while valid | Expiry (30 days) is checked only when the session opens; sign, approve, reject, request changes, acknowledge, comments and checklist accept an expired token | `GetSigningSessionQuery.cs:88` vs `Features/Signing/Commands` | High |
| E7 | Each signature records a certificate and a hash | `Signature.SetHash` and `SetCertificate` are never called; both columns are always empty | `Signature.cs` | High |
| E8 | Signing needs no account, only the recipient's link | The contract detail returns every recipient's token to the owner and to Administrators, who can then act as any signer | `GetDocumentByIdQuery.cs:109` | High |
| E9 | On a re-run, known keys keep their status and new ones are added as Open | Also: open items the new run no longer reports are auto-resolved with no user ("No longer reported by the latest review"). With a model reviewer, a risk can close itself between two runs | `EvaluateReadinessCommand.Reconcile` | High |
| E10 | The model proposes the obligation date and a person confirms it | When no date is found, upload stores an invented "Renewal review" at today + 1 year; the daily reminder job also fires on unconfirmed dates | `LlmAiService.SuggestObligationAsync`, `ObligationReminderJob` | High |
| E11 | Harvey's grid is the fallback path's trustworthy core | Harvey's API is open when `CORPUS_API_KEY` is unset, and its UI admin defaults to admin / admin | `legal-corpus/app/deps.py`, `config.py` | High |
| E12 | Security: role checks on controllers | Rate-limit policies are defined but attached to no endpoint; register is public and makes the caller an Employee | `Program.cs:75`, `RegisterCommand.cs:60` | High |
| E13 | Upload passes a virus-scan hook | The scanner only rejects executable file extensions; every file named .pdf passes | `VirusScanService.cs` | High |
| E14 | Two findings on two clauses are two items | Findings that share a source key collapse into one; the second is dropped silently | `EvaluateReadinessCommand.Reconcile` | Medium |
| E15 | Status flow Draft → Pending / InProgress / WaitingForOthers → Completed; exit Cancelled | Pending, WaitingForOthers and Cancelled are never set; there is no cancel endpoint; `Document.SetStatus` accepts any transition | `Document.cs:162` | Medium |
| E16 | Harvey search is hybrid: lexical plus embeddings | One or the other: BM25 by default (`CORPUS_EMBEDDER=hashing`), vectors only if configured; dated searches are always BM25. Vector search scans every article in Python per query | `legal-corpus/app/services/search.py` | Medium |
| E17 | Alert ladder messages differ for auto-renewing contracts | They do, but every alert subject and body is English only, against the bilingual rule | `ContractTermAlertJob.cs` | Medium |
| E18 | Obligation dates are read from the text | Dates are parsed with `DateTime.TryParse` under the server culture, so 03/04/2027 is March or April depending on the machine | `LlmAiService.cs:883` | Medium |
| E19 | Role checks are on the controllers | Split two ways: some on controller attributes, many only inside handlers (clauses, regulatory texts, templates, readiness). No single place lists who may do what, so a missed check is invisible | `IsInRole calls in Features/*/Command``s` | Medium |
| E20 | Audit: every state change writes an AuditLog row | `DocumentDownloaded` and `DocumentViewed` are never written outside the demo seeder; downloads and views leave no trace | `AuditAction.cs` usages | Medium |
| E21 | Presence: users viewing the same contract see each other | Any signed-in user can join any contract's group and see its viewers; presence lives in one process's memory and breaks with two API instances | `SignFlowHub.JoinDocumentGroup` | Medium |
| E22 | Access token plus refresh token | One refresh token per user, stored in clear: a login on a second device ends the first session | `LoginCommand.cs:54` | Medium |
| E23 | Sprint 11: in progress, uncommitted | Committed as 44761da on 24 October. The working tree now holds another uncommitted change: 28 frontend files that move routes to /contracts, /templates/pdf and /insights, with redirects | `git status` | Medium |
| E24 | docker-compose: SQL Server, Redis, MailHog, API, frontend | No Harvey service: the compose stack cannot review a contract | `docker-compose.yml` | Medium |
| E25 | API: GET audit, GET regulatory-watch | Routes are `/documents/{id}/audit` (organisation audit is `/reports/audit-log`) and `/regulatory-texts` | Controllers | Medium |
| E26 | The last required signature completes the document | Completion happens on the last recipient's explicit Approve, not on a field signature | `ApproveSigningCommand`` → WorkflowEngine``.cs:358` | Low |
| E27 | Secrets: the Groq key stays in uncommitted appsettings.json (also listed as a gap) | Fixed on 24 October: user secrets and a git-ignored .env | `docs/harvey-legaldesk-status.md` | Low |
| E28 | Regulatory watch built August 12–13 | Articles, verification and source URL were added on 13 October (migration AddLegalCorpus), as were the review gate and template defaults | `Infrastructure/Migrations` | Low |
| E29 | Harvey: 20 pytest files | 22 test files plus conftest.py | `legal-corpus/tests` | Low |
| E30 | The review page stops at 50 MB | The upload page stops at 50 MB too; the API takes 100 MB | `upload-document.component.ts:212` | Low |
| E31 | Alerts run daily at 07:00 and 08:00 | Hangfire cron runs in UTC: 08:00 and 09:00 in Tunis | `Program.cs` | Low |
| E32 | Harvey's language model is off unless configured | The settings class defaults to none, but `load_settings` defaults `CORPUS_AI_PROVIDER` to groq | `legal-corpus/app/config.py:70` | Low |

**E33 (High).** The spec says a never-reviewed contract is reviewed before it is sent. The API enforces that only for PDF-template contracts (`SendDocumentCommand.cs:47`). A revision resets the verdict to NotEvaluated (`Document.ApplyRevision`), so an uploaded contract that was Blocked can be revised and sent with no new review.

**E34 (High).** The spec says "edit text in place" saves a new version. `PdfTextEditService` paints a white box over the old line and writes the new text on top; its own comment says the covered text is not removed from the file. The signed PDF therefore still contains the old wording, and the next review extracts both the old and the new text.

**E35 (Medium).** The spec says the owner can add a watermark. `HasWatermark` and `WatermarkText` are never read or written by any feature.

**E36 (High).** The spec says a Critical can only be accepted by a Manager or Administrator with a reason. That check covers the Dismissed status only (`SetReadinessItemStatusCommand.cs`). The owner can mark the same Critical "Resolved" with no reason and no change to the text, and the Blocked verdict lifts.

**E37 (High).** The spec says every law, explanation and rule is checked or approved by a lawyer. Harvey has one shared admin login from environment variables and no user table (`legal-corpus/app/auth.py`). "Verified by", "confirmed by" and "approved by" are names typed into a form field (`ui.py:439`, `ui.py:883`), so the record does not prove which lawyer approved anything.

**E38 (High).** Harvey's severities are High, Medium and Low only (`review.py:20`); it never emits Critical. The Blocked verdict therefore comes only from language-model findings today, and would never fire once Harvey is the only source (D1) unless Critical is added to the grid.

**E39 (Medium).** The spec does not mention it: LegalDesk still contains its own 866-line C# rule engine (`ContractRuleEngine.Analyze` and its checks). No production code calls `Analyze`; seven unit-test files test it, so a green test run says nothing about the review users actually get. Delete it with its tests, keeping only the clause splitter.

## 3. Target architecture

Two services stay: the LegalDesk API owns contracts, people and every business state; Harvey owns law and the risk grid. Harvey moves from SQLite to its own database on the same SQL Server, so one engine is backed up and restored.

Harvey never calls the language model for a contract. The API calls it only for summary, chat, key data and the renewal note, and only for organisations that turned it on.

| Service | Runtime | Owns | Talks to |
|---|---|---|---|
| Web app | Angular 22, standalone components, signals, ngx-translate (FR, EN) | Screens only; no business rule that the API does not also enforce | API over HTTPS, SignalR |
| LegalDesk API | .NET 10, ASP.NET Core, EF Core 10, Hangfire | Organisations, users, counterparties, contracts, versions, circuits, signatures, checklist, obligations, audit | SQL Server, storage, Redis, SMTP, IdP, TSA, Harvey, optional model |
| Harvey | Python 3.11 (pin it), FastAPI, SQLAlchemy 2, sentence-transformers | Laws, articles, versions, explanations, risk grid, review, field detection, MCP | Its own database, local models; Groq only for law-text drafting by lawyers |
| Worker | Same API binary, Hangfire server role | Term alerts, obligation reminders, step reminders and escalations, SLA expiry, retention purge | Same as API |

## 4. Module specs

The full spec of each module, with the code as it stands, target rules numbered for tests, data, API and acceptance, is in . This table is the index.

| Module | What changes most | New or reshaped data |
|---|---|---|
| M0 Organisations (new) | Every row belongs to one organisation, enforced by query filters and tests | Organisation, OrganisationRuleSetting |
| M1 Identity | Invitation only, OIDC SSO, TOTP for admins, per-device sessions in cookies, permission table | UserSession, UserInvitation, RecoveryCode |
| M2 Contracts | Magic-byte and ClamAV checks, OCR, background intake, one state machine, no deletion of signed contracts | ContractStateMachine, file metadata per version |
| M3 Field designer | Coordinates in PDF points, rotation honoured, layout validated and versioned | Coordinate migration |
| M4 Circuits | Stages with All/Any, team pools, real reminders, escalation, delegation, transactions and an outbox | Team, OrganisationHoliday, OutboxMessage |
| M5 Signing | Hashed 256-bit links, e-mail code, server dates, sealed final PDF with evidence page and timestamp | SigningOtp, hashes per signature and version |
| M6 Templates | Sanitised HTML, locked-down Chrome, versioned approved templates, routing only by preset | Template versions, ContractFamily |
| M7 Collaboration | Rounds kept, internal vs shared comments, no in-place PDF edits, DOCX round trip | ContractRound |
| M8 Pre-flight review | Harvey only, approved rules only, item states that cannot be self-cleared, gate on every contract | ReviewRun, reshaped review items |
| M9 Clause library | Immutable versions, four-eyes approval, links to rules; regulatory watch removed | ClauseVersion, ClauseRuleLink |
| M10 Lifecycle | Ladder counted from the notice deadline, automatic rollover, obligations as a list | ContractTerm, Obligation, ContractEvent |
| M11 Harvey | Real lawyer accounts, versioned rules, Critical severity, quality gate in CI, hybrid search | harvey_users, rule_versions, review_feedback |
| M12 Assistant | Off by default, audited, quotes verified for every sentence, long-contract summaries | ModelOutput |
| M13 Audit and data protection | Hash-chained append-only audit, data-request handling, retention and purge | AuditAnchor, NotificationPreference |

## 5. Cross-cutting

These rules apply to every module; a pull request that breaks one is not merged.

### Security baseline

- HTTPS only behind the proxy, HSTS one year, forwarded headers from known proxies only. Security headers on the web app: CSP without `unsafe-inline` scripts, frame-ancestors none, referrer-policy strict-origin.
- Rate limits attached, not just defined: auth 10 per 15 min per IP and e-mail; signing links 60 per minute per token; model features per R-M12-5; everything else 300 per minute per user.
- Errors: RFC 7807 with a stable `code` and parameters; no exception message reaches the client. Today `ExceptionHandlingMiddleware` returns `InvalidOperationException` and other messages as `Detail`.
- Authorisation in one place: `[RequirePermission]` on every endpoint, a test that lists any endpoint without one, and tenant checks from M0.
- Secrets only from environment or a vault; the API, Harvey and the worker refuse to start without their required secrets.
- Dependencies scanned in CI (`dotnet list package --vulnerable`, `npm audit --omit=dev`, `pip-audit`); a High finding fails the build.
- A penetration test by an outside firm before the first paying client goes live.

### Data and API conventions

- Ids are GUIDs (v7 for new rows, so indexes stay ordered). Times are UTC in the database and shown in the organisation's time zone.
- Every mutable aggregate has a row version; writes send it and get 409 on conflict.
- Enums are stored as strings, never ints, so reordering an enum cannot corrupt data.
- Routes `/api/v1/...`; list endpoints page with `page`, `pageSize` (max 100), `sort`; responses camelCase JSON.
- Long work (intake, review, PDF build, seal) is a job with a run id; the client follows it over SignalR.
- Every user-visible string ships in French and English in the same change; backend sends codes, not sentences.

### Testing

| Layer | Tool | Gate |
|---|---|---|
| Domain and application rules | xUnit | Every R-rule in the module specs has at least one test named after it |
| API and database | xUnit + Testcontainers (SQL Server) | Tenant isolation suite (M0), permission suite, state machine suite |
| Harvey | pytest | All tests plus the matching quality gate (R-M11-6) |
| Web app | Angular unit tests (Vitest) on services and key components | Review checklist, designer, signing page |
| End to end | Playwright | Upload → review → circuit → sign → sealed PDF, in FR and EN |
| Load | k6 | 50 concurrent signers, 20 reviews per minute, p95 under 2 s for non-job endpoints |

Coverage is not a target; the R-rule mapping is. The dead C# rule engine and its 7 test files are deleted (E39).

### Operations

- One `docker-compose.yml` runs the whole product: SQL Server, Redis, MailHog (dev only), ClamAV, API, worker, Harvey, web app, nginx. Harvey's models are baked into its image so a server needs no internet.
- Health: `/health/live` and `/health/ready` per service; ready checks database, Redis, storage, Harvey, ClamAV.
- Logs: Serilog JSON to stdout with request id, organisation id and user id; one log file name (today there are two, `legaldesk-` and `signflow-`).
- Backups: database full daily and log every 15 min; storage versioned; a restore drill each quarter, written up.
- Releases: semantic versions, migrations run by a separate step before the new API starts, never at API start-up.

## 6. Sprint plan

Ten two-week sprints take the product from today's code to a first paying pilot by 19 March 2027. Trust comes first: nothing user-facing is polished until tenancy, identity and Harvey's approvals are real.

A sprint closes only at its gate; a missed gate moves the next sprint, never the gate. Dates assume one team of three developers plus one lawyer half-time from S3; with a different team size, keep the order and re-date.

| Sprint | Modules and rules | Also in this sprint |
|---|---|---|
| S1 | M0 all; R-M1-7 permission table; R-M2-5 state machine; R-M4-9 outbox; error codes | Commit or drop the 28 uncommitted frontend files; Harvey tests and Angular unit tests in CI; Harvey in docker-compose |
| S2 | M1 all; R-M2-1, R-M2-2 upload checks; security baseline | Rate limits attached; forwarded headers; CSP |
| S3 | M11 rules 1–5, 8, 9 | Lawyer re-approves every rule under their own account; Critical set where it applies |
| S4 | M8 all; R-M2-3, R-M2-4 intake job and OCR | Delete the C# rule engine and its tests (E39); remove keyword compliance |
| S5 | M5 all; M3 all (coordinate migration) | Choose the seal certificate provider and the timestamp authority |
| S6 | M4 all; M7 all | Remove in-place PDF editing (E34) |
| S7 | M6 all; M9 all | Export and retire the regulatory watch |
| S8 | M10 all; M12 all | Holiday calendar seeded |
| S9 | M13 all; M11 rules 6, 10–12 | k6 load test; outside penetration test |
| S10 | Fixes only | Restore drill; pilot client onboarding; user guide in FR and EN |
