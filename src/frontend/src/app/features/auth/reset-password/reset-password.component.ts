import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../../core/auth/auth.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const pw = control.get('password')?.value;
  const cpw = control.get('confirmPassword')?.value;
  return pw === cpw ? null : { passwordMismatch: true };
}

@Component({
  selector: 'sf-reset-password',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterLink,
    MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <div class="rp-container">
      <mat-card class="rp-card">
        @if (!success()) {
          <mat-card-header>
            <mat-card-title>{{ 'auth.resetPassword.title' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'auth.resetPassword.subtitle' | translate }}</mat-card-subtitle>
          </mat-card-header>

          <mat-card-content>
            @if (error()) {
              <div class="error-banner">
                <mat-icon>error_outline</mat-icon>
                <span>{{ error() }}</span>
              </div>
            }

            <form [formGroup]="form" (ngSubmit)="submit()" class="form">
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'auth.resetPassword.newPasswordLabel' | translate }}</mat-label>
                <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="new-password" />
                <button mat-icon-button matSuffix type="button" (click)="showPassword.update(v => !v)">
                  <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
                </button>
                @if (form.get('password')?.hasError('minlength') && form.get('password')?.touched) {
                  <mat-error>{{ 'auth.resetPassword.passwordMinLength' | translate }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'auth.resetPassword.confirmPasswordLabel' | translate }}</mat-label>
                <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="confirmPassword" autocomplete="new-password" />
                @if (form.hasError('passwordMismatch') && form.get('confirmPassword')?.touched) {
                  <mat-error>{{ 'auth.resetPassword.passwordMismatch' | translate }}</mat-error>
                }
              </mat-form-field>

              <button mat-flat-button color="primary" type="submit" class="submit-btn"
                      [disabled]="loading() || form.invalid">
                @if (loading()) {
                  <mat-spinner diameter="20"></mat-spinner>
                } @else {
                  {{ 'auth.resetPassword.resetPassword' | translate }}
                }
              </button>
            </form>
          </mat-card-content>
        } @else {
          <mat-card-content class="success-state">
            <div class="success-icon"><mat-icon>check_circle</mat-icon></div>
            <h3>{{ 'auth.resetPassword.passwordUpdated' | translate }}</h3>
            <p>{{ 'auth.resetPassword.successMessage' | translate }}</p>
            <a mat-flat-button color="primary" routerLink="/auth/login">{{ 'auth.resetPassword.signInNow' | translate }}</a>
          </mat-card-content>
        }
      </mat-card>
    </div>
  `,
  styleUrl: './reset-password.component.scss'
})
export class ResetPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly success = signal(false);
  readonly showPassword = signal(false);

  readonly form = this.fb.group({
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required]
  }, { validators: passwordsMatch });

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);

    const token = this.route.snapshot.queryParamMap.get('token') ?? '';
    const email = this.route.snapshot.queryParamMap.get('email') ?? '';

    this.auth.resetPassword({ token, email, newPassword: this.form.value.password! })
      .subscribe({
        next: () => { this.loading.set(false); this.success.set(true); },
        error: err => {
          this.loading.set(false);
          this.error.set(err.error?.detail ?? this.translate.instant('auth.resetPassword.defaultError'));
        }
      });
  }
}
