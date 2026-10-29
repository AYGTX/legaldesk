import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { UserService } from '../../core/services/user.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { AuthService } from '../../core/auth/auth.service';
import { UserDto } from '../../core/models';
import { UserDialogComponent } from './user-dialog.component';
import { RequestTypesComponent } from './request-types.component';

@Component({
  selector: 'sf-admin',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatTableModule, MatPaginatorModule, MatButtonModule, MatIconModule,
    MatChipsModule, MatProgressSpinnerModule,
    MatFormFieldModule, MatInputModule, MatMenuModule, MatDividerModule,
    MatTooltipModule, MatDialogModule, MatSnackBarModule,
    RequestTypesComponent, TranslatePipe
  ],
  template: `
    <div class="admin-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ sectionTitleKey() | translate }}</h1>
          <p class="page-subtitle">{{ 'admin.pageSubtitle' | translate }}</p>
        </div>
      </div>

        @if (section() === 'users') {
          <div class="tab-content">
            <div class="toolbar">
              <div class="search-wrap">
                <mat-icon class="search-icon">search</mat-icon>
                <input class="search-input" [formControl]="searchControl"
                       [placeholder]="'admin.users.searchPlaceholder' | translate"
                       [attr.aria-label]="'admin.users.searchLabel' | translate" autocomplete="off" />
                @if (searchControl.value) {
                  <button class="clear-btn" (click)="searchControl.setValue('')">
                    <mat-icon>close</mat-icon>
                  </button>
                }
              </div>
              <button mat-flat-button color="primary" (click)="openCreate()">
                <mat-icon>person_add</mat-icon> {{ 'admin.users.addUser' | translate }}
              </button>
            </div>

            <div class="table-container mat-elevation-z1">
              @if (loading()) {
                <div class="table-loading"><mat-spinner diameter="40"></mat-spinner></div>
              }
              <table mat-table [dataSource]="users()">
                <ng-container matColumnDef="user">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columnUser' | translate }}</th>
                  <td mat-cell *matCellDef="let u">
                    <div class="user-cell">
                      <div class="user-avatar">{{ initials(u) }}</div>
                      <div>
                        <span class="user-name">{{ u.fullName }}</span>
                        <span class="user-email">{{ u.email }}</span>
                      </div>
                    </div>
                  </td>
                </ng-container>

                <ng-container matColumnDef="roles">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columnRoles' | translate }}</th>
                  <td mat-cell *matCellDef="let u">
                    <div class="roles-chips">
                      @for (role of u.roles; track role) {
                        <span class="role-chip">{{ role }}</span>
                      }
                    </div>
                  </td>
                </ng-container>

                <ng-container matColumnDef="company">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columnCompany' | translate }}</th>
                  <td mat-cell *matCellDef="let u">{{ u.company || '—' }}</td>
                </ng-container>

                <ng-container matColumnDef="department">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columnDepartment' | translate }}</th>
                  <td mat-cell *matCellDef="let u">{{ u.department || '—' }}</td>
                </ng-container>

                <ng-container matColumnDef="status">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columnStatus' | translate }}</th>
                  <td mat-cell *matCellDef="let u">
                    <span [class]="u.isActive ? 'status-active' : 'status-inactive'">
                      <span class="status-dot"></span>{{ (u.isActive ? 'admin.users.active' : 'admin.users.inactive') | translate }}
                    </span>
                  </td>
                </ng-container>

                <ng-container matColumnDef="joined">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columnJoined' | translate }}</th>
                  <td mat-cell *matCellDef="let u">{{ u.createdAt | date:'MMM d, y' }}</td>
                </ng-container>

                <ng-container matColumnDef="actions">
                  <th mat-header-cell *matHeaderCellDef></th>
                  <td mat-cell *matCellDef="let u">
                    <button mat-icon-button [matMenuTriggerFor]="menu">
                      <mat-icon>more_vert</mat-icon>
                    </button>
                    <mat-menu #menu>
                      <button mat-menu-item (click)="openEdit(u)">
                        <mat-icon>edit</mat-icon><span>{{ 'common.edit' | translate }}</span>
                      </button>
                      <button mat-menu-item (click)="resetPassword(u)">
                        <mat-icon>lock_reset</mat-icon><span>{{ 'admin.users.resetPassword' | translate }}</span>
                      </button>
                      <button mat-menu-item (click)="toggleActive(u)">
                        <mat-icon>{{ u.isActive ? 'block' : 'check_circle' }}</mat-icon>
                        <span>{{ (u.isActive ? 'admin.users.deactivate' : 'admin.users.activate') | translate }}</span>
                      </button>
                      <mat-divider></mat-divider>
                      <button mat-menu-item class="danger-item" (click)="deleteUser(u)"
                              [disabled]="u.id === currentUserId()">
                        <mat-icon>delete</mat-icon><span>{{ 'common.delete' | translate }}</span>
                      </button>
                    </mat-menu>
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="columns"></tr>
                <tr mat-row *matRowDef="let row; columns: columns;"></tr>
                <tr class="mat-row" *matNoDataRow>
                  <td class="mat-cell no-data" [colSpan]="columns.length">
                    @if (!loading()) { <div class="empty-state"><mat-icon>people</mat-icon><p>{{ 'admin.users.noUsersFound' | translate }}</p></div> }
                  </td>
                </tr>
              </table>

              <mat-paginator [length]="totalCount()" [pageSize]="pageSize" [pageSizeOptions]="[10, 20, 50]"
                             (page)="onPage($event)" showFirstLastButtons></mat-paginator>
            </div>
          </div>
        }

        @if (section() === 'types') {
          <div class="tab-content">
            <sf-request-types></sf-request-types>
          </div>
        }

        @if (section() === 'settings') {
          <div class="tab-content coming-soon">
            <mat-icon>settings</mat-icon>
            <h3>{{ 'admin.systemSettings.title' | translate }}</h3>
            <p>{{ 'admin.systemSettings.description' | translate }}</p>
          </div>
        }
    </div>
  `,
  styleUrl: './admin.component.scss'
})
export class AdminComponent implements OnInit {
  private readonly userService = inject(UserService);
  private readonly confirmService = inject(ConfirmService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);

