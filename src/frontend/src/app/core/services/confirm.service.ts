import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { Observable, map } from 'rxjs';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog.component';

export interface ConfirmOptions {
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
}

export interface PromptOptions {
  title?: string;
  message: string;
  inputLabel?: string;
  inputType?: 'text' | 'password' | 'textarea';
  /** When true (default), the confirm button stays disabled until the input is non-empty. */
  required?: boolean;
  confirmLabel?: string;
  /** The dialog only closes through its own buttons, so a stray click outside cannot dismiss or submit it. */
  disableClose?: boolean;
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(MatDialog);
  private readonly translate = inject(TranslateService);

  /** Material replacement for window.confirm(). Emits true only when the user confirms. */
  confirm(options: ConfirmOptions): Observable<boolean> {
    return this.open({
      title: options.title ?? this.translate.instant('common.confirmTitle'),
      message: options.message,
      confirmLabel: options.confirmLabel ?? this.translate.instant('common.confirm'),
      cancelLabel: options.cancelLabel ?? this.translate.instant('common.cancel'),
      danger: options.danger
    }).pipe(map(result => result === true));
  }

  /** Material replacement for window.prompt(). Emits the entered value, or null when cancelled. */
  prompt(options: PromptOptions): Observable<string | null> {
    return this.open({
      title: options.title,
      message: options.message,
      confirmLabel: options.confirmLabel ?? this.translate.instant('common.confirm'),
      cancelLabel: this.translate.instant('common.cancel'),
      input: {
        label: options.inputLabel,
        type: options.inputType ?? 'text',
        required: options.required ?? true
      },
      disableClose: options.disableClose
    }).pipe(map(result => (typeof result === 'string' ? result : null)));
  }

  private open(data: ConfirmDialogData): Observable<boolean | string | undefined> {
    return this.dialog
      .open(ConfirmDialogComponent, { width: '440px', data, autoFocus: data.input ? 'input, textarea' : 'first-tabbable', disableClose: data.disableClose })
      .afterClosed();
  }
}
