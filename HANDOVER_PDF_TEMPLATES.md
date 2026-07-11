# SignFlow — PDF Template Request Type: Handover

> **Purpose:** Complete cross-session handover for the **PDF-based request type** (the 3rd request-origination
> flow) added to SignFlow. Covers backend + frontend, every file, design decisions, build/run/test, the
> non-obvious gotchas discovered while building it, and the one open bug. Written so a fresh session can
> continue with zero prior context.
>
> **Last updated:** 2026-07-06 · **Status:** Feature complete & compiling on both stacks; DB schema applied to dev; one open UI bug (designer drag-drop Y placement) still unverified.

---

## 0. TL;DR / Current State

- **What it is:** A reusable request template backed by a **persisted PDF** (uploaded once). Admins map fields to page **X/Y coordinates**; users create signing requests that **reuse the stored PDF** as the base document. It is the PDF analogue of the existing **HTML Templates** flow.
- **The three request-origination flows in SignFlow:**
  1. **PDF Designer** — upload a one-off PDF, drag fields, send. (`features/designer`)
  2. **HTML Templates** — reusable `{{field}}` HTML template → snapshot instances. (`features/html-templates`)
  3. **PDF Templates** — *this feature*. Reusable PDF + coordinate-mapped fields → instances. (`features/pdf-templates`)
- **Key architectural win:** On request creation, each configured template field is **materialized into a real `SignatureField`** at its coordinates on a **per-instance copy** of the stored PDF. This reuses the **existing signing UI + PDFsharp stamping pipeline** — no new renderer.
- **Builds:** Backend compiles (0 errors). Frontend `ng build` clean. i18n EN+FR parity holds (900/900 as of last edit).
- **Open bug:** In the **designer**, dropping a field sometimes lands at the **bottom** of the page. Multiple fixes attempted (see §9). **Not yet confirmed fixed** — could not self-verify (see §8 CORS).

---

## 1. Project Facts (needed by any session)

