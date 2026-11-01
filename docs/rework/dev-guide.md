# SignFlow Development Guide

## Architecture in one sentence
Clean Architecture (.NET 10): `Domain → Application → Infrastructure → API`. Angular 22 standalone signals frontend.

---

## Adding a backend feature (CQRS pattern)

### 1. Domain entity
```csharp
// src/backend/SignFlow.Domain/Entities/MyEntity.cs
public class MyEntity : BaseEntity          // Id, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted
{
    [MaxLength(256)]                          // ALWAYS add [MaxLength] to strings that need indexes
    public string Name { get; private set; } = string.Empty;

    private MyEntity() { }                   // EF Core constructor

    public static MyEntity Create(string name) => new() { Name = name };
}
```

### 2. Enum (if needed)
```csharp
// src/backend/SignFlow.Domain/Enums/MyEnum.cs
namespace SignFlow.Domain.Enums;
public enum MyEnum { A = 0, B = 1 }
```

### 3. DbContext registration — THREE files to update
```csharp
// IApplicationDbContext.cs
DbSet<MyEntity> MyEntities { get; }

// ApplicationDbContext.cs
public DbSet<MyEntity> MyEntities => Set<MyEntity>();

// ApplicationDbContextModelSnapshot.cs — see "EF Core Snapshot Rules" below
```

### 4. Migration file (manual)
```csharp
// src/backend/SignFlow.Infrastructure/Migrations/20260704HHMMSS_AddXxx.cs
public partial class AddXxx : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(name: "MyEntities", columns: t => new {
            Id = t.Column<Guid>(nullable: false),
            Name = t.Column<string>(maxLength: 256, nullable: false),
            // ... BaseEntity fields
        }, constraints: t => t.PrimaryKey("PK_MyEntities", x => x.Id));
    }
    protected override void Down(MigrationBuilder m) => m.DropTable("MyEntities");
}
```

**Apply manually via sqlcmd (dev DB = SignFlowDb_Dev, Windows auth):**
```
sqlcmd -S localhost -d SignFlowDb_Dev -E -Q "CREATE TABLE ..."
```

### 5. Command handler
```csharp
// src/backend/SignFlow.Application/Features/Xxx/Commands/DoXxxCommand.cs
public record DoXxxCommand(string Name) : ICommand<Guid>;      // ICommand or ICommand<T>

public class DoXxxCommandValidator : AbstractValidator<DoXxxCommand>
{
    public DoXxxCommandValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(256); }
}

public class DoXxxCommandHandler(IApplicationDbContext db) : ICommandHandler<DoXxxCommand, Guid>
{
    public async Task<Guid> HandleAsync(DoXxxCommand cmd, CancellationToken ct = default)
    {
        var entity = MyEntity.Create(cmd.Name);
        db.MyEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }
}
```

### 6. Register handler — ApplicationServiceExtensions.cs
```csharp
services.AddScoped<ICommandHandler<DoXxxCommand, Guid>, DoXxxCommandHandler>();
services.AddScoped<IQueryHandler<GetXxxQuery, XxxDto>, GetXxxQueryHandler>();
```

### 7. Controller
```csharp
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Authorize]
public class XxxController(ICommandHandler<DoXxxCommand, Guid> handler) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateXxxRequest req, CancellationToken ct)
    {
        var id = await handler.HandleAsync(new DoXxxCommand(req.Name), ct);
        return StatusCode(201, new { id });
    }
}
public record CreateXxxRequest(string Name);
```

---

## EF Core Snapshot Rules — CRITICAL

The snapshot (`ApplicationDbContextModelSnapshot.cs`) must exactly match what EF Core derives from entity classes. Getting it wrong causes `PendingModelChangesWarning` at startup.

### Rules derived from existing generated snapshots:

1. **Properties are listed alphabetically** (not in declaration order). `Id` is always first.
2. **Non-nullable strings**: require `.IsRequired()` in snapshot.
3. **Nullable strings**: no `.IsRequired()`.
4. **String with [MaxLength(N)]**: `.HasMaxLength(N).HasColumnType("nvarchar(N)")`.
5. **String without [MaxLength]**: `.HasColumnType("nvarchar(max)")` — no HasMaxLength call.
6. **bool IsDeleted**: just `.HasColumnType("bit")` — no `.HasDefaultValue(false)`.
7. **enum/int with CLR default 0**: just `.HasColumnType("int")` — no `.HasDefaultValue(0)`.
8. **Indexes**: `b.HasIndex("Column");` — NO `.HasDatabaseName(...)` unless you set it explicitly in Fluent API.
9. **FK indexes**: auto-added for navigation properties, NOT for plain string/Guid FK columns.

