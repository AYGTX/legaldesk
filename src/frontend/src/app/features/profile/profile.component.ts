import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { UserService } from '../../core/services/user.service';
import { AuthService } from '../../core/auth/auth.service';
import { ProfileDto } from '../../core/models';
import { resolveFileUrl } from '../../core/utils/file-url';
import { VacationPanelComponent } from './vacation-panel.component';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const pw = control.get('newPassword')?.value;
  const cpw = control.get('confirmPassword')?.value;
  return pw === cpw ? null : { passwordMismatch: true };
}

@Component({
  selector: 'sf-profile',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    MatChipsModule,
    MatSnackBarModule,
    VacationPanelComponent,
    TranslatePipe,
  ],
  template: `
    <div class="profile-page">
      <div class="page-header">
        <h1 class="page-title">{{ 'profile.title' | translate }}</h1>
        <p class="page-subtitle">{{ 'profile.subtitle' | translate }}</p>
      </div>

      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="48"></mat-spinner></div>
      } @else if (profile()) {
        <!-- Identity card -->
        <mat-card class="identity-card">
          <div class="avatar-block">
            @if (avatarSrc()) {
              <img [src]="avatarSrc()" class="avatar-img" [alt]="'profile.avatarAlt' | translate" />
            } @else {
              <div class="avatar-fallback">{{ initials() }}</div>
            }
            <button mat-stroked-button (click)="avatarInput.click()" [disabled]="uploadingAvatar()">
              @if (uploadingAvatar()) {
                <mat-spinner diameter="16"></mat-spinner>
              } @else {
                <mat-icon>photo_camera</mat-icon>
              }
              {{ 'profile.changePhoto' | translate }}
            </button>
            <input
              #avatarInput
              type="file"
              accept="image/*"
              hidden
              (change)="onAvatarSelected($event)"
            />
          </div>
          <div class="identity-info">
            <h2>{{ profile()!.fullName }}</h2>
            <p class="email">{{ profile()!.email }}</p>
            <div class="role-chips">
              @for (role of profile()!.roles; track role) {
                <span class="role-chip">{{ role }}</span>
              }
            </div>
            <p class="member-since">
              {{ 'profile.memberSince' | translate }} {{ profile()!.createdAt | date: 'MMMM y' }}
            </p>
          </div>
        </mat-card>

        <mat-tab-group class="profile-tabs">
          <!-- Profile details -->
          <mat-tab [label]="'profile.tabs.details' | translate">
            <mat-card class="tab-card">
              <form [formGroup]="profileForm" (ngSubmit)="saveProfile()" class="form">
                <div class="row">
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'profile.fields.firstName' | translate }}</mat-label>
                    <input matInput formControlName="firstName" />
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'profile.fields.lastName' | translate }}</mat-label>
                    <input matInput formControlName="lastName" />
                  </mat-form-field>
                </div>
                <mat-form-field appearance="outline" class="full">
                  <mat-label>{{ 'common.email' | translate }}</mat-label>
                  <input matInput [value]="profile()!.email" disabled />
                </mat-form-field>
                <div class="row">
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'profile.fields.company' | translate }}</mat-label>
                    <input matInput formControlName="company" />
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'profile.fields.department' | translate }}</mat-label>
                    <input matInput formControlName="department" />
                  </mat-form-field>
                </div>
                <mat-form-field appearance="outline" class="full">
                  <mat-label>{{ 'profile.fields.title' | translate }}</mat-label>
                  <input matInput formControlName="title" />
                </mat-form-field>
                <div class="actions">
                  <button
                    mat-flat-button
                    color="primary"
                    type="submit"
                    [disabled]="savingProfile() || profileForm.invalid"
                  >
                    @if (savingProfile()) {
                      <mat-spinner diameter="18"></mat-spinner>
                    } @else {
                      {{ 'profile.saveChanges' | translate }}
                    }
                  </button>
                </div>
              </form>
            </mat-card>
          </mat-tab>

          <!-- Security -->
          <mat-tab [label]="'profile.tabs.security' | translate">
            <mat-card class="tab-card">
              <h3 class="section-title">
                {{ 'profile.security.changePasswordTitle' | translate }}
              </h3>
              @if (passwordError()) {
                <div class="error-banner">
                  <mat-icon>error_outline</mat-icon><span>{{ passwordError() }}</span>
                </div>
              }
              <form [formGroup]="passwordForm" (ngSubmit)="changePassword()" class="form">
                <mat-form-field appearance="outline" class="full">
                  <mat-label>{{ 'profile.security.currentPassword' | translate }}</mat-label>
                  <input
                    matInput
                    [type]="showPw() ? 'text' : 'password'"
                    formControlName="currentPassword"
                    autocomplete="current-password"
                  />
                  <button mat-icon-button matSuffix type="button" (click)="showPw.set(!showPw())">
                    <mat-icon>{{ showPw() ? 'visibility_off' : 'visibility' }}</mat-icon>
                  </button>
                </mat-form-field>
                <mat-form-field appearance="outline" class="full">
                  <mat-label>{{ 'profile.security.newPassword' | translate }}</mat-label>
                  <input
                    matInput
                    [type]="showPw() ? 'text' : 'password'"
                    formControlName="newPassword"
                    autocomplete="new-password"
                  />
                  @if (
                    passwordForm.get('newPassword')?.hasError('minlength') &&
                    passwordForm.get('newPassword')?.touched
                  ) {
                    <mat-error>{{ 'profile.security.minLength' | translate }}</mat-error>
                  }
                  <mat-hint>{{ 'profile.security.passwordHint' | translate }}</mat-hint>
                </mat-form-field>
                <mat-form-field appearance="outline" class="full">
                  <mat-label>{{ 'profile.security.confirmPassword' | translate }}</mat-label>
                  <input
                    matInput
                    [type]="showPw() ? 'text' : 'password'"
                    formControlName="confirmPassword"
                    autocomplete="new-password"
                  />
                  @if (
                    passwordForm.hasError('passwordMismatch') &&
                    passwordForm.get('confirmPassword')?.touched
                  ) {
                    <mat-error>{{ 'profile.security.passwordMismatch' | translate }}</mat-error>
                  }
                </mat-form-field>
                <div class="actions">
                  <button
                    mat-flat-button
                    color="primary"
                    type="submit"
                    [disabled]="savingPassword() || passwordForm.invalid"
                  >
                    @if (savingPassword()) {
                      <mat-spinner diameter="18"></mat-spinner>
                    } @else {
                      {{ 'profile.security.updatePassword' | translate }}
                    }
                  </button>
                </div>
              </form>
            </mat-card>
          </mat-tab>

          <!-- Vacation -->
          <mat-tab [label]="'profile.tabs.vacation' | translate">
            <mat-card class="tab-card">
              <sf-vacation-panel></sf-vacation-panel>
            </mat-card>
          </mat-tab>
        </mat-tab-group>
      }
    </div>
  `,
  styleUrl: './profile.component.scss',
})
export class ProfileComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly userService = inject(UserService);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly profile = signal<ProfileDto | null>(null);
  readonly savingProfile = signal(false);
  readonly savingPassword = signal(false);
  readonly uploadingAvatar = signal(false);
  readonly passwordError = signal<string | null>(null);
  readonly showPw = signal(false);

  readonly profileForm = this.fb.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    company: [''],
    department: [''],
    title: [''],
  });

  readonly passwordForm = this.fb.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );

  initials(): string {
    const p = this.profile();
    if (!p) return '?';
    return `${p.firstName[0] ?? ''}${p.lastName[0] ?? ''}`.toUpperCase();
  }

  avatarSrc(): string | null {
    return resolveFileUrl(this.profile()?.avatarUrl);
  }

  ngOnInit(): void {
    this.userService.getProfile().subscribe({
      next: (p) => {
        this.profile.set(p);
        this.profileForm.patchValue({
          firstName: p.firstName,
          lastName: p.lastName,
          company: p.company ?? '',
          department: p.department ?? '',
          title: p.title ?? '',
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  saveProfile(): void {
    if (this.profileForm.invalid) return;
    this.savingProfile.set(true);
    const v = this.profileForm.value;
    this.userService
      .updateProfile({
        firstName: v.firstName!,
        lastName: v.lastName!,
        company: v.company || undefined,
        department: v.department || undefined,
        title: v.title || undefined,
      })
      .subscribe({
        next: (p) => {
          this.profile.set(p);
          this.savingProfile.set(false);
          this.auth.patchUserName(p.firstName, p.lastName, p.fullName);
          this.snackBar.open(
            this.translate.instant('profile.messages.profileUpdated'),
            this.translate.instant('common.close'),
            { duration: 3000 },
          );
        },
        error: () => {
          this.savingProfile.set(false);
          this.snackBar.open(
            this.translate.instant('profile.messages.profileUpdateFailed'),
            this.translate.instant('common.close'),
            { duration: 4000 },
          );
        },
      });
  }

  changePassword(): void {
    if (this.passwordForm.invalid) return;
    this.savingPassword.set(true);
    this.passwordError.set(null);
    const v = this.passwordForm.value;
    this.userService
      .changePassword({
        currentPassword: v.currentPassword!,
        newPassword: v.newPassword!,
      })
      .subscribe({
        next: () => {
          this.savingPassword.set(false);
          this.passwordForm.reset();
          this.snackBar.open(
            this.translate.instant('profile.messages.passwordChanged'),
            this.translate.instant('common.close'),
            { duration: 3000 },
          );
        },
        error: (err) => {
          this.savingPassword.set(false);
          const detail = err.error?.errors
            ? Object.values(err.error.errors).flat().join(' ')
            : (err.error?.detail ??
              this.translate.instant('profile.messages.passwordChangeFailed'));
          this.passwordError.set(detail as string);
        },
      });
  }

  onAvatarSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.uploadingAvatar.set(true);
    this.userService.uploadAvatar(file).subscribe({
      next: (res) => {
        this.profile.update((p) => (p ? { ...p, avatarUrl: res.avatarUrl } : p));
        this.auth.patchAvatar(res.avatarUrl);
        this.uploadingAvatar.set(false);
        this.snackBar.open(
          this.translate.instant('profile.messages.avatarUpdated'),
          this.translate.instant('common.close'),
          { duration: 3000 },
        );
      },
      error: () => {
        this.uploadingAvatar.set(false);
        this.snackBar.open(
          this.translate.instant('profile.messages.avatarUploadFailed'),
          this.translate.instant('common.close'),
          { duration: 4000 },
        );
      },
    });
  }
}
