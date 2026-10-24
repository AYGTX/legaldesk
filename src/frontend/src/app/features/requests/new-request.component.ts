import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HtmlTemplateService } from '../../core/services/html-template.service';
import { TemplateCardComponent } from '../../shared/template-card/template-card.component';
import { fromHtmlTemplate } from '../../core/models/template-gallery.model';
import { UserService } from '../../core/services/user.service';
import { DocumentService } from '../../core/services/document.service';
import { AuthService } from '../../core/auth/auth.service';
import { HtmlTemplateDto, HtmlTemplateDetailDto, HtmlTemplateFieldDto } from '../../core/models/html-template.models';
import { UserDto, RecipientInfo } from '../../core/models';
import { ApproverEntry, createEmptyApproverEntry, isApproverEntryValid } from '../../core/models/approver-entry.model';
import { ApproverPickerComponent } from '../../shared/approver-picker/approver-picker.component';
import { RequestStepperComponent } from '../../shared/request-stepper/request-stepper.component';
import { RequestSuccessComponent } from '../../shared/request-success/request-success.component';
import { formatSla } from '../../core/utils/sla';

type Step = 'pick' | 'fill' | 'done';

@Component({
  selector: 'sf-new-request',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatSnackBarModule, TranslatePipe,
    ApproverPickerComponent, RequestStepperComponent, RequestSuccessComponent, TemplateCardComponent
  ],
  template: `
    <div class="nr-page" [class.wide]="step() !== 'done'">
      <div class="page-header">
        <div class="header-text">
          <h1 class="page-title">{{ 'requests.new.pageTitle' | translate }}</h1>
          <p class="page-subtitle">{{ 'requests.new.pageSub' | translate }}</p>
        </div>
        @if (step() === 'pick' && canManageTemplates()) {
          <a mat-stroked-button routerLink="/admin/html-templates/new" class="create-template-btn">
            <mat-icon>article</mat-icon> {{ 'templates.createHtml' | translate }}
          </a>
        }
      </div>

      <!-- Step indicator -->
      <sf-request-stepper [steps]="stepLabels" [currentIndex]="stepIndex()" />

      <!-- Step 1: Pick template -->
      @if (step() === 'pick') {
        <div class="step-content">
          <div class="search-wrap">
            <span class="material-symbols-outlined">search</span>
            <input [(ngModel)]="search" (input)="onSearch()" class="search-input" placeholder="{{ 'requests.new.searchPlaceholder' | translate }}" />
          </div>

          @if (loadingTemplates()) {
            <div class="center-spinner"><mat-spinner diameter="40"></mat-spinner></div>
          } @else if (templates().length === 0) {
            <div class="empty-state">
              <span class="material-symbols-outlined">description</span>
              <p>{{ 'requests.new.noTemplates' | translate }}</p>
            </div>
          } @else {
            <div class="list-surface">
              @for (t of galleryTemplates(); track t.id) {
                <sf-template-card [template]="t" [canManage]="false" (use)="selectTemplate(t)" />
              }
            </div>
          }
        </div>
      }

      <!-- Step 2: Fill form -->
      @if (step() === 'fill' && selectedTemplate()) {
        <div class="step-content">
          <div class="fill-header">
            <div class="fill-tpl-name">
              <span class="material-symbols-outlined">article</span>
              {{ selectedTemplate()!.name }}
              @if (selectedTemplate()!.requestTypeId) {
                <span class="request-type-chip">
                  <span class="material-symbols-outlined">schedule</span>
                  {{ selectedTemplate()!.requestTypeName }} · {{ formatSla(selectedTemplate()!.requestTypeSlaHours) }}
                </span>
              }
            </div>
            <button mat-button (click)="goBack()">
              <mat-icon>arrow_back</mat-icon> {{ 'requests.new.changeTemplate' | translate }}
            </button>
          </div>

          <div class="fill-layout">
            <div class="fill-form">
              <!-- Optional title override -->
              <div class="form-group">
                <label class="form-label">{{ 'requests.new.requestTitle' | translate }} <span class="optional">{{ 'requests.new.requestTitleHint' | translate }}</span></label>
                <input [(ngModel)]="requestTitle" class="form-input" placeholder="{{ selectedTemplate()!.name }}" />
              </div>

              <!-- Initiator fields -->
              @for (f of initiatorFields(); track f.fieldKey) {
                <div class="form-group">
                  <label class="form-label">
                    {{ f.label }}
                    @if (f.isRequired && !f.autoFillFrom) { <span class="required">*</span> }
                    @if (f.autoFillFrom) { <span class="autofill-label-badge"><span class="material-symbols-outlined">lock</span> {{ 'requests.new.autoFilled' | translate }}</span> }
                  </label>
                  @if (f.autoFillFrom) {
                    <input type="text" [value]="filledValues[f.fieldKey] ?? ''" class="form-input autofill-input" readonly />
                  } @else if (f.type === 'Textarea') {
                    <textarea [(ngModel)]="filledValues[f.fieldKey]" class="form-textarea" [placeholder]="f.label" rows="3"></textarea>
                  } @else if (f.type === 'Dropdown') {
                    <select [(ngModel)]="filledValues[f.fieldKey]" class="form-input">
                      <option value=""></option>
                      @for (o of optionsOf(f); track o) { <option [value]="o">{{ o }}</option> }
                    </select>
                  } @else if (f.type === 'Checkbox') {
                    <div class="checkbox-wrap">
                      <input type="checkbox" [id]="f.fieldKey"
                        [checked]="filledValues[f.fieldKey] === 'true'"
                        (change)="filledValues[f.fieldKey] = $any($event.target).checked ? 'true' : 'false'" />
                      <label [for]="f.fieldKey">{{ f.label }}</label>
                    </div>
                  } @else if (f.type === 'Date') {
                    <input type="date" [(ngModel)]="filledValues[f.fieldKey]" class="form-input" />
                  } @else if (f.type === 'Number') {
                    <input type="number" [(ngModel)]="filledValues[f.fieldKey]" class="form-input" [placeholder]="f.label" />
                  } @else {
                    <input type="text" [(ngModel)]="filledValues[f.fieldKey]" class="form-input" [placeholder]="f.label" />
                  }
                </div>
              }

              <!-- Approver picker(s) — one per Approver Signature field; fixed fields show who it's locked to -->
              @for (sigF of approverSigFields(); track sigF.fieldKey; let i = $index) {
                <div class="form-group">
                  <label class="form-label">
                    @if (approverSigFields().length > 1) {
                      {{ 'requests.new.approverN' | translate: { n: i + 1 } }} <span class="approver-field-name">— {{ sigF.label }}</span>
                    } @else {
                      {{ 'requests.new.approver' | translate }}
                    }
                    @if (!fixedApproverNameFor(sigF) && !presetStepFor(sigF)) { <span class="required">*</span> }
                  </label>
                  @if (presetStepFor(sigF); as step) {
                    <div class="approver-fixed">
                      <span class="material-symbols-outlined">route</span>
                      {{ 'requests.new.approverByWorkflow' | translate: { workflow: selectedTemplate()!.workflowPreset!.name, step: step.label || step.order, who: step.who } }}
                    </div>
                  } @else if (fixedApproverNameFor(sigF); as fixedName) {
                    <div class="approver-fixed">
                      <span class="material-symbols-outlined">person_check</span>
                      {{ 'requests.new.approverFixed' | translate: { name: fixedName } }}
                    </div>
                  } @else if (selectedApprovers[approverIndexFor(sigF)]; as entry) {
                    <sf-approver-picker [entry]="entry" [users]="users()" [loadingUsers]="loadingUsers()" />
                  }
                </div>
              }

              <!-- Message -->
              <div class="form-group">
                <label class="form-label">{{ 'requests.new.messageLabel' | translate }} <span class="optional">{{ 'requests.new.optional' | translate }}</span></label>
                <textarea [(ngModel)]="message" class="form-textarea" placeholder="{{ 'requests.new.messagePlaceholder' | translate }}" rows="2"></textarea>
              </div>

              <div class="form-actions">
                <button mat-stroked-button (click)="goBack()">{{ 'common.cancel' | translate }}</button>
                <button mat-flat-button class="submit-btn" (click)="submit()" [disabled]="submitting()">
                  @if (submitting()) { <mat-spinner diameter="18"></mat-spinner> }
                  @else { <mat-icon>send</mat-icon> }
                  {{ 'requests.new.sendForApproval' | translate }}
                </button>
              </div>
            </div>

            <!-- Live preview — mirrors how the recipient will see the rendered document -->
            <div class="preview-panel">
              <div class="preview-panel-header">
                <span class="material-symbols-outlined">visibility</span>
                <span>{{ 'requests.new.previewTitle' | translate }}</span>
              </div>
              <p class="preview-hint">{{ 'requests.new.previewHint' | translate }}</p>
              <div class="preview-content contract-doc" [innerHTML]="previewHtml()"></div>
            </div>
          </div>
        </div>
      }

      <!-- Step done -->
      @if (step() === 'done') {
        <div class="step-content">
          <sf-request-success [recipients]="createdRecipients()">
            <a mat-flat-button routerLink="/requests" class="submit-btn">{{ 'requests.new.viewMyRequests' | translate }}</a>
            <button mat-stroked-button (click)="startOver()">{{ 'requests.new.submitAnother' | translate }}</button>
          </sf-request-success>
        </div>
      }
    </div>
  `,
  styles: [`

    .step-content { animation: fadeIn 0.2s ease; }
    @keyframes fadeIn { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: none; } }

    /* Template picker */
    .search-wrap { position: relative; margin-bottom: 16px; }
    .search-wrap .material-symbols-outlined { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); font-size: 18px; color: #5f6368; }
    .search-input { width: 100%; height: 44px; padding: 0 16px 0 44px; border-radius: 22px; border: 1px solid #c7d0dc; background: #fff; font-size: 14px; font-family: inherit; outline: none; box-sizing: border-box; }
    .search-input:focus { border-color: #0b57d0; box-shadow: 0 0 0 2px rgba(11,87,208,0.15); }

    .center-spinner { display: flex; justify-content: center; padding: 48px; }
    .empty-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 48px; color: #9aa0a6; }
    .empty-state .material-symbols-outlined { font-size: 40px; }

    .list-surface { border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; overflow: hidden; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .header-text { flex: 1; }
    .create-template-btn { align-self: flex-start; }

    /* Fill form */
    .fill-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24px; padding-bottom: 16px; border-bottom: 1px solid #e8edf2; }
    .fill-tpl-name { display: flex; align-items: center; gap: 8px; font-size: 15px; font-weight: 500; color: #1f1f1f; }
    .fill-tpl-name .material-symbols-outlined { color: #0b57d0; }
    .request-type-chip { display: inline-flex; align-items: center; gap: 4px; font-size: 12px; font-weight: 400; color: #5f6368; background: #f1f3f4; border-radius: 12px; padding: 3px 10px; }
    .request-type-chip .material-symbols-outlined { font-size: 15px; color: #5f6368; }

    .fill-layout { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 32px; align-items: start; }
    @media (max-width: 900px) { .fill-layout { grid-template-columns: 1fr; } }

    .fill-form { display: flex; flex-direction: column; gap: 20px; }
    .form-group { display: flex; flex-direction: column; gap: 6px; }
    .form-label { font-size: 13px; font-weight: 500; color: #3c4043; }
    .required { color: #c5221f; margin-left: 2px; }
    .optional { font-weight: 400; color: #9aa0a6; font-size: 12px; }
    .form-input, .form-select, .form-textarea { border-radius: 8px; border: 1px solid #c7d0dc; padding: 10px 14px; font-size: 14px; font-family: inherit; outline: none; background: #fff; transition: border-color 0.15s, box-shadow 0.15s; }
    .form-input, .form-select { height: 44px; }
    .form-input:focus, .form-select:focus, .form-textarea:focus { border-color: #0b57d0; box-shadow: 0 0 0 2px rgba(11,87,208,0.15); }
    .form-textarea { resize: vertical; line-height: 1.5; }
    .form-select { width: 100%; }
    .checkbox-wrap { display: flex; align-items: center; gap: 8px; }

    .approver-fixed { display: flex; align-items: center; gap: 8px; padding: 12px 16px; background: #e8f0fe; border-radius: 8px; font-size: 14px; color: #1f1f1f; }
    .approver-fixed .material-symbols-outlined { color: #0b57d0; }

    .autofill-input { background: #f0f4f9 !important; color: #5f6368; cursor: default; border-color: #c7d0dc !important; }
    .autofill-label-badge { display: inline-flex; align-items: center; gap: 2px; font-size: 11px; font-weight: 500; color: #0b57d0; margin-left: 6px; vertical-align: middle; }
    .autofill-label-badge .material-symbols-outlined { font-size: 13px; }
    .approver-field-name { font-weight: 400; color: #80868b; }

    .form-actions { display: flex; justify-content: flex-end; gap: 12px; padding-top: 8px; }
    .submit-btn { background: #0b57d0 !important; color: #fff !important; border-radius: 20px !important; display: flex; align-items: center; gap: 6px; }

    /* Live preview panel */
    .preview-panel { position: sticky; top: 16px; border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; box-shadow: 0 1px 2px rgba(0,0,0,0.06); overflow: hidden; }
    .preview-panel-header { display: flex; align-items: center; gap: 8px; padding: 14px 18px; border-bottom: 1px solid #e8edf2; background: #f8faff; font-size: 13px; font-weight: 500; color: #1f1f1f; }
    .preview-panel-header .material-symbols-outlined { font-size: 18px; color: #0b57d0; }
    .preview-hint { font-size: 12px; color: #9aa0a6; margin: 10px 18px 0; }
    .preview-content { padding: 20px 24px 28px; max-height: 70vh; overflow-y: auto; }
    .preview-content ::ng-deep .ph-empty { background: #fff3cd; color: #7a5c00; padding: 1px 6px; border-radius: 4px; font-style: italic; }
    .preview-content ::ng-deep .ph-sig { display: inline-block; padding: 6px 12px; border: 1px dashed #0b57d0; border-radius: 6px; color: #0b57d0; font-style: italic; font-size: 12px; }

  `]
})
export class NewRequestComponent implements OnInit {
  private readonly svc = inject(HtmlTemplateService);
  private readonly userSvc = inject(UserService);
  private readonly documentSvc = inject(DocumentService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly sanitizer = inject(DomSanitizer);
  readonly formatSla = formatSla;

  step = signal<Step>('pick');
  templates = signal<HtmlTemplateDto[]>([]);
  selectedTemplate = signal<HtmlTemplateDetailDto | null>(null);
  users = signal<UserDto[]>([]);
  loadingTemplates = signal(true);
  loadingUsers = signal(false);
  submitting = signal(false);
  createdRecipients = signal<RecipientInfo[]>([]);

  search = '';
  requestTitle = '';
  message = '';
  /** One entry per unassigned Approver Signature field (ordered) — user pick or external signer. */
  selectedApprovers: ApproverEntry[] = [];
  filledValues: Record<string, string> = {};

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly stepLabels = [this.translate.instant('requests.new.stepChoose'), this.translate.instant('requests.new.stepFill')];
  readonly stepIndex = computed(() => (this.step() === 'pick' ? 0 : this.step() === 'fill' ? 1 : 2));

  /** Initiator fields shown in the fill form (excluding Signature type). */
  initiatorFields = computed(() =>
    (this.selectedTemplate()?.fields ?? [])
      .filter(f => f.role === 'Initiator' && f.type !== 'Signature')
      .sort((a, b) => a.order - b.order)
  );

  /** Ordered Approver Signature fields — one per required signer. */
  approverSigFields = computed(() =>
    (this.selectedTemplate()?.fields ?? [])
      .filter(f => f.role === 'Approver' && f.type === 'Signature')
      .sort((a, b) => a.order - b.order)
  );

  /**
   * The workflow step that decides who signs this field, when the template follows a workflow: its steps go to the
   * approver signature fields in order. Null for fields past the last step.
   */
  presetStepFor(f: HtmlTemplateFieldDto) {
    const steps = this.selectedTemplate()?.workflowPreset?.steps ?? [];
    return steps[this.approverSigFields().indexOf(f)] ?? null;
  }

  /** Sig fields nobody has been named for (no workflow step, no field-level assignment, no template default) — these need a picker. */
  unassignedApproverSigFields = computed(() =>
    this.approverSigFields().filter(f =>
      !this.presetStepFor(f) && !f.preAssignedApproverId && !this.selectedTemplate()?.defaultApproverId)
  );

  /** The approver name to display as fixed for a field, or null if the initiator must pick one. */
  fixedApproverNameFor(f: HtmlTemplateFieldDto): string | null {
    return f.preAssignedApproverName ?? this.selectedTemplate()?.defaultApproverName ?? null;
  }

  /** Index into selectedApproverIds for a field that needs a picker. */
  approverIndexFor(f: HtmlTemplateFieldDto): number {
    return this.unassignedApproverSigFields().indexOf(f);
  }

  /**
   * Live-rendered preview of the template with current field values substituted — mirrors the
   * signing-page rendering (GetSigningSessionQuery). Unfilled fields and signature placeholders
   * are shown as highlighted markers so the initiator can see where they'll land.
   */
  previewHtml(): SafeHtml {
    const t = this.selectedTemplate();
    if (!t) return this.sanitizer.bypassSecurityTrustHtml('');

    const html = t.htmlContent.replace(/\{\{(\w+)\}\}/g, (match, key: string) => {
      const field = t.fields.find(f => f.fieldKey === key);
      if (field?.type === 'Signature') {
        return `<span class="ph-sig">${this.escapeHtml(field.label)}</span>`;
      }
      const value = this.filledValue(key);
      if (value.trim()) return this.escapeHtml(value);
      const label = field?.label ?? key;
      return `<span class="ph-empty">${this.escapeHtml(label)}</span>`;
    });

    return this.sanitizer.bypassSecurityTrustHtml(html);
  }

  /** Field values as text — a type="number" input stores a JS number, not a string. */
  private filledValue(key: string): string {
    const value: unknown = this.filledValues[key];
    return value === null || value === undefined ? '' : String(value);
  }

  private escapeHtml(value: string): string {
    const div = document.createElement('div');
    div.textContent = value;
    return div.innerHTML;
  }

  ngOnInit() {
    this.loadTemplates();
    // One-click deep link from the unified /templates gallery — pre-select the template it linked to.
    const templateId = this.route.snapshot.queryParamMap.get('templateId');
    if (templateId) this.selectTemplateById(templateId);
  }

  /** Creating a template is a manager task; everyone else only picks. */
  readonly canManageTemplates = () => {
    const roles = this.auth.user()?.roles ?? [];
    return roles.includes('Administrator') || roles.includes('Manager');
  };

  /** The gallery card takes the unified template shape; this page lists HTML templates only. */
  readonly galleryTemplates = computed(() => this.templates().map(fromHtmlTemplate));

  workflowLabelKey(w: string): string {
    return w === 'Parallel' ? 'designer.workflow.parallel' : w === 'Mixed' ? 'designer.workflow.mixed' : 'designer.workflow.sequential';
  }

  onSearch() {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.loadTemplates(), 300);
  }

