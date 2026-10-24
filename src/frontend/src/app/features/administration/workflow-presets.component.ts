import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Observable } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { WorkflowPresetService } from '../../core/services/workflow-preset.service';
import { UserService } from '../../core/services/user.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { UserDto } from '../../core/models';
import { WorkflowType } from '../../core/models/document.models';
import { PresetApproverKind, WorkflowPresetDto, WorkflowPresetStepInput } from '../../core/models/workflow-preset.models';
import { WorkflowTypeToggleComponent } from '../../shared/workflow-type-toggle/workflow-type-toggle.component';

/** A step while it is being edited: every kind's value is kept so switching kind does not lose what was typed. */
interface StepDraft {
  stage: number;
  label: string;
  kind: PresetApproverKind;
  userId: string;
  roleName: string;
  department: string;
  conditional: boolean;
  conditionFieldKey: string;
  conditionOperator: string;
  conditionValue: string;
}

const OPERATORS = ['', '==', '!=', '>', '>=', '<', '<='];

const emptyStep = (): StepDraft => ({
  stage: 1, label: '', kind: 'User', userId: '', roleName: '', department: '',
  conditional: false, conditionFieldKey: '', conditionOperator: '', conditionValue: ''
});

/**
 * The administrator's workflow library: named approval paths that templates follow, set up once and changed in one
 * place. A step names a person, or a role or department that is looked up when a request is created.
 */
