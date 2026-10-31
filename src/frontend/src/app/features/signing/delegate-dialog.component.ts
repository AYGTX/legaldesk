import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SigningService } from '../../core/services/signing.service';

export interface DelegateDialogData {
  token: string;
  ownEmail: string;
}

/**
 * Hands the participant's step to someone else, with a note (R-M4-5). The colleague gets their own link and acts in
 * the participant's place; this link stops working.
 */
@Component({
  selector: 'sf-delegate-dialog',
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'signing.delegate.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ 'signing.delegate.hint' | translate }}</p>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'signing.delegate.name' | translate }}</mat-label>
        <input matInput maxlength="200" [(ngModel)]="name" required />
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'signing.delegate.email' | translate }}</mat-label>
        <input matInput type="email" maxlength="256" [(ngModel)]="email" required />
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'signing.delegate.note' | translate }}</mat-label>
        <textarea matInput rows="3" maxlength="1000" [(ngModel)]="note" required></textarea>
      </mat-form-field>
      @if (error()) { <p class="error"><mat-icon>error_outline</mat-icon> {{ error() }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close()" [disabled]="busy()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="busy() || !valid()">
        <mat-icon>forward_to_inbox</mat-icon> {{ 'signing.delegate.submit' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full { width: 100%; }
    .hint { margin: 0 0 12px; color: #5f6b7a; font-size: 13px; }
    .error { display: flex; align-items: center; gap: 6px; color: #c5221f; font-size: 13px; margin: 0; }
  `]
})
export class DelegateDialogComponent {
  readonly ref = inject(MatDialogRef<DelegateDialogComponent, { name: string } | undefined>);
  private readonly data = inject<DelegateDialogData>(MAT_DIALOG_DATA);
  private readonly signing = inject(SigningService);
  private readonly translate = inject(TranslateService);

  name = '';
  email = '';
  note = '';
  readonly busy = signal(false);
  readonly error = signal('');

  valid(): boolean {
    return !!this.name.trim() && /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(this.email.trim()) && !!this.note.trim();
  }

  submit(): void {
    if (this.email.trim().toLowerCase() === this.data.ownEmail.toLowerCase()) {
      this.error.set(this.translate.instant('signing.delegate.self'));
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.signing.delegate(this.data.token, { name: this.name.trim(), email: this.email.trim(), note: this.note.trim() }).subscribe({
      next: stand => this.ref.close({ name: stand.name }),
      error: (e: { error?: { code?: string; detail?: string; errors?: Record<string, string[]> } }) => {
        this.busy.set(false);
        const key = e?.error?.code ? `signing.delegate.errors.${e.error.code}` : '';
        const text = key ? this.translate.instant(key) : key;
        const messages = e?.error?.errors ? Object.values(e.error.errors).flat().join(' ') : '';
        this.error.set(text && text !== key ? text : messages || e?.error?.detail || this.translate.instant('signing.delegate.failed'));
      }
    });
  }
}
