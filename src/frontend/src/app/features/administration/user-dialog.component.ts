import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { UserService } from '../../core/services/user.service';
import { UserDto } from '../../core/models';

export interface UserDialogData {
  user: UserDto | null;       // null = create mode
  roles: string[];
}

@Component({
  selector: 'sf-user-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatIconModule, MatSlideToggleModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>{{ (isEdit ? 'admin.userDialog.editTitle' : 'admin.userDialog.createTitle') | translate }}</h2>

    <mat-dialog-content>
      @if (error()) {
        <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
      }

      <form [formGroup]="form" class="form">
        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'admin.userDialog.firstName' | translate }}</mat-label>
            <input matInput formControlName="firstName" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'admin.userDialog.lastName' | translate }}</mat-label>
            <input matInput formControlName="lastName" />
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'common.email' | translate }}</mat-label>
          <input matInput type="email" formControlName="email" />
          @if (isEdit) { <mat-hint>{{ 'admin.userDialog.emailCannotBeChanged' | translate }}</mat-hint> }
        </mat-form-field>

        @if (!isEdit) {
          <mat-form-field appearance="outline" class="full">
            <mat-label>{{ 'admin.userDialog.temporaryPassword' | translate }}</mat-label>
            <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" />
            <button mat-icon-button matSuffix type="button" (click)="showPassword.set(!showPassword())">
              <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
            </button>
            <mat-hint>{{ 'admin.userDialog.passwordHint' | translate }}</mat-hint>
          </mat-form-field>
        }

        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'admin.userDialog.company' | translate }}</mat-label>
            <input matInput formControlName="company" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'admin.userDialog.department' | translate }}</mat-label>
            <input matInput formControlName="department" />
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'admin.userDialog.title' | translate }}</mat-label>
          <input matInput formControlName="title" />
        </mat-form-field>

        <mat-form-field appearance="outline" class="full">
          <mat-label>{{ 'admin.userDialog.roles' | translate }}</mat-label>
          <mat-select formControlName="roles" multiple>
            @for (role of data.roles; track role) {
              <mat-option [value]="role">{{ role }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        @if (isEdit) {
          <div class="toggle-row">
            <mat-slide-toggle formControlName="isActive">{{ 'admin.userDialog.accountActive' | translate }}</mat-slide-toggle>
          </div>
        }
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="saving() || form.invalid">
        @if (saving()) { <mat-spinner diameter="18"></mat-spinner> }
        @else { {{ (isEdit ? 'admin.userDialog.saveChanges' : 'admin.userDialog.createUser') | translate }} }
      </button>
    </mat-dialog-actions>
  `,
  styleUrl: './user-dialog.component.scss'
})
export class UserDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly userService = inject(UserService);
  private readonly translate = inject(TranslateService);
  readonly dialogRef = inject(MatDialogRef<UserDialogComponent>);
  readonly data: UserDialogData = inject(MAT_DIALOG_DATA);

  readonly isEdit = !!this.data.user;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  readonly form = this.fb.group({
    firstName: [this.firstNameFromUser(), Validators.required],
    lastName: [this.lastNameFromUser(), Validators.required],
    email: [{ value: this.data.user?.email ?? '', disabled: this.isEdit }, [Validators.required, Validators.email]],
    password: ['', this.isEdit ? [] : [Validators.required, Validators.minLength(8)]],
    company: [this.data.user?.company ?? ''],
    department: [this.data.user?.department ?? ''],
    title: [this.data.user?.title ?? ''],
    roles: [this.data.user?.roles ?? ['Employee'], Validators.required],
    isActive: [this.data.user?.isActive ?? true]
  });

  private firstNameFromUser(): string {
    return this.data.user?.fullName?.split(' ')[0] ?? '';
  }
  private lastNameFromUser(): string {
    return this.data.user?.fullName?.split(' ').slice(1).join(' ') ?? '';
  }

  submit(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();

    const request$: Observable<unknown> = this.isEdit
      ? this.userService.updateUser(this.data.user!.id, {
          firstName: v.firstName!,
          lastName: v.lastName!,
          company: v.company || undefined,
          department: v.department || undefined,
          title: v.title || undefined,
          isActive: v.isActive!,
          roles: v.roles!
        })
      : this.userService.createUser({
          firstName: v.firstName!,
          lastName: v.lastName!,
          email: v.email!,
          password: v.password!,
          company: v.company || undefined,
          department: v.department || undefined,
          title: v.title || undefined,
          roles: v.roles!
        });

    request$.subscribe({
      next: () => { this.saving.set(false); this.dialogRef.close(true); },
      error: (err: { error?: { errors?: Record<string, string[]>; detail?: string; title?: string } }) => {
        this.saving.set(false);
        this.error.set(this.extractError(err));
      }
    });
  }

  private extractError(err: { error?: { errors?: Record<string, string[]>; detail?: string; title?: string } }): string {
    if (err.error?.errors) {
      return Object.values(err.error.errors).flat().join(' ');
    }
    return err.error?.detail ?? err.error?.title ?? this.translate.instant('admin.userDialog.operationFailed');
  }
}
