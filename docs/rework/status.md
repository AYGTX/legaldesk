# Rework status, sprint by sprint

Carried over from the local session notes on 2026-11-01. Update this file at the end of every sprint.

Rework spec: https://claude.ai/artifact/AaUWp2zVCKe5LtFjxH6M13 (Claude doc; main tab + "Module specs" tab).
User said (2026-10-29): no demo pressure, aim for a product that wins clients, and let Claude make the decisions.

Decisions made there: D1 checklist risks only from Harvey /review using approved rules only (model never creates
items); D3 model off by default per organisation; D5 readiness score without compliance; D6 regulatory watch retired;
D7 multi-tenant (OrganisationId + global filter); D9 sealed PAdES-B-T final PDF; D10 hashed 256-bit signing tokens +
e-mail OTP; D11 invitation-only + OIDC + TOTP; D12 obligations as a list, never invented dates; D13 local + S3 only.

Highest-impact code facts found (verify before citing; they were true at commit 845d5bc):
Harvey review uses unconfirmed rules; Harvey has no Critical severity; Harvey approvals are typed names on one shared
admin login; owner can mark a Critical "Resolved" and lift Blocked; revision resets verdict to NotEvaluated and send
gate lets it through; PDF in-place edit leaves old text in the file; signature Hash/Certificate never set; owner sees
recipients' tokens; field coords stored at PDF.js scale 1.5; alert ladder fires nothing before a 90+ day notice
deadline; upload invents a "today + 1 year" obligation; C# ContractRuleEngine.Analyze is dead code tested by 7 files.

Progress (2026-10-29, branch rework/s1-foundations, 8 commits off 845d5bc, not pushed, no remote): S1 done =
state machine (R-M2-5/6), permission table Permissions.cs + [RequirePermission] (R-M1-7), tenancy M0
(Organisation, TenantEntity on 28 tables + users, named filters Tenant/SoftDelete, TenantResolutionMiddleware,
PlatformQueries, default org 6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11), outbox for e-mail (R-M4-9), Harvey in CI.
Tests 372 unit + 124 integration + 148 Harvey green. S1 left: Harvey in docker-compose, error codes app-wide,
engine transactions (moved to S6). Migrations AddOrganisations + AddOutbox apply to SignFlowDb_Dev on next API start.
E-mail bodies are still a placeholder (template name + JSON model) → R-M13-5.
S2 (2026-10-30, branch rework/s2-identity off s1, 5 commits, not pushed): UserSession (hashed rotating refresh in
HttpOnly cookie ld_session, path /api/v1/auth), TOTP via Identity + hashed recovery codes, MfaSetupGateMiddleware
(off in Development via Security:RequireMfaForPrivilegedRoles), invitations replace register/admin-create/delete,
reset links from Security:PublicUrl, partitioned rate limits (RateLimits.cs), PdfUploadGuard + ClamAV
(Security:ClamAv; Required=false only in Development), nginx CSP, Harvey+ClamAV in compose. Tests 372+146 green.
S2 left: OIDC SSO (no IdP to test), admin-action audit (needs nullable AuditLog.DocumentId), absence delegate (→S6).
Browser flow (cookie refresh, MFA screens) NOT tested in a browser; Docker files not run (no Docker on machine).

**How to apply:** cite rule ids (R-M8-4 etc.) in tickets/tests; work sprint by sprint (S1 = tenancy, permissions,
state machine, outbox). Related: [[project-status-backlog]], [[project-harvey-review]], [[project-clients-plan]].

S3 (2026-10-30, branch rework/s3-harvey off s2, commits 780f654 Alembic + a076b3a M11, not pushed): Harvey accounts
(Reader/Editor/Lawyer + admin, scrypt, TOTP, lockout), approvals record the account, rule_versions (reviews serve the
latest approved version; four-eyes), Critical severity, /review takes disabled_rules + severity_overrides and returns
grid_version + per-finding rule_version/detection/confidence/quote; API key mandatory; run via create_app --factory.
Gotcha: SQLite batch migration + ON DELETE CASCADE wiped all articles — runner turns FKs off and checks after.
The local real DB has 0 approved rules (there was never an approve button); local admin must change "admin" + set TOTP.
S3 left: R-M11-6/7/10/12 (→S9); no browser test. Next = S4 (grid-only review, M8).