### If you're still getting PendingModelChangesWarning after fixing the snapshot:
Add this to `AddDbContext` in `InfrastructureServiceExtensions.cs` (already done for GDPR tables):
```csharp
.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
```
This is safe when the DB is already up-to-date via direct SQL.

---

## Adding a frontend feature (Angular 22 Signals)

### Service
```typescript
// src/frontend/src/app/core/services/my.service.ts
@Injectable({ providedIn: 'root' })
export class MyService {
  constructor(private api: ApiService) {}
  getAll(): Observable<MyDto[]> { return this.api.get<MyDto[]>('/v1/my'); }
}
```

### Component skeleton
```typescript
@Component({ selector: 'sf-my', standalone: true, imports: [...], template: `...` })
export class MyComponent {
  private svc = inject(MyService);
  readonly items = signal<MyDto[]>([]);
  readonly loading = signal(true);

  ngOnInit() {
    this.svc.getAll().subscribe({ next: d => { this.items.set(d); this.loading.set(false); } });
  }
}
```

### Route registration — app.routes.ts
```typescript
{
  path: 'my-page',
  loadComponent: () => import('./features/my/my.component').then(m => m.MyComponent),
  title: 'My Page — SignFlow'
}
```
Routes under the `LayoutComponent` parent are protected by `authGuard` automatically.

### i18n (required for any new user-facing text)
Add matching keys to **both** `public/i18n/en.json` and `public/i18n/fr.json` in the same edit — see [[feedback-bilingual-output]] for why. Use `TranslatePipe` in the component's `imports` array and `TranslateService.instant()` for TS-side strings (toasts, dynamic labels). Reuse existing namespaces before adding new ones (`common`, `designer`, etc. — full namespace list in [[project-signflow]]). Verify key-parity (flatten + diff both JSON files) before considering the work done.

---

## Key file locations

| Purpose | Path |
|---|---|
| Domain entities | `src/backend/SignFlow.Domain/Entities/` |
| Domain enums | `src/backend/SignFlow.Domain/Enums/` |
| Application commands | `src/backend/SignFlow.Application/Features/*/Commands/` |
| Application queries | `src/backend/SignFlow.Application/Features/*/Queries/` |
| Handler registration | `src/backend/SignFlow.Application/Extensions/ApplicationServiceExtensions.cs` |
| DbContext interface | `src/backend/SignFlow.Application/Common/Interfaces/IApplicationDbContext.cs` |
| DbContext impl | `src/backend/SignFlow.Infrastructure/Persistence/ApplicationDbContext.cs` |
| Migrations | `src/backend/SignFlow.Infrastructure/Migrations/` |
| Snapshot | `src/backend/SignFlow.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` |
| Controllers | `src/backend/SignFlow.Api/Controllers/V1/` |
| Angular services | `src/frontend/src/app/core/services/` |
| Angular models | `src/frontend/src/app/core/models/` |
| Angular features | `src/frontend/src/app/features/` |
| Shared components | `src/frontend/src/app/shared/` |
| Routes | `src/frontend/src/app/app.routes.ts` |
| Shell layout | `src/frontend/src/app/shared/layout/layout.component.ts` |
| Signing component | `src/frontend/src/app/features/signing/signing.component.ts` |

---

## Dev database
- Name: `SignFlowDb_Dev`
- Auth: Windows authentication (no password needed in sqlcmd)
- Apply migrations: `sqlcmd -S localhost -d SignFlowDb_Dev -E -Q "..."`
- Connection string: `appsettings.Development.json` → `ConnectionStrings.DefaultConnection`

