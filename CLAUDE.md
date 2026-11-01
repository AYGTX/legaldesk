# LegalDesk

Contract lifecycle platform (formerly SignFlow): .NET 10 Clean Architecture backend in `src/backend`, Angular 22 frontend
in `src/frontend`, Harvey legal corpus service (Python) in `src/legal-corpus`, tests in `tests/`.

## Where we are

The product is being rebuilt to the rework spec, one sprint at a time. Read these before starting work:

- `docs/rework/spec.md` — decisions D1–D14, errata, architecture, sprint plan (section 6).
- `docs/rework/module-specs.md` — modules M0–M13 with rule ids (R-M12-3, …) that tests and commits cite.
- `docs/rework/status.md` — what each sprint built, what it left undone, and the next sprint. **Update it at the end
  of every sprint.**
- `docs/rework/dev-guide.md` — how to add a feature (CQRS, EF snapshot rules, i18n), file locations, gotchas.

The spec's source is the Claude doc https://claude.ai/artifact/AaUWp2zVCKe5LtFjxH6M13; the copies in `docs/rework` are
for sessions that cannot read it. S1–S8 are done (S8 on `rework/s8`). Next: **S9** = M13 all + M11 rules 6 and 10–12 +
k6 load test + outside penetration test. Then S10 = fixes only, restore drill, pilot onboarding, user guide.

## How to work

- Make the product decisions yourself; do not stop to ask. Aim for a product that wins clients, not a demo.
- Each sprint gets its own branch off the previous one (`rework/s9` off `rework/s8`), one commit per step, commit
  messages that cite the rule ids. Never push unless the user asks.
- Every new UI text ships in English and French together (`src/frontend/public/i18n/en.json` and `fr.json`).
- UI and API text says **Harvey** (or "Harvey assistant"), never "AI", "Groq" or "LLM". The legal pages are the exception.
- Never start the dev or preview server unless the user asks for it.
- Never commit `docs/sales/` (local only), secrets, or `appsettings.Local.json`. The Groq key comes from the
  environment variable `Groq__ApiKey`; tests do not need it.
- Every migration is checked up, down, up. Locally: on a throwaway copy of `SignFlowDb_Dev` (copy-only backup,
  restore under another name, migrate, drop the copy, delete the .bak). Without the local SQL Server:
  `scripts/check-migration.sh` (Docker), and say that a data-moving migration still needs the check on real data.
- Before finishing a sprint: `dotnet test tests/SignFlow.UnitTests`, `dotnet test tests/SignFlow.IntegrationTests`,
  `npx ng build` in `src/frontend`; then update `docs/rework/status.md` and report plainly what is not done or not verified.

## Codebase rules the tests enforce

- Only `PlatformQueries` may call `IgnoreQueryFilters`.
- Every anonymous endpoint has a rate limit.
- `TenantIsolationTests` counts the organisation-owned tables: a new `TenantEntity` table changes that number.
- Refusals carry a stable `code` in the problem details (`ConflictException(code, …)`, `NotFoundException.WithCode`,
  `TooManyRequestsException`).
- Model features go through `ModelAccess` (off unless the organisation turned it on, per-person limits, audited) and
  only show quotes found word for word in the contract (`QuoteMatcher`).