S4 (2026-10-30, branch rework/s4-review off s3, commits b839f46 backend+Harvey, ed6bad0 web, dee8acf intake/OCR,
030bfdd no-rules guard; not pushed): findings only from Harvey /review with approved rules (C# rule engine, model
analysis, all compliance code and AiRisk/AiCompliance JSON deleted). ReviewRun + Hangfire job (POST reviews 202,
GET reviews/latest, PATCH review-items; SignalR "review-progress"), states Open/Fixed/Accepted/NotApplicable,
grading 25/10/3/1, send gate (NotEvaluated refused, Blocked refused, NeedsWork needs reason → SentWithOpenRisks
audit). OrganisationRuleSetting + Admin > Review rules page. NotApplicable → Harvey review_feedback (0003).
Intake job after upload/revision: text, OCR (pdftoppm+tesseract CLI, Ocr settings; not installed locally),
then auto-review; IntakePending blocks manual review (review.intake_pending). Zero rules in service →
run fails review.no_rules (local Harvey has 0 approved rules, so every local review fails until a lawyer approves).
Migration ReviewRuns resets every verdict to NotEvaluated; checked on a copy of dev DB (295 items kept).
S4 not done: R-M8-10 determinism set of 20 contracts in CI; per-family rule sets (R-M8-1) and R-M8-12 required
mentions as Harvey checks (Harvey has no families); as-of date recorded but unused by Harvey; no browser test,
OCR never run for real, Docker not run. Next = S5 (M5 + M3: obligations as a list, E10; coordinate migration).

S5 (2026-10-31, branch rework/s5-signing off s4, commits fc39ecb access, c935c24 seal, a204519 M3, 6512b1c web;
not pushed): signing links = SHA-256 only (migration hashed existing tokens, so old links work), ISigningAccess gate
with coded refusals, 6-digit e-mail code -> SigningSession (header X-Signing-Session, 30 min sliding), account
sessions via POST requests/{recipientId}/signing-session + /sign/session?r=..., resend-link replaces copy-link,
evidence on Signature (verified e-mail/method, version, file+image SHA-256), PNG re-encode (own decoder, no deps),
server-filled dates (Organisation.TimeZoneId/DateFormat). Seal: evidence page + PAdES-B-T (PDFsharp placeholder,
SubFilter swapped in place to ETSI.CAdES.detached, SignedCms + signing-certificate-v2 + RFC3161), Hangfire
SealDocumentJob, Document.FinalSha256/SealedAt, public /verify/{code}. Dev uses a self-signed TEST seal in
App_Data (git-ignored); TSA default DigiCert public. M3: coordinates in PDF points (migration /1.5), stamper handles
/Rotate (PdfPig reports letters in DISPLAYED space - test gotcha), layout validation, LayoutVersion 409, owner
POST documents/{id}/detect-fields, field-layout-report. Tests: unit 334, integration 191.
S5 not done: Adobe/EU DSS validation of the seal and a live TSA call; SMS; delegate endpoint (S6); browser test.
Next = S6 (M4 + M7, remove in-place PDF editing E34).

S6 (DONE 2026-10-31, branch rework/s6-circuits off s5, commits 0e1a51f M4, 1eafb49 M7 backend, cf0f160 web; not
pushed): ContractRound per send (revision closes round, next send opens N+1; steps/signatures carry RoundId), stages by
Order with StageRule All/Any, Team/TeamMember pools (pool@legaldesk.invalid, members act via account session),
hand-over POST sign/{token}/delegate (old link 410 link.delegated), vacation stand-in, WorkingCalendar + holidays,
StepFollowUpJob hourly. M7: in-place PDF edit removed (E34); Word round trip = GET documents/{id}/docx (text-only DOCX)
+ .docx accepted by revision upload, converted by LibreOffice headless (Office:SofficePath; NOT installed locally ->
revision.docx_unavailable; Dockerfile installs libreoffice-writer-nogui; never run for real). DocumentVersion stamps
FileSha256/TextSha256/TextSource/RoundId of the OUTGOING version. Comments Internal/Participants (migration keeps old
ones Participants), mention picker via GET documents/{id}/mentionables. Compare: clauses matched without leading
number, Moved type. Hub JoinDocumentGroup checks DocumentReadAccess; Redis backplane only if SignalR:RedisBackplane
(on in compose); presence list itself is still per-instance memory. GET documents/{id}/rounds, GET vacations/stand-ins.
Web: Admin > Teams (teams + days off), participant circuit settings in new-contract flow, hand-over dialog, rounds card,
coded errors via core/utils/problem-text.ts (errors.* keys). Tests: unit 337, integration 206. Migration checked on a
DB copy. NOT done: browser test of any S6 screen; roles as pools in the UI (API supports PoolRole); presence in Redis.
Next = S7.

