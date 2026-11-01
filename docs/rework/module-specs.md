<!-- Copied from the Claude doc https://claude.ai/artifact/AaUWp2zVCKe5LtFjxH6M13 on 2026-11-01. The doc is the source; this copy lets sessions without the doc connector read it. -->

# Module specs

One section per module, in the same outline: scope, what the code does today (checked 29 October), defects, target rules, data, API, acceptance. Rule ids (R-M5-3) are what tests and tickets cite.

## M0. Organisations and tenancy (new)

**Scope.** Let several client organisations share one install with no way to see each other's data (D7).

**Today.** No Organisation entity. `GetDocumentsQuery` and `GetContractScheduleQuery` show every contract in the database to any Administrator. Background jobs scan all contracts. Storage paths have no tenant prefix.

**Target rules**

1. R-M0-1. `Organisation`: Id, Name, Slug (unique), Country, DefaultLanguage (fr, en), ModelMode (Off, Groq, Local; default Off), RetentionYears (default 10), SealCertificateRef, IsActive, CreatedAt.
2. R-M0-2. Every business table carries a non-null `OrganisationId` with an index leading on it: users, counterparties, documents and all their children, templates, presets, request types, clauses, notifications, audit, consent, data requests, rule settings.
3. R-M0-3. One EF Core global query filter per entity: `OrganisationId == tenant.Id && !IsDeleted`. `SaveChanges` stamps `OrganisationId` on insert and throws if a tracked entity's value differs from the current tenant.
4. R-M0-4. The tenant comes from the `org` claim of the access token; for a signing link, from the recipient row the token resolves to; for a job, from an explicit `TenantScope` opened per organisation. No request runs without a tenant except platform endpoints.
5. R-M0-5. Raw SQL and `IgnoreQueryFilters()` are banned outside one reviewed class (`PlatformQueries`); an architecture test fails the build otherwise.
6. R-M0-6. Storage keys start with `{organisationId}/`. A download checks the key prefix against the tenant before reading.
7. R-M0-7. Harvey stays tenant-free (law is public). Per-organisation choices about the grid live in LegalDesk: `OrganisationRuleSetting` (RuleKey, Enabled, SeverityOverride, ChangedBy, ChangedAt).
8. R-M0-8. A platform operator role, outside any tenant, creates organisations and their first Administrator. It cannot read contracts.
9. R-M0-9. Migration: all existing rows go to one organisation created by the migration (name from configuration, default "Consultim IT").

**API.** `GET/POST /platform/organisations`, `PATCH /platform/organisations/{id}` (operator); `GET/PATCH /organisation` (Administrator: name, language, model mode, retention).

**Acceptance**

- [ ] For each entity, an integration test creates rows in organisations A and B and proves a user of A gets 404 on B's ids and never sees B's rows in lists, reports, search, exports or SignalR groups.
- [ ] Each Hangfire job runs once per organisation and its notifications stay inside it.
- [ ] The architecture test fails on a new `IgnoreQueryFilters()` outside `PlatformQueries`.

## M1. Identity and administration

**Scope.** Who each user is, how they sign in, what they may do, and who covers when they are away.

**Today (verified)**

- ASP.NET Identity; roles seeded: Administrator, Manager, Employee, ExternalSigner (`DatabaseSeeder.cs:24`). ExternalSigner is assigned to nobody: signers are recipients, not users.
- `POST auth/register` is public and adds the caller to Employee (`RegisterCommand.cs:60`).
- Access token 60 min. One refresh token per user, stored in clear on `ApplicationUser`, 7 days (30 with remember-me): a second device's login ends the first session. The browser keeps both tokens in `localStorage`.
- Password policy 8 characters with upper, lower, digit, symbol; lockout 5 failures for 15 min. A deactivated account answers "Account is deactivated" before the password is checked, which tells an attacker the e-mail exists.
- `MfaEnabled` and `MfaSecret` exist on the user and are never read.
- Reset token kept on the user row for 24 h.
- Deleting a user is refused when they own contracts, and for the last Administrator. Admin actions on users are not audited: `AuditLog.DocumentId` is required, so an event without a contract cannot be recorded.
- Vacations: create, list, cancel, and an availability check when building a circuit. No delegate is named.

**Target rules**

1. R-M1-1. No self-registration. An Administrator invites by e-mail; the link is single-use, stored hashed, valid 72 h, and lets the person set a password or link SSO.
2. R-M1-2. Single sign-on by OpenID Connect per organisation (Microsoft Entra ID first). An organisation may disable password login once SSO works.
3. R-M1-3. Password login requires TOTP for Administrators and Managers, optional for others; 10 single-use recovery codes stored hashed.
4. R-M1-4. Sessions: a `UserSession` row per device (token hash, created, last used, IP, user agent, expiry 14 days idle, 90 days absolute). Refresh rotates on every use; reuse of an old token revokes the whole session. Access token 15 min, kept in memory; refresh token in an HttpOnly, Secure, SameSite=Strict cookie.
5. R-M1-5. Login answers the same message and timing for unknown e-mail, wrong password and deactivated account. Rate limit: 10 attempts per 15 min per IP and per e-mail.
6. R-M1-6. Password reset token: single use, hashed, 60 min. A reset or change revokes every session.
7. R-M1-7. Roles: Administrator (users, settings, everything), Manager (legal team: accepts Critical risks, sees reports, edits templates, presets and clauses), Employee (own contracts). ExternalSigner is deleted. Rights are declared in one `Permissions` table (role → permission) and checked by one `[RequirePermission]` attribute, never by scattered `IsInRole` calls.
8. R-M1-8. Absence names a delegate. While the absence runs, steps that would open for the absent user open for the delegate instead, with an audit entry naming both. A step already open moves only if the absent user or the owner chooses.
9. R-M1-9. Every user and settings change by an Administrator writes an organisation audit entry (M13).

**Data.** ApplicationUser + OrganisationId, PreferredLanguage, MfaEnabledAt; drop RefreshToken, RefreshTokenExpiryTime, PasswordResetToken, PasswordResetTokenExpiry from the user. New: UserSession, UserInvitation (email, role, token hash, expires, accepted at), RecoveryCode, Vacation + DelegateUserId.

**API**

