import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth/auth.service';
import { MfaSetup } from '../../../core/models';

/**
 * Two-factor setup (R-M1-3): scan the QR code, prove the app works with one code, then keep the recovery codes,
 * shown only here. Turning it on ends every session, so the page sends the user to sign in again, with a code.
 */
@Component({
  selector: 'sf-mfa-setup',
  standalone: true,
  imports: [ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, TranslatePipe],
  template: `
    <div class="mfa-page">
      <div class="rp-container">
        <mat-card class="rp-card">
          <mat-card-header>
            <mat-card-title>{{ 'auth.mfaSetup.title' | translate }}</mat-card-title>
            <mat-card-subtitle>
              {{ (auth.mfaSetupRequired() ? 'auth.mfaSetup.requiredSubtitle' : 'auth.mfaSetup.subtitle') | translate }}
            </mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            @if (error()) {
              <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
            }
            @if (recoveryCodes(); as codes) {
              <div class="success-state">
                <div class="success-icon"><mat-icon>verified_user</mat-icon></div>
                <h3>{{ 'auth.mfaSetup.doneTitle' | translate }}</h3>
                <p>{{ 'auth.mfaSetup.recoveryIntro' | translate }}</p>
                <ul class="codes">
                  @for (code of codes; track code) { <li><code>{{ code }}</code></li> }
                </ul>
                <button mat-stroked-button type="button" (click)="copy(codes)">
                  <mat-icon>content_copy</mat-icon> {{ (copied() ? 'auth.mfaSetup.copied' : 'auth.mfaSetup.copy') | translate }}
                </button>
                <button mat-flat-button color="primary" class="submit-btn" type="button" (click)="finish()">
                  {{ 'auth.mfaSetup.continue' | translate }}
                </button>
              </div>
            } @else if (setup(); as s) {
              <ol class="steps">
                <li>{{ 'auth.mfaSetup.step1' | translate }}</li>
                <li>{{ 'auth.mfaSetup.step2' | translate }}</li>
              </ol>
              <div class="qr"><img [src]="s.qrCodeDataUri" [alt]="'auth.mfaSetup.qrAlt' | translate" width="200" height="200" /></div>
              <p class="key">{{ 'auth.mfaSetup.manualKey' | translate }} <code>{{ s.sharedKey }}</code></p>
              <form [formGroup]="form" (ngSubmit)="confirm()" class="form">
                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>{{ 'auth.mfa.codeLabel' | translate }}</mat-label>
                  <input matInput formControlName="code" inputmode="numeric" maxlength="6" autocomplete="one-time-code" />
                </mat-form-field>
                <button mat-flat-button color="primary" type="submit" class="submit-btn" [disabled]="loading() || form.invalid">
                  @if (loading()) { <mat-spinner diameter="20"></mat-spinner> } @else { {{ 'auth.mfaSetup.confirm' | translate }} }
                </button>
              </form>
              <button mat-button type="button" class="submit-btn" (click)="auth.logout()">{{ 'auth.mfaSetup.signOut' | translate }}</button>
            } @else if (!error()) {
              <div class="loading"><mat-spinner diameter="32"></mat-spinner></div>
            }
          </mat-card-content>
        </mat-card>
      </div>
    </div>
  `,
  styleUrls: ['../reset-password/reset-password.component.scss', './mfa-setup.component.scss']
})
export class MfaSetupComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly setup = signal<MfaSetup | null>(null);
  readonly recoveryCodes = signal<string[] | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly copied = signal(false);

  readonly form = this.fb.group({ code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]] });

  ngOnInit(): void {
    this.auth.beginMfaSetup().subscribe({
      next: s => this.setup.set(s),
      error: err => this.error.set(err.error?.detail ?? this.translate.instant('auth.mfaSetup.defaultError'))
    });
  }

  confirm(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);
    this.auth.confirmMfaSetup(this.form.value.code!).subscribe({
      next: r => { this.loading.set(false); this.recoveryCodes.set(r.codes); },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.errors?.code?.[0] ?? err.error?.detail ?? this.translate.instant('auth.mfa.invalidCode'));
      }
    });
  }

  copy(codes: string[]): void {
    navigator.clipboard?.writeText(codes.join('\n')).then(() => this.copied.set(true));
  }

  /** Every session ended when two-factor turned on: sign in again, this time with a code. */
  finish(): void {
    this.auth.clear();
    this.router.navigate(['/auth/login']);
  }
}
