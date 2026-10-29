import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { DashboardData, RecentActivity, SlaMetrics } from '../../core/models';
import { formatSla } from '../../core/utils/sla';
import { CreateDocumentDialogComponent } from '../../shared/create-document-dialog/create-document-dialog.component';

interface StatCard {
  label: string;
  value: number;
  icon: string;
  color: string;
  bgColor: string;
  route: string;
  queryParam?: Record<string, string>;
}

@Component({
  selector: 'sf-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatDialogModule,
    TranslatePipe,
  ],
  template: `
    <div class="dashboard">
      <!-- ── Header ── -->
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'dashboard.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'dashboard.subtitle' | translate }}</p>
        </div>
        <button mat-flat-button color="primary" (click)="openCreateDialog()">
          <mat-icon>add</mat-icon> {{ 'createDocument.newDocument' | translate }}
        </button>
      </div>

      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (data()) {
        <!-- ── Stat cards ── -->
        <div class="stats-grid">
          @for (card of statCards(); track card.label) {
            <a [routerLink]="card.route" [queryParams]="card.queryParam" class="stat-card">
              <div
                class="stat-icon-wrap"
                [style.background]="card.bgColor"
                [style.color]="card.color"
              >
                <mat-icon>{{ card.icon }}</mat-icon>
              </div>
              <div class="stat-body">
                <span class="stat-num">{{ card.value }}</span>
                <span class="stat-lbl">{{ card.label }}</span>
              </div>
              <mat-icon class="stat-arrow">chevron_right</mat-icon>
            </a>
          }
        </div>

        <!-- ── Main content grid ── -->
        <div class="content-grid">
          <!-- Left: Activity + SLA -->
          <div class="left-col">
            <!-- Recent Activity -->
            <div class="panel">
              <div class="panel-header">
                <span class="panel-title">{{ 'dashboard.activity.title' | translate }}</span>
                <a class="panel-link" routerLink="/contracts">{{
                  'dashboard.activity.viewAll' | translate
                }}</a>
              </div>

              @if (data()!.recentActivity.length === 0) {
                <div class="empty-state">
                  <div class="empty-icon-wrap"><mat-icon>history</mat-icon></div>
                  <p class="empty-title">{{ 'dashboard.activity.emptyTitle' | translate }}</p>
                  <p class="empty-sub">{{ 'dashboard.activity.emptySub' | translate }}</p>
                </div>
              } @else {
                @for (item of data()!.recentActivity; track item.documentId; let last = $last) {
                  <div class="activity-row" [class.no-border]="last">
                    <span class="act-dot dot-{{ dotClass(item.action) }}"></span>
                    <div class="act-body">
                      <a [routerLink]="['/contracts', item.documentId]" class="act-title">{{
                        item.documentTitle
                      }}</a>
                      <span class="act-action">{{ actionKey(item.action) | translate }}</span>
                    </div>
                    <span class="act-time">{{ item.timestamp | date: 'MMM d, h:mm a' }}</span>
                  </div>
                }
              }
            </div>

            <!-- SLA Overview -->
            @if (sla(); as m) {
              <div class="panel" style="margin-top: 16px">
                <div class="panel-header">
                  <span class="panel-title">{{ 'dashboard.sla.title' | translate }}</span>
                </div>

                <div class="sla-row">
                  <div class="sla-block">
                    <span class="sla-num sla-warning">{{ m.nearingExpiration }}</span>
                    <span class="sla-label">
                      <mat-icon>timelapse</mat-icon> {{ 'dashboard.sla.nearingExpiry' | translate }}
                    </span>
                  </div>
                  <div class="sla-divider"></div>
                  <div class="sla-block">
                    <span class="sla-num sla-danger">{{ m.expired }}</span>
                    <span class="sla-label">
                      <mat-icon>event_busy</mat-icon> {{ 'dashboard.sla.expired' | translate }}
                    </span>
                  </div>
                  <div class="sla-divider"></div>
                  <div class="sla-block">
                    <span
                      class="sla-num"
                      [class.sla-ok]="m.slaCompliancePercent >= 80"
                      [class.sla-danger]="m.slaCompliancePercent < 50"
                    >
                      {{ m.slaCompliancePercent }}%
                    </span>
                    <span class="sla-label">
                      <mat-icon>verified</mat-icon> {{ 'dashboard.sla.withinSla' | translate }}
                    </span>
                    <span class="sla-sub">{{
                      'dashboard.sla.completedRatio'
                        | translate: { completed: m.completedWithinSla, total: m.completedTotal }
                    }}</span>
                  </div>
                </div>

                @if (m.averageSigningTimeByType.length > 0) {
                  <div class="avg-section">
                    <p class="avg-title">{{ 'dashboard.sla.avgSigningTime' | translate }}</p>
                    @for (a of m.averageSigningTimeByType; track a.requestType) {
                      <div class="avg-row">
                        <span class="avg-name" [matTooltip]="a.requestType">{{
                          a.requestType
                        }}</span>
                        <div class="avg-track">
                          <div class="avg-fill" [style.width.%]="avgBarWidth(a.averageHours)"></div>
                        </div>
                        <span class="avg-val">{{ formatHours(a.averageHours) }}</span>
                        <span class="avg-count">×{{ a.count }}</span>
                      </div>
                    }
                  </div>
                }
              </div>
            }
          </div>

          <!-- Right: Pending action -->
          <div class="right-col">
            <div class="panel">
              <div class="panel-header">
                <span class="panel-title">{{ 'dashboard.pending.title' | translate }}</span>
                @if (data()!.pendingSignatures > 0) {
                  <span class="badge-red">{{ data()!.pendingSignatures }}</span>
                }
              </div>

              @if (data()!.pendingSignatures === 0) {
                <div class="empty-state">
                  <div class="empty-icon-wrap ok"><mat-icon>check_circle</mat-icon></div>
                  <p class="empty-title">{{ 'dashboard.pending.emptyTitle' | translate }}</p>
                  <p class="empty-sub">{{ 'dashboard.pending.emptySub' | translate }}</p>
                </div>
              } @else {
                <div class="pending-body">
                  <div class="pending-num">{{ data()!.pendingSignatures }}</div>
                  <p class="pending-sub">
                    {{
                      (data()!.pendingSignatures > 1
                        ? 'dashboard.pending.waitingPlural'
                        : 'dashboard.pending.waitingSingular'
                      ) | translate: { count: data()!.pendingSignatures }
                    }}
                  </p>
                  <a mat-flat-button color="primary" routerLink="/contracts" class="pending-cta">
                    {{ 'dashboard.pending.viewRequests' | translate }} <mat-icon>arrow_forward</mat-icon>
                  </a>
                </div>
              }
            </div>

            <!-- Quick actions -->
            <div class="panel" style="margin-top: 16px">
              <div class="panel-header">
                <span class="panel-title">{{ 'dashboard.quickActions.title' | translate }}</span>
              </div>
              <div class="quick-actions">
                <button type="button" class="quick-btn" (click)="openCreateDialog()">
                  <div class="quick-icon" style="background:#e8f0fe;color:#1a73e8">
                    <mat-icon>add</mat-icon>
                  </div>
                  <span>{{ 'createDocument.newDocument' | translate }}</span>
                </button>
                <a class="quick-btn" routerLink="/contracts">
                  <div class="quick-icon" style="background:#fce8e6;color:#ea4335">
                    <mat-icon>description</mat-icon>
                  </div>
                  <span>{{ 'dashboard.quickActions.documents' | translate }}</span>
                </a>
                <a class="quick-btn" routerLink="/templates">
                  <div class="quick-icon" style="background:#e6f4ea;color:#34a853">
                    <mat-icon>article</mat-icon>
                  </div>
                  <span>{{ 'dashboard.quickActions.templates' | translate }}</span>
                </a>
                <a class="quick-btn" routerLink="/insights/audit">
                  <div class="quick-icon" style="background:#fef7e0;color:#f29900">
                    <mat-icon>history</mat-icon>
                  </div>
                  <span>{{ 'dashboard.quickActions.audit' | translate }}</span>
                </a>
              </div>
            </div>
          </div>
        </div>
      } @else if (error()) {
        <div class="error-state">
          <div class="empty-icon-wrap err"><mat-icon>error_outline</mat-icon></div>
          <p class="empty-title">{{ 'dashboard.errorTitle' | translate }}</p>
          <button mat-stroked-button (click)="load()">
            <mat-icon>refresh</mat-icon> {{ 'common.retry' | translate }}
          </button>
        </div>
      }
    </div>
  `,
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly documentService = inject(DocumentService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly translate = inject(TranslateService);
  private readonly dialog = inject(MatDialog);

  readonly loading = signal(true);
  readonly data = signal<DashboardData | null>(null);
  readonly sla = signal<SlaMetrics | null>(null);
  readonly error = signal<string | null>(null);

  openCreateDialog(): void {
    this.dialog.open(CreateDocumentDialogComponent, { width: '720px', maxWidth: '92vw' });
  }

  formatHours(hours: number): string {
    return formatSla(Math.round(hours));
  }

  avgBarWidth(hours: number): number {
    const max = Math.max(
      ...(this.sla()?.averageSigningTimeByType.map((a) => a.averageHours) ?? [1]),
      1,
    );
    return Math.max(6, Math.round((hours / max) * 100));
  }

  readonly statCards = () => {
    const d = this.data();
    if (!d) return [];
    return [
      {
        label: this.translate.instant('dashboard.stats.totalDocuments'),
        value: d.totalDocuments,
        icon: 'description',
        color: '#1a73e8',
        bgColor: '#e8f0fe',
        route: '/contracts',
      },
      {
        label: this.translate.instant('dashboard.stats.completed'),
        value: d.completed,
        icon: 'check_circle',
        color: '#22c55e',
        bgColor: '#f0fdf4',
        route: '/contracts',
        queryParam: { status: 'Completed' },
      },
      {
        label: this.translate.instant('dashboard.stats.pending'),
        value: d.pendingSignatures,
        icon: 'schedule',
        color: '#f59e0b',
        bgColor: '#fffbeb',
        route: '/contracts',
        queryParam: { status: 'Sent' },
      },
      // Attention state — only surfaces while a revision is being waited on.
      ...(d.changesRequested > 0 ? [{
        label: this.translate.instant('dashboard.stats.changesRequested'),
        value: d.changesRequested,
        icon: 'published_with_changes',
        color: '#d97706',
        bgColor: '#fffbeb',
        route: '/contracts',
        queryParam: { status: 'ChangesRequested' },
      }] : []),
      // Lifecycle state — only surfaces while one of the user's contracts is running out.
      ...(d.expiringSoon > 0 ? [{
        label: this.translate.instant('dashboard.stats.expiringSoon'),
        value: d.expiringSoon,
        icon: 'update',
        color: '#b45309',
        bgColor: '#fffbeb',
        route: '/contracts',
        queryParam: { status: 'Completed' },
      }] : []),
      {
        label: this.translate.instant('dashboard.stats.drafts'),
        value: d.drafts,
        icon: 'edit_note',
        color: '#5f6368',
        bgColor: '#f1f3f4',
        route: '/contracts',
        queryParam: { status: 'Draft' },
      },
    ] satisfies StatCard[];
  };

  dotClass(action: string): string {
    const a = action.toLowerCase();
    if (a.includes('complet') || a.includes('sign')) return 'completed';
    if (a.includes('reject')) return 'rejected';
    if (a.includes('sent')) return 'sent';
    if (a.includes('expire')) return 'expired';
    return 'draft';
  }

  private static readonly ACTION_KEYS: Record<string, string> = {
    DocumentCreated: 'dashboard.activity.actions.documentCreated',
    DocumentViewed: 'dashboard.activity.actions.documentViewed',
    DocumentSent: 'dashboard.activity.actions.documentSent',
    DocumentSigned: 'dashboard.activity.actions.documentSigned',
    DocumentRejected: 'dashboard.activity.actions.documentRejected',
    DocumentCompleted: 'dashboard.activity.actions.documentCompleted',
    DocumentExpired: 'dashboard.activity.actions.documentExpired',
    DocumentCancelled: 'dashboard.activity.actions.documentCancelled',
    DocumentDownloaded: 'dashboard.activity.actions.documentDownloaded',
    FieldSigned: 'dashboard.activity.actions.fieldSigned',
    FieldCleared: 'dashboard.activity.actions.fieldCleared',
    RecipientAdded: 'dashboard.activity.actions.recipientAdded',
    RecipientRemoved: 'dashboard.activity.actions.recipientRemoved',
    WorkflowStarted: 'dashboard.activity.actions.workflowStarted',
    WorkflowCompleted: 'dashboard.activity.actions.workflowCompleted',
    WorkflowStepCompleted: 'dashboard.activity.actions.workflowStepCompleted',
    WorkflowStepRejected: 'dashboard.activity.actions.workflowStepRejected',
    EmailSent: 'dashboard.activity.actions.emailSent',
    ReminderSent: 'dashboard.activity.actions.reminderSent',
    DelegationAdded: 'dashboard.activity.actions.delegationAdded',
    EscalationTriggered: 'dashboard.activity.actions.escalationTriggered',
    CommentAdded: 'dashboard.activity.actions.commentAdded',
    AttachmentUploaded: 'dashboard.activity.actions.attachmentUploaded',
    AuthenticationEvent: 'dashboard.activity.actions.authenticationEvent',
    ChangesRequested: 'dashboard.activity.actions.changesRequested',
    RevisionUploaded: 'dashboard.activity.actions.revisionUploaded',
    SignaturesVoided: 'dashboard.activity.actions.signaturesVoided',
  };

  actionKey(action: string): string {
    return DashboardComponent.ACTION_KEYS[action] ?? action;
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.documentService.getDashboard().subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(this.translate.instant('dashboard.errorTitle'));
        this.loading.set(false);
      },
    });
    this.requestTypeService.getSlaMetrics().subscribe({
      next: (m) => this.sla.set(m),
      error: () => this.sla.set(null),
    });
  }
}