| Endpoint | Who | Purpose |
|---|---|---|
| POST auth/login, auth/mfa, auth/refresh, auth/logout | Public / session | Password + TOTP, rotate, end session |
| GET auth/sso/{org}/start, auth/sso/callback | Public | OIDC sign-in |
| POST auth/invitations/accept, auth/forgot-password, auth/reset-password | Public | Invitation and recovery |
| GET/POST admin/users, PATCH admin/users/{id}, POST admin/users/{id}/deactivate | Administrator | No hard delete; deactivate and keep history |
| POST admin/invitations, DELETE admin/invitations/{id} | Administrator | Invite |
| GET/DELETE profile/sessions/{id} | Self | See and end sessions |
| POST profile/mfa/setup, profile/mfa/confirm, profile/mfa/disable | Self | TOTP |
| GET/POST/DELETE vacations | Self | Absence with delegate |

**Acceptance**

- [ ] `POST auth/register` returns 404.
- [ ] A stolen refresh token used after its rotation revokes the session (test).
- [ ] An Administrator without TOTP cannot reach any endpoint except MFA setup.
- [ ] Login timing for unknown and known e-mail differs by less than 50 ms at the 95th percentile over 100 tries.
- [ ] A step for an absent user opens for the delegate and the audit names both.

## M2. Contracts, storage and status machine

**Scope.** One record per contract that carries its file, versions, parties, review and lifecycle, and moves only through allowed states.

**Today (verified)**

- Upload checks the name ends in .pdf and the client's content-type header; it does not check the file starts with `%PDF`. Limit 100 MB on the API, 50 MB in both browser pages.
- Upload runs, inside the request: the extension-only virus check, page count, SHA-256, text extraction, Harvey's review, the playbook attachment, an obligation guess and a term guess (`UploadDocumentCommand.cs:56-102`). A scanned PDF has no text, so it gets no review and no warning at upload.
- `Document.SetStatus` accepts any value; Pending, WaitingForOthers and Cancelled are never set (E15). Delete is allowed in every status except InProgress (E3).
- `HasWatermark`, `WatermarkText`, `IsPublic`, `FolderId`, `VerificationCode` and `ParentDocumentId` are never written by any feature. The legacy `Template` entity and `POST /templates` are not called by the web app.
- `LocalStorageService.DownloadAsync` copies the whole file into memory before streaming it.
- Downloads and views write no audit entry (E20).

**Target rules**

1. R-M2-1. Upload accepts a file only if its first bytes are `%PDF-`, it opens with PdfPig, it is not encrypted, and it is at most 50 MB (one limit, everywhere).
2. R-M2-2. Every upload is scanned by ClamAV (clamd over TCP). Infected → 422 and an audit entry; scanner down → the upload waits in quarantine and the user is told, never silently accepted.
3. R-M2-3. The upload request stores the file and returns. Text extraction, OCR, review and suggestions run as one background job (`ContractIntakeJob`) that updates the contract and notifies the owner over SignalR.
4. R-M2-4. A PDF with less than 200 characters of text per page on average goes through OCR (Tesseract, French + English + Arabic) before review. The contract shows "text from OCR" and the review's quotes are marked as such.
5. R-M2-5. Status changes go through one `ContractStateMachine` with the transitions in the table below; any other transition throws and is a bug.
6. R-M2-6. Only a Draft can be deleted by its owner (soft delete). An Administrator may cancel, never delete, a contract in circuit. Completed contracts are never deleted before the organisation's retention period (M13).
7. R-M2-7. Each stored file records SHA-256, size, page count, text source (embedded, OCR) and storage key; keys are `{org}/{contract}/{version}/{kind}.pdf`.
8. R-M2-8. Storage providers: local disk and S3-compatible (D13), chosen per install. Files are streamed, never buffered whole. S3 uses server-side encryption; local disk relies on encrypted volumes, documented in the install guide.
9. R-M2-9. Every download and inline view writes `DocumentDownloaded` or `DocumentViewed` with user, IP and version number.
10. R-M2-10. Remove the unused fields listed above and the legacy Template entity and endpoint in one migration.

**Status transitions**

| From | To | Trigger | Who |
|---|---|---|---|
| Draft | InProgress | Send passes the readiness gate (M8) | Owner |
| InProgress | Completed | Last required step approved | System |
| InProgress | Rejected | A participant declines with a reason | Participant |
| InProgress | ChangesRequested | A participant requests changes with a reason | Participant |
| InProgress | Expired | Deadline passed (SLA or expiry job) | System |
| InProgress, ChangesRequested, Expired | Cancelled | Cancel with a reason; every open link stops working | Owner, Administrator |
| ChangesRequested, Rejected | Draft | A new version is saved (M7) | Owner |
| Expired | InProgress | Reopen with a new deadline | Administrator |

Pending and WaitingForOthers are removed from the enum; "waiting for others" is computed from steps, not stored.

**API changes.** Add `POST documents/{id}/cancel` (reason required). `DELETE documents/{id}` only for Draft. `GET documents/{id}/download?version=n` replaces the separate version route. `stream` and `download` share one access check and one audit write.

**Acceptance**

- [ ] A text file renamed .pdf is refused with 422.
- [ ] The EICAR test file inside a PDF is refused and audited.
- [ ] A unit test walks every (from, to) pair: allowed pairs pass, all others throw.
- [ ] Deleting a Completed contract returns 409 for every role.
- [ ] A scanned 10-page contract gets OCR text, a review, and the "text from OCR" label.
- [ ] Downloading a 50 MB file keeps API memory growth under 10 MB.

## M3. Field designer

**Scope.** Place every field a participant fills or signs on the PDF, assign it to one participant, and put it on the final PDF exactly where it was placed.

**Today (verified)**

- 19 field types (`FieldType`): Signature, Initials, Date, Name, Email, Company, Title, Text, MultilineText, Number, Checkbox, Radio, Dropdown, Image, Stamp, Attachment, QrCode, Barcode, Custom.
- Positions are stored in PDF.js canvas pixels at a fixed render scale of 1.5, not in PDF units. The designer, the signing page, the demo seeder and `PdfStampService` each hard-code 1.5. `Rotation` is saved but the stamper ignores it.
- `PUT documents/{id}/fields` replaces the layout: owner only, Draft only, at least one field. It does not check that the field's recipient belongs to this contract, that the page exists, or that the box sits inside the page. It loads fields one by one (N+1) and has no concurrency check.
- Fields with signatures from an earlier round cannot be deleted, only moved (evidence kept).
- "Detect fields" in the contract designer calls the PDF-template endpoint `POST pdf-templates/detect-fields`, which needs Administrator or Manager: an Employee preparing a contract gets 403.
- The designer palette offers 13 types; MultilineText, Number, Image, QrCode, Barcode and Custom exist only in the enum. Attachment is offered, but the signing page has no upload for it. Palette labels are hard-coded in English (`designer.component.ts:64-76`).

**Target rules**