## API conventions
- All responses use **camelCase** (configured globally in Program.cs)
- Enums serialize as **PascalCase string names** (JsonStringEnumConverter)
- Auth endpoints: `[Authorize]` — JWT Bearer via `Authorization: Bearer {token}`
- Public endpoints: `[AllowAnonymous]`
- Validation errors → HTTP 422 (`ValidationException` from `Common/Exceptions`)
- **FluentValidation validators DO run** as of 2026-08-15 — `Common/Behaviors/ValidationDecorators.cs`
  decorates every handler registered in `AddApplication()`. Declare rules in an `AbstractValidator<T>`
  next to the command; they fire before the handler and surface as 422. Details and the gotchas
  (decoration must stay the last line of `AddApplication`; `ScenarioFixture` bypasses it) in
  [[project-validation-pipeline]]. Handlers may still guard inline where they need a *different*
  status than 422 (e.g. `RefreshTokenCommand` wants 401) or where the check needs the DB.
- Not found → HTTP 404 (`NotFoundException`)
- Forbidden → HTTP 403 (`ForbiddenAccessException`)

---

## Reusable UI pieces (check before writing a new one)
- **Confirm/prompt dialogs — NEVER use native `confirm()`/`prompt()`/`alert()`**: `core/services/confirm.service.ts` (`ConfirmService.confirm({message, confirmLabel?, danger?}): Observable<boolean>` and `.prompt({message, inputType?: 'text'|'password'|'textarea', required?}): Observable<string|null>`, null = cancelled) backed by `shared/confirm-dialog/confirm-dialog.component.ts` (MatDialog). Added 2026-07-12, replaced all native dialogs app-wide; errors go to MatSnackBar. Uses `common.confirmTitle` as default title.
- **Approver/recipient picker**: `shared/approver-picker/approver-picker.component.ts` (`<sf-approver-picker>`) — existing-user vs external-signer toggle + the matching input. Backed by `core/models/approver-entry.model.ts` (`ApproverEntry`, `createEmptyApproverEntry()`, `isApproverEntryValid()`). Used by both `new-request.component.ts` and `pdf-template-request.component.ts`; extend this instead of re-inlining a mode-toggle. See [[project-signflow]]'s "UX unification" note for the broader plan this is part of.
- **Template gallery card**: `shared/template-card/template-card.component.ts` (`<sf-template-card [template] [canManage] (use) (manage)>`) — normalized card for either template kind. Backed by `core/models/template-gallery.model.ts` (`UnifiedTemplate`, `fromHtmlTemplate()`, `fromPdfTemplate()`). Only consumer today is `features/templates/templates.component.ts` (the real `/templates` gallery); reuse it if templates ever need to surface elsewhere (e.g. a "recently used" dashboard widget).
- **Request-flow stepper**: `shared/request-stepper/request-stepper.component.ts` (`<sf-request-stepper [steps]="string[]" [currentIndex]="n">`) — pass pre-translated labels, purely presentational. Used by `new-request.component.ts` and `pdf-template-request.component.ts`.
- **Request success/signing-links screen**: `shared/request-success/request-success.component.ts` (`<sf-request-success [recipients]>`, actions row via `<ng-content>`) — owns its own i18n (`shared.requestSuccess.*`) and clipboard-copy logic. Used by both template-based request flows after a successful submit; if the upload/designer flow ever gets a matching "done" step, reuse this instead of re-building a signing-links card.

## Feature status
See [[project-signflow]] for the current, up-to-date list of what's implemented — don't duplicate it here.

## Backlog (not yet built)
- IP address last-octet truncation in audit logs (GDPR)
- Hangfire data-retention job (auto-purge drafts after 90 days)
- Admin DSAR management panel (view/handle submitted requests)
- Bulk send / bulk reminders (same document to multiple independent signers)
- ~~Document version comparison (v1 vs v2) + old-version download~~ — DONE 2026-08-14 as a
  clause-level redline, not a side-by-side PDF view; see [[project-version-redline]]. Reuse
  `ContractDiffer` for any future text comparison rather than adding a second differ.
