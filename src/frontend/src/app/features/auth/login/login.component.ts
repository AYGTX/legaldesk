import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../../core/auth/auth.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'sf-login',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterLink,
    MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule,
    MatIconModule, MatCheckboxModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <div class="login-container">
      <mat-card class="login-card">
        <mat-card-header>
          <mat-card-title>{{ 'auth.login.title' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'auth.login.subtitle' | translate }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          @if (error()) {
            <div class="error-banner">
              <mat-icon>error_outline</mat-icon>
              <span>{{ error() }}</span>
            </div>
          }

          @if (challenge()) {
            <!-- Second step: two-factor sign-in (R-M1-3) -->
            <form [formGroup]="codeForm" (ngSubmit)="submitCode()" class="form">
              <p class="mfa-hint">{{ (useRecovery() ? 'auth.mfa.recoveryHint' : 'auth.mfa.codeHint') | translate }}</p>
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ (useRecovery() ? 'auth.mfa.recoveryLabel' : 'auth.mfa.codeLabel') | translate }}</mat-label>
                <input matInput formControlName="code" autocomplete="one-time-code"
                       [attr.inputmode]="useRecovery() ? 'text' : 'numeric'" [attr.maxlength]="useRecovery() ? 11 : 6" />
                <mat-icon matSuffix>{{ useRecovery() ? 'key' : 'pin' }}</mat-icon>
              </mat-form-field>
              <div class="form-actions">
                <a href="" class="forgot-link" (click)="$event.preventDefault(); useRecovery.update(v => !v); codeForm.reset()">
                  {{ (useRecovery() ? 'auth.mfa.useApp' : 'auth.mfa.useRecovery') | translate }}
                </a>
              </div>
              <button mat-flat-button color="primary" type="submit" class="submit-btn" [disabled]="loading() || codeForm.invalid">
                @if (loading()) { <mat-spinner diameter="20"></mat-spinner> } @else { {{ 'auth.mfa.verify' | translate }} }
              </button>
              <button mat-button type="button" class="submit-btn" (click)="restart()">{{ 'auth.mfa.back' | translate }}</button>
            </form>
          } @else {
          <form [formGroup]="form" (ngSubmit)="submit()" class="form">
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'auth.login.emailLabel' | translate }}</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="email" [placeholder]="'auth.login.emailPlaceholder' | translate" />
              <mat-icon matSuffix>email</mat-icon>
              @if (form.get('email')?.hasError('required') && form.get('email')?.touched) {
                <mat-error>{{ 'auth.login.emailRequired' | translate }}</mat-error>
              }
              @if (form.get('email')?.hasError('email') && form.get('email')?.touched) {
                <mat-error>{{ 'auth.login.emailInvalid' | translate }}</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'auth.login.passwordLabel' | translate }}</mat-label>
              <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" />
              <button mat-icon-button matSuffix type="button" (click)="showPassword.update(v => !v)">
                <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
              @if (form.get('password')?.hasError('required') && form.get('password')?.touched) {
                <mat-error>{{ 'auth.login.passwordRequired' | translate }}</mat-error>
              }
            </mat-form-field>

            <div class="form-actions">
              <a routerLink="/auth/forgot-password" class="forgot-link">{{ 'auth.login.forgotPassword' | translate }}</a>
            </div>

            <button mat-flat-button color="primary" type="submit" class="submit-btn"
                    [disabled]="loading() || form.invalid">
              @if (loading()) {
                <mat-spinner diameter="20"></mat-spinner>
                <span>{{ 'auth.login.signingIn' | translate }}</span>
              } @else {
                {{ 'auth.login.signIn' | translate }}
              }
            </button>
          </form>
          }
        </mat-card-content>

        <mat-card-actions>
          <p class="signup-link">{{ 'auth.login.invitationOnly' | translate }}</p>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  /** Set when the password was right and two-factor sign-in is on: the second step answers it. */
  readonly challenge = signal<string | null>(null);
  readonly useRecovery = signal(false);

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  readonly codeForm = this.fb.group({
    code: ['', [Validators.required, Validators.minLength(6)]]
  });

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);

    this.auth.login({ email: this.form.value.email!, password: this.form.value.password! })
      .subscribe({
        next: result => {
          this.loading.set(false);
          if (result.mfaRequired) {
            this.challenge.set(result.mfaChallenge ?? null);
            this.form.controls.password.reset();
          } else {
            this.goIn();
          }
        },
        error: err => this.fail(err, 'auth.login.defaultError')
      });
  }

  submitCode(): void {
    const challenge = this.challenge();
    if (!challenge || this.codeForm.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);
    this.auth.verifyMfa(challenge, this.codeForm.value.code!).subscribe({
      next: () => this.goIn(),
      // Wrong and expired codes get the same answer; "Back" restarts from the password when the 5 minutes ran out.
      error: err => this.fail(err, 'auth.mfa.invalidCode')
    });
  }

  restart(): void {
    this.challenge.set(null);
    this.useRecovery.set(false);
    this.codeForm.reset();
    this.error.set(null);
  }

  private goIn(): void {
    this.router.navigate([this.auth.mfaSetupRequired() ? '/auth/mfa-setup' : '/dashboard']);
  }

  private fail(err: { status?: number; error?: { detail?: string; title?: string } }, fallbackKey: string): void {
    this.loading.set(false);
    // 429: too many attempts from this address.
    this.error.set(err.status === 429
      ? this.translate.instant('auth.login.tooManyAttempts')
      : err.error?.detail ?? err.error?.title ?? this.translate.instant(fallbackKey));
  }
}