1. R-M3-1. Coordinates are PDF points, origin at the top-left of the page as displayed (after the page's own /Rotate), stored as x, y, width, height. Every client converts at its own zoom; no constant is shared.
2. R-M3-2. The stamper applies page rotation and the field's own rotation; a golden-file test covers pages rotated 0, 90, 180 and 270.
3. R-M3-3. Save validates: page ≤ page count, box inside the page, recipient belongs to this contract, a signature-type field is assigned to a participant whose action can sign (Signature or Stamp), options present for Dropdown and Radio. A layout may be empty (approval-only circuits).
4. R-M3-4. Save sends the contract's row version; a stale layout returns 409 with the current one.
5. R-M3-5. Field types are cut to what the product supports end to end: Signature, Initials, Stamp, Date (signing date, auto), Name, Email, Company, Title, Text, MultilineText, Number, Checkbox, Radio, Dropdown. Image, Attachment, QrCode, Barcode and Custom are removed until a client needs them.
6. R-M3-6. Signing progress counts Signature, Initials and Stamp only; required fills block approval like today (`RequiredFieldRules`).
7. R-M3-7. Field detection for a contract has its own endpoint `POST documents/{id}/detect-fields`, open to the contract owner, and never saves: the owner accepts or drops each proposal.
8. R-M3-8. One migration converts existing coordinates: divide by 1.5 and normalise for page rotation; a script reports any field that falls outside its page after conversion.

**Data.** SignatureField: drop Rotation's ambiguity (degrees, 0/90/180/270 only), drop ZIndex, VisibilityRules, FieldName, MaxLength/MinLength unless a validation rule uses them; keep TemplateKey.

**Acceptance**

- [ ] A field placed at the bottom-right corner of a landscape page lands within 1 pt of that corner on the final PDF.
- [ ] Two owners saving the same layout: the second gets 409.
- [ ] An Employee can detect fields on their own contract.
- [ ] Assigning a field to another contract's recipient returns 422.

## M4. Circuits and workflow engine

**Scope.** Decide who acts on a contract and in what order, move it from one step to the next, and chase people who are late.

**Today (verified)**

- Actions (`ApprovalType`): Signature, Approval, Review, ViewOnly, Stamp, Reject. ViewOnly recipients get no step. "Reject" as an action has no meaning distinct from declining.
- Circuit types Sequential, Parallel, Mixed. Stages are built by grouping recipients on `Order`; only Mixed marks a shared order as a parallel group (`WorkflowEngine.cs:57-70`).
- Signing turn: a recipient may act only when every required recipient with a lower `Order` is Completed or Skipped (`SignFieldCommand`).
- Presets: steps name a user, a role or a department, optionally conditional on a template answer (== != > >= < <=). A role or department step must resolve to exactly one active person, else the request fails with an English message (`WorkflowPresetResolver.ExactlyOne`). Department is free text on the user.
- Reminders, escalation, step due dates, step conditions and delegation are fields only (E4, E5). The only time-based job is the SLA sweep every 15 min.
- `WorkflowEngine` calls `SaveChanges` at 10 places, several per operation, with no transaction, and sends e-mails inline: an SMTP failure after the save returns an error for an action that did happen.
- Recipient access to circuit data is by e-mail match (`GetDocumentByIdQuery.cs:102`).

**Target rules**

1. R-M4-1. Actions are: Sign, Approve, Review (acknowledge, no signature), Stamp, CC (view only, no step). "Reject" is removed as an action; any acting participant may decline.
2. R-M4-2. A circuit is an ordered list of stages; a stage holds one or more steps; a stage completes when its rule is met: All (every step) or Any (first step completes the stage, the others are closed as Skipped). Sequential = one step per stage; Parallel = one stage. Mixed is just stages.
3. R-M4-3. A step's assignee is a person, or a pool (role or team). A pool step is visible to every member; the first member to act takes it, and the audit names who acted for the pool.
4. R-M4-4. Teams replace the free-text department: `Team` (name, members) per organisation; presets and pools reference teams.
5. R-M4-5. Signing turn follows stages: a step is actionable only when its stage is open. The UI never decides turn; the API does.
6. R-M4-6. Each step can carry: due date (absolute or N working days after it opens), reminder every N days (default 3, max 5 reminders), escalation after N days to the step's escalation contact or the contract owner's Manager. A Hangfire job `StepFollowUpJob` runs hourly and is idempotent (last reminder time stored per step).
7. R-M4-7. Conditions are only those of presets (template answer compared to a value); free-text `ConditionalExpression` is removed.
8. R-M4-8. Delegation: an acting participant may hand their step to another person with a note; the new person gets a new link; the old link stops working; the audit records both. An internal user's absence delegate applies automatically (R-M1-8).
9. R-M4-9. Every engine operation runs in one database transaction. E-mails, SignalR and notifications go through an outbox table and a dispatcher job: a failed e-mail is retried (5 times, exponential backoff) and never fails the user's action.
10. R-M4-10. Working days use the organisation's calendar: weekends Saturday and Sunday by default, plus a holiday list (Tunisian public holidays seeded).
11. R-M4-11. Engine errors are returned as codes with parameters (`preset.step.poolEmpty`, `{step: 3, team: "Legal"}`), translated in the UI.

**Data.** Workflow → Circuit; WorkflowStep gets StageNumber, StageRule (All, Any), AssigneeUserId or AssigneeTeamId, DueAt, NextReminderAt, ReminderCount, EscalatedAt, DelegatedFromStepId. New: Team, TeamMember, OrganisationHoliday, OutboxMessage (type, payload, attempts, next attempt, sent at, error).

**API**

| Endpoint | Purpose |
|---|---|
| PUT documents/{id}/circuit | Stages, steps, assignees, due dates, reminders (Draft only) |
| POST sign/{token}/delegate | Hand my step to someone else with a note |
| GET/POST/PUT/DELETE teams | Manage teams (Administrator) |
| GET/POST/PUT/DELETE workflow-presets | Presets on stages, people, teams, conditions (Administrator) |

**Acceptance**

- [ ] A two-stage circuit, stage 1 "Any of Legal team (3 people)": the first approval closes the other two steps as Skipped and opens stage 2.
- [ ] A step due in 2 working days opened on Thursday is due Monday end of day (Tunis time).
- [ ] With SMTP down, approving a step succeeds; the e-mail is sent when SMTP returns.
- [ ] After delegation the original link returns 410 Gone.

## M5. Signing and evidence

**Scope.** Let each participant read the contract and act through a secure link, and leave evidence that holds up when a signature is disputed.

**Today (verified)**

- Token: `Guid.NewGuid().ToString("N")`, stored in clear on `Recipient`, valid 30 days, renewed on revision. Returned to the owner and Administrators in the contract detail (E8).
- Expiry is checked only when the session opens; every action endpoint accepts an expired token (E6). No second factor.
- Before signing, approving, reviewing or stamping, the participant must acknowledge the contract and its review (`ReviewGateRules`).
- Sign field: checks turn, status, document expiry and field ownership, voids other choices of a radio group, stores the signature image under `signatures/{documentId}` with no size or format check, and records IP from `RemoteIpAddress` (no forwarded-header handling, so behind a proxy it records the proxy) and a browser geolocation string the client sends.
- `Signature.Hash` and `Certificate` are never set (E7). The final PDF is the original with images stamped on it (`PdfStampService`); it carries no seal, no timestamp and no evidence page, and its hash is not stored.
- Completion runs when the last required participant approves (`WorkflowEngine.cs:358`).
- The "Date" field is filled by the signer's browser (`new Date().toLocaleDateString()`), so its value and format follow the signer's clock and locale, and the API stores whatever text it receives.

**Target rules**

1. R-M5-1. Link token: 32 random bytes from `RandomNumberGenerator`, base64url, shown once in the e-mail, stored as SHA-256. Nobody can read it back, including Administrators.
2. R-M5-2. Every `sign/{token}` endpoint goes through one filter that resolves the token, checks expiry, the contract's status, the participant's status and whether the step is open; a refusal returns a code the page explains (expired, not your turn, contract cancelled, already done).
3. R-M5-3. Before the first action, the participant enters a 6-digit code sent by e-mail (valid 10 min, 5 tries, then a new code). Organisations may add SMS later through the same interface. The verified e-mail and time go into the evidence.
4. R-M5-4. A signing session lasts 30 minutes after the code, renewed by activity; the token alone never acts.
5. R-M5-5. Evidence per action: participant name and e-mail, verification method and time, IP (real client IP via configured forwarded headers and known proxies), user agent, server time (UTC), document version number and SHA-256 of the exact file shown, field id, signature image SHA-256. Browser geolocation is dropped (not needed, and personal data).
6. R-M5-6. Signature image: PNG only, at most 500 KB and 1200 × 600 px, re-encoded by the server.
7. R-M5-7. Date fields are filled by the server at signing time in the organisation's time zone and format; signers cannot type them.
8. R-M5-8. On completion the system builds the final PDF: stamped fields, plus an evidence page (participants, actions, times, IPs, verification method, document hash per version, audit chain head), then applies a PAdES-B-T seal with the organisation's certificate and an RFC 3161 timestamp. Its SHA-256 is stored on the contract and in the audit.
9. R-M5-9. A public verification page `GET /verify/{code}` (code printed on the evidence page) confirms that an uploaded PDF's hash matches a sealed contract, without showing its content.
10. R-M5-10. The product says what this is: an advanced electronic signature with a qualified timestamp, not a qualified signature. Qualified signatures through a Tunisian provider (TunTrust) are a later integration behind `ISignatureProvider`.

**Data.** Recipient: TokenHash, TokenExpiresAt, VerifiedAt, VerificationMethod; drop SecureToken, SignedLocation. Signature: DocumentVersion, DocumentSha256, ImageSha256, VerifiedEmail; drop Location, Latitude, Longitude, Certificate. Document: FinalSha256, SealedAt, VerificationCode (now used). New: SigningOtp (recipient, code hash, expires, attempts).

**API (public, under /sign/{token})**

| Endpoint | Purpose |
|---|---|
| POST otp/send, POST otp/verify | E-mail code, opens a session |
| GET /, GET document, GET summary | Session data, the PDF version being signed, plain summary |
| POST fields/{fieldId} | Fill or sign one field |
| POST approve, decline, request-changes, delegate | Decisions (reason required for the last three) |
| POST acknowledge-review, POST checklist/{itemId} | Gate and checklist |
| GET/POST comments | Comments |
| GET /verify/{code} (no token) | Public seal check |

**Acceptance**

- [ ] The contract detail response contains no token or token hash for any role.
- [ ] Every sign endpoint called with an expired token returns 410 with code `link.expired` (one test per endpoint).
- [ ] The final PDF validates as PAdES-B-T in Adobe Reader and in the EU DSS demo validator.
- [ ] Changing one byte of the final PDF makes `/verify` report a mismatch.
- [ ] Behind the nginx container, the recorded IP is the client's, not 172.x.

## M6. Templates and requests

**Scope.** Business users produce correct contracts from templates legal approved, and the request routes itself.

**Today (verified)**

- Two template kinds. HTML templates: clause text with `{{field}}` placeholders, field types Text, Textarea, Date, Number, Checkbox, Signature, Dropdown, roles Initiator or Approver, rendered to PDF by PuppeteerSharp. PDF templates: a fixed PDF with fields (Text, Date, Number, Checkbox, Signature, Dropdown, Textarea), fill roles Requester, Approver, System.
- Routing is set in three places at once: the template's DefaultApprover, each field's PreAssignedApprover and WorkflowStage, and the template's WorkflowPreset; `CreateRequestFromHtmlTemplateCommand` merges them in a fixed precedence (preset > field > template > caller).
- Answers are HTML-encoded before insertion (`HtmlContractRenderer.cs:71`). But the new-request page shows the template HTML through `bypassSecurityTrustHtml` (`new-request.component.ts:360`): a Manager who writes a script into a template runs it in every requester's browser.
- Chrome is launched for every render with `--no-sandbox` and with network access, so template HTML can make the server fetch internal URLs.
- Template review calls Harvey's grid directly (approved and unapproved rules, E1). Request types: name, icon, colour, SLA hours, active.
- The legacy `Template` entity and `POST templates` are dead (M2).

**Target rules**

1. R-M6-1. Keep both kinds; they answer different jobs (generate a contract vs fill a fixed form). Both share one field model: key, label, type, required, options, default, auto-fill source, who fills it (Requester, an approver step, System).
2. R-M6-2. Routing comes only from the template's preset (M4). Template DefaultApprover, field PreAssignedApprover and field WorkflowStage are removed; a field filled by an approver names the preset step, not a person.
3. R-M6-3. Template HTML is sanitised on save with an allow-list (headings, paragraphs, lists, tables, bold, italic, underline, spans with class, placeholders); scripts, event handlers, iframes, external images and styles with url() are stripped. The web app never bypasses Angular's sanitiser.
4. R-M6-4. PDF rendering uses one long-lived headless Chrome per worker, sandbox on, JavaScript off, every network request blocked except data: URIs and the bundled fonts, 30 s timeout.
5. R-M6-5. A template has versions: Draft → In legal review → Approved → Retired. Only Approved versions are offered to requesters; a contract records the template version it came from. Approval needs a Manager and runs Harvey's review first; open Critical findings block approval.
6. R-M6-6. A request created from a template runs the pre-flight review (M8) before its circuit starts, always (not optional per template).
7. R-M6-7. Approved clauses from the library (M9) can be inserted into an HTML template as locked blocks, or attached to a PDF template as an annex; the contract records which clause versions it used.
8. R-M6-8. Request types become "contract families": name, colour, icon, SLA hours, default preset, and the Harvey rule sets that apply (bank, competition, consumer, data protection).

**Data.** HtmlTemplate and PdfTemplate get Version, Status, ApprovedBy, ApprovedAt, ParentTemplateId; drop DefaultApproverId; fields drop PreAssignedApproverId and WorkflowStage, add FilledByStepKey. Document gets TemplateVersionId. RequestType → ContractFamily (+ DefaultPresetId, RuleSetKeys).

**Acceptance**

- [ ] A template saved with `<img src=x onerror=alert(1)>` is stored without the handler, and no page ever runs it.
- [ ] Rendering a template with `<img src="http://169.254.169.254/">` makes no network call (proxy log empty).
- [ ] A requester sees only Approved template versions; a contract shows its template name and version.
- [ ] Rendering 20 contracts in a row takes under 20 s on the reference server (warm Chrome).

## M7. Collaboration, revisions, versions

**Scope.** Let the people on a contract discuss it and send it back for changes, and keep every version with what changed and who signed what.

**Today (verified)**

- Comments: threaded, resolvable, from the contract page and the signing link. Mentions are detected by finding e-mail addresses in the text (`AddCommentCommand.cs:115`) and notify those people.
- Revision upload (Draft or ChangesRequested): old file kept as `DocumentVersion` with the change note; signed fields voided but kept; recipients reset; the old `Workflow` and its steps are deleted (`UploadRevisionCommand.cs:117`), so the previous round's circuit survives only in the audit text.
- Template contracts are revised by changing answers; each version stores its answers.
- "Edit text in place" covers old text with white boxes; the old text stays in the file (E34).
- Compare: clause-level redline (`ContractDiffer`, word diff up to 4,000 words).
- Presence over SignalR, in process memory, no access check on joining a contract's group (E21).

**Target rules**

1. R-M7-1. Comments: threads anchored to a version and optionally to a page and position; mentions by picking a person (user or participant) from a list, not by typing an e-mail. Participants see only threads marked visible to them; internal notes stay internal.
2. R-M7-2. A revision creates a new version and a new round. Rounds are kept: each round has its circuit, steps, decisions and signatures, and the contract page shows them per round. Nothing is deleted.
3. R-M7-3. Revisions come from three sources only: a new uploaded file, new template answers (the PDF is regenerated), or a DOCX round trip (download the version as DOCX converted by LibreOffice headless, edit, upload). In-place PDF text editing is removed.
4. R-M7-4. Every version stores file SHA-256, text SHA-256, text source (embedded or OCR), author, change note, and the round it opened.
5. R-M7-5. Compare works between any two versions, clause by clause, with moved clauses detected; it is the input of the negotiation explainer (M12) when the model is on.
6. R-M7-6. After a revision the readiness verdict is NotEvaluated and sending is refused until a new review has run (R-M8-8).
7. R-M7-7. Presence uses a Redis backplane; joining a contract's group requires read access to it (same check as `GET documents/{id}`).

**Data.** New: ContractRound (number, opened at, opened by version, closed at, outcome). WorkflowStep, Signature and decisions get RoundId. Comment gets VersionNumber, Visibility (Internal, Participants), MentionedUserIds/RecipientIds. DocumentVersion gets TextSha256, TextSource, RoundId.

**Acceptance**

- [ ] After two rounds, the contract page lists both circuits with every decision and signature.
- [ ] An internal note is absent from every `sign/{token}/comments` response.
- [ ] A user without access to a contract who calls `JoinDocumentGroup` receives no presence events.
- [ ] The DOCX round trip of a 20-page contract keeps clause numbering (golden test on 3 sample contracts).

## M8. Pre-flight review and readiness gate

**Scope.** Before anyone is asked to sign, say whether the contract can go out, list exactly what to change with the rule and the law behind each point, and stop it while a critical risk is open.

**Today (verified)**

- `EvaluateReadinessCommand`: extracts text, runs `IAiService.AnalyzeContractAsync` (model first, Harvey only if no model, a failure, or zero findings), optionally compliance (off), attaches the grid's articles and model clauses (unapproved ones included, E1), reconciles items, grades, saves, audits.
- Item key `risk:{clauseType}` or `risk:{clauseType}|{reference}`; duplicates collapse (E14); open items missing from a re-run are auto-resolved (E9).
- Owner decisions: Resolved or Dismissed with an optional note. Only dismissing a Critical needs a Manager and a reason. Marking a Critical "Resolved" needs neither, and lifts the block without any change to the text.
- Verdict: open Critical → Blocked, open High → NeedsWork, else Ready. Score: 65 % × (100 − risk) + 35 % × compliance (constant 50 today) − 15 per Critical − 5 per High.
- Send gate: Blocked refuses; NeedsWork asks for `force=true`; NotEvaluated passes except for PDF-template contracts (E33).
- The review runs inside the HTTP request; the model call alone takes tens of seconds.

**Target rules**

1. R-M8-1. Source of findings: Harvey's `/review` only (D1). The request carries the clauses of the current version, the language, an as-of date (signature date if known, else today), the contract family's rule sets and the organisation's rule settings (R-M0-7).
2. R-M8-2. Harvey uses only approved rules and returns, per finding: rule key, rule version, label, severity, detection kind (CodeCheck or MeaningMatch), confidence (similarity and margin for meaning matches), clause reference, the exact quoted span, explanation, recommended action. LegalDesk attaches linked articles and the approved model clause from `/playbook` (approved only).
3. R-M8-3. One item per (rule key, clause reference). Two hits of the same rule in one clause become one item with both quotes; two clauses are two items.
4. R-M8-4. Item states: Open; Fixed (set only by the system, when a review of a newer version no longer finds it); Accepted (risk taken on purpose: Critical needs a Manager, High needs the owner; reason always required); NotApplicable (false positive; reason required; sent to Harvey as lawyer feedback). Re-running the same version never changes an item's state.
5. R-M8-5. Verdict on the current version: NotEvaluated (no completed review of this version), Blocked (any Open Critical), NeedsWork (any Open High), Ready. Accepted and NotApplicable count as handled.
6. R-M8-6. Readiness score per D5: 100 − 25 × open Critical − 10 × open High − 3 × open Medium − 1 × open Low, floor 0. Shown beside the verdict, never instead of it.
7. R-M8-7. Risk score comes from Harvey's findings, not from a model: the same formula over all findings (handled or not) on the current version, so it changes only with the text.
8. R-M8-8. Send gate, for every contract: NotEvaluated → refused ("run the review"); Blocked → refused, never forceable; NeedsWork → the owner confirms with a reason, kept in the audit; Ready → sends.
9. R-M8-9. The review runs as a background job: `POST readiness` returns 202 with a run id; progress and result arrive over SignalR; a run times out after 120 s. Harvey down → the run fails visibly and the verdict stays NotEvaluated. There is no fallback to the model or to keywords.
10. R-M8-10. Determinism: the same text, rule-grid version and settings give the same findings, tested on a fixed set of 20 contracts in CI.
11. R-M8-11. Every finding on screen shows its origin: rule name and version, approved by whom and when, detection kind, confidence, the quoted clause, linked articles. Wording is the rule's, in the contract's language.
12. R-M8-12. Compliance as a separate score is removed (D5, D6). Required mentions per contract family become Harvey code checks and produce ordinary findings.

**Data.** ReviewRun (version, started, finished, status, grid version, as-of date, error). DocumentReadinessItem: RuleKey, RuleVersion, ClauseReference, QuotesJson, DetectionKind, Confidence, State (Open, Fixed, Accepted, NotApplicable), DecidedBy, DecidedAt, Reason, FixedInVersion. Drop the JSON blobs `AiRiskAnalysisJson` and `AiComplianceAnalysisJson` from Document.

**API**

| Endpoint | Purpose |
|---|---|
| POST documents/{id}/reviews | Start a review of the current version (202, run id) |
| GET documents/{id}/reviews/latest | Run status, verdict, scores, items |
| PATCH documents/{id}/review-items/{itemId} | Accept or mark not applicable, with reason |
| POST documents/{id}/send | Gate per R-M8-8; body carries the reason when NeedsWork |

**Acceptance**

- [ ] With the model provider unset, findings, verdict and scores are identical to a run with it set.
- [ ] An unapproved rule in Harvey produces no item.
- [ ] Marking a Critical as Accepted as an Employee returns 403; as a Manager without a reason returns 422.
- [ ] Re-running a review on the same version leaves every item's state unchanged.
- [ ] A revised contract cannot be sent until its new review has completed.
- [ ] Harvey stopped: `POST reviews` fails with code `review.unavailable` and no item changes.

## M9. Clause library (regulatory watch retired)

**Scope.** Keep each organisation's approved wording in one place, versioned, and offer it where a finding or a template needs it.

**Today (verified)**

- Clause: title, category (12 values), body, guidance, language, risk level (free text), version, usage count, optional contract family. Status Draft, Approved, Deprecated; only Administrators change status; Administrators and Managers create and edit.
- Editing the body of an Approved clause bumps `Version` and overwrites the text in place: it stays Approved without a new approval, and the previous wording is lost (`Clause.Update`).
- Clause bodies are rendered with `[innerHTML]` (Angular sanitises them).
- The regulatory watch (RegulatoryText, RegulatoryArticle, keyword compliance, publish alerts) duplicates Harvey's corpus and is switched off in reviews (`Features:RegulatoryCompliance`).
- Harvey also holds approved "model clauses" per rule; nothing links a library clause to a rule.

**Target rules**

1. R-M9-1. Clause versions are immutable. Editing an Approved clause creates a new Draft version; the Approved one stays in use until the new one is approved; contracts and templates record the version they used.
2. R-M9-2. Approval by a Manager; the approver cannot be the author of that version (four-eyes). Deprecated versions remain readable and cited, never offered.
3. R-M9-3. A clause may be linked to one or more Harvey rule keys. A finding then proposes, in order: the organisation's approved clause for that rule, then Harvey's approved model clause.
4. R-M9-4. "Add to library" from a finding creates a Draft linked to that rule key, in the contract's language.
5. R-M9-5. Risk level becomes an enum (Low, Medium, High) or is removed; free text is not allowed.
6. R-M9-6. The regulatory watch is retired (D6): its menu entry, routes, controller, features, entities, keyword compliance code and the `RegulatoryAlert` notification are removed in one migration after export. Existing regulatory texts are exported to JSON and, where verified, imported into Harvey's corpus by a lawyer.
7. R-M9-7. Clause bodies are stored as sanitised HTML (same allow-list as templates, R-M6-3).

**Data.** Clause → Clause (stable id, title, category, contract family) + ClauseVersion (number, body, language, status, author, approver, approved at). ClauseRuleLink (clause id, rule key). Delete RegulatoryText, RegulatoryArticle.

**API.** `GET/POST clauses`, `POST clauses/{id}/versions`, `POST clauses/{id}/versions/{n}/approve`, `POST .../deprecate`, `PUT clauses/{id}/rules`; `GET clauses/for-rule/{ruleKey}?language=fr`.

**Acceptance**

- [ ] Editing an Approved clause leaves the Approved version unchanged and creates version n+1 as Draft.
- [ ] The author of a version gets 403 when approving it.
- [ ] A finding on a rule with a linked library clause shows that clause before Harvey's model clause.
- [ ] After the migration no route, table or menu entry of the regulatory watch remains.

## M10. Lifecycle: term, obligations, alerts

**Scope.** After signature, nobody misses a notice deadline, a renewal, an expiry or a contractual obligation.

**Today (verified)**

- Term on Document: start, end, auto-renews, notice period days, confirmed. Notice deadline = end − notice days, computed, never stored (`ContractTermEvaluator`).
- Alert ladder counted from the end date: T90, T60, T30, T7, notice deadline passed, expired; one per stage; escalation to every Administrator and Manager from the notice-deadline stage on. When the notice period is 90 days or more, the notice deadline falls before T90, so the first alert anyone gets is "notice deadline passed": too late to act. With 60 days' notice, the only warning before the deadline is T90, 30 days ahead.
- An auto-renewing contract reaches "Expired" at its end date like any other; the term never rolls forward.
- One obligation per contract (date, free-text type, rationale, confirmed); reminders weekly from 30 days before, also for unconfirmed and invented dates (E10).
- Messages are English only (E17); jobs run on UTC cron (E31).

**Target rules**

1. R-M10-1. Term: start date, initial end date, renewal mode (None, Automatic, Option), renewal period in months, notice period (days or months), notice form (letter, e-mail, registered mail), confirmed by and at. Current end date and next notice deadline are computed.
2. R-M10-2. Automatic renewal: on the end date without a recorded termination, the term rolls forward by the renewal period and the ladder restarts; the audit records the rollover.
3. R-M10-3. The ladder is counted from the notice deadline when there is one, else from the end date: 60, 30, 14 and 7 days before, the day itself (escalation to the contract's team and Managers), then the end date. Each organisation can change the offsets.
4. R-M10-4. Recording a decision stops the ladder: Renew, Renegotiate, Terminate (with date the notice was sent and proof attached), Let expire.
5. R-M10-5. Obligations are a list (D12): title, type (Payment, Report, Insurance certificate, Audit, Renewal, Other), due date or recurrence (monthly, quarterly, yearly), owner, quote from the contract, source (person, Harvey key data), status (Upcoming, Done, Missed, Waived). Reminders only for confirmed obligations.
6. R-M10-6. No date is ever invented. A suggested date carries its quote; without a quote it is not suggested.
7. R-M10-7. Dates in text are parsed by an explicit parser: dd/mm/yyyy and written French, English and Arabic month names; mm/dd is never assumed.
8. R-M10-8. Jobs run per organisation in its time zone (default Africa/Tunis) at 08:00 local; messages use each recipient's language.
9. R-M10-9. Calendar export: an ICS feed per user (token in the URL, revocable) with notice deadlines, end dates and obligations.

**Data.** ContractTerm (1:1 with Document): fields above + CurrentEndDate, NextNoticeDeadline, LastAlertOffset, Decision, DecisionAt, DecisionBy. Obligation (n per contract). ContractEvent (Rollover, Decision, NoticeSent). Drop the obligation and term columns from Document.

**Acceptance**

- [ ] A contract ending 31 Dec with 90 days' notice gets its first alert 60 days before 2 Oct, not on 2 Oct.
- [ ] An automatic-renewal contract with no decision rolls to the next term on its end date and its ladder restarts.
- [ ] Upload of a contract with no dates creates no obligation.
- [ ] "03/04/2027" in a French contract is read as 3 April 2027.
- [ ] A French user receives French alert e-mails; an English user, English ones.

## M11. Harvey: corpus, grid, review, MCP

**Scope.** The separate service that holds Tunisian law article by article and reviews contract clauses against a risk grid lawyers maintain. It is what makes the product's legal claims true, so it carries the strictest rules.

**Today (verified)**

- FastAPI on port 8100, SQLAlchemy 2, SQLite file `data/corpus.db` by default. Tables: legal_texts, articles, article_versions, article_explanations, playbook_rules, playbook_rule_articles. 22 test files, not in CI.
- API protected by `X-API-Key` only when `CORPUS_API_KEY` is set; open otherwise. Web UI: one admin account from environment variables, default admin / admin, sessions in a signed cookie.
- Approvals (text verified, rule confirmed, model clause approved, explanation approved) store a typed name and an ISO string date, not an authenticated user (E37). A rule has no version: editing it changes past and future reviews alike.
- `/review`: code checks (missing termination or force majeure, personal data, consumer online, contradictory payment figures, missing references, inconsistent party names, bank checks) plus meaning matches against each rule's example clauses with paraphrase-multilingual-MiniLM-L12-v2 (threshold 0.60, margin 0.03). It uses every rule, approved or not (E1). Severities High, Medium, Low; no Critical (E38).
- `/review` refuses (503) when the meaning model is not installed; LegalDesk then shows "review unavailable".
- Search: BM25 by default, vectors if configured, never both (E16); vector search loads every article's embedding per query.
- MCP server over stdio with 6 tools: list_laws, search_law, get_article, find_definition, check_quote, explain_article.
- `CORPUS_AI_PROVIDER` defaults to groq in `load_settings` (E32); the model is used on law text only.

**Target rules**

1. R-M11-1. Harvey users: individual accounts with roles Lawyer (verify texts, approve rules, clauses and explanations), Editor (import and edit, cannot approve), Reader. Passwords hashed with Argon2, TOTP required for Lawyers. The env admin account is removed after first setup.
2. R-M11-2. Every approval records the authenticated user id, name, bar or firm reference (free text on the profile), and a UTC timestamp column, not a string. An Editor cannot approve their own change.
3. R-M11-3. Rules are versioned: any change to examples, counter-examples, phrases, checks, severity, articles or model clause creates a new Draft version. Reviews use the latest Approved version; each finding carries rule key and version.
4. R-M11-4. Severity scale Critical, High, Medium, Low. Critical is reserved for findings a lawyer has marked as blocking (for example an illegal clause, a missing mandatory mention). Each approved rule sets its severity; each code check has a default and can be overridden per rule.
5. R-M11-5. `/review` serves only Approved rule versions. It accepts `as_of`, `rule_sets` and per-organisation overrides (enabled, severity), returns grid version, and for every finding: rule key and version, detection kind, confidence, clause reference, exact quoted span (character offsets in the clause).
6. R-M11-6. Meaning-match quality gate: a labelled set of at least 300 clauses (French and English, then Arabic) with precision and recall per rule; CI fails if precision for any approved rule drops below 0.85 or overall recall below 0.70. `tools/calibrate_matching.py` becomes this test.
7. R-M11-7. Feedback loop: a NotApplicable decision from LegalDesk (R-M8-4) arrives in Harvey's queue with the clause; a Lawyer turns it into a counter-example or dismisses it.
8. R-M11-8. Database: SQL Server (or PostgreSQL) through `CORPUS_DATABASE_URL`, migrations with Alembic; SQLite remains for tests only.
9. R-M11-9. API key always required; Harvey refuses to start without one outside tests. Keys are per client service and rotatable.
10. R-M11-10. Search is hybrid: BM25 and vector scores combined by reciprocal-rank fusion; vectors held in memory (or a vector index) and refreshed on change, not reloaded per query.
11. R-M11-11. Import keeps bis, ter and quater articles as separate articles; Arabic texts are a second language version of the same article, linked.
12. R-M11-12. MCP over HTTP with an API key (streamable HTTP transport) in addition to stdio; every tool call is logged with key, tool and arguments.
13. R-M11-13. The language model in Harvey is off by default and only ever sees law text; drafts it produces are marked as drafts until a Lawyer approves them.

**Data.** New: harvey_users, rule_versions (rule key, version, status, payload JSON, author, approver, approved_at), review_feedback, api_keys. Dates as timestamp columns. Severity enum adds Critical.

**Acceptance**

- [ ] Approving a rule as an Editor returns 403; as the author Lawyer of that version returns 403.
- [ ] A finding cites a rule version, and that version's content can be fetched afterwards unchanged.
- [ ] Harvey without `CORPUS_API_KEY` exits at start-up with a clear message.
- [ ] CI runs Harvey's tests and the matching quality gate on every pull request.
- [ ] An MCP client over HTTP without a key gets 401.

## M12. Contract assistant (language model)

**Scope.** Help people understand a contract and act on its dates, without trusting anything the model cannot point to in the text. Available only when the organisation turned the model on (D3).

**Today (verified)**

- Provider behind `ILlmProvider`: Groq (OpenAI-compatible), local (Ollama-style), or none. Input limits: 48,000 characters for analysis on Groq, 24,000 locally, 12,000 for the summary (`LlmAiService.cs:22`), with no notice to the reader that a long contract was summarised from its first part only.
- Summary is generated on first view and stored. It is also generated when an external signer opens their link (`sign/{token}/summary`), which sends the contract to the provider with no audit entry.
- Chat: the browser keeps the conversation; the last 10 turns are sent; answers carry excerpts checked word for word (`QuoteMatcher`); audited. The question has no length limit.
- Key data and renewal note: quotes verified, unverified values dropped, nothing stored, audited.
- The model also writes the checklist risks and the contract profile today (M8); that ends with D1.

**Target rules**

1. R-M12-1. Model features (summary, profile, chat, key data, renewal note, negotiation explainer) exist only when the organisation's model mode is Groq or Local. With Off, the UI hides them and the API returns 404.
2. R-M12-2. Every call that sends contract text writes an audit entry: feature, provider, model name, characters sent, user or participant. Including the signer's summary.
3. R-M12-3. Every model output is labelled "Harvey assistant, model-generated" with provider and date. Sentences of a summary or profile that cite the contract carry a verified quote; a sentence whose quote fails the word-for-word check is dropped, as key data does today.
4. R-M12-4. Long contracts: the summary is built clause by clause (map, then reduce) so every clause is read; if a limit still cuts the text, the output says which clauses were not read.
5. R-M12-5. Inputs: a question is at most 1,000 characters; history at most 10 turns of 2,000 characters each; per-user limit 30 calls per hour per contract, 300 per day.
6. R-M12-6. Prompts are versioned files; the prompt version is stored with every output. A regression set of 20 contracts with expected key data runs in CI against a recorded provider response.
7. R-M12-7. Negotiation explainer (new): given two versions, it explains each changed clause in plain words, with both quotes verified; it never rates risk (that is M8).
8. R-M12-8. External signers see a summary only if the owner ticked "share the summary with participants" when sending.
9. R-M12-9. Applying key data to the term or obligations is always a person's action (M10); the model never writes a date into the contract record.

**Data.** ModelOutput (document, version, feature, provider, model, prompt version, created, text, quotes JSON, verified flag); replaces `AiSummary` and `AiSummaryGeneratedAt` on Document.

**Acceptance**

- [ ] With model mode Off, `POST documents/{id}/ask` returns 404 and no request leaves the server (network capture).
- [ ] Opening a signer's summary writes an audit entry naming the participant and the provider.
- [ ] A 60-page contract's summary mentions its last clause.
- [ ] A summary sentence with a fabricated quote is not shown.

## M13. Audit, notifications, reports, data protection

**Scope.** Prove who did what and when, tell people what needs them, give the legal team a portfolio view, and respect data-protection rights.

**Today (verified)**

- `AuditLog`: required DocumentId, action (36 values), actor, IP, user agent, location, free-text English description, metadata, timestamp. Rows can be updated or deleted like any other; nothing chains them. Events without a contract (logins, user admin, settings) cannot be recorded. Downloads and views are not recorded (E20).
- Notifications: stored per user, pushed over SignalR, e-mailed inline by MailKit; texts English only.
- Reports (Administrator, Manager): overview, risk map, lifecycle, organisation audit. The risk map loads every contract's risk JSON into memory and parses it (`GetRiskMapQuery.cs:34`).
- Data protection: consent log per tier; a public form records access, erasure and other requests; a user can export their own data. No screen or endpoint lets anyone process a request, and no retention or purge exists.

**Target rules**

1. R-M13-1. Audit entries: OrganisationId, DocumentId (nullable), ActorType (User, Participant, System, Platform), actor id, action, structured data (JSON with ids and values, no prose), IP, user agent, UTC time, PreviousHash, Hash = SHA-256 of the canonical row plus PreviousHash, chained per organisation.
2. R-M13-2. The audit table is append-only: the application's database login has INSERT and SELECT only on it; a trigger refuses UPDATE and DELETE. A daily job timestamps the chain head (RFC 3161) and stores the token.
3. R-M13-3. Every action has an FR and EN label and a sentence template; the UI builds the description from the structured data in the reader's language. A CI test fails if an action lacks a label in either file.
4. R-M13-4. Audit export per contract (PDF with chain verification result) and per organisation (CSV), both audited.
5. R-M13-5. Notifications go through the outbox (R-M4-9); each user chooses per type in-app, e-mail, or daily digest; texts in the user's language; e-mails from templates with the organisation's name.
6. R-M13-6. Reports query SQL aggregates and indexed columns only (no JSON parsing in memory): portfolio by counterparty, family, stage and owner; open risks by rule; cycle time per circuit step; upcoming deadlines. Every report exports to CSV and XLSX.
7. R-M13-7. Data requests: an Administrator screen lists requests with a 30-day due date, identity check, and actions Export, Erase, Rectify, Reject (reason); each action is audited. Erase anonymises a person's data where no retention duty applies, and keeps signature evidence for the retention period, telling the requester why.
8. R-M13-8. Retention: per organisation (default 10 years after a contract's end or cancellation). A monthly job lists contracts past retention for an Administrator to confirm purge; purge removes files and personal data and keeps an audit stub.
9. R-M13-9. Privacy pages list sub-processors (hosting, e-mail, Groq when the model is on) and are generated from the organisation's settings. Legal texts cite Tunisia's personal data protection law (organic law 2004-63, to confirm with counsel) and the GDPR for EU data subjects.

**Data.** AuditLog as above; AuditAnchor (organisation, date, head hash, timestamp token). NotificationPreference. DataSubjectRequest + IdentityVerifiedAt, HandledBy as user id, Actions log. Organisation.RetentionYears.

**Acceptance**

- [ ] `UPDATE AuditLogs` run as the application login fails.
- [ ] Changing one audit row by hand (as DBA) makes the chain check report the first broken row.
- [ ] Creating, editing and deactivating a user each write an organisation audit entry.
- [ ] The risk map for 10,000 contracts answers in under 1 s.
- [ ] An erasure request on a signer keeps their signature evidence and anonymises their other data; the reply explains the retention duty.
