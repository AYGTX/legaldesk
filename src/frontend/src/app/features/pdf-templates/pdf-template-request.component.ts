import { Component, OnInit, OnDestroy, inject, signal, computed, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { PdfTemplateService } from '../../core/services/pdf-template.service';
import { UserService } from '../../core/services/user.service';
import { DocumentService } from '../../core/services/document.service';
import { AuthService } from '../../core/auth/auth.service';
import {
  PdfTemplateDetailDto, PdfTemplateFieldDto, PdfApproverSelection
} from '../../core/models/pdf-template.models';
import { UserDto, RecipientInfo } from '../../core/models';
import { ApproverEntry, createEmptyApproverEntry, isApproverEntryValid } from '../../core/models/approver-entry.model';
import { ApproverPickerComponent } from '../../shared/approver-picker/approver-picker.component';
import { RequestStepperComponent } from '../../shared/request-stepper/request-stepper.component';
import { RequestSuccessComponent } from '../../shared/request-success/request-success.component';
import { formatSla } from '../../core/utils/sla';

@Component({
  selector: 'sf-pdf-template-request',
  standalone: true,
  imports: [
    CommonModule, RouterLink, FormsModule,
    MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatCheckboxModule, MatProgressSpinnerModule, MatSnackBarModule,
    MatTooltipModule, TranslatePipe, ApproverPickerComponent, RequestStepperComponent, RequestSuccessComponent
  ],
  template: `
    <div class="req-page">
      <div class="req-header">
        <button mat-icon-button routerLink="/pdf-templates"><mat-icon>arrow_back</mat-icon></button>
        <div>
          <h1 class="req-title">{{ template()?.name ?? ('common.loading' | translate) }}</h1>
          <p class="req-sub">{{ 'pdfTemplates.request.subtitle' | translate }}</p>
        </div>
        @if (template()?.requestTypeId) {
          <span class="request-type-chip">
            <mat-icon>schedule</mat-icon>
            {{ template()!.requestTypeName }} · {{ formatSla(template()!.requestTypeSlaHours) }}
          </span>
        }
      </div>

      <sf-request-stepper [steps]="stepLabels" [currentIndex]="stepIndex()" />

      @if (step() === 'done') {
        <sf-request-success [recipients]="createdRecipients()">
          <a mat-flat-button routerLink="/requests" class="submit-btn">{{ 'requests.new.viewMyRequests' | translate }}</a>
          <a mat-stroked-button routerLink="/pdf-templates">{{ 'pdfTemplates.request.backToForms' | translate }}</a>
        </sf-request-success>
      } @else if (loading()) {
        <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (template(); as t) {
        <div class="req-grid">
          <div class="form-col">
            <!-- Requester fields -->
            @if (requesterFields().length > 0) {
              <section class="card">
                <h2 class="card-title">{{ 'pdfTemplates.request.yourFields' | translate }}</h2>
                @for (f of requesterFields(); track f.fieldKey) {
                  <div class="fld">
                    <label class="fld-label">{{ f.label }}@if (f.isRequired) { <span class="req-star">*</span> }</label>
                    @switch (f.type) {
                      @case ('Checkbox') {
                        <mat-checkbox [checked]="values[f.fieldKey] === 'true'"
                                      (change)="values[f.fieldKey] = $event.checked ? 'true' : 'false'">{{ f.label }}</mat-checkbox>
                      }
                      @case ('Dropdown') {
                        <mat-form-field appearance="outline" class="ff">
                          <mat-select [(ngModel)]="values[f.fieldKey]">
                            @for (opt of optionsOf(f); track opt) { <mat-option [value]="opt">{{ opt }}</mat-option> }
                          </mat-select>
                        </mat-form-field>
                      }
                      @case ('Date') {
                        <input class="ti" type="date" [(ngModel)]="values[f.fieldKey]" />
                      }
                      @case ('Number') {
                        <input class="ti" type="number" [(ngModel)]="values[f.fieldKey]" />
                      }
                      @default {
                        <input class="ti" type="text" [(ngModel)]="values[f.fieldKey]" [placeholder]="f.label" />
                      }
                    }
                  </div>
                }
              </section>
            }

            @if (autoFilledFields().length > 0) {
              <section class="card info-card">
                <mat-icon>auto_awesome</mat-icon>
                <span>{{ 'pdfTemplates.request.autoFilledNote' | translate: { fields: autoFilledFields().join(', ') } }}</span>
              </section>
            }

            <!-- Approver pickers -->
            @if (approverSlots().length > 0) {
              <section class="card">
                <h2 class="card-title">{{ 'pdfTemplates.request.approvers' | translate }}</h2>
                @for (slot of approverSlots(); track $index; let i = $index) {
                  <div class="approver-slot">
                    <div class="slot-head">
                      <span class="slot-num">{{ i + 1 }}</span>
                      <span>{{ 'pdfTemplates.request.approverN' | translate: { n: i + 1 } }}</span>
                    </div>
                    <sf-approver-picker [entry]="approvers[i]" [users]="users()" />
                  </div>
                }
              </section>
            } @else {
              <section class="card info-card">
                <mat-icon>info</mat-icon>
                <span>{{ 'pdfTemplates.request.approversPreassigned' | translate }}</span>
              </section>
            }

            <!-- Options -->
            <section class="card">
              <h2 class="card-title">{{ 'pdfTemplates.request.options' | translate }}</h2>
              <div class="fld">
                <label class="fld-label">{{ 'pdfTemplates.request.customTitle' | translate }}</label>
                <input class="ti" [(ngModel)]="title" [placeholder]="template()?.name" />
              </div>
              <div class="fld">
                <label class="fld-label">{{ 'pdfTemplates.request.message' | translate }}</label>
                <textarea class="ti" rows="2" [(ngModel)]="message"></textarea>
              </div>
            </section>

            <div class="submit-row">
              <button mat-flat-button class="submit-btn" [disabled]="submitting()" (click)="submit()">
                @if (submitting()) { <mat-spinner diameter="18"></mat-spinner> }
                <mat-icon>send</mat-icon> {{ 'pdfTemplates.request.sendRequest' | translate }}
              </button>
            </div>
          </div>

          <!-- Live PDF preview with a field overlay reflecting current/pre-filled values -->
          <aside class="preview-col">
            <div class="preview-toolbar">
              <span class="preview-label">{{ 'pdfTemplates.request.previewTitle' | translate }}</span>
              @if (t.pageCount > 1) {
                <div class="page-controls">
                  <button mat-icon-button (click)="prevPage()" [disabled]="currentPage() <= 1"><mat-icon>chevron_left</mat-icon></button>
                  <span class="page-indicator">{{ currentPage() }}/{{ t.pageCount }}</span>
                  <button mat-icon-button (click)="nextPage()" [disabled]="currentPage() >= t.pageCount"><mat-icon>chevron_right</mat-icon></button>
                </div>
              }
            </div>
            <div class="preview-frame" #previewFrame>
              @if (pdfLoading()) { <div class="pdf-loading"><mat-spinner diameter="36"></mat-spinner></div> }
              <!-- Scaler sizes the layout box to the fitted dimensions; the inner wrap keeps the native
                   scale-1.5 coordinate space so the field overlay stays pixel-aligned while CSS scales it. -->
              <div class="pdf-scaler" [style.width.px]="canvasWidth * previewScale()" [style.height.px]="canvasHeight * previewScale()">
                <div class="pdf-wrap" #pdfWrapper [style.width.px]="canvasWidth" [style.height.px]="canvasHeight"
                     [style.transform]="'scale(' + previewScale() + ')'">
                  <canvas #pdfCanvas class="pdf-canvas"></canvas>
                  @for (f of fieldsOnPage(t, currentPage()); track f.fieldKey) {
                  <div class="ov-field" [class.ov-approver]="f.fillRole === 'Approver'" [class.ov-empty]="!previewValueFor(f)"
                       [style.left.px]="f.x" [style.top.px]="f.y" [style.width.px]="f.width" [style.height.px]="f.height"
                       [matTooltip]="f.label">
                    @if (f.fillRole === 'Approver') {
                      <mat-icon class="ov-icon">draw</mat-icon><span class="ov-text">{{ approverLabelFor(f) }}</span>
                    } @else if (f.type === 'Checkbox') {
                      <mat-icon class="ov-icon">{{ previewValueFor(f) === 'true' ? 'check_box' : 'check_box_outline_blank' }}</mat-icon>
                    } @else {
                      <span class="ov-text">{{ previewValueFor(f) || f.label }}</span>
                    }
                  </div>
                  }
                </div>
              </div>
            </div>
            <p class="preview-hint">{{ 'pdfTemplates.request.previewHint' | translate }}</p>
          </aside>
        </div>
      }
    </div>
  `,
  styles: [`
    .req-page { max-width: 1500px; margin: 0 auto; }
    .req-header { display: flex; align-items: center; gap: 8px; margin-bottom: 20px; }
    .req-title { font-size: 22px; font-weight: 500; margin: 0; }
    .req-sub { font-size: 13px; color: #5f6368; margin: 0; }
    .request-type-chip { display: inline-flex; align-items: center; gap: 4px; font-size: 12px; color: #5f6368; background: #f1f3f4; border-radius: 12px; padding: 3px 10px; margin-left: auto; }
    .request-type-chip mat-icon { font-size: 15px; width: 15px; height: 15px; color: #5f6368; }
    .loading { display: flex; justify-content: center; padding: 64px; }
    .req-grid { display: grid; grid-template-columns: minmax(340px, 1fr) minmax(520px, 1.5fr); gap: 20px; align-items: start; }
    @media (max-width: 1100px) { .req-grid { grid-template-columns: 1fr; } }
    .card { background: #fff; border: 1px solid #e0e0e0; border-radius: 12px; padding: 20px; margin-bottom: 16px; }
    .card-title { font-size: 15px; font-weight: 500; margin: 0 0 14px; }
    .info-card { display: flex; align-items: center; gap: 10px; color: #5f6368; font-size: 13px; }
    .info-card mat-icon { color: #0b57d0; flex-shrink: 0; }
    .fld { margin-bottom: 14px; }
    .fld-label { display: block; font-size: 13px; font-weight: 500; color: #444; margin-bottom: 5px; }
    .req-star { color: #d93025; }
    .ti { width: 100%; min-height: 38px; padding: 8px 12px; border: 1px solid #c7d0dc; border-radius: 8px; font-size: 14px; font-family: inherit; box-sizing: border-box; outline: none; }
    .ti:focus { border-color: #0b57d0; }
    .ff { width: 100%; }
    .approver-slot { border: 1px solid #e8edf2; border-radius: 10px; padding: 14px; margin-bottom: 10px; }
    .slot-head { display: flex; align-items: center; gap: 8px; margin-bottom: 10px; font-size: 13px; font-weight: 500; }
    .slot-num { width: 22px; height: 22px; border-radius: 50%; background: #188038; color: #fff; display: flex; align-items: center; justify-content: center; font-size: 12px; }
    .submit-row { display: flex; justify-content: flex-end; }
    .submit-btn { background: #0b57d0 !important; color: #fff !important; border-radius: 22px !important; height: 44px; padding: 0 24px !important; }

    .preview-col { position: sticky; top: 16px; }
    .preview-toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px; }
    .preview-label { font-size: 12px; font-weight: 600; text-transform: uppercase; color: #80868b; }
    .page-controls { display: flex; align-items: center; }
    .page-indicator { font-size: 12px; color: #5f6368; }
    .preview-frame { border: 1px solid #e0e0e0; border-radius: 10px; background: #eef1f4; padding: 16px; overflow-x: hidden; overflow-y: auto; max-height: calc(100vh - 140px); display: flex; justify-content: center; position: relative; }
    .pdf-loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; z-index: 2; }
    .pdf-scaler { position: relative; flex-shrink: 0; }
    .pdf-wrap { position: absolute; top: 0; left: 0; transform-origin: top left; background: #fff; box-shadow: 0 2px 10px rgba(0,0,0,0.12); }
    .pdf-canvas { display: block; }
    .ov-field { position: absolute; border: 1.5px solid #0b57d0; border-radius: 3px; background: rgba(11,87,208,0.10); box-sizing: border-box; display: flex; align-items: center; gap: 3px; padding: 0 4px; overflow: hidden; }
    .ov-field.ov-approver { border-color: #188038; background: rgba(24,128,56,0.08); }
    .ov-field.ov-empty { border-style: dashed; background: rgba(0,0,0,0.03); }
    .ov-icon { font-size: 14px; width: 14px; height: 14px; color: inherit; flex-shrink: 0; }
    .ov-field:not(.ov-approver) .ov-icon { color: #0b57d0; }
    .ov-field.ov-approver .ov-icon { color: #188038; }
    .ov-text { font-size: 11px; line-height: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: #202124; }
    .ov-field.ov-empty .ov-text { color: #9aa0a6; font-style: italic; }
    .preview-hint { font-size: 11px; color: #9aa0a6; margin: 8px 2px 0; text-align: center; }
  `]
})
export class PdfTemplateRequestComponent implements OnInit, OnDestroy {
  @ViewChild('pdfCanvas') pdfCanvasRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('pdfWrapper') pdfWrapperRef!: ElementRef<HTMLDivElement>;
  @ViewChild('previewFrame') previewFrameRef?: ElementRef<HTMLDivElement>;

  private readonly route = inject(ActivatedRoute);
  private readonly svc = inject(PdfTemplateService);
  private readonly userService = inject(UserService);
  private readonly documentSvc = inject(DocumentService);
  private readonly auth = inject(AuthService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  readonly formatSla = formatSla;

  readonly step = signal<'prepare' | 'done'>('prepare');
  readonly stepLabels = [this.translate.instant('pdfTemplates.request.stepPrepare'), this.translate.instant('pdfTemplates.request.stepSent')];
  readonly stepIndex = computed(() => (this.step() === 'prepare' ? 0 : 1));
  readonly createdRecipients = signal<RecipientInfo[]>([]);

  readonly template = signal<PdfTemplateDetailDto | null>(null);
  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly users = signal<UserDto[]>([]);

  readonly pdfLoading = signal(true);
  readonly currentPage = signal(1);
  readonly previewScale = signal(1);
  canvasWidth = 794;
  canvasHeight = 1123;
  private pdfDoc: unknown = null;
  private readonly onWindowResize = () => this.fitPreview();

  values: Record<string, string> = {};
  approvers: ApproverEntry[] = [];
  title = '';
  message = '';
  templateId = '';

  private profileCompany?: string;
  private profileDepartment?: string;

  readonly requesterFields = computed(() =>
    (this.template()?.fields ?? []).filter(f => f.fillRole === 'Requester' && !f.autoFillFrom && f.type !== 'Signature')
      .sort((a, b) => a.order - b.order)
  );

  // Approver signature slots that still need a choice at request time (no field-level or template-level approver).
  readonly approverSlots = computed(() => {
    const t = this.template();
    if (!t) return [];
    if (t.defaultApproverId) return [];
    return t.fields
      .filter(f => f.fillRole === 'Approver' && f.type === 'Signature' && !f.preAssignedApproverId)
      .sort((a, b) => a.order - b.order);
  });

  /** Labels of fields that are auto-populated (shown to the user so they know why they're already filled in). */
  readonly autoFilledFields = computed(() =>
    (this.template()?.fields ?? []).filter(f => !!f.autoFillFrom).map(f => f.label)
  );

  optionsOf(f: PdfTemplateFieldDto): string[] {
    if (!f.optionsJson) return [];
    try { return JSON.parse(f.optionsJson) as string[]; } catch { return []; }
  }

  fieldsOnPage(t: PdfTemplateDetailDto, page: number): PdfTemplateFieldDto[] {
    return t.fields.filter(f => f.page === page);
  }

  /** Current preview value for a non-approver field: live-typed, auto-filled, or its static default. */
  previewValueFor(f: PdfTemplateFieldDto): string {
    if (f.fillRole === 'Approver') return '';
    if (f.autoFillFrom) return this.resolveAutoFill(f.autoFillFrom) ?? this.values[f.fieldKey] ?? '';
    return this.values[f.fieldKey] ?? f.defaultValue ?? '';
  }

  /** Who will sign an approver field, reflecting the current picker selection so the preview stays live. */
  approverLabelFor(f: PdfTemplateFieldDto): string {
    if (f.preAssignedApproverName) return f.preAssignedApproverName;
    if (this.template()?.defaultApproverName) return this.template()!.defaultApproverName!;
    const idx = this.approverSlots().findIndex(s => s.fieldKey === f.fieldKey);
    if (idx === -1) return f.label;
    const entry = this.approvers[idx];
    if (!entry) return f.label;
    if (entry.mode === 'user' && entry.userId) {
      return this.users().find(u => u.id === entry.userId)?.fullName ?? f.label;
    }
    if (entry.mode === 'external' && entry.externalName.trim()) return entry.externalName.trim();
    return f.label;
  }

  private resolveAutoFill(source: string): string | null {
    const user = this.auth.user();
    switch (source) {
      case 'user.fullName': return user?.fullName ?? null;
      case 'user.firstName': return user?.firstName ?? null;
      case 'user.lastName': return user?.lastName ?? null;
      case 'user.email': return user?.email ?? null;
      case 'user.company': return this.profileCompany ?? null;
      case 'user.department': return this.profileDepartment ?? user?.department ?? null;
      case 'system.today': return new Date().toISOString().slice(0, 10);
      case 'system.now': return new Date().toISOString().slice(0, 16).replace('T', ' ');
      case 'request.title': return this.title.trim() || this.template()?.name || null;
      default: return null;
    }
  }

  /** Populates values for every auto-fillable field immediately so the user sees them already filled in. */
  private prefillAutoFillFields(fields: PdfTemplateFieldDto[]): void {
    for (const f of fields) {
      if (!f.autoFillFrom) continue;
      const value = this.resolveAutoFill(f.autoFillFrom);
      if (value) this.values[f.fieldKey] = value;
    }
  }

  ngOnInit(): void {
    this.templateId = this.route.snapshot.paramMap.get('id')!;
    window.addEventListener('resize', this.onWindowResize);

    this.userService.getUsers({ pageSize: 200 }).subscribe({ next: r => this.users.set(r.items), error: () => {} });

    // Fetch the richer self profile (has company, unlike the auth-session UserInfo) purely for auto-fill preview accuracy.
    this.userService.getProfile().subscribe({
      next: p => { this.profileCompany = p.company; this.profileDepartment = p.department; },
      error: () => {}
    });

    this.svc.getById(this.templateId).subscribe({
      next: t => {
        this.template.set(t);
        this.prefillAutoFillFields(t.fields);
        this.approvers = this.approverSlots().map(() => createEmptyApproverEntry());
        this.loading.set(false);
        this.loadPdf();
      },
      error: () => { this.loading.set(false); this.snack.open(this.translate.instant('pdfTemplates.request.loadFailed'), 'OK', { duration: 4000 }); }
    });
  }

  private async loadPdf(): Promise<void> {
    try {
      const pdfjsLib = await import('pdfjs-dist');
      pdfjsLib.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/build/pdf.worker.mjs', import.meta.url).toString();
      const blob = await firstValueFrom(this.svc.stream(this.templateId));
      const data = await blob.arrayBuffer();
      this.pdfDoc = await pdfjsLib.getDocument({ data }).promise;
      this.pdfLoading.set(false);
      setTimeout(() => this.renderPage(this.currentPage()), 0);
    } catch {
      this.pdfLoading.set(false);
    }
  }

  private async renderPage(pageNum: number): Promise<void> {
    if (!this.pdfDoc || !this.pdfCanvasRef) return;
    const pdfjsLib = await import('pdfjs-dist');
    const pdfDocTyped = this.pdfDoc as Awaited<ReturnType<typeof pdfjsLib.getDocument>['promise']>;
    const page = await pdfDocTyped.getPage(pageNum);
    // Scale 1.5 matches the designer's rendering scale, which is what the fields' stored coordinates assume.
    const viewport = page.getViewport({ scale: 1.5 });
    const canvas = this.pdfCanvasRef.nativeElement;
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    this.canvasWidth = viewport.width;
    this.canvasHeight = viewport.height;
    const ctx = canvas.getContext('2d')!;
    await page.render({ canvasContext: ctx, viewport, canvas }).promise;
    this.fitPreview();
  }

  /** Scale the native scale-1.5 page down to fit the preview frame's width (never scales up past 1:1). */
  private fitPreview(): void {
    const frame = this.previewFrameRef?.nativeElement;
    if (!frame || !this.canvasWidth) return;
    const available = frame.clientWidth - 32; // matches .preview-frame horizontal padding
    this.previewScale.set(Math.min(1, Math.max(0.1, available / this.canvasWidth)));
  }

  ngOnDestroy(): void { window.removeEventListener('resize', this.onWindowResize); }

  async prevPage(): Promise<void> { if (this.currentPage() > 1) { this.currentPage.update(p => p - 1); await this.renderPage(this.currentPage()); } }
  async nextPage(): Promise<void> {
    const t = this.template();
    if (t && this.currentPage() < t.pageCount) { this.currentPage.update(p => p + 1); await this.renderPage(this.currentPage()); }
  }

  submit(): void {
    // Validate required requester fields.
    const missing = this.requesterFields().filter(f => f.isRequired && !(this.values[f.fieldKey] ?? '').trim());
    if (missing.length > 0) {
      this.snack.open(this.translate.instant('pdfTemplates.request.missingRequired', { fields: missing.map(f => f.label).join(', ') }), 'OK', { duration: 5000 });
      return;
    }

    // Build approver selections.
    const approverSelections: PdfApproverSelection[] = [];
    for (let i = 0; i < this.approverSlots().length; i++) {
      const a = this.approvers[i];
      if (a.mode === 'user') {
        if (!a.userId) { this.snack.open(this.translate.instant('pdfTemplates.request.selectApprover', { n: i + 1 }), 'OK', { duration: 4000 }); return; }
        approverSelections.push({ userId: a.userId });
      } else {
        if (!isApproverEntryValid(a)) { this.snack.open(this.translate.instant('pdfTemplates.request.completeApprover', { n: i + 1 }), 'OK', { duration: 4000 }); return; }
        approverSelections.push({ externalName: a.externalName.trim(), externalEmail: a.externalEmail.trim() });
      }
    }

    this.submitting.set(true);
    this.svc.submit(this.templateId, {
      filledValues: this.values,
      approvers: approverSelections.length ? approverSelections : undefined,
      title: this.title.trim() || undefined,
      message: this.message.trim() || undefined
    }).subscribe({
      next: res => {
        this.submitting.set(false);
        this.step.set('done');
        this.documentSvc.getDocument(res.documentId).subscribe({
          next: doc => this.createdRecipients.set(doc.recipients),
          error: () => {} // non-critical: the done screen still works without the link list
        });
      },
      error: (err: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.submitting.set(false);
        const msg = err.error?.errors ? Object.values(err.error.errors).flat().join(' ')
          : err.error?.detail ?? this.translate.instant('pdfTemplates.request.sendFailed');
        this.snack.open(msg, 'OK', { duration: 6000 });
      }
    });
  }
}
