import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { RegulatoryWatchService } from '../../core/services/regulatory-watch.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import {
  RegulatoryText,
  RegulatoryTextInput,
  RegulatoryLanguage,
  RegulatoryCategory,
  RegulatoryJurisdiction,
  REGULATORY_CATEGORIES,
  REGULATORY_JURISDICTIONS,
  RequestType
} from '../../core/models';

export interface RegulatoryTextDialogData {
  text: RegulatoryText | null; // null = create
}

@Component({
  selector: 'sf-regulatory-text-dialog',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatDatepickerModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>{{ (isEdit ? 'regulatoryWatch.dialog.editTitle' : 'regulatoryWatch.dialog.createTitle') | translate }}</h2>
    <mat-dialog-content>
      @if (error()) {
        <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
      }
      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'regulatoryWatch.dialog.textTitle' | translate }}</mat-label>
          <input matInput formControlName="title" [placeholder]="'regulatoryWatch.dialog.textTitlePlaceholder' | translate" />
        </mat-form-field>

        <div class="row-3">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'regulatoryWatch.dialog.category' | translate }}</mat-label>
            <mat-select formControlName="category">
              @for (c of categories; track c) {
                <mat-option [value]="c">{{ ('regulatoryWatch.category.' + c) | translate }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'regulatoryWatch.dialog.jurisdiction' | translate }}</mat-label>
            <mat-select formControlName="jurisdiction">
              @for (j of jurisdictions; track j) {
                <mat-option [value]="j">{{ ('regulatoryWatch.jurisdiction.' + j) | translate }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'regulatoryWatch.dialog.language' | translate }}</mat-label>
            <mat-select formControlName="language">
              <mat-option value="en">{{ 'regulatoryWatch.language.en' | translate }}</mat-option>
              <mat-option value="fr">{{ 'regulatoryWatch.language.fr' | translate }}</mat-option>
            </mat-select>
          </mat-form-field>
        </div>

        <div class="row-2">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'regulatoryWatch.dialog.reference' | translate }}</mat-label>
            <input matInput formControlName="reference" [placeholder]="'regulatoryWatch.dialog.referencePlaceholder' | translate" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'regulatoryWatch.dialog.effectiveDate' | translate }}</mat-label>
            <input matInput [matDatepicker]="picker" formControlName="effectiveDate" />
            <mat-datepicker-toggle matIconSuffix [for]="picker"></mat-datepicker-toggle>
            <mat-datepicker #picker></mat-datepicker>
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'regulatoryWatch.dialog.scope' | translate }}</mat-label>
          <mat-select formControlName="requestTypeId">
            <mat-option [value]="null">{{ 'regulatoryWatch.dialog.scopeAll' | translate }}</mat-option>
            @for (t of requestTypes(); track t.id) {
              <mat-option [value]="t.id">{{ t.name }}</mat-option>
            }
          </mat-select>
          <mat-hint>{{ 'regulatoryWatch.dialog.scopeHint' | translate }}</mat-hint>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'regulatoryWatch.dialog.summary' | translate }}</mat-label>
          <textarea matInput formControlName="summary" rows="6"
                    [placeholder]="'regulatoryWatch.dialog.summaryPlaceholder' | translate"></textarea>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'regulatoryWatch.dialog.keywords' | translate }}</mat-label>
          <input matInput formControlName="keywords" [placeholder]="'regulatoryWatch.dialog.keywordsPlaceholder' | translate" />
          <mat-hint>{{ 'regulatoryWatch.dialog.keywordsHint' | translate }}</mat-hint>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="saving() || form.invalid">
        @if (saving()) { <mat-spinner diameter="18"></mat-spinner> } @else { {{ (isEdit ? 'common.save' : 'regulatoryWatch.dialog.create') | translate }} }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .form { display: flex; flex-direction: column; gap: 4px; min-width: 560px; padding-top: 8px; }
    .full { width: 100%; }
    .row-3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 12px; }
    .row-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .error-banner { display: flex; align-items: center; gap: 8px; background: #fee2e2; color: #991b1b; border-radius: 8px; padding: 10px 12px; margin-bottom: 12px; font-size: 13px; }
    .error-banner mat-icon { font-size: 18px; width: 18px; height: 18px; }
    @media (max-width: 640px) {
      .form { min-width: 0; }
      .row-3, .row-2 { grid-template-columns: 1fr; }
    }
  `]
})
export class RegulatoryTextDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(RegulatoryWatchService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly translate = inject(TranslateService);
  readonly dialogRef = inject(MatDialogRef<RegulatoryTextDialogComponent>);
  readonly data: RegulatoryTextDialogData = inject(MAT_DIALOG_DATA);

  readonly categories = REGULATORY_CATEGORIES;
  readonly jurisdictions = REGULATORY_JURISDICTIONS;
  readonly isEdit = !!this.data.text;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly requestTypes = signal<RequestType[]>([]);

  readonly form = this.fb.group({
    title: [this.data.text?.title ?? '', [Validators.required, Validators.maxLength(300)]],
    category: [this.data.text?.category ?? 'Other', Validators.required],
    jurisdiction: [this.data.text?.jurisdiction ?? 'Tunisia', Validators.required],
    language: [this.data.text?.language ?? 'en', Validators.required],
    reference: [this.data.text?.reference ?? ''],
    effectiveDate: [this.data.text?.effectiveDate ? new Date(this.data.text.effectiveDate) : null],
    requestTypeId: [this.data.text?.requestTypeId ?? null],
    summary: [this.data.text?.summary ?? '', Validators.required],
    keywords: [this.data.text?.keywords ?? '', Validators.required]
  });

  ngOnInit(): void {
    this.requestTypeService.getActive().subscribe({
      next: types => this.requestTypes.set(types),
      error: () => this.requestTypes.set([])
    });
  }

  submit(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    this.error.set(null);
    const v = this.form.value;
    const payload: RegulatoryTextInput = {
      title: v.title!,
      category: v.category! as RegulatoryCategory,
      jurisdiction: v.jurisdiction! as RegulatoryJurisdiction,
      summary: v.summary!,
      keywords: v.keywords!,
      language: v.language! as RegulatoryLanguage,
      reference: v.reference || null,
      effectiveDate: v.effectiveDate ? (v.effectiveDate as Date).toISOString() : null,
      requestTypeId: v.requestTypeId || null
    };

    const req$: Observable<unknown> = this.isEdit
      ? this.service.update(this.data.text!.id, payload)
      : this.service.create(payload);

    req$.subscribe({
      next: () => { this.saving.set(false); this.dialogRef.close(true); },
      error: (err: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.saving.set(false);
        this.error.set(err.error?.errors
          ? Object.values(err.error.errors).flat().join(' ')
          : err.error?.detail ?? this.translate.instant('regulatoryWatch.dialog.operationFailed'));
      }
    });
  }
}
