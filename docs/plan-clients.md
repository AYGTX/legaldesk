# Plan A — Clients (counterparties) in LegalDesk

Written 24 October 2026. Goal: every contract is linked to the company it is signed with, so a user can open
"Société X" and see all its contracts, deadlines and signatories, and templates fill the client's details by
themselves. This is **not** multi-tenancy (keeping LegalDesk's own customers apart); that is a separate decision.

## Why a Client record, not folders

- A record carries data (legal name, tax ID, address, signatories) that templates, reviews and reports can use.
- Today the counterparty is typed by hand on each recipient (`Recipient.Company`) and in free-text `Tags`, so the
  same client appears under several spellings. `Document.FolderId` exists but is an unused leftover.
- Folders can come later as a personal way to organise; they are not the core.

## Scope

In: Client and contacts, link contracts to a client, client page, client picker, template auto-fill, filters,
duplicate-contract warning, CSV import, audit, FR/EN text.
Out (later): several clients per contract, folders, access restricted per client (that is plan B), merging
duplicates automatically.

## Data model (SignFlow.Domain)

`Client` (new, `BaseEntity`):

| Field | Type | Notes |
| --- | --- | --- |
| LegalName | string(300), required | « Raison sociale » |
| TradeName | string(300)? | Commercial name |
| TaxId | string(50)? | Matricule fiscal; unique when set |
| RegistryNumber | string(50)? | RNE number |
| Address, City, PostalCode, Country | strings | Country defaults to "TN" |
| Sector | string(100)? | Free text for now |
| Email, Phone, Website | strings? | |
| Notes | string? | |
| IsActive | bool | Archive instead of delete once contracts point to it |

`ClientContact` (new): ClientId, FullName, Role (e.g. « Directeur général »), Email, Phone, IsSignatory.

Changes to existing entities:
- `Document.ClientId` (Guid?, FK, indexed). Remove the unused `FolderId` in the same migration.
- `Recipient.ClientContactId` (Guid?, optional): a recipient picked from a client's contacts keeps the link.

One migration: `AddClients`. Regenerate the snapshot; never hand-edit it.

## Business rules

1. A contract has at most one client (the main counterparty) in this phase.
2. LegalName is required; TaxId, when given, is unique (case- and space-insensitive). Creating a client whose
   TaxId or normalised name already exists returns 409 with the existing client, so the UI offers to use it.
3. A client with linked contracts cannot be deleted, only archived (`IsActive = false`); archived clients are hidden
   from the picker but keep their contracts.
4. Who can do what: any signed-in user can view clients and pick one; Employees can create a client while creating a
   contract; Managers and Administrators can edit, archive and import.
5. Duplicate warning: when a contract is created or reviewed, if the same client already has an active contract of the
   same request type (not Completed past its end date, not Cancelled/Rejected), the readiness review adds a Low
   "Structure" item: « Un contrat actif du même type existe déjà avec ce client » with a link to it.
6. Every change writes an audit entry (new `AuditAction` values: `ClientCreated`, `ClientUpdated`, `ClientArchived`,
   `DocumentClientLinked`), each with FR and EN labels in `audit.actions.*` and `orgAudit.actions.*`.

## API (SignFlow.Api, `/api/v1`)

| Endpoint | Purpose |
| --- | --- |
| `GET clients?search=&active=&page=` | List and search (name, trade name, tax ID) |
| `GET clients/{id}` | Detail with contacts and summary counts |
| `POST clients`, `PUT clients/{id}` | Create, update (409 on duplicate) |
| `PATCH clients/{id}/status` | Archive or reactivate |
| `GET/POST/PUT/DELETE clients/{id}/contacts[/{contactId}]` | Contacts |
| `GET clients/{id}/documents` | Contracts of the client (reuse `GetDocumentsQuery` with a ClientId filter) |
| `GET clients/{id}/deadlines` | Term and obligation dates of its contracts (reuse the schedule query) |
| `POST clients/import` | CSV upload: preview with row errors first (`?commit=false`), then commit |
| `PATCH documents/{id}/client` | Link or unlink a contract |

Also: `ClientId` as an optional field on upload (`POST documents`), on template submit (PDF and HTML), and as a
filter on `GET documents`, `GET documents/schedule` and `GET reports/*`.

Application layer: `Features/Clients/{Commands,Queries}` following the usual pattern (record + validator + handler;
validators run through the handler decorators).

## Templates

Add auto-fill sources next to the existing `user.*` keys: `client.legalName`, `client.tradeName`, `client.taxId`,
`client.registryNumber`, `client.address`, `client.city`, `client.signatoryName`, `client.signatoryRole`.
The new-request flow asks for the client first, then fills those fields (still editable unless the template locks
them). Add the keys to the auto-fill dropdowns of both template editors.

## Harvey

- Key data (`POST documents/{id}/key-terms`) already reads the parties: match each party against clients by tax ID,
  then by normalised name, and propose « Lier à Société X » or « Créer le client ». Nothing is linked without a click.
- No change to the Harvey Python service.

## Frontend (Angular)

- `features/clients`: list page (search, active filter, import button), client page with tabs Contracts, Deadlines,
  Contacts, Details.
- Shared `sf-client-picker` (autocomplete with « + Nouveau client » inline): on upload, contract review page,
  new-request flows, and the contract page.
- Contract page: client chip in the header, links to the client page.
- Documents list, schedule and reports: client filter and column.
- Sidebar entry « Clients » between Contracts and Templates.
- All text in `fr.json` and `en.json` together; the agent stays « Harvey ».

## Build order

| Step | What | Estimate |
| --- | --- | --- |
| 1 | Entities, configuration, migration `AddClients` (+ drop FolderId) | 0.5 day |
| 2 | Clients CRUD + contacts + duplicate rules + audit actions, unit tests | 1 day |
| 3 | Link on upload / template submit / `PATCH documents/{id}/client`, filters on lists, schedule, reports | 0.5 day |
| 4 | Clients list and client page, client picker, contract chip, sidebar, FR/EN | 1.5 days |
| 5 | Template auto-fill `client.*` in both editors and request flows | 0.5 day |
| 6 | Duplicate-contract readiness item | 0.5 day |
| 7 | CSV import with preview and invitation-free commit | 0.5 day |
| 8 | Harvey key data → client match suggestion | 0.5 day |
| 9 | Demo seed: give the 10 demo contracts real clients; fix `DemoContractSeedingTests` (expects 9, seeder makes 10) | 0.5 day |

About 6 days. Steps 1–4 already give a demo: « montrez-moi tout ce qu'on a signé avec Société X ».

## Done when

- A contract can be linked to a client at creation and later; the client page lists its contracts and deadlines.
- A template fills the client's name, tax ID, address and signatory without typing.
- The same tax ID cannot create two clients.
- Backend builds, all tests green (including the fixed seeding test), new handlers covered by unit tests.