| Thing | Value |
|---|---|
| Repo root | `C:\Users\aymen\Downloads\formbuilder` |
| Stack | .NET 10 (Clean Architecture + Vertical Slice + CQRS, **no MediatR**), Angular 22 (standalone, **zoneless**, signals), SQL Server, EF Core |
| Backend solution | `src/backend/SignFlow.slnx` |
| **API port** | **5005** (per `launchSettings.json` / `environment.ts`) — *NOT 5000* |
| Frontend dev | `cd src/frontend && npx ng serve` → **http://localhost:4200** |
| Frontend API base | `environment.apiUrl = http://localhost:5005/api` |
| Dev DB | `SignFlowDb_Dev`, SQL Server localhost, **Windows auth** (`sqlcmd -S localhost -d SignFlowDb_Dev -E`) |
| i18n | `@ngx-translate/core` 18, files `src/frontend/public/i18n/en.json` & `fr.json`, must be **key-parity** checked |
| PDF render (frontend) | PDF.js 6 — fetch as Blob (JWT can't attach to worker) → `getDocument({ data })`, render at **scale 1.5** |
| PDF stamp (backend) | **PDFsharp** stamps fields; **PdfPig** for text extraction only |

**Demo credentials (seeded):**
- `admin@signflow.app` / `Admin@SignFlow1!`  (Administrator)
- `manager@signflow.app` / `Manager@SignFlow1!` (Manager)
- `employee@signflow.app` / `Employee@SignFlow1!` (Employee)

---

## 2. Design Decisions (the spec, locked)

| Concern | Decision |
|---|---|
| **Naming** | New origination flow = **PDF Template**. Entities `PdfTemplate` + `PdfTemplateField`. This is distinct from the `RequestType` config entity (which is just a category label + SLA — a PDF-template request can still carry a `RequestTypeId`). |
| **Field types** (`PdfFieldType`) | `Text, Date, Number, Checkbox, Signature, Dropdown` |
| **Fill responsibility** (`PdfFieldFillRole`) | `Requester` (initiator fills at creation), `Approver` (a workflow signer fills/signs), `System` (auto-populated, no human). "Route to a specific person/role" = per-field **PreAssignedApprover** (a concrete user). |
| **Auto-fill** (`AutoFillFrom` dot-path) | Profile: `user.fullName / user.firstName / user.lastName / user.email / user.company / user.department`. Metadata: `system.today`, `system.now`, `request.title`. External-system source left as an open switch (resolver `switch` — nothing wired, no integration exists). |
| **PDF persistence** | Uploaded once → stored at `pdf-templates/{userId}`. Each instance `IStorageService.CopyAsync`-clones it to the document store so the template original stays pristine and is reused as the base for every instance. |
| **Runtime** | `CreateRequestFromPdfTemplateCommand`: load template+fields → copy PDF → create PDF-based `Document` (snapshot `PdfTemplateId` + filled values) → apply auto-fill → materialize `SignatureField`s (Requester/System = pre-filled + read-only, Approver = routed to recipients) → create Recipients (PreAssigned > Default > user-picked, routed by WorkflowType/WorkflowStage) → start workflow. |
| **Constraint** | A template must have **≥1 Approver+Signature field** or `submit` rejects it (there'd be no one to send to). |

---

## 3. Backend — Files & Responsibilities

All paths relative to repo root. **New** unless marked *(modified)*.

### Domain (`src/backend/SignFlow.Domain`)
- `Enums/PdfFieldType.cs` — `Text, Date, Number, Checkbox, Signature, Dropdown`. Has `ToSignatureFieldType()` mapping on the entity.
- `Enums/PdfFieldFillRole.cs` — `Requester, Approver, System`.
- `Entities/PdfTemplate.cs` — mirrors `HtmlTemplate`. Fields: `Name, Description, IsActive, StoragePath, FileName, FileSizeBytes, PageCount, DefaultApproverId(+nav), WorkflowType, HideRecipientIdentities, CreatedById(+nav), Fields` collection. Factory `Create(...)`, `Update(...)`. Private ctor for EF.
- `Entities/PdfTemplateField.cs` — fuses `HtmlTemplateField` config + `SignatureField` coordinates. Fields: `PdfTemplateId(+nav), FieldKey, Label, Type(PdfFieldType), FillRole(PdfFieldFillRole), Page, X, Y, Width, Height, AutoFillFrom, DefaultValue, IsRequired, Order, OptionsJson, PreAssignedApproverId(+nav), WorkflowStage`. Method `ToSignatureFieldType()` maps `PdfFieldType` → `FieldType`.
- `Entities/Document.cs` *(modified)* — added `PdfTemplateId (Guid?) + PdfTemplate nav`, and method `SetPdfTemplateSource(pdfTemplateId, filledValuesJson)` (reuses existing `FilledValuesJson`).

### Application (`src/backend/SignFlow.Application`)
- `Common/Interfaces/IApplicationDbContext.cs` *(modified)* — added `DbSet<PdfTemplate> PdfTemplates`, `DbSet<PdfTemplateField> PdfTemplateFields`.
- `Features/PdfTemplates/Commands/CreatePdfTemplateCommand.cs` — **also defines shared `PdfTemplateFieldInput` record**. Uploads PDF (virus scan → page count → `IStorageService.UploadAsync` under `pdf-templates/{userId}`), creates `PdfTemplate` with no fields. Returns `CreatePdfTemplateResult(Id, Name, PageCount)`. Admin/Manager only.
- `Features/PdfTemplates/Commands/UpdatePdfTemplateCommand.cs` — updates metadata/workflow config + **replaces the whole field set** (designer save semantics). Validates unique field keys, `Page >= 1`. PDF file itself is immutable.
- `Features/PdfTemplates/Commands/DeletePdfTemplateCommand.cs` — soft-delete config only.
- `Features/PdfTemplates/Commands/CreateRequestFromPdfTemplateCommand.cs` — **the runtime centerpiece** (see §2 Runtime). Reuses `ApproverSelection` from `HtmlTemplates.Commands` (via `using`). Copies PDF, materializes `SignatureField`s, applies auto-fill (`ApplyAutoFill`), resolves approvers, creates recipients, starts workflow, audit-logs.
- `Features/PdfTemplates/Queries/GetPdfTemplatesQuery.cs` — paged list. `PdfTemplateDto`. Admins/managers see all; others see active only.
- `Features/PdfTemplates/Queries/GetPdfTemplateByIdQuery.cs` — detail incl. `Fields` + `StoragePath` (used by the `/stream` endpoint). `PdfTemplateDetailDto`, `PdfTemplateFieldDto`. Non-admins can only view active.
- `Extensions/ApplicationServiceExtensions.cs` *(modified)* — registered all 4 command handlers + 2 query handlers (search for the `// PDF templates` comment block).

### Infrastructure (`src/backend/SignFlow.Infrastructure`)
- `Persistence/ApplicationDbContext.cs` *(modified)* — added the two `DbSet` properties.
- `Persistence/Configurations/PdfTemplateConfiguration.cs` — EF config for both entities. `PdfTemplate` has global soft-delete query filter (`HasQueryFilter(t => !t.IsDeleted)`), FKs: `CreatedById`→Users **Restrict**, `DefaultApproverId`→Users **SetNull**, `Fields`→**Cascade**. `PdfTemplateField`: unique index on `(PdfTemplateId, FieldKey)`, `PreAssignedApproverId`→Users **SetNull**. (Mirrors `HtmlTemplateConfiguration`.)
- `Migrations/20260706120000_AddPdfTemplates.cs` — **hand-written migration**. Creates `PdfTemplates` + `PdfTemplateFields` tables, adds `Documents.PdfTemplateId` column + FK + indexes. **⚠️ HAS NO `.Designer.cs` COMPANION — see §7.1.**
- `Migrations/ApplicationDbContextModelSnapshot.cs` *(modified)* — hand-edited to add both entity blocks, `Document.PdfTemplateId` property + index + FK relationship, and `Fields` navigation. (Snapshot rules are documented in the `guide-development` memory: alphabetical props, nvarchar rules, etc.)
- `Services/Pdf/AppFontResolver.cs` — **required** `IFontResolver` for PDFsharp core build. See §7.2.

### API (`src/backend/SignFlow.Api`)
- `Controllers/V1/PdfTemplatesController.cs` — route `api/v1/pdf-templates`. Endpoints:
  - `GET /` — list (`?pageNumber&pageSize&search&activeOnly`)
  - `GET /{id}` — detail
  - `GET /{id}/stream` — inline PDF (for designer canvas + request preview)
  - `POST /` — **multipart** upload `[FromForm] CreatePdfTemplateRequest(File, Name, Description)`, **Admin/Manager**
  - `PUT /{id}` — update + fields, **Admin/Manager**
  - `DELETE /{id}` — soft delete, **Admin/Manager**
  - `POST /{id}/submit` — create request → returns `{ documentId }`
  - **Note:** reuses `ApproverSelectionRequest` record already declared in `HtmlTemplatesController.cs` (same namespace `SignFlow.Api.Controllers.V1` — do **not** redeclare it or you get a duplicate-type error).
- `Program.cs` *(modified)* — registers `GlobalFontSettings.FontResolver = new AppFontResolver()` **before** `builder` host build. Also where CORS is configured (§8).

### API JSON contract reminders (System.Text.Json, camelCase, `JsonStringEnumConverter`)
- Enums serialize as **PascalCase string names**: `PdfFieldType` (`'Text'`,`'Signature'`,…), `PdfFieldFillRole` (`'Requester'`,`'Approver'`,`'System'`), `WorkflowType` (`'Sequential'`,`'Parallel'`,`'Mixed'`).

---

## 4. Frontend — Files & Responsibilities

Paths under `src/frontend/src/app` unless noted. **New** unless marked *(modified)*.

- `core/models/pdf-template.models.ts` — types: `PdfFieldType`, `PdfFieldFillRole` (string unions matching backend PascalCase), `PdfTemplateDto`, `PdfTemplateFieldDto`, `PdfTemplateDetailDto`, `CreatePdfTemplateResult`, `UpdatePdfTemplateRequest`, `PdfApproverSelection`, `SubmitPdfTemplateRequest`, and `PDF_AUTO_FILL_OPTIONS` (value+labelKey list). **`WorkflowType` is imported from `./document.models`** (not `workflow.models`).
- `core/models/index.ts` *(modified)* — re-exports `pdf-template.models`.
- `core/services/pdf-template.service.ts` — `getAll, getById, create(file,name,desc→FormData via api.upload), update, delete, submit, stream(id)→Blob via api.download, streamUrl(id)`.
- `features/pdf-templates/pdf-templates-list.component.ts` — **role-aware** list. Everyone sees active templates with **"Start request"**; Admin/Manager also get inline **upload panel** (file + name + desc → create → navigate to designer), **Configure fields**, activate filter, delete. Uses `AuthService` (`auth.user()?.roles`).
- `features/pdf-templates/pdf-template-designer.component.ts` — the **coordinate designer**. PDF.js render + CDK drag/drop (palette → canvas), absolutely-positioned field boxes, drag/resize, properties panel (label, fill-role, required, auto-fill dropdown, dropdown options, default value, pre-assigned approver, workflow stage, X/Y/W/H). Toolbar: template name, workflow-type selector, **grid-snap toggle**, save. Saves via `svc.update`. **See §9 for the open drag-drop bug + attempted fixes.**
- `features/pdf-templates/pdf-template-request.component.ts` — request creation. Fills Requester fields, picks approvers (user or external), **live PDF preview** with a coordinate field-overlay reflecting typed/auto-filled values (green = approver signature, blue = your values). Auto-fill is **pre-populated on load** (`prefillAutoFillFields`) so users see values already filled. Preview **fit-scales** to the frame width (CSS transform on the wrapper keeps overlay aligned).
- `features/pdf-templates/pdf-templates.routes.ts` — child routes: `''` (list), `':id/designer'`, `':id/new'`.
- `app.routes.ts` *(modified)* — added `{ path: 'pdf-templates', loadChildren: … }` under the authGuard `LayoutComponent`.
- `shared/layout/layout.component.ts` *(modified)* — added nav item `{ labelKey: 'nav.pdfForms', icon: 'picture_as_pdf', path: '/pdf-templates' }`.
- `public/i18n/en.json` & `fr.json` *(modified)* — added the **`pdfTemplates`** namespace (list/upload/designer/fieldTypes/fillRoles/autofill/request sub-namespaces), `nav.pdfForms`, and `common.more`. **Keep key-parity.**
- `.claude/launch.json` *(modified)* — added `"autoPort": true` so preview servers don't fight the user's :4200.

### Frontend routes (user-facing)
- `/pdf-templates` — list / gallery (role-aware)
- `/pdf-templates/:id/designer` — field designer (admin/manager; backend also guards)
- `/pdf-templates/:id/new` — create a request from a template

---

## 5. Database

### Tables (applied to `SignFlowDb_Dev`)
- `PdfTemplates` — `Id, Name, Description, IsActive, StoragePath, FileName, FileSizeBytes(bigint), PageCount, DefaultApproverId, WorkflowType(int), HideRecipientIdentities, CreatedById, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted`. FKs → Users (CreatedById NO ACTION/Restrict, DefaultApproverId SET NULL). Indexes: CreatedById, DefaultApproverId, IsActive.
- `PdfTemplateFields` — `Id, PdfTemplateId, FieldKey, Label, Type(int), FillRole(int), Page, X/Y/Width/Height(real), AutoFillFrom, DefaultValue, IsRequired, Order, OptionsJson, PreAssignedApproverId, WorkflowStage, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted`. FKs → PdfTemplates (Cascade), Users (PreAssignedApproverId SET NULL). Unique index `(PdfTemplateId, FieldKey)`, index PreAssignedApproverId.
- `Documents` — added nullable `PdfTemplateId` + FK → PdfTemplates (SET NULL) + index.

### Verify schema
```bash
sqlcmd -S localhost -d SignFlowDb_Dev -E -Q "SELECT name FROM sys.tables WHERE name IN ('PdfTemplates','PdfTemplateFields')"
sqlcmd -S localhost -d SignFlowDb_Dev -E -Q "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('Documents') AND name='PdfTemplateId'"
```

The full `Up()` SQL that was applied is in the migration file; re-run it via `sqlcmd -i` on a fresh DB. **See §7.1 for why you must apply it manually.**

---

## 6. Build / Run / Test

### Backend
- **VS locks the API DLL** when running. Build individual layers, or compile-only the API:
  ```bash
  dotnet build src/backend/SignFlow.Infrastructure/SignFlow.Infrastructure.csproj   # builds Domain+Application+Infra
  dotnet build src/backend/SignFlow.Api/SignFlow.Api.csproj -t:CoreCompile --no-dependencies   # compile API without the DLL-copy step
  ```
  A build failing **only** with `MSB3021`/`MSB3026`/`MSB3027` (file locked by "SignFlow.Api" / "Visual Studio") = the app is running; the C# actually compiled fine.
- Run: normally started from **Visual Studio** on **:5005**. A separate `dotnet run` will fail on the DLL lock while VS holds it.

### Frontend
```bash
cd src/frontend
npx ng build --configuration=development        # budgets NOT enforced in dev; prod build has a known SCSS-budget failure unrelated to this feature
npx ng serve                                     # :4200
```
New PDF-template components use **inline `styles: [...]`** (not `styleUrl`), so they don't hit the `anyComponentStyle` prod budget.

### i18n parity check (run before considering i18n done)
```bash
cd src/frontend && node -e '
const en=require("./public/i18n/en.json"),fr=require("./public/i18n/fr.json");
const flat=(o,p="")=>Object.entries(o).flatMap(([k,v])=>v&&typeof v==="object"?flat(v,p+k+"."):[p+k]);
const ek=new Set(flat(en)),fk=new Set(flat(fr));
console.log("en",ek.size,"fr",fk.size,"| onlyEn",[...ek].filter(k=>!fk.has(k)),"| onlyFr",[...fk].filter(k=>!ek.has(k)));'
```

---

## 7. Critical Gotchas Discovered This Session

### 7.1 ⚠️ Migrations here are applied by **manual sqlcmd**, not EF
The recent hand-written migrations (incl. `AddPdfTemplates`) have **no `.Designer.cs` companion file**. Without it, EF's migration discovery **does not see them** — `dotnet ef migrations list` stops at `AddHtmlTemplates`, and `context.Database.MigrateAsync()` (called at API startup in `InfrastructureServiceExtensions.SeedDatabaseAsync`) **silently applies nothing** for them. The dev DB is kept in sync by **running each migration's `Up()` SQL via `sqlcmd`**.
- **When adding a new table/column:** write the migration `.cs` + hand-edit the snapshot **AND** apply the SQL manually via `sqlcmd -S localhost -d SignFlowDb_Dev -E -i your.sql`.
- A background task was flagged to properly add the missing `Designer.cs` files (so `MigrateAsync` works again) — see if it was done. Affected: `AddDepartmentToUser`, `AddWorkflowConfigToHtmlTemplates`, `AddHtmlContentSnapshotToDocument`, `AddPdfTemplates`.
- Symptom if you forget: runtime `SqlException: Invalid column name 'PdfTemplateId'` even though code compiles.

### 7.2 ⚠️ PDFsharp needs a font resolver (.NET 10 core build)
PDFsharp's cross-platform core build ships **no fonts / no OS lookup** and throws on first `XFont` use:
`"... assign to 'GlobalFontSettings.FontResolver' to use fonts."` Fixed by `AppFontResolver.cs` (registered in `Program.cs`). It resolves the single family used (`"Arial"` in `PdfStampService`) — tries Windows Fonts, falls back to Linux **Liberation**/**DejaVu** paths.
- **Docker note:** the API image must have `fonts-liberation` (or `fonts-dejavu-core`) installed, or add another candidate path in `AppFontResolver.CandidateSets`. (Dockerfile was **not** modified this session.)

### 7.3 ⚠️ Angular is **zoneless** — mutate signals, not plain fields, for template bindings
Plain field mutations after the last change-detection pass **do not refresh bindings**. `canvasWidth/canvasHeight` in the designer were converted to **signals** for this reason. If a template value updates "sometime later" (e.g., after an async PDF render) and the DOM doesn't reflect it, this is almost certainly the cause.

### 7.4 API port is **5005**, not 5000
Earlier memory said 5000; the real port (launchSettings + environment.ts) is **5005**. (Corrected in memory.)

### 7.5 ⚠️ CORS blocks preview verification (see §8)

---

## 8. Verification Constraint (READ before trying to browser-test)

The API CORS policy (`Program.cs`) **only allows `http://localhost:4200`**:
```csharp
policy.WithOrigins(builder.Configuration.GetValue<string>("AllowedOrigins")?.Split(',') ?? ["http://localhost:4200"])
      .AllowAnyHeader().AllowAnyMethod().AllowCredentials();
```
Consequences:
- The Claude **preview server** gets a **random port** (autoPort), so its login POST to :5005 is **CORS-blocked** → the app bounces back to `/auth/login`. **You cannot log in / drive the app from a preview server** against this API.
- **To browser-verify:** use the user's own **:4200** dev server, **or** temporarily add the preview origin to `AllowedOrigins` in `appsettings.Development.json` and restart the API (only safe if VS isn't the one running it — don't kill the user's VS session).
- Manual `fetch()` from page context to :5005 also hits CORS (`Failed to fetch`) unless the origin is allowed.

---

## 9. 🔴 OPEN BUG — Designer drag-drop lands at the bottom of the page

**Symptom (user-reported, reproduced on their :4200):** In `pdf-template-designer.component.ts`, dropping a field from the palette sometimes places it at/near the **bottom** of the page instead of where the cursor is ("goes outside the page, to the bottom").

**Interpretation:** the drop **Y-origin computation overshoots**, and the bounds-clamp then pins the field to the bottom edge — so the clamp "works" but the underlying coordinate is wrong.

**Fixes ATTEMPTED this session (in order) — bug may still not be resolved:**
1. Added bounds clamping (`clampPos`) to drop / drag-end / resize / manual X-Y-W-H inputs so a field can't exceed the page.
2. Converted `canvasWidth`/`canvasHeight` to **signals** (zoneless staleness, §7.3).
3. **Container containment** — changed `:host` to `{ display:flex; flex-direction:column; height:100vh; overflow:hidden }` and `.dz` to `{ flex:1; min-height:0 }` (was `height: calc(100vh - 64px)` on an unstyled host). This matches the **working** document designer (`features/designer/designer.component.scss` uses `:host{height:100vh;overflow:hidden}`). Rationale: an un-contained host lets an ancestor/body scroll, which offsets `event.dropPoint` vs `getBoundingClientRect()`.
4. Removed `align-items: flex-start` from `.canvas-area` to match the working designer.
5. Drop origin + extent now measured from the **live `canvas.getBoundingClientRect()`** (both in one coordinate space) instead of the wrapper + size signals.

**Why it was hard to confirm:** couldn't self-verify due to CORS (§8). The working `features/designer/designer.component.ts` uses the **identical** `dropPoint - wrapper.left/top` formula (divided by its zoom level) and works — so the difference is layout/scroll context, which is what fix #3/#4 target.

**NEXT DEBUG STEP if still broken:** add a temporary on-screen readout in the designer that logs, per drop: `event.dropPoint.y`, `canvasRect.top`, `canvasRect.height`, and the computed `y`. One drop + the numbers will pinpoint whether `dropPoint` is page- vs client-coordinates (scroll overshoot) or whether the canvas rect is unexpectedly short. Then either subtract the scroll offset or switch off CDK's `dropPoint` in favor of a tracked `pointermove` position over the canvas.

**Relevant methods:** `onDrop`, `onFieldDragEnd`, `startResize`, `updateProp`, `snap`, `clampPos`, `renderPage` (sets `canvasWidth/Height` signals at PDF.js scale 1.5).

---

## 10. Runtime behavior notes / things to verify end-to-end

- **Materialization:** `CreateRequestFromPdfTemplateCommand` creates one `SignatureField` per template field at its coords. Requester/System fields → `recipientId = null`, `IsReadOnly = true`, value = filled/auto-filled/default, and `SetSigned(value)` so they carry a value. Approver fields → assigned to the resolved recipient; non-signature approver fields follow the same **WorkflowStage** recipient (fallback: first recipient).
- **⚠️ Unverified:** whether the **final stamped/flattened PDF** actually renders the read-only pre-filled Requester/System values depends on the existing embed/stamp pipeline (`PdfStampService` / `IPdfService.EmbedSignaturesAsync` / `FlattenFieldsAsync`). The signing **UI** will show them (it renders `SignatureField`s by coordinate). **Do an end-to-end test**: upload → configure → submit → open the completed document → confirm values appear on the downloaded PDF. If they don't, extend the stamping pipeline to include read-only pre-filled fields.
- **Coordinate space:** fields are stored in **scale-1.5 pixel space** (what the designer renders). The request preview re-renders at 1.5 and CSS-scales the whole wrapper to fit, keeping overlay aligned. If you ever change the render scale, update both the designer and the request preview together, and consider whether stored coordinates need migration.

---

## 11. Suggested Next Steps / TODO

1. **Resolve the designer drop-Y bug** (§9) — add the debug readout, capture numbers, fix root cause. Highest priority (user-facing, reported 3×).
2. **End-to-end stamping test** (§10) — confirm pre-filled Requester/System values land on the final PDF; extend `PdfStampService` if not.
3. **Add missing `.Designer.cs`** for the hand-written migrations (§7.1) so `MigrateAsync` is reliable again (background task was flagged).
4. **Dockerfile:** ensure `fonts-liberation` is installed for the API image (§7.2).
5. **UX polish:** hide "Start request" for templates with no Approver+Signature field (backend already rejects them, but the button shouldn't invite failure).
6. Consider exposing `gridSize` (currently constant `10`) as a user setting in the designer.

---

## 12. Pattern references (mirror these when extending)

- HTML-template analogues (closest prior art): `Features/HtmlTemplates/**`, `features/html-templates/**`, `core/services/html-template.service.ts`, `features/requests/new-request.component.ts` (its `previewHtml()` + `prefillAutoFillFields()` were the model for the PDF request preview + prefill).
- PDF designer canvas mechanics (PDF.js + CDK drag/drop): `features/designer/designer.component.ts` (+ `.scss` for the working `:host` containment).
- CQRS "add a backend feature" checklist and EF snapshot rules: the `guide-development` auto-memory file.
