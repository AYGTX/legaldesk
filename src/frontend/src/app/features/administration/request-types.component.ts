import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { RequestTypeService } from '../../core/services/request-type.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { RequestType } from '../../core/models';
import { formatSla } from '../../core/utils/sla';
import { RequestTypeDialogComponent } from './request-type-dialog.component';

@Component({
  selector: 'sf-request-types',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatSlideToggleModule,
    MatMenuModule, MatProgressSpinnerModule, MatDialogModule, MatSnackBarModule, MatTooltipModule, TranslatePipe
  ],
  template: `
    <div class="rt-panel">
      <div class="rt-toolbar">
        <p class="intro">{{ 'admin.requestTypes.intro' | translate }}</p>
        <button mat-flat-button color="primary" (click)="openCreate()">
          <mat-icon>add</mat-icon> {{ 'admin.requestTypes.newRequestType' | translate }}
        </button>
      </div>

      @if (loading()) {
        <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (types().length === 0) {
        <div class="empty">
          <mat-icon>category</mat-icon>
          <p>{{ 'admin.requestTypes.noRequestTypesYet' | translate }}</p>
          <button mat-stroked-button (click)="openCreate()">{{ 'admin.requestTypes.createFirst' | translate }}</button>
        </div>
      } @else {
        <div class="rt-grid">
          @for (t of types(); track t.id) {
            <div class="rt-card" [class.inactive]="!t.isActive">
              <div class="rt-icon" [style.background]="(t.color || '#6366f1') + '20'" [style.color]="t.color || '#6366f1'">
                <mat-icon>{{ t.icon || 'description' }}</mat-icon>
              </div>
              <div class="rt-body">
                <div class="rt-title-row">
                  <span class="rt-name">{{ t.name }}</span>
                  @if (!t.isActive) { <span class="inactive-badge">{{ 'admin.requestTypes.inactive' | translate }}</span> }
                </div>
                @if (t.description) { <p class="rt-desc">{{ t.description }}</p> }
                <div class="rt-sla">
                  <mat-icon>schedule</mat-icon> {{ 'admin.requestTypes.slaLabel' | translate: { sla: formatSla(t.slaHours) } }}
                </div>
              </div>
              <div class="rt-actions">
                <mat-slide-toggle [checked]="t.isActive" (change)="toggle(t, $event.checked)"
                                  [matTooltip]="'admin.requestTypes.activeToggleTooltip' | translate"></mat-slide-toggle>
                <button mat-icon-button [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
                <mat-menu #menu>
                  <button mat-menu-item (click)="openEdit(t)"><mat-icon>edit</mat-icon><span>{{ 'common.edit' | translate }}</span></button>
                  <button mat-menu-item class="danger" (click)="remove(t)"><mat-icon>delete</mat-icon><span>{{ 'common.delete' | translate }}</span></button>
                </mat-menu>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './request-types.component.scss'
})
export class RequestTypesComponent implements OnInit {
  private readonly service = inject(RequestTypeService);
  private readonly confirmService = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly types = signal<RequestType[]>([]);
  readonly formatSla = formatSla;

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.service.getAll().subscribe({
      next: t => { this.types.set(t); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  openCreate(): void {
    const ref = this.dialog.open(RequestTypeDialogComponent, { width: '520px', data: { requestType: null } });
    ref.afterClosed().subscribe(saved => { if (saved) { this.snackBar.open(this.translate.instant('admin.requestTypes.requestTypeCreated'), this.translate.instant('common.close'), { duration: 3000 }); this.load(); } });
  }

  openEdit(t: RequestType): void {
    const ref = this.dialog.open(RequestTypeDialogComponent, { width: '520px', data: { requestType: t } });
    ref.afterClosed().subscribe(saved => { if (saved) { this.snackBar.open(this.translate.instant('admin.requestTypes.requestTypeUpdated'), this.translate.instant('common.close'), { duration: 3000 }); this.load(); } });
  }

  toggle(t: RequestType, isActive: boolean): void {
    this.service.setStatus(t.id, isActive).subscribe({
      next: () => {
        this.snackBar.open(this.translate.instant(isActive ? 'admin.requestTypes.activated' : 'admin.requestTypes.deactivated'), this.translate.instant('common.close'), { duration: 2500 });
        this.load();
      },
      error: () => { this.snackBar.open(this.translate.instant('admin.requestTypes.failedToUpdateStatus'), this.translate.instant('common.close'), { duration: 4000 }); this.load(); }
    });
  }

  remove(t: RequestType): void {
    this.confirmService.confirm({
      message: this.translate.instant('admin.requestTypes.confirmDelete', { name: t.name }),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true
    }).subscribe(ok => {
      if (!ok) return;
      this.service.delete(t.id).subscribe({
        next: () => { this.snackBar.open(this.translate.instant('admin.requestTypes.requestTypeDeleted'), this.translate.instant('common.close'), { duration: 3000 }); this.load(); },
        error: (err: { error?: { detail?: string } }) =>
          this.snackBar.open(err.error?.detail ?? this.translate.instant('admin.requestTypes.failedToDelete'), this.translate.instant('common.close'), { duration: 5000 })
      });
    });
  }
}
