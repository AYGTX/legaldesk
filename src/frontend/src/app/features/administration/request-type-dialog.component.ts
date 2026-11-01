import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { RequestTypeService } from '../../core/services/request-type.service';
import { RequestType, RULE_SETS } from '../../core/models';
import { WorkflowPresetService } from '../../core/services/workflow-preset.service';
import { WorkflowPresetDto } from '../../core/models/workflow-preset.models';
import { MatCheckboxModule } from '@angular/material/checkbox';

export interface RequestTypeDialogData {
  requestType: RequestType | null; // null = create
}

const ICONS = ['description', 'beach_access', 'sick', 'flight', 'shopping_cart', 'receipt_long', 'paid', 'badge', 'gavel', 'handshake', 'event', 'work'];
const COLORS = ['#6366f1', '#ec4899', '#f59e0b', '#22c55e', '#3b82f6', '#8b5cf6', '#ef4444', '#14b8a6'];

@Component({
  selector: 'sf-request-type-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatCheckboxModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>{{ (isEdit ? 'admin.requestTypeDialog.editTitle' : 'admin.requestTypeDialog.createTitle') | translate }}</h2>
    <mat-dialog-content>
      @if (error()) {
        <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
      }
      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'admin.requestTypeDialog.requestName' | translate }}</mat-label>
          <input matInput formControlName="name" [placeholder]="'admin.requestTypeDialog.requestNamePlaceholder' | translate" />
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'admin.requestTypeDialog.description' | translate }}</mat-label>
          <textarea matInput formControlName="description" rows="2"
                    [placeholder]="'admin.requestTypeDialog.descriptionPlaceholder' | translate"></textarea>
        </mat-form-field>

        <div class="sla-row">
          <mat-form-field appearance="outline" class="sla-value">
            <mat-label>{{ 'admin.requestTypeDialog.slaDeadline' | translate }}</mat-label>
            <input matInput type="number" formControlName="slaValue" min="1" />
          </mat-form-field>
          <mat-form-field appearance="outline" class="sla-unit">
            <mat-label>{{ 'admin.requestTypeDialog.unit' | translate }}</mat-label>
            <mat-select formControlName="slaUnit">
              <mat-option value="hours">{{ 'admin.requestTypeDialog.hours' | translate }}</mat-option>
              <mat-option value="days">{{ 'admin.requestTypeDialog.days' | translate }}</mat-option>
            </mat-select>
          </mat-form-field>
        </div>
        <p class="sla-hint">{{ 'admin.requestTypeDialog.slaHint' | translate }}</p>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'admin.requestTypeDialog.defaultPreset' | translate }}</mat-label>
          <mat-select formControlName="defaultPresetId">
            <mat-option [value]="null">{{ 'admin.requestTypeDialog.noDefaultPreset' | translate }}</mat-option>
            @for (p of presets(); track p.id) { <mat-option [value]="p.id">{{ p.name }}</mat-option> }
          </mat-select>
          <mat-hint>{{ 'admin.requestTypeDialog.defaultPresetHint' | translate }}</mat-hint>
        </mat-form-field>

        <label class="picker-label">{{ 'admin.requestTypeDialog.ruleSets' | translate }}</label>
        <p class="sla-hint">{{ 'admin.requestTypeDialog.ruleSetsHint' | translate }}</p>
        <div class="rule-sets">
          @for (k of ruleSetKeys; track k) {
            <mat-checkbox [checked]="ruleSets().includes(k)" (change)="toggleRuleSet(k, $event.checked)">
              {{ ('admin.requestTypeDialog.ruleSet.' + k) | translate }}
            </mat-checkbox>
          }
        </div>

        <label class="picker-label">{{ 'admin.requestTypeDialog.icon' | translate }}</label>
        <div class="icon-grid">
          @for (ic of icons; track ic) {
            <button type="button" class="icon-btn" [class.selected]="form.value.icon === ic"
                    (click)="form.patchValue({ icon: ic })">
              <mat-icon>{{ ic }}</mat-icon>
            </button>
          }
        </div>

        <label class="picker-label">{{ 'admin.requestTypeDialog.color' | translate }}</label>
        <div class="color-grid">
          @for (c of colors; track c) {
            <button type="button" class="color-btn" [class.selected]="form.value.color === c"
                    [style.background]="c" (click)="form.patchValue({ color: c })"></button>
          }
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="saving() || form.invalid">
        @if (saving()) { <mat-spinner diameter="18"></mat-spinner> } @else { {{ (isEdit ? 'admin.requestTypeDialog.save' : 'admin.requestTypeDialog.create') | translate }} }
      </button>
    </mat-dialog-actions>
  `,
  styleUrl: './request-type-dialog.component.scss'
})
export class RequestTypeDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(RequestTypeService);
  private readonly translate = inject(TranslateService);
  readonly dialogRef = inject(MatDialogRef<RequestTypeDialogComponent>);
  readonly data: RequestTypeDialogData = inject(MAT_DIALOG_DATA);

  readonly icons = ICONS;
  readonly colors = COLORS;
  readonly isEdit = !!this.data.requestType;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.group({
    name: [this.data.requestType?.name ?? '', [Validators.required, Validators.maxLength(150)]],
    description: [this.data.requestType?.description ?? ''],
    slaValue: [this.initialSlaValue(), [Validators.required, Validators.min(1)]],
    slaUnit: [this.initialSlaUnit()],
    icon: [this.data.requestType?.icon ?? 'description'],
    color: [this.data.requestType?.color ?? COLORS[0]],
    defaultPresetId: [this.data.requestType?.defaultPresetId ?? null as string | null]
  });

  readonly ruleSetKeys = RULE_SETS;
  readonly ruleSets = signal<string[]>([...(this.data.requestType?.ruleSets ?? [])]);
  readonly presets = signal<WorkflowPresetDto[]>([]);
  private readonly presetService = inject(WorkflowPresetService);

  constructor() {
    this.presetService.getAll().subscribe({ next: p => this.presets.set(p.filter(x => x.isActive || x.id === this.form.value.defaultPresetId)), error: () => {} });
  }

  toggleRuleSet(key: string, on: boolean): void {
    this.ruleSets.update(list => on ? [...new Set([...list, key])] : list.filter(k => k !== key));
  }

  private initialSlaUnit(): 'hours' | 'days' {
    const h = this.data.requestType?.slaHours ?? 72;
    return h % 24 === 0 ? 'days' : 'hours';
  }
  private initialSlaValue(): number {
    const h = this.data.requestType?.slaHours ?? 72;
    return h % 24 === 0 ? h / 24 : h;
  }

  submit(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    this.error.set(null);
    const v = this.form.value;
    const slaHours = v.slaUnit === 'days' ? Number(v.slaValue) * 24 : Number(v.slaValue);
    const payload = {
      name: v.name!,
      description: v.description || undefined,
      slaHours,
      icon: v.icon || undefined,
      color: v.color || undefined,
      defaultPresetId: v.defaultPresetId || null,
      ruleSets: this.ruleSets()
    };

    const req$: Observable<unknown> = this.isEdit
      ? this.service.update(this.data.requestType!.id, payload)
      : this.service.create(payload);

    req$.subscribe({
      next: () => { this.saving.set(false); this.dialogRef.close(true); },
      error: (err: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.saving.set(false);
        this.error.set(err.error?.errors
          ? Object.values(err.error.errors).flat().join(' ')
          : err.error?.detail ?? this.translate.instant('admin.requestTypeDialog.operationFailed'));
      }
    });
  }
}
