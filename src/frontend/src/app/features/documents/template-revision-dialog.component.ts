import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { TemplateRevisionForm, TemplateRevisionFormField } from '../../core/models';

export interface TemplateRevisionDialogData {
  documentId: string;
  documentTitle: string;
}

/**
 * The form an owner fills again when a reviewer asked for changes on a contract built from a template.
 * Only the template's answers are offered — the PDF, the layout and the approval circuit are the template's.
 * Saving keeps the version the reviewer sent back and creates the next one, then the circuit starts again.
 */
@Component({
  selector: 'sf-template-revision-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title class="tr-title">
      <mat-icon>published_with_changes</mat-icon>
      {{ 'documents.detail.reviseAnswers.title' | translate }}
      <span class="tr-doc">{{ data.documentTitle }}</span>
    </h2>

    <mat-dialog-content class="tr-content">
      @if (loading()) {
        <div class="tr-state"><mat-spinner diameter="28"></mat-spinner> {{ 'documents.detail.reviseAnswers.loading' | translate }}</div>
      } @else if (form(); as f) {
        @for (request of f.changeRequests; track $index) {
          <div class="tr-request">
            <mat-icon>rate_review</mat-icon>
            <div>
              <strong>{{ request.requestedBy || ('documents.detail.reviseAnswers.aReviewer' | translate) }}</strong>
              <span>{{ request.reason || ('documents.detail.reviseAnswers.noReason' | translate) }}</span>
            </div>
          </div>
        }

        <p class="tr-hint">{{ 'documents.detail.reviseAnswers.hint' | translate: { version: f.currentVersion, next: f.currentVersion + 1 } }}</p>

        @for (field of f.fields; track field.fieldKey) {
          <div class="tr-field">
            <label class="tr-label" [for]="'tr-' + field.fieldKey">
              {{ field.label }}@if (field.isRequired) { <span class="tr-req">*</span> }
              @if (isChanged(field)) { <span class="tr-changed">{{ 'documents.detail.reviseAnswers.changed' | translate }}</span> }
            </label>

            @switch (field.type) {
              @case ('Textarea') {
                <textarea class="tr-input" rows="3" [id]="'tr-' + field.fieldKey"
                          [ngModel]="valueOf(field)" (ngModelChange)="setValue(field, $event)"></textarea>
              }
              @case ('Date') {
                <input class="tr-input" type="date" [id]="'tr-' + field.fieldKey"
                       [ngModel]="valueOf(field)" (ngModelChange)="setValue(field, $event)" />
              }
              @case ('Number') {
                <input class="tr-input" type="number" [id]="'tr-' + field.fieldKey"
                       [ngModel]="valueOf(field)" (ngModelChange)="setValue(field, $event)" />
              }
              @case ('Checkbox') {
                <label class="tr-check">
                  <input type="checkbox" [id]="'tr-' + field.fieldKey"
                         [checked]="valueOf(field) === 'true'"
                         (change)="setValue(field, $any($event.target).checked ? 'true' : 'false')" />
                  {{ field.label }}
                </label>
              }
              @case ('Dropdown') {
                <select class="tr-input" [id]="'tr-' + field.fieldKey"
                        [ngModel]="valueOf(field)" (ngModelChange)="setValue(field, $event)">
                  <option value=""></option>
                  @for (option of optionsOf(field); track option) { <option [value]="option">{{ option }}</option> }
                </select>
              }
              @default {
                <input class="tr-input" type="text" [id]="'tr-' + field.fieldKey"
                       [ngModel]="valueOf(field)" (ngModelChange)="setValue(field, $event)" />
              }
            }
          </div>
        }

        <div class="tr-field">
          <label class="tr-label" for="tr-note">{{ 'documents.detail.reviseAnswers.noteLabel' | translate }}</label>
          <input class="tr-input" id="tr-note" type="text" [(ngModel)]="note"
                 [placeholder]="'documents.detail.changeNotePlaceholder' | translate" />
        </div>

        @if (error()) { <div class="tr-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }
      }
    </mat-dialog-content>

    <mat-dialog-actions class="tr-actions">
      <span class="tr-count">
        @if (changedCount() > 0) { {{ 'documents.detail.reviseAnswers.changedCount' | translate: { count: changedCount() } }} }
        @else { {{ 'documents.detail.reviseAnswers.nothingChanged' | translate }} }
      </span>
      <span class="tr-spacer"></span>
      <button mat-button (click)="ref.close()" [disabled]="saving()">{{ 'documents.detail.editText.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="saving() || loading() || changedCount() === 0">
        <mat-icon>send</mat-icon>
        {{ (saving() ? 'documents.detail.reviseAnswers.sending' : 'documents.detail.reviseAnswers.send') | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .tr-title { display: flex; align-items: center; gap: 8px; }
    .tr-doc { font-size: 13px; font-weight: 400; color: #5f6368; margin-left: 8px; }
    .tr-content { min-width: min(560px, 85vw); max-height: 70vh; }
    .tr-state { display: flex; align-items: center; gap: 10px; padding: 24px; color: #5f6368; justify-content: center; }
    .tr-request { display: flex; gap: 10px; padding: 10px 12px; margin-bottom: 8px; border-radius: 10px; background: #fef7e0; color: #b06000; font-size: 13px; }
    .tr-request > div { display: flex; flex-direction: column; gap: 2px; }
    .tr-hint { font-size: 13px; color: #5f6368; margin: 8px 0 14px; }
    .tr-field { margin-bottom: 14px; }
    .tr-label { display: flex; align-items: center; gap: 6px; font-size: 12px; font-weight: 500; color: #5f6368; margin-bottom: 4px; }
    .tr-req { color: #d93025; }
    .tr-changed { font-size: 11px; font-weight: 500; color: #b06000; background: #fef7e0; border-radius: 8px; padding: 1px 8px; }
    .tr-input { width: 100%; box-sizing: border-box; padding: 8px 10px; border: 1px solid #c7d0dc; border-radius: 6px; font-size: 14px; font-family: inherit; outline: none; background: #fff; }
    .tr-input:focus { border-color: #0b57d0; }
    .tr-check { display: flex; align-items: center; gap: 8px; font-size: 14px; color: #202124; }
    .tr-error { display: flex; align-items: center; gap: 6px; font-size: 13px; color: #d93025; background: #fce8e6; border-radius: 8px; padding: 8px 10px; }
    .tr-actions { padding: 12px 24px; }
    .tr-count { font-size: 12px; color: #5f6368; }
    .tr-spacer { flex: 1; }
  `]
})
export class TemplateRevisionDialogComponent implements OnInit {
  readonly data = inject<TemplateRevisionDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<TemplateRevisionDialogComponent, { version: number } | undefined>>(MatDialogRef);
  private readonly documentService = inject(DocumentService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly form = signal<TemplateRevisionForm | null>(null);
  /** The answers as the owner is editing them; starts as the current ones. */
  private readonly values = signal<Record<string, string>>({});
  note = '';

  readonly changedCount = computed(() => {
    const f = this.form();
    return f ? f.fields.filter(field => this.isChanged(field)).length : 0;
  });

  ngOnInit(): void {
    this.documentService.getRevisionForm(this.data.documentId).subscribe({
      next: form => {
        this.form.set(form);
        this.values.set({ ...form.values });
        this.loading.set(false);
      },
      error: (err: { error?: { detail?: string } }) => {
        this.snack.open(err.error?.detail ?? this.translate.instant('documents.detail.reviseAnswers.loadFailed'), 'OK', { duration: 6000 });
        this.ref.close();
      }
    });
  }

  /** Always text: a number input hands back a JS number, and the API's answers are strings. */
  valueOf(field: TemplateRevisionFormField): string {
    const v: unknown = this.values()[field.fieldKey];
    return v === null || v === undefined ? '' : String(v);
  }

  setValue(field: TemplateRevisionFormField, value: unknown): void {
    this.error.set('');
    this.values.update(v => ({ ...v, [field.fieldKey]: value === null || value === undefined ? '' : String(value) }));
  }

  isChanged(field: TemplateRevisionFormField): boolean {
    const original = this.form()?.values[field.fieldKey] ?? '';
    return this.valueOf(field) !== String(original);
  }

  optionsOf(field: TemplateRevisionFormField): string[] {
    if (!field.optionsJson) return [];
    try { return JSON.parse(field.optionsJson) as string[]; } catch { return []; }
  }

  submit(): void {
    const f = this.form();
    if (!f) return;

    const missing = f.fields.filter(field => field.isRequired && field.type !== 'Checkbox' && this.valueOf(field).trim() === '');
    if (missing.length > 0) {
      this.error.set(this.translate.instant('documents.detail.reviseAnswers.missing', { fields: missing.map(m => m.label).join(', ') }));
      return;
    }

    // Only what changed is sent; the server keeps every other answer exactly as it was.
    const changed: Record<string, string> = {};
    for (const field of f.fields) {
      if (this.isChanged(field)) changed[field.fieldKey] = this.valueOf(field);
    }

    this.saving.set(true);
    this.documentService.reviseAnswers(this.data.documentId, changed, this.note).subscribe({
      next: res => {
        this.saving.set(false);
        this.snack.open(this.translate.instant('documents.detail.reviseAnswers.sent', { version: res.version }), 'OK', { duration: 5000 });
        this.ref.close({ version: res.version });
      },
      error: (err: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.saving.set(false);
        this.error.set(err.error?.detail
          ?? (err.error?.errors ? Object.values(err.error.errors).flat().join(' ') : '')
          ?? this.translate.instant('documents.detail.reviseAnswers.failed'));
        if (!this.error()) this.error.set(this.translate.instant('documents.detail.reviseAnswers.failed'));
      }
    });
  }
}
