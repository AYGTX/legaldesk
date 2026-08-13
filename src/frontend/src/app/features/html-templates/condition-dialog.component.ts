import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** The subset of a template field this dialog needs — avoids importing the editor's input type. */
export interface ConditionField {
  fieldKey: string;
  label: string;
  type: string;
}

export interface ConditionDialogData {
  fields: ConditionField[];
}

/**
 * Builds one condition for a conditional clause. Deliberately a picker rather than a free-text
 * expression box: the author chooses a field that already exists on the template, so a condition
 * can never reference a value the contract will not collect.
 *
 * Mirrors the backend evaluator exactly — one comparison, no AND/OR, no nesting. If the two ever
 * drift, the backend is the authority.
 */
@Component({
  selector: 'sf-condition-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatButtonModule, MatIconModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>
      <mat-icon class="title-icon">rule</mat-icon>
      {{ 'htmlTemplateEditor.conditions.title' | translate }}
    </h2>
    <mat-dialog-content>
      <p class="cond-hint">{{ 'htmlTemplateEditor.conditions.hint' | translate }}</p>

      @if (data.fields.length === 0) {
        <p class="cond-empty">{{ 'htmlTemplateEditor.conditions.noFields' | translate }}</p>
      } @else {
        <div class="cond-row">
          <mat-form-field appearance="outline" class="cond-field">
            <mat-label>{{ 'htmlTemplateEditor.conditions.field' | translate }}</mat-label>
            <mat-select [(ngModel)]="fieldKey" (selectionChange)="onFieldChange()">
              @for (f of data.fields; track f.fieldKey) {
                <mat-option [value]="f.fieldKey">{{ f.label || f.fieldKey }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline" class="cond-op">
            <mat-label>{{ 'htmlTemplateEditor.conditions.operator' | translate }}</mat-label>
            <mat-select [(ngModel)]="operator">
              @for (o of operators(); track o.value) {
                <mat-option [value]="o.value">{{ o.labelKey | translate }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          @if (operator !== 'truthy') {
            <mat-form-field appearance="outline" class="cond-value">
              <mat-label>{{ 'htmlTemplateEditor.conditions.value' | translate }}</mat-label>
              <input matInput [(ngModel)]="value" [type]="valueInputType()" />
            </mat-form-field>
          }
        </div>

        <div class="cond-preview">
          <span class="cond-preview-label">{{ 'htmlTemplateEditor.conditions.preview' | translate }}</span>
          <code>{{ expression() }}</code>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!isValid()" (click)="apply()">
        {{ 'htmlTemplateEditor.conditions.apply' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .title-icon { vertical-align: middle; margin-right: 6px; }
    .cond-hint { font-size: 13px; color: var(--g-text-3); margin: 0 0 16px; line-height: 1.55; }
    .cond-empty { font-size: 13px; color: var(--g-text-muted); font-style: italic; }
    .cond-row { display: flex; gap: 12px; flex-wrap: wrap; }
    .cond-field { flex: 2 1 200px; }
    .cond-op { flex: 1 1 150px; }
    .cond-value { flex: 1 1 150px; }
    .cond-preview { margin-top: 8px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .cond-preview-label { font-size: 12px; color: var(--g-text-muted); }
    .cond-preview code { font-size: 12px; background: var(--g-surface-2, #f1f3f4);
                         padding: 4px 8px; border-radius: 4px; color: var(--g-text); }
  `]
})
export class ConditionDialogComponent {
  private readonly ref = inject(MatDialogRef<ConditionDialogComponent, string>);
  readonly data = inject<ConditionDialogData>(MAT_DIALOG_DATA);

  fieldKey = this.data.fields[0]?.fieldKey ?? '';
  operator = 'truthy';
  value = '';

  /**
   * Plain getters rather than computed signals: the inputs above are bound with ngModel and are
   * not signals, so a computed would cache its first value and never track an edit.
   */
  private get selectedType(): string {
    return this.data.fields.find(f => f.fieldKey === this.fieldKey)?.type ?? 'Text';
  }

  /**
   * Only offers comparisons the backend can actually satisfy for the chosen field type — text
   * supports equality only, and a signature is never a condition input.
   */
  operators(): { value: string; labelKey: string }[] {
    const truthy = { value: 'truthy', labelKey: 'htmlTemplateEditor.conditions.ops.truthy' };
    const equality = [
      { value: '==', labelKey: 'htmlTemplateEditor.conditions.ops.eq' },
      { value: '!=', labelKey: 'htmlTemplateEditor.conditions.ops.neq' }
    ];
    const relational = [
      { value: '>', labelKey: 'htmlTemplateEditor.conditions.ops.gt' },
      { value: '>=', labelKey: 'htmlTemplateEditor.conditions.ops.gte' },
      { value: '<', labelKey: 'htmlTemplateEditor.conditions.ops.lt' },
      { value: '<=', labelKey: 'htmlTemplateEditor.conditions.ops.lte' }
    ];

    return this.selectedType === 'Number' || this.selectedType === 'Date'
      ? [truthy, ...equality, ...relational]
      : [truthy, ...equality];
  }

  valueInputType(): string {
    return this.selectedType === 'Number' ? 'number'
      : this.selectedType === 'Date' ? 'date' : 'text';
  }

  /** The exact marker the backend will parse, shown so the author can see what they are creating. */
  expression(): string {
    if (!this.fieldKey) return '';
    if (this.operator === 'truthy') return `{{#if ${this.fieldKey}}}`;
    const needsQuotes = this.selectedType !== 'Number' && this.selectedType !== 'Date';
    const rhs = needsQuotes ? `"${this.value}"` : this.value;
    return `{{#if ${this.fieldKey} ${this.operator} ${rhs}}}`;
  }

  /** A relational operator left over from a Number field would be unsatisfiable on a Text one. */
  onFieldChange(): void {
    if (!this.operators().some(o => o.value === this.operator)) {
      this.operator = 'truthy';
      this.value = '';
    }
  }

  isValid(): boolean {
    if (!this.fieldKey) return false;
    return this.operator === 'truthy' || this.value.trim().length > 0;
  }

  apply(): void {
    this.ref.close(this.expression());
  }
}