  loadTemplates() {
    this.loadingTemplates.set(true);
    this.svc.getAll({ activeOnly: true, search: this.search || undefined, pageSize: 100 }).subscribe({
      next: r => { this.templates.set(r.items); this.loadingTemplates.set(false); },
      error: () => this.loadingTemplates.set(false)
    });
  }

  selectTemplate(t: { id: string }) {
    this.selectTemplateById(t.id);
  }

  private selectTemplateById(id: string) {
    this.loadingTemplates.set(true);
    this.svc.getById(id).subscribe({
      next: detail => {
        this.selectedTemplate.set(detail);
        this.filledValues = {};
        this.prefillAutoFillFields(detail.fields);

        const unassignedCount = this.unassignedApproverSigFields().length;
        this.selectedApprovers = Array.from({ length: unassignedCount }, () => createEmptyApproverEntry());

        this.step.set('fill');
        this.loadingTemplates.set(false);

        if (unassignedCount > 0) {
          this.loadingUsers.set(true);
          this.userSvc.getUsers({ pageSize: 200 }).subscribe(r => {
            this.users.set(r.items);
            this.loadingUsers.set(false);
          });
        }
      },
      error: () => this.loadingTemplates.set(false)
    });
  }

  private prefillAutoFillFields(fields: HtmlTemplateFieldDto[]): void {
    const user = this.auth.user();
    if (!user) return;
    for (const f of fields) {
      if (!f.autoFillFrom) continue;
      const value =
        f.autoFillFrom === 'user.fullName' ? user.fullName :
        f.autoFillFrom === 'user.firstName' ? user.firstName :
        f.autoFillFrom === 'user.lastName' ? user.lastName :
        f.autoFillFrom === 'user.email' ? user.email :
        f.autoFillFrom === 'user.department' ? user.department :
        f.autoFillFrom === 'user.company' ? user.company :
        null;
      if (value) this.filledValues[f.fieldKey] = value;
    }

    // Request-level sources and the template's own defaults, so the form opens with what the server would fill anyway.
    const today = new Date().toISOString().slice(0, 10);
    const sources: Record<string, string> = {
      'system.today': today,
      'system.now': `${today} ${new Date().toISOString().slice(11, 16)}`,
      'request.title': this.selectedTemplate()?.name ?? ''
    };
    for (const f of fields) {
      if (f.autoFillFrom && sources[f.autoFillFrom] && !this.filledValues[f.fieldKey]) this.filledValues[f.fieldKey] = sources[f.autoFillFrom];
      else if (!f.autoFillFrom && f.role === 'Initiator' && f.defaultValue && !this.filledValues[f.fieldKey]) this.filledValues[f.fieldKey] = f.defaultValue;
    }
  }