- Visual pin overlay for page-anchored comments (backend carries Page/X/Y since collab Phase 1)
- Two-factor authentication (TOTP) — `mfaEnabled` field already exists on the user model, unused
- Delegation / reassignment of signing tasks
- Signer identity verification (SMS OTP, ID upload)
- Webhook notifications (outbound POST on document events)
- Public API / API key auth for integrations
- Mobile-responsive signing experience
- Advanced field types: dropdown, attachment upload — radio groups DONE 2026-07-12 (single-choice via shared `GroupName`; see `Features/Signing/RequiredFieldRules.cs` for the group-aware required-check shared by SignFieldCommand + ApproveSigningCommand; main designer only, pdf-template designer has no Radio in its palette)
- Template marketplace (share templates between users)
- ~~Reporting / analytics dashboard for admins~~ — DONE 2026-07-12 as collab Phase 4a (`/reports`, `Features/Reports/`); extend that page rather than adding a second reporting surface
- SSO/OIDC login — recommended as external-IdP integration (not a custom SSO server) if/when requested; real cost is role-mapping + JIT provisioning + multi-tenant IdP config, not the protocol handshake
- Branding/white-label (logo + color in admin settings, applied to layout + emails)
- **Legal corpus article versions** (requested 2026-10-18, after the demo): each article keeps history —
  amending text ("modifié par loi X"), applies-from date, verifier. Enables "Amended by" badge on findings,
  audit trail, and checking a contract against the law as of its signing date. Today an edit overwrites the
  text and clears verification; repealed laws can only be set to Archived. See [[project-legal-corpus-service]].
- Legal corpus "Banking" category (loi 2016-48, BCT circulars currently fall under Other); splitter support for
  circulars numbered "1.", "2." instead of "Article".

---

## Known pre-existing issue
- **Production build fails on SCSS style budgets**: `npm run build` (prod config) errors out because `designer.component.scss` (12.12kB) and `signing.component.scss` (9.32kB) exceed the `anyComponentStyle` error budget (8kB, warning at 4kB) in angular.json. Not caused by any specific recent change — discovered 2026-07-04 during i18n work (which didn't touch these .scss files). `ng build --configuration=development` is unaffected (budgets aren't enforced there). Fix by raising the budget in angular.json or trimming those two stylesheets.

## Common gotchas

- **[MaxLength] on entity strings** — required for any string that will be indexed OR appears in the snapshot with a specific length. Without it, EF Core maps to nvarchar(max).
- **Migration snapshot is hand-written** — always follow the alphabetical property ordering + index format rules above.
- **VS locks DLLs** — `dotnet build` from the API project fails when VS is running the API. Build individual layers (Domain, Application, Infrastructure) instead; to compile-check the Api layer itself, redirect output past the lock: `dotnet build src/backend/SignFlow.Api -p:OutputPath=<scratchpad>\apibuild\` (compile still runs, copy lands elsewhere).
  - **Whole-solution builds and `dotnet ef` work too**, without stopping the running API: MSBuild reads environment variables as properties, so `export OutputPath='<scratchpad>\efbuild\'` before `dotnet build src/backend/SignFlow.slnx` or `dotnet ef migrations add X --project SignFlow.Infrastructure --startup-project SignFlow.Api` sidesteps the lock entirely. `dotnet ef` has no `-p:` passthrough, so the env var is the only way to make it work while the API is up. Verified 2026-08-13; `ef migrations add` then generates a clean migration + snapshot with no hand-editing.
- **PdfPig** is used for text extraction only; **PDFsharp** is used for stamping signatures onto PDFs.
- **CDK drag-drop on designer canvases** — don't put `cdkDrag` on absolutely-positioned placed fields inside the canvas `cdkDropList` (CDK hides the original, drags a clone, snaps back, and `event.distance` breaks when the pane auto-scrolls); use manual mousedown dragging like the resize handles. Also hide `.cdk-drag-placeholder` inside the canvas wrapper — CDK inserts an in-flow chip clone that renders below the pdf canvas. Fixed in both designers (pdf-template-designer and features/designer) 2026-07-11/12 — apply the same pattern to any future canvas.
- **Angular NgModel in standalone** — must import `FormsModule` explicitly.
- **DatePipe in standalone** — must import `DatePipe` explicitly (not included in CommonModule in v17+).
- **API base URL** — `environment.apiUrl` in Angular is `http://localhost:5005/api` (matches `launchSettings.json`, not 5000). Signing service endpoints use `/api/v1/sign/{token}/...` (public, no JWT).
- **Groq API key** — never in `appsettings.json`. Set the environment variable `Groq__ApiKey`, or put it in `src/backend/SignFlow.Api/appsettings.Local.json` (git-ignored).
