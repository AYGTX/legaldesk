import { Component, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { RecipientInfo } from '../../core/models';

/**
 * Post-submit confirmation screen shared by every request-origination flow: checkmark, signing-links
 * card (one row per recipient with a copy-link button), and a caller-supplied actions row via projection —
 * each flow's "next step" differs (submit another vs. back to forms), so that part isn't generalized.
 */
@Component({
  selector: 'sf-request-success',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatSnackBarModule, TranslatePipe],
  template: `
    <div class="rsu-state">
      <div class="rsu-icon">
        <mat-icon>check_circle</mat-icon>
      </div>
      <h2>{{ 'shared.requestSuccess.title' | translate }}</h2>
      <p>{{ 'shared.requestSuccess.subtitle' | translate }}</p>

      @if (recipients().length > 0) {
        <div class="rsu-links-card">
          <div class="rsu-links-title">{{ 'shared.requestSuccess.signingLinks' | translate }}</div>
          @for (r of recipients(); track r.id) {
            <div class="rsu-link-row">
              <div class="rsu-link-info">
                <span class="rsu-link-name">{{ r.name }}</span>
                <span class="rsu-link-email">{{ r.email }}</span>
              </div>
              <button mat-stroked-button (click)="copyLink(r.secureToken)">
                <mat-icon>link</mat-icon> {{ 'shared.requestSuccess.copyLink' | translate }}
              </button>
            </div>
          }
        </div>
      }

      <div class="rsu-actions">
        <ng-content></ng-content>
      </div>
    </div>
  `,
  styles: [`
    .rsu-state { display: flex; flex-direction: column; align-items: center; gap: 16px; padding: 48px 0; text-align: center; }
    .rsu-icon mat-icon { font-size: 64px; width: 64px; height: 64px; color: #1e6b2e; }
    .rsu-state h2 { font-size: 22px; font-weight: 500; margin: 0; }
    .rsu-state p { color: #5f6368; font-size: 14px; margin: 0; }
    .rsu-actions { display: flex; gap: 12px; flex-wrap: wrap; justify-content: center; }

    .rsu-links-card { width: 100%; max-width: 480px; border: 1px solid #c7d0dc; border-radius: 12px; padding: 16px 20px; text-align: left; background: #fff; }
    .rsu-links-title { font-size: 12px; font-weight: 600; color: #5f6368; text-transform: uppercase; letter-spacing: 0.4px; margin-bottom: 10px; }
    .rsu-link-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 0; border-top: 1px solid #e8edf2; }
    .rsu-link-row:first-of-type { border-top: none; }
    .rsu-link-info { display: flex; flex-direction: column; min-width: 0; }
    .rsu-link-name { font-size: 13px; font-weight: 500; color: #1f1f1f; }
    .rsu-link-email { font-size: 12px; color: #9aa0a6; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `]
})
export class RequestSuccessComponent {
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly recipients = input<RecipientInfo[]>([]);

  copyLink(secureToken: string): void {
    const url = `${window.location.origin}/sign/${secureToken}`;
    navigator.clipboard.writeText(url).then(
      () => this.snack.open(this.translate.instant('shared.requestSuccess.linkCopied'), 'OK', { duration: 2500 }),
      () => this.snack.open(this.translate.instant('shared.requestSuccess.linkCopyFailed'), 'OK', { duration: 3000 })
    );
  }
}