  /** Which section of the page the menu opened: `/admin/users | types | settings`. */
  readonly section = signal<'users' | 'types' | 'settings'>('users');
  readonly sectionTitleKey = () => ({
    users: 'admin.tabs.users',
    types: 'admin.tabs.requestTypes',
    settings: 'admin.tabs.systemSettings'
  }[this.section()]);

  readonly columns = ['user', 'roles', 'company', 'department', 'status', 'joined', 'actions'];
  readonly loading = signal(true);
  readonly users = signal<UserDto[]>([]);
  readonly totalCount = signal(0);
  readonly roles = signal<string[]>([]);

  readonly searchControl = new FormControl('');
  pageSize = 20;
  private pageNumber = 1;

  readonly currentUserId = () => this.auth.user()?.id ?? '';

  initials(u: UserDto): string {
    return u.fullName.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);
  }

  ngOnInit(): void {
    this.route.data.subscribe(data => {
      const tab = data['tab'];
      this.section.set(tab === 'types' || tab === 'settings' ? tab : 'users');
    });

    this.load();
    this.userService.getRoles().subscribe({
      next: r => this.roles.set(r),
      error: () => this.roles.set(['Administrator', 'Manager', 'Employee', 'ExternalSigner'])
    });
    this.searchControl.valueChanges.pipe(debounceTime(300), distinctUntilChanged()).subscribe(() => {
      this.pageNumber = 1;
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.userService.getUsers({
      pageNumber: this.pageNumber,
      pageSize: this.pageSize,
      search: this.searchControl.value ?? ''
    }).subscribe({
      next: r => { this.users.set(r.items); this.totalCount.set(r.totalCount); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  onPage(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.load();
  }

  openCreate(): void {
    const ref = this.dialog.open(UserDialogComponent, {
      width: '540px',
      data: { user: null, roles: this.roles() }
    });
    ref.afterClosed().subscribe(saved => {
      if (saved) { this.snackBar.open(this.translate.instant('admin.users.userCreated'), this.translate.instant('common.close'), { duration: 3000 }); this.load(); }
    });
  }

  openEdit(user: UserDto): void {
    const ref = this.dialog.open(UserDialogComponent, {
      width: '540px',
      data: { user, roles: this.roles() }
    });
    ref.afterClosed().subscribe(saved => {
      if (saved) { this.snackBar.open(this.translate.instant('admin.users.userUpdated'), this.translate.instant('common.close'), { duration: 3000 }); this.load(); }
    });
  }

  toggleActive(user: UserDto): void {
    const [firstName, ...rest] = user.fullName.split(' ');
    this.userService.updateUser(user.id, {
      firstName,
      lastName: rest.join(' '),
      company: user.company,
      department: user.department,
      title: user.title,
      isActive: !user.isActive,
      roles: user.roles
    }).subscribe({
      next: () => {
        this.snackBar.open(this.translate.instant(user.isActive ? 'admin.users.userDeactivated' : 'admin.users.userActivated'), this.translate.instant('common.close'), { duration: 3000 });
        this.load();
      },
      error: () => this.snackBar.open(this.translate.instant('admin.users.failedToUpdateUser'), this.translate.instant('common.close'), { duration: 4000 })
    });
  }

  resetPassword(user: UserDto): void {
    this.confirmService.prompt({
      message: this.translate.instant('admin.users.resetPasswordPrompt', { name: user.fullName }),
      inputType: 'password'
    }).subscribe(pw => {
      if (!pw) return;
      this.userService.resetUserPassword(user.id, pw).subscribe({
        next: () => this.snackBar.open(this.translate.instant('admin.users.passwordResetSuccess'), this.translate.instant('common.close'), { duration: 3000 }),
        error: err => this.snackBar.open(this.errorText(err, this.translate.instant('admin.users.failedToResetPassword')), this.translate.instant('common.close'), { duration: 5000 })
      });
    });
  }

  deleteUser(user: UserDto): void {
    this.confirmService.confirm({
      message: this.translate.instant('admin.users.confirmDelete', { name: user.fullName }),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true
    }).subscribe(ok => {
      if (!ok) return;
      this.userService.deleteUser(user.id).subscribe({
        next: () => { this.snackBar.open(this.translate.instant('admin.users.userDeleted'), this.translate.instant('common.close'), { duration: 3000 }); this.load(); },
        error: err => this.snackBar.open(this.errorText(err, this.translate.instant('admin.users.failedToDeleteUser')), this.translate.instant('common.close'), { duration: 5000 })
      });
    });
  }

  private errorText(err: { error?: { detail?: string; errors?: Record<string, string[]> } }, fallback: string): string {
    if (err.error?.errors) return Object.values(err.error.errors).flat().join(' ');
    return err.error?.detail ?? fallback;
  }
}
