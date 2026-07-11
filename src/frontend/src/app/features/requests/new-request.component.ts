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
import { UserService } from '../../core/services/user.service';
import { DocumentService } from '../../core/services/document.service';
import { AuthService } from '../../core/auth/auth.service';
import { HtmlTemplateDto, HtmlTemplateDetailDto, HtmlTemplateFieldDto } from '../../core/models/html-template.models';
import { UserDto, RecipientInfo } from '../../core/models';
import { ApproverEntry, createEmptyApproverEntry, isApproverEntryValid } from '../../core/models/approver-entry.model';
import { ApproverPickerComponent } from '../../shared/approver-picker/approver-picker.component';
import { RequestStepperComponent } from '../../shared/request-stepper/request-stepper.component';
import { RequestSuccessComponent } from '../../shared/request-success/request-success.component';

type Step = 'pick' | 'fill' | 'done';

@Component({
  selector: 'sf-new-request',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatSnackBarModule, TranslatePipe,
    ApproverPickerComponent, RequestStepperComponent, RequestSuccessComponent
  ],
  template: `
    <div class="nr-page" [class.wide]="step() === 'fill'">
      <div class="page-header">
        <a mat-icon-button routerLink="/requests" class="back-btn">
          <mat-icon>arrow_back</mat-icon>
        </a>
        <div>
          <h1 class="page-title">{{ 'requests.new.pageTitle' | translate }}</h1>
          <p class="page-sub">{{ 'requests.new.pageSub' | translate }}</p>
        </div>
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
            <div class="template-grid">
              @for (t of templates(); track t.id) {
                <div class="tpl-card" (click)="selectTemplate(t)">
                  <div class="tpl-icon">
                    <span class="material-symbols-outlined">article</span>
                  </div>
                  <div class="tpl-body">
                    <span class="tpl-name">{{ t.name }}</span>
                    @if (t.description) {
                      <p class="tpl-desc">{{ t.description }}</p>
                    }
                    <div class="tpl-meta">
                      <span>{{ (t.fieldCount === 1 ? 'requests.new.fieldCount' : 'requests.new.fieldCountPlural') | translate: { count: t.fieldCount } }}</span>
                      @if (t.defaultApproverName) {
                        <span class="sep">·</span>
                        <span>{{ 'requests.new.approverName' | translate: { name: t.defaultApproverName } }}</span>
                      }
                      @if (t.workflowType !== 'Sequential') {
                        <span class="sep">·</span>
                        <span>{{ workflowLabelKey(t.workflowType) | translate }}</span>
                      }
                    </div>
                  </div>
                  <mat-icon class="tpl-arrow">chevron_right</mat-icon>
                </div>
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
                    @if (!fixedApproverNameFor(sigF)) { <span class="required">*</span> }
                  </label>
                  @if (fixedApproverNameFor(sigF); as fixedName) {
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
              <div class="preview-content" [innerHTML]="previewHtml()"></div>
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
    .nr-page { max-width: 720px; margin: 0 auto; transition: max-width 0.2s ease; }
    .nr-page.wide { max-width: 1200px; }
    .page-header { display: flex; align-items: flex-start; gap: 8px; margin-bottom: 24px; }
    .back-btn { margin-top: -4px; }
    .page-title { font-size: 22px; font-weight: 500; color: #1f1f1f; margin: 0 0 4px; letter-spacing: -0.2px; }
    .page-sub { font-size: 13px; color: #5f6368; margin: 0; }

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

    .template-grid { display: flex; flex-direction: column; gap: 0; border: 1px solid #c7d0dc; border-radius: 12px; overflow: hidden; background: #fff; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .tpl-card { display: flex; align-items: center; gap: 16px; padding: 16px 20px; border-bottom: 1px solid #e8edf2; cursor: pointer; transition: background 0.1s; }
    .tpl-card:last-child { border-bottom: none; }
    .tpl-card:hover { background: #f0f4f9; }
    .tpl-icon { width: 44px; height: 44px; border-radius: 10px; background: #e8f0fe; color: #0b57d0; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .tpl-icon .material-symbols-outlined { font-size: 22px; }
    .tpl-body { flex: 1; min-width: 0; }
    .tpl-name { font-size: 14px; font-weight: 500; color: #1f1f1f; display: block; margin-bottom: 2px; }
    .tpl-desc { font-size: 13px; color: #5f6368; margin: 0 0 4px; }
    .tpl-meta { font-size: 12px; color: #80868b; display: flex; gap: 4px; }
    .sep { color: #c7d0dc; }
    .tpl-arrow { color: #9aa0a6; }

    /* Fill form */
    .fill-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24px; padding-bottom: 16px; border-bottom: 1px solid #e8edf2; }
    .fill-tpl-name { display: flex; align-items: center; gap: 8px; font-size: 15px; font-weight: 500; color: #1f1f1f; }
    .fill-tpl-name .material-symbols-outlined { color: #0b57d0; }

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
    .preview-content { padding: 20px 24px 28px; font-family: 'Google Sans Text', sans-serif; font-size: 14px; line-height: 1.6; color: #1f1f1f; max-height: 70vh; overflow-y: auto; }
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

  /** Sig fields with no fixed approver (no field-level assignment, no template-wide default) — these need a picker. */
  unassignedApproverSigFields = computed(() =>
    this.approverSigFields().filter(f => !f.preAssignedApproverId && !this.selectedTemplate()?.defaultApproverId)
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
      const value = this.filledValues[key];
      if (value && value.trim()) return this.escapeHtml(value);
      const label = field?.label ?? key;
      return `<span class="ph-empty">${this.escapeHtml(label)}</span>`;
    });

    return this.sanitizer.bypassSecurityTrustHtml(html);
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

  selectTemplate(t: HtmlTemplateDto) {
    this.selectTemplateById(t.id);
  }

  private selectTemplateById(id: string) {
    this.loadingTemplates.set(true);
    this.svc.getById(id).subscribe({
      next: detail => {
        this.selectedTemplate.set(detail);
        this.filledValues = {};
        this.prefillAutoFillFields(detail.fields);

        const unassignedCount = detail.fields
          .filter(f => f.role === 'Approver' && f.type === 'Signature' && !f.preAssignedApproverId && !detail.defaultApproverId)
          .length;
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
        null;
      if (value) this.filledValues[f.fieldKey] = value;
    }
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
      .filter(f => f.isRequired && !f.autoFillFrom && !this.filledValues[f.fieldKey]?.trim())
      .map(f => f.label);
    if (missing.length > 0) {
      this.snack.open(this.translate.instant('requests.new.missingFields', { fields: missing.join(', ') }), 'OK', { duration: 4000 });
      return;
    }

    this.submitting.set(true);
    this.svc.submit(t.id, {
      filledValues: this.filledValues,
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