@Component({
  selector: 'sf-workflow-presets',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatSlideToggleModule, MatProgressSpinnerModule, MatTooltipModule, MatSnackBarModule, TranslatePipe,
    WorkflowTypeToggleComponent
  ],
  template: `
    <div class="wp-page">
      @if (!editing()) {
        <div class="wp-toolbar">
          <div class="wp-heading">
            <h2 class="wp-title">{{ 'admin.tabs.workflows' | translate }}</h2>
            <p class="intro">{{ 'admin.workflows.intro' | translate }}</p>
          </div>
          <div class="wp-actions">
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="wp-search">
              <mat-icon matPrefix>search</mat-icon>
              <input matInput [ngModel]="search()" (ngModelChange)="search.set($event)"
                     [placeholder]="'admin.workflows.search' | translate" />
              @if (search()) {
                <button matSuffix mat-icon-button (click)="search.set('')"><mat-icon>close</mat-icon></button>
              }
            </mat-form-field>
            <button mat-flat-button color="primary" (click)="startCreate()">
              <mat-icon>add</mat-icon> {{ 'admin.workflows.new' | translate }}
            </button>
          </div>
        </div>
      }

      @if (editing()) {
        <section class="editor">
          <header class="editor-bar">
            <button mat-button type="button" class="back" (click)="requestClose()">
              <mat-icon>arrow_back</mat-icon> {{ 'admin.workflows.back' | translate }}
            </button>
            <h2>{{ (editId() ? 'admin.workflows.editTitle' : 'admin.workflows.createTitle') | translate }}</h2>
            <span class="spacer"></span>
            @if (isDirty()) { <span class="unsaved"><span class="dot"></span>{{ 'admin.workflows.unsaved' | translate }}</span> }
            <button mat-button type="button" (click)="requestClose()" [disabled]="saving()">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button color="primary" type="button" (click)="save()" [disabled]="saving()">
              @if (saving()) { <mat-spinner diameter="18"></mat-spinner> }
              {{ 'common.save' | translate }}
            </button>
          </header>

          <div class="editor-grid">
          <div class="editor-form">
          @if (error()) { <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div> }

          <div class="row">
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
              <mat-label>{{ 'admin.workflows.name' | translate }}</mat-label>
              <input matInput [(ngModel)]="name" [placeholder]="'admin.workflows.namePlaceholder' | translate" />
            </mat-form-field>
            @if (editId()) {
              <mat-slide-toggle [(ngModel)]="isActive">{{ (isActive ? 'htmlTemplateEditor.active' : 'htmlTemplateEditor.inactive') | translate }}</mat-slide-toggle>
            }
          </div>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full">
            <mat-label>{{ 'common.description' | translate }}</mat-label>
            <input matInput [(ngModel)]="description" />
          </mat-form-field>

          <label class="lbl">{{ 'admin.workflows.howTheyAct' | translate }}</label>
          <sf-workflow-type-toggle [value]="workflowType()" (valueChange)="workflowType.set($event)"></sf-workflow-type-toggle>

          <label class="lbl">{{ 'admin.workflows.steps' | translate }}</label>
          <p class="hint">{{ 'admin.workflows.stepsHint' | translate }}</p>

          @for (s of steps(); track s; let i = $index) {
            <div class="step">
              <span class="badge">{{ i + 1 }}</span>
              <div class="step-body">
                <div class="row">
                  <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
                    <mat-label>{{ 'admin.workflows.stepLabel' | translate }}</mat-label>
                    <input matInput [(ngModel)]="s.label" [placeholder]="'admin.workflows.stepLabelPlaceholder' | translate" />
                  </mat-form-field>
                  @if (workflowType() === 'Mixed') {
                    <mat-form-field appearance="outline" subscriptSizing="dynamic" class="stage">
                      <mat-label>{{ 'admin.workflows.stage' | translate }}</mat-label>
                      <input matInput type="number" min="1" [(ngModel)]="s.stage" />
                    </mat-form-field>
                  }
                </div>
                <div class="row">
                  <mat-form-field appearance="outline" subscriptSizing="dynamic" class="kind">
                    <mat-label>{{ 'admin.workflows.approver' | translate }}</mat-label>
                    <mat-select [(ngModel)]="s.kind">
                      <mat-option value="User">{{ 'admin.workflows.kind.User' | translate }}</mat-option>
                      <mat-option value="Role">{{ 'admin.workflows.kind.Role' | translate }}</mat-option>
                      <mat-option value="Department">{{ 'admin.workflows.kind.Department' | translate }}</mat-option>
                    </mat-select>
                  </mat-form-field>
                  @switch (s.kind) {
                    @case ('User') {
                      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
                        <mat-label>{{ 'admin.workflows.kind.User' | translate }}</mat-label>
                        <mat-select [(ngModel)]="s.userId">
                          @for (u of users(); track u.id) { <mat-option [value]="u.id">{{ u.fullName }} ({{ u.email }})</mat-option> }
                        </mat-select>
                      </mat-form-field>
                    }
                    @case ('Role') {
                      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
                        <mat-label>{{ 'admin.workflows.kind.Role' | translate }}</mat-label>
                        <mat-select [(ngModel)]="s.roleName">
                          @for (r of roles(); track r) { <mat-option [value]="r">{{ r }}</mat-option> }
                        </mat-select>
                      </mat-form-field>
                    }
                    @default {
                      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
                        <mat-label>{{ 'admin.workflows.kind.Department' | translate }}</mat-label>
                        <mat-select [(ngModel)]="s.department">
                          @for (d of departments(); track d) { <mat-option [value]="d">{{ d }}</mat-option> }
                        </mat-select>
                      </mat-form-field>
                    }
                  }
                </div>
                @if (s.kind !== 'User') { <p class="hint">{{ 'admin.workflows.exactlyOne' | translate }}</p> }

                <mat-slide-toggle [(ngModel)]="s.conditional" class="cond-toggle">{{ 'admin.workflows.onlyWhen' | translate }}</mat-slide-toggle>
                @if (s.conditional) {
                  <div class="row">
                    <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
                      <mat-label>{{ 'admin.workflows.fieldKey' | translate }}</mat-label>
                      <input matInput [(ngModel)]="s.conditionFieldKey" placeholder="amount" />
                    </mat-form-field>
                    <mat-form-field appearance="outline" subscriptSizing="dynamic" class="op">
                      <mat-label>{{ 'admin.workflows.operator' | translate }}</mat-label>
                      <mat-select [(ngModel)]="s.conditionOperator">
                        @for (o of operators; track o) {
                          <mat-option [value]="o">{{ o || ('admin.workflows.isFilled' | translate) }}</mat-option>
                        }
                      </mat-select>
                    </mat-form-field>
                    @if (s.conditionOperator) {
                      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
                        <mat-label>{{ 'admin.workflows.value' | translate }}</mat-label>
                        <input matInput [(ngModel)]="s.conditionValue" placeholder="10000" />
                      </mat-form-field>
                    }
                  </div>
                  <p class="hint">{{ 'admin.workflows.conditionHint' | translate }}</p>
                }
              </div>
              <div class="moves">
                <button mat-icon-button type="button" (click)="move(i, -1)" [disabled]="i === 0"><mat-icon>keyboard_arrow_up</mat-icon></button>
                <button mat-icon-button type="button" (click)="move(i, 1)" [disabled]="i === steps().length - 1"><mat-icon>keyboard_arrow_down</mat-icon></button>
                <button mat-icon-button type="button" (click)="removeStep(i)" [disabled]="steps().length === 1"><mat-icon>delete_outline</mat-icon></button>
              </div>
            </div>
          }
          <button mat-stroked-button type="button" (click)="addStep()"><mat-icon>add</mat-icon> {{ 'admin.workflows.addStep' | translate }}</button>

          </div>

          <aside class="preview">
            <h3><mat-icon>route</mat-icon> {{ 'admin.workflows.preview' | translate }}</h3>
            <p class="hint">{{ 'admin.workflows.previewHint' | translate }}</p>
            <ol class="flow">
              @for (group of previewGroups(); track $index; let last = $last) {
                <li class="flow-group" [class.last]="last">
                  @if (group.length > 1) { <span class="together">{{ 'admin.workflows.together' | translate }}</span> }
                  @for (item of group; track item.n) {
                    <div class="flow-step">
                      <span class="badge">{{ item.n }}</span>
                      <div class="flow-text">
                        <strong>{{ item.s.label || ('admin.workflows.step' | translate) }}</strong>
                        <span class="flow-who">{{ whoDraft(item.s) }}</span>
                        @if (item.s.conditional && item.s.conditionFieldKey) {
                          <span class="cond">{{ 'admin.workflows.onlyWhen' | translate }} {{ conditionText(item.s) }}</span>
                        }
                      </div>
                    </div>
                  }
                </li>
              }
            </ol>
          </aside>
          </div>
        </section>
      } @else if (loading()) {
        <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (presets().length === 0) {
        <div class="empty">
          <mat-icon>route</mat-icon>
          <p>{{ 'admin.workflows.none' | translate }}</p>
          <button mat-stroked-button (click)="startCreate()">{{ 'admin.workflows.createFirst' | translate }}</button>
        </div>
      } @else if (filteredPresets().length === 0) {
        <div class="empty">
          <mat-icon>search_off</mat-icon>
          <p>{{ 'admin.workflows.noMatch' | translate }}</p>
        </div>
      } @else {
        <div class="grid">
          @for (p of filteredPresets(); track p.id) {
            <div class="card" [class.inactive]="!p.isActive">
              <div class="card-head">
                <strong>{{ p.name }}</strong>
                <span class="pill">{{ 'workflow.type.' + p.workflowType | translate }}</span>
                @if (!p.isActive) { <span class="pill off">{{ 'htmlTemplateEditor.inactive' | translate }}</span> }
                <span class="spacer"></span>
                <button mat-icon-button (click)="startEdit(p)" [matTooltip]="'common.edit' | translate"><mat-icon>edit</mat-icon></button>
                <button mat-icon-button (click)="remove(p)" [matTooltip]="'common.delete' | translate"><mat-icon>delete_outline</mat-icon></button>
              </div>
              @if (p.description) { <p class="desc">{{ p.description }}</p> }
              <ol class="path">
                @for (s of p.steps; track s.order) {
                  <li>
                    <span class="badge">{{ s.order }}</span>
                    <span class="who"><strong>{{ s.label || ('admin.workflows.step' | translate) }}</strong> · {{ who(s) }}</span>
                    @if (s.conditionFieldKey) { <span class="cond">{{ 'admin.workflows.onlyWhen' | translate }} {{ conditionText(s) }}</span> }
                  </li>
                }
              </ol>
              <p class="used">{{ 'admin.workflows.usedBy' | translate: { count: p.templateCount } }}</p>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .wp-page { max-width: 1160px; }
    .wp-toolbar { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 16px; flex-wrap: wrap; }
    .wp-heading { min-width: 0; }
    .wp-title { margin: 0 0 4px; font-size: 20px; font-weight: 600; color: #202124; }
    .intro { margin: 0; color: #5f6368; max-width: 640px; }
    .wp-actions { display: flex; align-items: center; gap: 10px; }
    .wp-search { width: 260px; }
    .loading, .empty { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 56px 0; color: #5f6368; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(420px, 1fr)); gap: 14px; }
    .card { background: #fff; border: 1px solid #c7d0dc; border-radius: 12px; padding: 14px 16px; }
    .card.inactive { opacity: 0.65; }
    .card-head { display: flex; align-items: center; gap: 8px; }
    .spacer { flex: 1; }
    .pill { font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: 10px; background: #e8f0fe; color: #0b57d0; }
    .pill.off { background: #f1f3f4; color: #5f6368; }
    .desc { margin: 6px 0 0; color: #5f6368; font-size: 13px; }
    .path { list-style: none; margin: 12px 0 8px; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .path li { display: flex; align-items: baseline; flex-wrap: wrap; gap: 4px 8px; font-size: 13px; }
    .badge { flex: none; width: 22px; height: 22px; border-radius: 50%; background: #0b57d0; color: #fff; font-size: 12px; font-weight: 600; display: inline-flex; align-items: center; justify-content: center; }
    .cond { width: 100%; padding-left: 30px; font-size: 12px; color: #b06000; }
    .used { margin: 0; font-size: 12px; color: #80868b; }
    .editor { background: #fff; border: 1px solid #c7d0dc; border-radius: 12px; }
    .editor-bar { position: sticky; top: 0; z-index: 10; display: flex; align-items: center; gap: 8px; padding: 10px 16px;
      background: #fff; border-bottom: 1px solid #e0e4ea; border-radius: 12px 12px 0 0; box-shadow: 0 2px 6px rgba(0,0,0,0.04); }
    .editor-bar h2 { margin: 0 0 0 4px; font-size: 17px; font-weight: 500; }
    .editor-bar .back { margin-left: -6px; }
    .unsaved { display: inline-flex; align-items: center; gap: 6px; margin-right: 6px; font-size: 12px; color: #b06000; }
    .unsaved .dot { width: 8px; height: 8px; border-radius: 50%; background: #f29900; }
    .editor-grid { display: grid; grid-template-columns: minmax(0, 1fr) 300px; gap: 28px; padding: 20px 22px 28px; align-items: start; }
    .editor-form { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
    .preview { position: sticky; top: 76px; padding: 14px 16px; border: 1px solid #e0e4ea; border-radius: 12px; background: #f8fafd; }
    .preview h3 { display: flex; align-items: center; gap: 6px; margin: 0 0 4px; font-size: 14px; font-weight: 600; }
    .preview h3 mat-icon { font-size: 18px; width: 18px; height: 18px; color: #0b57d0; }
    .flow { list-style: none; margin: 14px 0 0; padding: 0; }
    .flow-group { position: relative; display: flex; flex-direction: column; gap: 10px; padding-bottom: 18px; }
    .flow-group:not(.last)::after { content: ''; position: absolute; left: 10px; top: 24px; bottom: 0; width: 2px; background: #c2d7f5; }
    .flow-group.last { padding-bottom: 0; }
    .together { align-self: flex-start; margin-left: 32px; font-size: 11px; font-weight: 600; color: #0b57d0; background: #e8f0fe; padding: 1px 8px; border-radius: 10px; }
    .flow-step { display: flex; gap: 10px; align-items: flex-start; }
    .flow-text { display: flex; flex-direction: column; font-size: 13px; min-width: 0; }
    .flow-who { color: #5f6368; }
    .flow-text .cond { width: auto; padding-left: 0; }
    @media (max-width: 960px) { .editor-grid { grid-template-columns: 1fr; } .preview { position: static; } }
    .row { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    .grow { flex: 1; min-width: 200px; }
    .full { width: 100%; }
    .kind { width: 170px; } .stage { width: 110px; } .op { width: 170px; }
    .lbl { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.06em; color: #5f6368; margin-top: 6px; }
    .hint { margin: 0; font-size: 12px; color: #5f6368; line-height: 1.5; }
    .step { display: flex; gap: 12px; align-items: flex-start; padding: 12px; border: 1px solid #e0e4ea; border-radius: 10px; background: #fbfcfe; }
    .step-body { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 10px; }
    .moves { display: flex; flex-direction: column; }
    .cond-toggle { margin-top: 2px; }
    .error-banner { display: flex; align-items: center; gap: 8px; padding: 10px 12px; border-radius: 8px; background: #fce8e6; color: #c5221f; font-size: 13px; }
    .editor ::ng-deep .mat-mdc-form-field-infix { min-height: 44px; padding-top: 10px !important; padding-bottom: 10px !important; font-size: 14px; }
  `]
})
export class WorkflowPresetsComponent implements OnInit {
  private readonly svc = inject(WorkflowPresetService);
  private readonly userSvc = inject(UserService);
  private readonly confirm = inject(ConfirmService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly operators = OPERATORS;
  presets = signal<WorkflowPresetDto[]>([]);
  readonly search = signal('');
  /** Matches the circuit's name, description and step labels. */
  readonly filteredPresets = computed(() => {
    const q = this.search().trim().toLowerCase();
    if (!q) return this.presets();
    return this.presets().filter(p =>
      [p.name, p.description ?? '', ...p.steps.map(s => s.label ?? '')].some(t => t.toLowerCase().includes(q)));
  });
  users = signal<UserDto[]>([]);
  roles = signal<string[]>([]);
  readonly departments = computed(() =>
    [...new Set(this.users().map(u => u.department?.trim()).filter((d): d is string => !!d))].sort());
  loading = signal(true);
  saving = signal(false);
  error = signal('');

  editing = signal(false);
  editId = signal<string | null>(null);
  name = '';
  description = '';
  isActive = true;
  workflowType = signal<WorkflowType>('Sequential');
  steps = signal<StepDraft[]>([emptyStep()]);

  ngOnInit(): void {
    this.load();
    this.userSvc.getUsers({ pageSize: 200 }).subscribe(r => this.users.set(r.items.filter(u => u.isActive)));
    this.userSvc.getRoles().subscribe(r => this.roles.set(r.filter(x => x !== 'ExternalSigner')));
  }

  private load(): void {
    this.loading.set(true);
    this.svc.getAll().subscribe({
      next: p => { this.presets.set(p); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  who(s: { kind: PresetApproverKind; userName?: string; roleName?: string; department?: string }): string {
    switch (s.kind) {
      case 'User': return s.userName ?? '?';
      case 'Role': return `${this.translate.instant('admin.workflows.kind.Role')}: ${s.roleName}`;
      default: return `${this.translate.instant('admin.workflows.kind.Department')}: ${s.department}`;
    }
  }

  conditionText(s: { conditionFieldKey?: string; conditionOperator?: string; conditionValue?: string }): string {
    return s.conditionOperator ? `${s.conditionFieldKey} ${s.conditionOperator} ${s.conditionValue}` : `${s.conditionFieldKey} (${this.translate.instant('admin.workflows.isFilled')})`;
  }

  startCreate(): void {
    this.editId.set(null);
    this.name = ''; this.description = ''; this.isActive = true;
    this.workflowType.set('Sequential');
    this.steps.set([emptyStep()]);
    this.error.set('');
    this.editing.set(true);
    this.snapshot = this.serialize();
  }

  startEdit(p: WorkflowPresetDto): void {
    this.editId.set(p.id);
    this.name = p.name; this.description = p.description ?? ''; this.isActive = p.isActive;
    this.workflowType.set(p.workflowType);
    this.steps.set(p.steps.map(s => ({
      stage: s.stage, label: s.label ?? '', kind: s.kind,
      userId: s.userId ?? '', roleName: s.roleName ?? '', department: s.department ?? '',
      conditional: !!s.conditionFieldKey,
      conditionFieldKey: s.conditionFieldKey ?? '', conditionOperator: s.conditionOperator ?? '', conditionValue: s.conditionValue ?? ''
    })));
    this.error.set('');
    this.editing.set(true);
    this.snapshot = this.serialize();
  }

  cancel(): void { this.editing.set(false); }

  /** What the form held when it was opened: the "unsaved changes" test compares against it. */
  private snapshot = '';
  private serialize(): string {
    return JSON.stringify({
      name: this.name, description: this.description, isActive: this.isActive,
      type: this.workflowType(), steps: this.steps()
    });
  }
  isDirty(): boolean { return this.editing() && this.serialize() !== this.snapshot; }

  /** Leaves the editor. Unsaved changes are never thrown away silently. */
  requestClose(): void {
    if (this.saving()) return;
    if (!this.isDirty()) { this.cancel(); return; }
    this.confirm.confirm({
      title: this.translate.instant('admin.workflows.discardTitle'),
      message: this.translate.instant('admin.workflows.discardMessage'),
      confirmLabel: this.translate.instant('admin.workflows.discard'),
      cancelLabel: this.translate.instant('admin.workflows.keepEditing'),
      danger: true
    }).subscribe(ok => { if (ok) this.cancel(); });
  }

  /** Escape leaves the editor, unless a dropdown or dialog is open: that one takes the key. */
  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (!this.editing() || this.saving()) return;
    if (document.querySelector('.cdk-overlay-pane')) return;
    this.requestClose();
  }

  /** The path as the requester will meet it: one row per step, or one row for steps that act together. */
  previewGroups(): { n: number; s: StepDraft }[][] {
    const items = this.steps().map((s, i) => ({ n: i + 1, s }));
    switch (this.workflowType()) {
      case 'Sequential': return items.map(x => [x]);
      case 'Parallel': return items.length ? [items] : [];
      default: {
        const byStage = new Map<number, { n: number; s: StepDraft }[]>();
        for (const x of items) {
          const stage = Math.max(1, +x.s.stage || 1);
          byStage.set(stage, [...(byStage.get(stage) ?? []), x]);
        }
        return [...byStage.entries()].sort((a, b) => a[0] - b[0]).map(([, group]) => group);
      }
    }
  }

  whoDraft(s: StepDraft): string {
    switch (s.kind) {
      case 'User': return this.users().find(u => u.id === s.userId)?.fullName ?? '—';
      case 'Role': return s.roleName ? `${this.translate.instant('admin.workflows.kind.Role')}: ${s.roleName}` : '—';
      default: return s.department ? `${this.translate.instant('admin.workflows.kind.Department')}: ${s.department}` : '—';
    }
  }

  addStep(): void { this.steps.update(a => [...a, { ...emptyStep(), stage: a.length + 1 }]); }
  removeStep(i: number): void { this.steps.update(a => a.filter((_, idx) => idx !== i)); }
  move(i: number, by: number): void {
    this.steps.update(a => {
      const next = [...a]; const j = i + by;
      if (j < 0 || j >= next.length) return a;
      [next[i], next[j]] = [next[j], next[i]];
      return next;
    });
  }

  save(): void {
    if (!this.name.trim()) { this.error.set(this.translate.instant('admin.workflows.nameRequired')); return; }
    const payload = {
      name: this.name.trim(),
      description: this.description.trim() || undefined,
      workflowType: this.workflowType(),
      isActive: this.isActive,
      steps: this.steps().map((s): WorkflowPresetStepInput => ({
        stage: this.workflowType() === 'Mixed' ? Math.max(1, +s.stage || 1) : 1,
        label: s.label.trim() || undefined,
        kind: s.kind,
        userId: s.kind === 'User' ? s.userId || undefined : undefined,
        roleName: s.kind === 'Role' ? s.roleName || undefined : undefined,
        department: s.kind === 'Department' ? s.department || undefined : undefined,
        conditionFieldKey: s.conditional ? s.conditionFieldKey.trim() || undefined : undefined,
        conditionOperator: s.conditional && s.conditionFieldKey.trim() ? s.conditionOperator || undefined : undefined,
        conditionValue: s.conditional && s.conditionOperator ? s.conditionValue.trim() || undefined : undefined
      }))
    };

    this.saving.set(true);
    this.error.set('');
    const id = this.editId();
    const call = id ? this.svc.update(id, payload) : this.svc.create(payload);
    (call as Observable<unknown>).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.load();
        this.snack.open(this.translate.instant('admin.workflows.saved'), 'OK', { duration: 3000 });
      },
      error: (e: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.saving.set(false);
        // A validation failure keeps its message under `errors`; `detail` is only a generic headline.
        const messages = e?.error?.errors ? Object.values(e.error.errors).flat().join(' ') : '';
        this.error.set(messages || e?.error?.detail || this.translate.instant('admin.workflows.saveFailed'));
      }
    });
  }

  remove(p: WorkflowPresetDto): void {
    this.confirm.confirm({
      title: this.translate.instant('admin.workflows.deleteTitle'),
      message: this.translate.instant('admin.workflows.deleteMessage', { name: p.name }),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true
    }).subscribe(ok => {
      if (!ok) return;
      this.svc.delete(p.id).subscribe({
        next: () => { this.load(); this.snack.open(this.translate.instant('admin.workflows.deleted'), 'OK', { duration: 3000 }); },
        error: (e: { error?: { detail?: string } }) =>
          this.snack.open(e?.error?.detail || this.translate.instant('admin.workflows.deleteFailed'), 'OK', { duration: 5000 })
      });
    });
  }
}
