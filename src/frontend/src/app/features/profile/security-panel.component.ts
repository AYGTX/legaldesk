import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { UserService } from '../../core/services/user.service';
import { UserSessionInfo } from '../../core/models';

/** Profile › Security: two-factor sign-in (R-M1-3) and the devices signed in to the account (R-M1-4). */
@Component({
  selector: 'sf-security-panel',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatSnackBarModule, TranslatePipe],
  template: `
    <div class="security-panel">
      <section>
        <h3 class="section-title">{{ 'profile.security.mfaTitle' | translate }}</h3>
        @if (auth.user()?.mfaEnabled) {
          <p class="status on"><mat-icon>verified_user</mat-icon> {{ 'profile.security.mfaOn' | translate }}</p>
          <button mat-stroked-button color="warn" (click)="disableMfa()">{{ 'profile.security.mfaDisable' | translate }}</button>
        } @else {
          <p class="status off"><mat-icon>gpp_maybe</mat-icon> {{ 'profile.security.mfaOff' | translate }}</p>
          <button mat-flat-button color="primary" (click)="router.navigate(['/auth/mfa-setup'])">
            {{ 'profile.security.mfaEnable' | translate }}
          </button>
        }
      </section>

      <section>
        <h3 class="section-title">{{ 'profile.security.sessionsTitle' | translate }}</h3>
        <p class="hint">{{ 'profile.security.sessionsHint' | translate }}</p>
        <ul class="sessions">
          @for (s of sessions(); track s.id) {
            <li>
              <mat-icon>devices</mat-icon>
              <div class="session-text">
                <span class="device">{{ describe(s.userAgent) }}</span>
                <span class="meta">
                  {{ 'profile.security.lastUsed' | translate: { date: (s.lastUsedAt | date:'d MMM y, HH:mm') } }}
                  @if (s.ipAddress) { · {{ s.ipAddress }} }
                </span>
              </div>
              <button mat-button color="warn" (click)="end(s)">{{ 'profile.security.signOutDevice' | translate }}</button>
            </li>
          } @empty {
            <li class="empty">{{ 'profile.security.noSessions' | translate }}</li>
          }
        </ul>
      </section>
    </div>
  `,
  styles: [`
    .security-panel { display: grid; gap: 28px; }
    .section-title { font-size: 16px; font-weight: 600; margin: 0 0 8px; }
    .status { display: flex; align-items: center; gap: 8px; margin: 0 0 12px; font-size: 14px; }
    .status.on { color: #15803d; }
    .status.off { color: #b45309; }
    .hint { margin: 0 0 8px; color: #64748b; font-size: 13px; }
    .sessions { list-style: none; margin: 0; padding: 0; border: 1px solid #e2e8f0; border-radius: 8px; }
    .sessions li { display: flex; align-items: center; gap: 12px; padding: 10px 14px; }
    .sessions li + li { border-top: 1px solid #e2e8f0; }
    .session-text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
    .device { font-weight: 500; }
    .meta { font-size: 12px; color: #64748b; }
    .empty { color: #64748b; font-size: 14px; }
  `]
})
export class SecurityPanelComponent implements OnInit {
  readonly auth = inject(AuthService);
  readonly router = inject(Router);
  private readonly users = inject(UserService);
  private readonly confirm = inject(ConfirmService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly sessions = signal<UserSessionInfo[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.users.getSessions().subscribe({ next: list => this.sessions.set(list), error: () => this.sessions.set([]) });
  }

  end(session: UserSessionInfo): void {
    this.users.endSession(session.id).subscribe(() => this.load());
  }

  disableMfa(): void {
    this.confirm.prompt({ message: this.translate.instant('profile.security.mfaDisablePrompt'), inputType: 'text', required: true })
      .subscribe(code => {
        if (!code) return;
        this.auth.disableMfa(code).subscribe({
          next: () => {
            this.snackBar.open(this.translate.instant('profile.security.mfaDisabled'), this.translate.instant('common.close'), { duration: 3000 });
            this.auth.refresh().subscribe();
          },
          error: err => this.snackBar.open(
            err.error?.errors?.code?.[0] ?? err.error?.detail ?? this.translate.instant('auth.mfa.invalidCode'),
            this.translate.instant('common.close'), { duration: 5000 })
        });
      });
  }

  /** A readable device name from the browser's user agent. */
  describe(userAgent?: string): string {
    if (!userAgent) return this.translate.instant('profile.security.unknownDevice');
    const browser = /Edg\//.test(userAgent) ? 'Edge' : /Chrome\//.test(userAgent) ? 'Chrome'
      : /Firefox\//.test(userAgent) ? 'Firefox' : /Safari\//.test(userAgent) ? 'Safari' : '';
    const os = /Windows/.test(userAgent) ? 'Windows' : /Mac OS X/.test(userAgent) ? 'macOS'
      : /Android/.test(userAgent) ? 'Android' : /iPhone|iPad/.test(userAgent) ? 'iOS' : /Linux/.test(userAgent) ? 'Linux' : '';
    return [browser, os].filter(Boolean).join(' · ') || this.translate.instant('profile.security.unknownDevice');
  }
}
