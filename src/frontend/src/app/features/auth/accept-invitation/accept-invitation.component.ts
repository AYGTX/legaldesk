import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth/auth.service';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  return control.get('password')?.value === control.get('confirmPassword')?.value ? null : { passwordMismatch: true };
}

/** The page an invitation e-mail links to (R-M1-1): set a password, then straight in. */
@Component({
  selector: 'sf-accept-invitation',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule,
    MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <div class="rp-container">
      <mat-card class="rp-card">
        <mat-card-header>
          <mat-card-title>{{ 'auth.invitation.title' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'auth.invitation.subtitle' | translate }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @if (!token) {
            <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ 'auth.invitation.missingToken' | translate }}</span></div>
            <a mat-button routerLink="/auth/login">{{ 'auth.invitation.toSignIn' | translate }}</a>
          } @else {
            @if (error()) {
              <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
            }
            <form [formGroup]="form" (ngSubmit)="submit()" class="form">
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'auth.invitation.passwordLabel' | translate }}</mat-label>
                <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="new-password" />
                <button mat-icon-button matSuffix type="button" (click)="showPassword.update(v => !v)">
                  <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
                </button>
                <mat-hint>{{ 'auth.invitation.passwordRules' | translate }}</mat-hint>
              </mat-form-field>
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'auth.invitation.confirmLabel' | translate }}</mat-label>
                <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="confirmPassword" autocomplete="new-password" />
                @if (form.hasError('passwordMismatch') && form.get('confirmPassword')?.touched) {
                  <mat-error>{{ 'auth.resetPassword.passwordMismatch' | translate }}</mat-error>
                }
              </mat-form-field>
              <button mat-flat-button color="primary" type="submit" class="submit-btn" [disabled]="loading() || form.invalid">
                @if (loading()) { <mat-spinner diameter="20"></mat-spinner> } @else { {{ 'auth.invitation.submit' | translate }} }
              </button>
            </form>
          }
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styleUrl: '../reset-password/reset-password.component.scss'
})
export class AcceptInvitationComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  // Same rules as the API: 10 characters with upper and lower case, a digit and a symbol.
  readonly form = this.fb.group({
    password: ['', [Validators.required, Validators.minLength(10),
      Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$/)]],
    confirmPassword: ['', Validators.required]
  }, { validators: passwordsMatch });

  submit(): void {
    if (!this.token || this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);
    this.auth.acceptInvitation(this.token, this.form.value.password!).subscribe({
      next: () => this.router.navigate([this.auth.mfaSetupRequired() ? '/auth/mfa-setup' : '/dashboard']),
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.errors?.password?.[0] ?? err.error?.detail ?? this.translate.instant('auth.invitation.defaultError'));
      }
    });
  }
}
