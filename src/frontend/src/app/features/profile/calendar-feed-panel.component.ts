import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LifecycleService } from '../../core/services/lifecycle.service';

/**
 * The user's private calendar address (R-M10-9): notice deadlines, end dates and obligations in any calendar. The
 * address is shown once; a new one replaces it, and stopping it ends the old one.
 */
@Component({
  selector: 'sf-calendar-feed-panel',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatSnackBarModule, TranslatePipe],
  template: `
    <h3 class="title"><mat-icon>event</mat-icon> {{ 'calendarFeed.title' | translate }}</h3>
    <p class="hint">{{ 'calendarFeed.hint' | translate }}</p>
    @if (url(); as u) {
      <div class="url-row">
        <code class="url">{{ u }}</code>
        <button mat-stroked-button (click)="copy(u)"><mat-icon>content_copy</mat-icon> {{ 'calendarFeed.copy' | translate }}</button>
      </div>
      <p class="hint warn">{{ 'calendarFeed.secret' | translate }}</p>
    }
    <div class="actions">
      <button mat-flat-button color="primary" (click)="issue()" [disabled]="busy()">
        <mat-icon>link</mat-icon> {{ (url() ? 'calendarFeed.replace' : 'calendarFeed.issue') | translate }}
      </button>
      <button mat-button (click)="revoke()" [disabled]="busy()">{{ 'calendarFeed.revoke' | translate }}</button>
    </div>
  `,
  styles: [`
    .title { display: flex; align-items: center; gap: 6px; margin: 0 0 6px; font-size: 16px; }
    .hint { color: #5f6b7a; font-size: 13px; margin: 0 0 10px; } .hint.warn { color: #92400e; }
    .url-row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin-bottom: 6px; }
    .url { flex: 1; min-width: 0; overflow-wrap: anywhere; background: #f3f4f6; border-radius: 6px; padding: 8px; font-size: 12px; }
    .actions { display: flex; gap: 8px; flex-wrap: wrap; }
  `]
})
export class CalendarFeedPanelComponent {
  private readonly svc = inject(LifecycleService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly url = signal<string | null>(null);
  readonly busy = signal(false);

  issue(): void {
    this.busy.set(true);
    this.svc.issueCalendarFeed().subscribe({
      next: r => { this.url.set(r.url); this.busy.set(false); },
      error: () => { this.busy.set(false); this.snack.open(this.translate.instant('calendarFeed.failed'), 'OK', { duration: 4000 }); }
    });
  }

  revoke(): void {
    this.busy.set(true);
    this.svc.revokeCalendarFeed().subscribe({
      next: () => { this.url.set(null); this.busy.set(false); this.snack.open(this.translate.instant('calendarFeed.revoked'), 'OK', { duration: 3000 }); },
      error: () => { this.busy.set(false); this.snack.open(this.translate.instant('calendarFeed.failed'), 'OK', { duration: 4000 }); }
    });
  }

  copy(url: string): void {
    navigator.clipboard.writeText(url).then(() => this.snack.open(this.translate.instant('calendarFeed.copied'), 'OK', { duration: 2500 }));
  }
}
