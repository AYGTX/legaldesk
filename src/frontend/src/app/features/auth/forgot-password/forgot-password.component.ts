import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../../core/auth/auth.service';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'sf-forgot-password',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterLink,
    MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <div class="fp-container">
      <mat-card class="fp-card">
        @if (!submitted()) {
          <mat-card-header>
            <mat-card-title>{{ 'auth.forgotPassword.title' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'auth.forgotPassword.subtitle' | translate }}</mat-card-subtitle>
          </mat-card-header>

          <mat-card-content>
            <form [formGroup]="form" (ngSubmit)="submit()" class="form">
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'auth.forgotPassword.emailLabel' | translate }}</mat-label>
                <input matInput type="email" formControlName="email" autocomplete="email" />
                <mat-icon matSuffix>email</mat-icon>
                @if (form.get('email')?.hasError('required') && form.get('email')?.touched) {
                  <mat-error>{{ 'auth.forgotPassword.emailRequired' | translate }}</mat-error>
                }
                @if (form.get('email')?.hasError('email') && form.get('email')?.touched) {
                  <mat-error>{{ 'auth.forgotPassword.emailInvalid' | translate }}</mat-error>
                }
              </mat-form-field>

              <button mat-flat-button color="primary" type="submit" class="submit-btn"
                      [disabled]="loading() || form.invalid">
                @if (loading()) {
                  <mat-spinner diameter="20"></mat-spinner>
                  <span>{{ 'auth.forgotPassword.sending' | translate }}</span>
                } @else {
                  {{ 'auth.forgotPassword.sendResetLink' | translate }}
                }
              </button>
            </form>
          </mat-card-content>
        } @else {
          <mat-card-content class="success-state">
            <div class="success-icon">
              <mat-icon>mark_email_read</mat-icon>
            </div>
            <h3>{{ 'auth.forgotPassword.checkInbox' | translate }}</h3>
            <p>{{ 'auth.forgotPassword.successPrefix' | translate }} <strong>{{ sentEmail() }}</strong>{{ 'auth.forgotPassword.successSuffix' | translate }}</p>
          </mat-card-content>
        }

        <mat-card-actions>
          <a routerLink="/auth/login" class="back-link">
            <mat-icon>arrow_back</mat-icon>
            {{ 'auth.forgotPassword.backToSignIn' | translate }}
          </a>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styleUrl: './forgot-password.component.scss'
})
export class ForgotPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly loading = signal(false);
  readonly submitted = signal(false);
  readonly sentEmail = signal('');

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]]
  });

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    const email = this.form.value.email!;

    this.auth.forgotPassword({ email }).subscribe({
      next: () => {
        this.sentEmail.set(email);
        this.submitted.set(true);
        this.loading.set(false);
      },
      error: () => {
        // Always show success to prevent email enumeration
        this.sentEmail.set(email);
        this.submitted.set(true);
        this.loading.set(false);
      }
    });
  }
}