  optionsOf(f: HtmlTemplateFieldDto): string[] {
    if (!f.optionsJson) return [];
    try { return JSON.parse(f.optionsJson) as string[]; } catch { return []; }
  }

  goBack() {
    this.step.set('pick');
    this.selectedTemplate.set(null);
    this.filledValues = {};
    this.selectedApprovers = [];
  }

  submit() {
    const t = this.selectedTemplate();
    if (!t) return;

    const missingApprover = this.selectedApprovers.some(e => !isApproverEntryValid(e));
    if (missingApprover) {
      this.snack.open(this.translate.instant('requests.new.missingApprover'), 'OK', { duration: 3000 });
      return;
    }

    // Validate required non-autofill fields
    const missing = this.initiatorFields()
      .filter(f => f.isRequired && !f.autoFillFrom && !this.filledValue(f.fieldKey).trim())
      .map(f => f.label);
    if (missing.length > 0) {
      this.snack.open(this.translate.instant('requests.new.missingFields', { fields: missing.join(', ') }), 'OK', { duration: 4000 });
      return;
    }

    this.submitting.set(true);
    this.svc.submit(t.id, {
      filledValues: Object.fromEntries(Object.entries(this.filledValues).map(([k, v]) => [k, String(v ?? '')])),
      approvers: this.selectedApprovers.length > 0
        ? this.selectedApprovers.map(e => e.mode === 'user'
            ? { userId: e.userId }
            : { externalName: e.externalName.trim(), externalEmail: e.externalEmail.trim() })
        : undefined,
      title: this.requestTitle || undefined,
      message: this.message || undefined
    }).subscribe({
      next: res => {
        this.submitting.set(false);
        this.step.set('done');
        this.documentSvc.getDocument(res.documentId).subscribe({
          next: doc => this.createdRecipients.set(doc.recipients),
          error: () => {} // non-critical: the done screen still works without the link list
        });
      },
      error: () => {
        this.submitting.set(false);
        this.snack.open(this.translate.instant('requests.new.submitFailed'), 'OK', { duration: 3000 });
      }
    });
  }

  startOver() {
    this.step.set('pick');
    this.selectedTemplate.set(null);
    this.filledValues = {};
    this.requestTitle = '';
    this.message = '';
    this.selectedApprovers = [];
    this.createdRecipients.set([]);
    this.loadTemplates();
  }
}
