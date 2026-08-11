import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ReportService } from '../../core/services/report.service';
import { OrgAuditLogEntry } from '../../core/models';

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
  selector: 'sf-org-audit',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
    MatTableModule, MatPaginatorModule,
    MatIconModule, MatProgressSpinnerModule, MatTooltipModule,
    TranslatePipe
  ],
  template: `
    <div class="audit-page">
      <div class="page-header">
        <div class="header-titles">
          <h1 class="page-title">{{ 'orgAudit.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'orgAudit.subtitle' | translate }}</p>
        </div>
      </div>

      <div class="toolbar">
        <div class="search-wrap">
          <mat-icon class="search-icon">search</mat-icon>
          <input
            class="search-input"
            [formControl]="searchControl"
            [placeholder]="'orgAudit.searchPlaceholder' | translate"
            autocomplete="off" />
          @if (searchControl.value) {
            <button class="clear-btn" (click)="searchControl.setValue('')" [matTooltip]="'orgAudit.clearSearch' | translate">
              <mat-icon>close</mat-icon>
            </button>
          }
        </div>

        <select class="action-select" [formControl]="actionControl">
          <option value="">{{ 'orgAudit.allActions' | translate }}</option>
          @for (a of actionOptions; track a) {
            <option [value]="a">{{ actionLabel(a) }}</option>
          }
        </select>
      </div>

      <div class="table-container mat-elevation-z1">
        @if (loading()) {
          <div class="table-loading"><mat-spinner diameter="40"></mat-spinner></div>
        }

        <table mat-table [dataSource]="entries()">
          <ng-container matColumnDef="action">
            <th mat-header-cell *matHeaderCellDef>{{ 'orgAudit.columns.action' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <div class="action-cell">
                <div class="action-icon-wrap">
                  <mat-icon>{{ actionIcon(e.action) }}</mat-icon>
                </div>
                <div>
                  <span class="action-name">{{ actionLabel(e.action) }}</span>
                  @if (e.description) { <span class="action-desc">{{ e.description }}</span> }
                </div>
              </div>
            </td>
          </ng-container>

          <ng-container matColumnDef="document">
            <th mat-header-cell *matHeaderCellDef>{{ 'orgAudit.columns.document' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <a [routerLink]="['/documents', e.documentId]" class="doc-link">{{ e.documentTitle }}</a>
            </td>
          </ng-container>

          <ng-container matColumnDef="actor">
            <th mat-header-cell *matHeaderCellDef>{{ 'orgAudit.columns.actor' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <div class="user-cell">
                <div class="user-avatar">{{ initials(e.actorName) }}</div>
                <div>
                  <span class="user-name">{{ e.actorName || '—' }}</span>
                  @if (e.actorEmail) { <span class="user-email">{{ e.actorEmail }}</span> }
                </div>
              </div>
            </td>
          </ng-container>

          <ng-container matColumnDef="ip">
            <th mat-header-cell *matHeaderCellDef>{{ 'orgAudit.columns.ip' | translate }}</th>
            <td mat-cell *matCellDef="let e">
              <span class="ip-badge">{{ e.ipAddress || '—' }}</span>
            </td>
          </ng-container>

          <ng-container matColumnDef="timestamp">
            <th mat-header-cell *matHeaderCellDef>{{ 'orgAudit.columns.timestamp' | translate }}</th>
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
                  <p>{{ 'orgAudit.noEvents' | translate }}</p>
                </div>
              }
            </td>
          </tr>
        </table>

        <mat-paginator [length]="totalCount()" [pageSize]="pageSize" [pageSizeOptions]="[25, 50, 100]" (page)="onPage($event)" showFirstLastButtons></mat-paginator>
      </div>
    </div>
  `,
  styleUrl: './org-audit.component.scss'
})
export class OrgAuditComponent implements OnInit {
  private readonly reportService = inject(ReportService);
  private readonly translate = inject(TranslateService);

  readonly columns = ['action', 'document', 'actor', 'ip', 'timestamp'];
  readonly actionOptions = Object.keys(ACTION_ICONS);
  readonly loading = signal(true);
  readonly entries = signal<OrgAuditLogEntry[]>([]);
  readonly totalCount = signal(0);

  readonly searchControl = new FormControl('');
  readonly actionControl = new FormControl('');

  pageSize = 50;
  private pageNumber = 1;

  actionIcon(action: string): string { return ACTION_ICONS[action] ?? 'info'; }
  actionLabel(action: string): string {
    const translated = this.translate.instant(`orgAudit.actions.${action}`);
    return translated === `orgAudit.actions.${action}` ? action : translated;
  }
  initials(name?: string): string {
    if (!name) return '?';
    return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);
  }

  ngOnInit(): void {
    this.load();

    this.searchControl.valueChanges.pipe(debounceTime(300), distinctUntilChanged()).subscribe(() => {
      this.pageNumber = 1;
      this.load();
    });
    this.actionControl.valueChanges.subscribe(() => {
      this.pageNumber = 1;
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.reportService.getAuditLog({
      pageNumber: this.pageNumber,
      pageSize: this.pageSize,
      search: this.searchControl.value ?? '',
      action: this.actionControl.value || undefined
    }).subscribe({
      next: result => {
        this.entries.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  onPage(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.load();
  }
}
