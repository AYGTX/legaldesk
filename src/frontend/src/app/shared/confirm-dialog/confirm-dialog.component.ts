import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface ConfirmDialogData {
  title?: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  danger?: boolean;
  input?: {
    label?: string;
    type: 'text' | 'password' | 'textarea';
    required: boolean;
  };
}

/** Closes with `true` (confirm mode) or the entered string (input mode); `undefined` when dismissed. */
@Component({
  selector: 'sf-confirm-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    @if (data.title) {
      <h2 mat-dialog-title>{{ data.title }}</h2>
    }
    <mat-dialog-content>
      <p class="dialog-message">{{ data.message }}</p>
      @if (data.input; as input) {
        <mat-form-field appearance="outline" class="dialog-input">
          @if (input.label) {
            <mat-label>{{ input.label }}</mat-label>
          }
          @if (input.type === 'textarea') {
            <textarea matInput [formControl]="value" rows="3"></textarea>
          } @else {
            <input matInput [type]="input.type" [formControl]="value" (keyup.enter)="canConfirm() && ok()" />
          }
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ data.cancelLabel }}</button>
      <button mat-flat-button [color]="data.danger ? 'warn' : 'primary'" [disabled]="!canConfirm()" (click)="ok()">
        {{ data.confirmLabel }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-message { margin: 0; font-size: 14px; color: #444; white-space: pre-line; }
    .dialog-input { width: 100%; margin-top: 16px; }
  `]
})
export class ConfirmDialogComponent {
  readonly dialogRef = inject(MatDialogRef<ConfirmDialogComponent>);
  readonly data: ConfirmDialogData = inject(MAT_DIALOG_DATA);
  readonly value = new FormControl('', { nonNullable: true });

  canConfirm(): boolean {
    return !this.data.input?.required || !!this.value.value.trim();
  }

  ok(): void {
    this.dialogRef.close(this.data.input ? this.value.value : true);
  }
}
