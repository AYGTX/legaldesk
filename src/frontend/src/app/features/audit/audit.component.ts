import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';

interface AuditEntry {
  id: string;
  action: string;
  description: string;
  performedBy: string;
  performedByEmail: string;
  ipAddress: string;
  userAgent: string;
  timestamp: string;
}

const ACTION_ICONS: Record<string, string> = {
  DocumentCreated: 'add_circle', DocumentViewed: 'visibility', DocumentSent: 'send',
  DocumentSigned: 'draw', DocumentRejected: 'cancel', DocumentCompleted: 'check_circle',
  DocumentExpired: 'schedule', DocumentCancelled: 'block', DocumentDownloaded: 'download',
  FieldSigned: 'draw', FieldCleared: 'clear', RecipientAdded: 'person_add', RecipientRemoved: 'person_remove',
  WorkflowStarted: 'play_circle', WorkflowCompleted: 'task_alt', WorkflowStepCompleted: 'check_circle',
  WorkflowStepRejected: 'cancel', EmailSent: 'mail', ReminderSent: 'notifications_active',
  DelegationAdded: 'forward', EscalationTriggered: 'priority_high', CommentAdded: 'comment',
  AttachmentUploaded: 'attach_file', AuthenticationEvent: 'lock', ChangesRequested: 'published_with_changes',
  RevisionUploaded: 'upload_file', SignaturesVoided: 'block'
};

@Component({
  selector: 'sf-audit',
  standalone: true,
  imports: [
    CommonModule, RouterLink,
    MatTableModule, MatPaginatorModule, MatButtonModule,
    MatIconModule, MatProgressSpinnerModule, MatTooltipModule, MatChipsModule,
    MatSnackBarModule, TranslatePipe
  ],
  template: `
    <div class="audit-page">
      <div class="page-header">
        <button mat-icon-button [routerLink]="['/documents', documentId]"><mat-icon>arrow_back</mat-icon></button>
        <div class="header-titles">
          <h1 class="page-title">{{ 'audit.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'audit.subtitle' | translate }}</p>
        </div>
        <button mat-stroked-button (click)="exportCsv()" [disabled]="exporting()">
          @if (exporting()) { <mat-spinner diameter="18"></mat-spinner> } @else { <mat-icon>download</mat-icon> }
          {{ 'audit.exportCsv' | translate }}
        </button>
      </div>

      <div class="table-container mat-elevation-z1">
        @if (loading()) {
          <div class="table-loading"><mat-spinner diameter="40"></mat-spinner></div>
        }

        <table mat-table [dataSource]="entries()">
          <ng-container matColumnDef="action">
            <th mat-header-cell *matHeaderCellDef>{{ 'audit.columns.action' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <div class="action-cell">
                <div class="action-icon-wrap" [class]="'action-' + e.action.toLowerCase()">
                  <mat-icon>{{ actionIcon(e.action) }}</mat-icon>
                </div>
                <div>
                  <span class="action-name">{{ actionLabel(e.action) }}</span>
                  <span class="action-desc">{{ e.description }}</span>
                </div>
              </div>
            </td>
          </ng-container>

          <ng-container matColumnDef="user">
            <th mat-header-cell *matHeaderCellDef>{{ 'audit.columns.user' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <div class="user-cell">
                <div class="user-avatar">{{ e.performedBy ? initials(e.performedBy) : '⚙' }}</div>
                <div>
                  <span class="user-name">{{ e.performedBy || ('audit.system' | translate) }}</span>
                  <span class="user-email">{{ e.performedByEmail }}</span>
                </div>
              </div>
            </td>
          </ng-container>

          <ng-container matColumnDef="ip">
            <th mat-header-cell *matHeaderCellDef>{{ 'audit.columns.ip' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <span class="ip-badge">{{ e.ipAddress || '—' }}</span>
            </td>
          </ng-container>

          <ng-container matColumnDef="timestamp">
            <th mat-header-cell *matHeaderCellDef>{{ 'audit.columns.timestamp' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <span [matTooltip]="e.timestamp | date:'full'">
                {{ e.timestamp | date:'MMM d, y HH:mm:ss' }}
              </span>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns;"></tr>

          <tr class="mat-row" *matNoDataRow>
            <td class="mat-cell no-data" [colSpan]="columns.length">
              @if (!loading()) {
                <div class="empty-state">
                  <mat-icon>history</mat-icon>
                  <p>{{ 'audit.noEvents' | translate }}</p>
                </div>
              }
            </td>
          </tr>
        </table>

        <mat-paginator [length]="totalCount()" [pageSize]="50" (page)="onPage($event)" showFirstLastButtons></mat-paginator>
      </div>
    </div>
  `,
  styleUrl: './audit.component.scss'
})
export class AuditComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly documentService = inject(DocumentService);
  private readonly translate = inject(TranslateService);
  private readonly snack = inject(MatSnackBar);

  readonly columns = ['action', 'user', 'ip', 'timestamp'];
  readonly loading = signal(true);
  readonly exporting = signal(false);
  readonly entries = signal<AuditEntry[]>([]);
  readonly totalCount = signal(0);
  documentId = '';

  actionIcon(action: string): string { return ACTION_ICONS[action] ?? 'info'; }
  actionLabel(action: string): string {
    const translated = this.translate.instant(`audit.actions.${action}`);
    return translated === `audit.actions.${action}` ? action : translated;
  }
  initials(name?: string | null): string { return (name || '?').split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2); }

  ngOnInit(): void {
    this.documentId = this.route.snapshot.paramMap.get('id')!;
    this.load(1);
  }

  load(page: number): void {
    this.loading.set(true);
    this.documentService.getAuditTrail(this.documentId, page).subscribe({
      next: result => {
        this.entries.set((result.items as AuditEntry[]));
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  onPage(event: PageEvent): void { this.load(event.pageIndex + 1); }

  exportCsv(): void {
    this.exporting.set(true);
    this.documentService.exportAuditTrail(this.documentId).subscribe({
      next: blob => {
        this.exporting.set(false);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `audit-trail-${this.documentId}.csv`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.exporting.set(false);
        this.snack.open(this.translate.instant('audit.exportFailed'), 'OK', { duration: 3000 });
      }
    });
  }
}
