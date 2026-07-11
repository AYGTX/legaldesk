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
        </mat-card-content>

        <mat-card-actions>
          <p class="signup-link">
            {{ 'auth.login.noAccount' | translate }} <a routerLink="/auth/register">{{ 'auth.login.createOne' | translate }}</a>
          </p>
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

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);

    this.auth.login({ email: this.form.value.email!, password: this.form.value.password! })
      .subscribe({
        next: () => this.router.navigate(['/dashboard']),
        error: err => {
          this.loading.set(false);
          this.error.set(err.error?.detail ?? err.error?.title ?? this.translate.instant('auth.login.defaultError'));
        }
      });
  }
}
