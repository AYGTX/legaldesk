import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../../core/auth/auth.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirm = control.get('confirmPassword')?.value;
  return password === confirm ? null : { passwordMismatch: true };
}

@Component({
  selector: 'sf-register',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterLink,
    MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatCheckboxModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <div class="register-container">
      <mat-card class="register-card">
        <mat-card-header>
          <mat-card-title>{{ 'auth.register.title' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'auth.register.subtitle' | translate }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          @if (error()) {
            <div class="error-banner">
              <mat-icon>error_outline</mat-icon>
              <span>{{ error() }}</span>
            </div>
          }

          <form [formGroup]="form" (ngSubmit)="submit()" class="form">
            <div class="name-row">
              <mat-form-field appearance="outline">
                <mat-label>{{ 'auth.register.firstNameLabel' | translate }}</mat-label>
                <input matInput formControlName="firstName" autocomplete="given-name" />
                @if (form.get('firstName')?.hasError('required') && form.get('firstName')?.touched) {
                  <mat-error>{{ 'common.required' | translate }}</mat-error>
                }
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'auth.register.lastNameLabel' | translate }}</mat-label>
                <input matInput formControlName="lastName" autocomplete="family-name" />
                @if (form.get('lastName')?.hasError('required') && form.get('lastName')?.touched) {
                  <mat-error>{{ 'common.required' | translate }}</mat-error>
                }
              </mat-form-field>
            </div>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'auth.register.emailLabel' | translate }}</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="email" />
              <mat-icon matSuffix>email</mat-icon>
              @if (form.get('email')?.hasError('required') && form.get('email')?.touched) {
                <mat-error>{{ 'auth.register.emailRequired' | translate }}</mat-error>
              }
              @if (form.get('email')?.hasError('email') && form.get('email')?.touched) {
                <mat-error>{{ 'auth.register.emailInvalid' | translate }}</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'auth.register.passwordLabel' | translate }}</mat-label>
              <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="new-password" />
              <button mat-icon-button matSuffix type="button" (click)="showPassword.update(v => !v)">
                <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
              @if (form.get('password')?.hasError('required') && form.get('password')?.touched) {
                <mat-error>{{ 'auth.register.passwordRequired' | translate }}</mat-error>
              }
              @if (form.get('password')?.hasError('minlength') && form.get('password')?.touched) {
                <mat-error>{{ 'auth.register.passwordMinLength' | translate }}</mat-error>
              }
              <mat-hint>{{ 'auth.register.passwordHint' | translate }}</mat-hint>
            </mat-form-field>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'auth.register.confirmPasswordLabel' | translate }}</mat-label>
              <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="confirmPassword" autocomplete="new-password" />
              @if (form.hasError('passwordMismatch') && form.get('confirmPassword')?.touched) {
                <mat-error>{{ 'auth.register.passwordMismatch' | translate }}</mat-error>
              }
            </mat-form-field>

            <div class="consent-row">
              <mat-checkbox formControlName="consentToS" color="primary"></mat-checkbox>
              <span class="consent-text">
                {{ 'auth.register.consentText' | translate }}
                <a routerLink="/legal/terms" target="_blank">{{ 'auth.register.termsOfService' | translate }}</a>
                {{ 'auth.register.and' | translate }}
                <a routerLink="/legal/privacy" target="_blank">{{ 'auth.register.privacyPolicy' | translate }}</a>
              </span>
            </div>
            @if (form.get('consentToS')?.invalid && form.get('consentToS')?.touched) {
              <p class="consent-error">{{ 'auth.register.consentRequired' | translate }}</p>
            }

            <button mat-flat-button color="primary" type="submit" class="submit-btn"
                    [disabled]="loading() || form.invalid">
              @if (loading()) {
                <mat-spinner diameter="20"></mat-spinner>
                <span>{{ 'auth.register.creatingAccount' | translate }}</span>
              } @else {
                {{ 'auth.register.createAccount' | translate }}
              }
            </button>
          </form>
        </mat-card-content>

        <mat-card-actions>
          <p class="login-link">
            {{ 'auth.register.haveAccount' | translate }} <a routerLink="/auth/login">{{ 'auth.register.signIn' | translate }}</a>
          </p>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styleUrl: './register.component.scss'
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  readonly form = this.fb.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required],
    consentToS: [false, Validators.requiredTrue]
  }, { validators: passwordsMatch });

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);

    const { firstName, lastName, email, password } = this.form.value;
    this.auth.register({ firstName: firstName!, lastName: lastName!, email: email!, password: password! })
      .subscribe({
        next: () => this.router.navigate(['/dashboard']),
        error: err => {
          this.loading.set(false);
          const detail = err.error?.errors
            ? Object.values(err.error.errors).flat().join(', ')
            : err.error?.detail ?? this.translate.instant('auth.register.defaultError');
          this.error.set(detail as string);
        }
      });
  }
}