S7 (DONE 2026-11-01, branch rework/s7 off s6; S7 = M6 all + M9 all + retire regulatory watch).
DONE commit f02f80d (M9): Clause + ClauseVersion (immutable once approved; Redraft rewrites an open draft in place,
else drafts n+1) + ClauseRuleLink; approve = Manager/Admin, never the version's author (ForbiddenAccessException now
has Code -> 403 problem "code" clause.ownWording); approving deprecates previous approved; ImportApproved() for
seed/migration only. API: GET clauses, /approved (ApprovedClauseDto), /for-rule/{key}, POST clauses (RuleKey),
PUT {id} (describe only), POST {id}/versions, /versions/{n}/approve|deprecate, PUT {id}/rules. Findings in
readiness-checklist show library clause before Harvey model clause. SafeHtml (HtmlSanitizer pkg, allow-list) cleans
template + clause HTML on save; renderer HtmlDecodes {{#if}} conditions. Annex: deprecated wording w/o approved
replacement is left out of new contracts. Regulatory watch removed everywhere; export kept in raw-SQL table
LegacyExports (Name='regulatory-watch', JSON per org). Migrations ClauseVersions + RetireRegulatoryWatch checked on DB
copy down/up. Tests: unit 346, integration 199 (LegalCorpusTests deleted with the watch).
DONE commit 298bd6e (R-M6-4): ChromeRenderer singleton (Pdf:ChromeSandbox default true, TimeoutSeconds 30,
MaxConcurrentPages 4), JS off, contract served via request interception from https://contract.legaldesk.invalid/, all
else aborted, plus --proxy-server=127.0.0.1:9 --proxy-bypass-list=<-loopback> (Chrome preconnects otherwise reached
loopback). Real-Chrome tests [Trait Category=Chrome]: no connection; 20 renders ~10 s. Dockerfile USER $APP_UID (never
run in Docker). DONE commit 64eb3c3 (R-M6-5/6): VersionedTemplate base (Status Draft/InLegalReview/Approved/Retired,
Version, ParentTemplateId, Submitted*/Approved*); Update on Approved/Retired -> ConflictException template.notEditable;
API template-versions/{html|pdf}/{id}/submit|approve|retire|next; approve = Manager/Admin after Harvey review, Critical
finding -> template.criticalFindings, retires parent; requests only from Approved (template.notApproved);
Document.TemplateVersion; template requests never start the circuit (StartImmediately removed; HTML now navigates to
/contracts/review); migration TemplateVersions makes existing templates Approved v1; seeders ImportApproved.
Tests: unit 346, integration 205 (+2 Chrome).
DONE commit dcdeb91 (R-M6-2): template DefaultApprover + field PreAssignedApprover removed everywhere; field
WorkflowStage renamed FilledByStep; routing = template preset, else family DefaultPresetId, else requester (fields past
the last step too). Migration TemplateRoutingFromPreset turns fixed approvers into presets (temp tables #RoutingHtml/
#RoutingPdf - one #Routing twice in a batch fails 2714). DONE commit 72c2b0e (R-M6-7/8): LockedClauses (AngleSharp)
- editor inserts <div class="library-clause" data-clause-id data-clause-version data-locked>, save refuses edited words
(clause.lockedEdited) or Draft wording; Document.LibraryClausesJson (HTML blocks + PDF annex) shown on contract page;
RequestType.DefaultPresetId + RuleSetKeys (banking, competition, consumer, data-protection, employment,
public-procurement) - recorded only, Harvey has no rule sets. Kept code name RequestType and UI "contract type".
Whole S7 chain checked on dev DB copy down to S6 and up. Tests: unit 346, integration 205 (+2 Chrome). Not pushed.
S7 NOT DONE: no browser test of any S7 screen; Docker non-root/sandbox untested; locked block can still be edited in
the editor textarea (refused only at save); existing templates' already-inserted clauses are plain (not locked).
Next = S8 (M10 lifecycle + M12 assistant).

S8 (DONE 2026-11-01, branch rework/s8 off s7, 3 commits, not pushed; S8 = M10 + M12 + holidays). Commit e6478a7 (M10): ContractTerm 1:1
(RenewalMode None/Automatic/Option, RenewalMonths, NoticeAmount+NoticeUnit Days/Months, NoticeForm, ConfirmedBy/At,
LastAlertAnchor/Offset, EndAlertSent, Decision+NoticeSentAt+NoticeProofPath), Obligation list (kind, due, recurrence,
owner, quote, source Person/Harvey, status, Confirmed), ContractEvent; Document term/obligation columns dropped.
TermLadder.Due (offsets Organisation.AlertOffsets "60,30,14,7,0", anchor = notice deadline else end; then end rung),
ContractTermEvaluator.Evaluate(start,end,noticeDeadline,asOf). LifecycleSweepJob hourly "lifecycle-sweep", runs per
org once/day after 08:00 local (Organisation.LastLifecycleSweepOn), rollover + ladder + obligation reminders (7,0),
FR/EN by user PreferredLanguage, notification + e-mail templates LifecycleAlert/ObligationDue. API: GET
documents/{id}/lifecycle, PUT .../term, POST .../term/decision (multipart proof), obligations CRUD/actions;
calendar: POST/DELETE calendar/feed, anon GET calendar/{token}.ics (hash on ApplicationUser.CalendarTokenHash,
lookup via IPlatformQueries.UserOfCalendarTokenAsync, rate-limited Verify). ContractDates.Parse (dd/mm/yyyy, FR/EN/AR).
Intake no longer suggests dates (R-M12-9). Migration ContractLifecycle (dev DB: 33 guessed obligation dates dropped).
Commit 055c401 (M12): ModelOutput (doc, version, feature summary|explainer, provider, model, PromptVersion, ContentJson,
Key "2->3"); Document.AiSummary* dropped, ShareSummaryWithParticipants set at send (SendDocumentRequest.ShareSummary).
ModelAccess (scoped): RequireOnAsync 404 code model.off (Organisation.ModelMode Off or server has no model);
BeginAsync 30/h per contract + 300/day per person counted from AuditLogs ModelUsed(42) -> TooManyRequestsException 429
model.limit; RecordAsync audits feature/provider/model/chars, names the recipient on the signing page. Summary
(Features/Assistant): clause batches 6000 chars, max 40, NotRead listed, quotes checked by QuoteMatcher, stored per
version. Explainer POST documents/{id}/versions/explain?from&to. Chat/key terms/renewal note gated+labelled;
template drafting RequireOn only. Organisation.SetModelMode had NO setter before: new admin page /admin/settings
(OrganisationSettingsController GET/PUT organisation-settings, ManageReviewSettings; model on/off + AlertOffsets) and
GET assistant/status (frontend AssistantService.enabled hides every model UI). Migration ContractAssistant checked on
DB copy. Commit a7664c4: OrganisationHoliday.TunisianReligious2026To2028 (estimated, one-off), seeder adds missing ones
to existing DBs, skips a year that already has a one-off holiday. Tests unit 350, integration 219.
S8 NOT DONE: no browser test of any M10/M12 screen; R-M12-6 regression set in CI not built; frontend ng test not run;
ScenarioFixture has SetModelAsync(on) + FakeModelInfo for tests. Religious dates are estimates, admins confirm.
Next = S9 (M13 + M11 rules 6, 10-12 + k6 load test + outside pen test).
