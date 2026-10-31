import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, concat, of, toArray } from 'rxjs';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ClauseService } from '../../core/services/clause.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { ClauseCategory, ClauseInput, ClauseLanguage, ClauseRiskLevel, CLAUSE_CATEGORIES, LibraryClause, RequestType } from '../../core/models';
import { problemText } from '../../core/utils/problem-text';

export interface ClauseDialogData {
  clause: LibraryClause | null; // null = create
  /** Seeds the create form — e.g. from a contract-analysis finding's proposed language. Ignored when editing. */
  prefill?: Partial<ClauseInput>;
}

@Component({
  selector: 'sf-clause-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>{{ (isEdit ? 'clauses.dialog.editTitle' : 'clauses.dialog.createTitle') | translate }}</h2>
    <mat-dialog-content>
      @if (error()) {
        <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
      }
      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'clauses.dialog.clauseTitle' | translate }}</mat-label>
          <input matInput formControlName="title" [placeholder]="'clauses.dialog.clauseTitlePlaceholder' | translate" />
        </mat-form-field>

        <div class="row-3">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'clauses.dialog.category' | translate }}</mat-label>
            <mat-select formControlName="category">
              @for (c of categories; track c) {
                <mat-option [value]="c">{{ ('clauses.category.' + c) | translate }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'clauses.dialog.language' | translate }}</mat-label>
            <mat-select formControlName="language">
              <mat-option value="en">{{ 'clauses.language.en' | translate }}</mat-option>
              <mat-option value="fr">{{ 'clauses.language.fr' | translate }}</mat-option>
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'clauses.dialog.riskLevel' | translate }}</mat-label>
            <mat-select formControlName="riskLevel">
              <mat-option [value]="null">{{ 'clauses.dialog.riskNone' | translate }}</mat-option>
              <mat-option value="Low">{{ 'clauses.risk.Low' | translate }}</mat-option>
              <mat-option value="Medium">{{ 'clauses.risk.Medium' | translate }}</mat-option>
              <mat-option value="High">{{ 'clauses.risk.High' | translate }}</mat-option>
            </mat-select>
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'clauses.dialog.scope' | translate }}</mat-label>
          <mat-select formControlName="requestTypeId">
            <mat-option [value]="null">{{ 'clauses.dialog.scopeAll' | translate }}</mat-option>
            @for (t of requestTypes(); track t.id) {
              <mat-option [value]="t.id">{{ t.name }}</mat-option>
            }
          </mat-select>
          <mat-hint>{{ 'clauses.dialog.scopeHint' | translate }}</mat-hint>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'clauses.dialog.body' | translate }}</mat-label>
          <textarea matInput formControlName="body" rows="10"
                    [placeholder]="'clauses.dialog.bodyPlaceholder' | translate"></textarea>
          <mat-hint>{{ 'clauses.dialog.bodyHint' | translate }}</mat-hint>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'clauses.dialog.guidance' | translate }}</mat-label>
          <textarea matInput formControlName="guidance" rows="3"
                    [placeholder]="'clauses.dialog.guidancePlaceholder' | translate"></textarea>
          <mat-hint>{{ 'clauses.dialog.guidanceHint' | translate }}</mat-hint>
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'clauses.dialog.ruleKeys' | translate }}</mat-label>
          <input matInput formControlName="ruleKeys" [placeholder]="'clauses.dialog.ruleKeysPlaceholder' | translate" />
          <mat-hint>{{ 'clauses.dialog.ruleKeysHint' | translate }}</mat-hint>
        </mat-form-field>

        @if (isEdit && latest?.status !== 'Draft') {
          <p class="version-note">
            <mat-icon inline>info</mat-icon>
            {{ 'clauses.dialog.redraftNote' | translate: { approved: latest?.number, next: (latest?.number ?? 0) + 1 } }}
          </p>
        } @else if (isEdit) {
          <p class="version-note">{{ 'clauses.dialog.draftNote' | translate: { version: latest?.number } }}</p>
        }
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="saving() || form.invalid">
        @if (saving()) { <mat-spinner diameter="18"></mat-spinner> } @else { {{ (isEdit ? 'common.save' : 'clauses.dialog.create') | translate }} }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .form { display: flex; flex-direction: column; gap: 4px; min-width: 560px; padding-top: 8px; }
    .full { width: 100%; }
    .row-3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 12px; }
    .error-banner { display: flex; align-items: center; gap: 8px; background: #fee2e2; color: #991b1b; border-radius: 8px; padding: 10px 12px; margin-bottom: 12px; font-size: 13px; }
    .error-banner mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .version-note { font-size: 12px; color: #6b7280; margin: 4px 0 0; }
    textarea { font-family: 'Consolas', 'Courier New', monospace; font-size: 13px; line-height: 1.6; }
    @media (max-width: 640px) {
      .form { min-width: 0; }
      .row-3 { grid-template-columns: 1fr; }
    }
  `]
})
export class ClauseDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(ClauseService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly translate = inject(TranslateService);
  readonly dialogRef = inject(MatDialogRef<ClauseDialogComponent>);
  readonly data: ClauseDialogData = inject(MAT_DIALOG_DATA);

  readonly categories = CLAUSE_CATEGORIES;
  readonly isEdit = !!this.data.clause;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly requestTypes = signal<RequestType[]>([]);

  private readonly prefill = this.data.clause ? undefined : this.data.prefill;
  /** The wording an edit starts from: the newest one. */
  readonly latest = this.data.clause?.latest ?? this.data.clause?.versions?.[0] ?? null;

  readonly form = this.fb.group({
    title: [this.data.clause?.title ?? this.prefill?.title ?? '', [Validators.required, Validators.maxLength(300)]],
    category: [this.data.clause?.category ?? this.prefill?.category ?? 'Other', Validators.required],
    language: [this.latest?.language ?? this.prefill?.language ?? 'en', Validators.required],
    riskLevel: [(this.data.clause?.riskLevel ?? this.prefill?.riskLevel ?? null) as ClauseRiskLevel | null],
    requestTypeId: [this.data.clause?.requestTypeId ?? this.prefill?.requestTypeId ?? null],
    body: [this.latest?.body ?? this.prefill?.body ?? '', Validators.required],
    guidance: [this.data.clause?.guidance ?? this.prefill?.guidance ?? ''],
    ruleKeys: [(this.data.clause?.ruleKeys ?? (this.prefill?.ruleKey ? [this.prefill.ruleKey] : [])).join(', ')]
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
    const ruleKeys = (v.ruleKeys ?? '').split(',').map(k => k.trim()).filter(k => !!k);
    const description = {
      title: v.title!,
      category: v.category! as ClauseCategory,
      guidance: v.guidance || null,
      riskLevel: (v.riskLevel || null) as ClauseRiskLevel | null,
      requestTypeId: v.requestTypeId || null
    };

    let req$: Observable<unknown>;
    if (!this.isEdit) {
      const input: ClauseInput = { ...description, body: v.body!, language: v.language! as ClauseLanguage, ruleKey: ruleKeys[0] ?? null };
      // A new clause carries one rule from a finding; any further ones are linked right after.
      req$ = ruleKeys.length > 1
        ? new Observable(sub => this.service.create(input).subscribe({
            next: r => this.service.linkRules(r.id, ruleKeys).subscribe(sub),
            error: e => sub.error(e)
          }))
        : this.service.create(input);
    } else {
      const id = this.data.clause!.id;
      const wordingChanged = v.body !== this.latest?.body || v.language !== this.latest?.language;
      const rulesChanged = ruleKeys.join('|') !== [...(this.data.clause!.ruleKeys ?? [])].join('|');
      // An approved wording never changes: new wording drafts the next version, the approved one stays in use (R-M9-1).
      req$ = concat(
        this.service.describe(id, description),
        wordingChanged ? this.service.redraft(id, v.body!, v.language! as ClauseLanguage) : of(null),
        rulesChanged ? this.service.linkRules(id, ruleKeys) : of(null)
      ).pipe(toArray());
    }

    req$.subscribe({
      next: () => { this.saving.set(false); this.dialogRef.close(true); },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(problemText(this.translate, err, 'clauses.dialog.operationFailed'));
      }
    });
  }
}
