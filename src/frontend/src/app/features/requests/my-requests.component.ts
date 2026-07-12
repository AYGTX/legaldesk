import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { MyRequests, SentRequest, ReceivedRequest } from '../../core/models';
import { remainingTime, RemainingTime } from '../../core/utils/sla';

const STATUS_COLORS: Record<string, string> = {
  Draft: '#5f6368', Pending: '#f59e0b', InProgress: '#1a73e8', Sent: '#1a73e8',
  Completed: '#34a853', Rejected: '#ea4335', Expired: '#f29900',
  Cancelled: '#80868b', WaitingForOthers: '#8430ce', Skipped: '#80868b',
  Delegated: '#8430ce', Escalated: '#f29900', ChangesRequested: '#d97706'
};

const STATUS_LABEL_KEYS: Record<string, string> = {
  Draft: 'draft', Pending: 'pending', InProgress: 'inProgress', Sent: 'sent',
  Completed: 'completed', Rejected: 'rejected', Expired: 'expired',
  Cancelled: 'cancelled', WaitingForOthers: 'waitingForOthers', Skipped: 'skipped',
  Delegated: 'delegated', Escalated: 'escalated', ChangesRequested: 'changesRequested'
};

@Component({
  selector: 'sf-my-requests',
  standalone: true,
  imports: [
    CommonModule, RouterLink,
    MatTabsModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatTooltipModule, TranslatePipe
  ],
  template: `
    <div class="requests-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'requests.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'requests.subtitle' | translate }}</p>
        </div>
        <div class="header-btns">
          <a mat-stroked-button routerLink="/requests/new" class="tpl-btn">
            <mat-icon>article</mat-icon> {{ 'requests.fromTemplate' | translate }}
          </a>
          <button mat-flat-button color="primary" routerLink="/documents/upload" class="upload-btn">
            <mat-icon>upload_file</mat-icon> {{ 'requests.newRequest' | translate }}
          </button>
        </div>
      </div>

      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="40"></mat-spinner></div>
      } @else {
        <mat-tab-group animationDuration="0ms">

          <!-- ── Action Required ── -->
          <mat-tab>
            <ng-template mat-tab-label>
              <mat-icon class="tab-icon">inbox</mat-icon>
              {{ 'requests.tabs.actionRequired' | translate }}
              @if (actionRequiredCount() > 0) {
                <span class="tab-badge">{{ actionRequiredCount() }}</span>
              }
            </ng-template>

            <div class="tab-body">
              @if (received().length === 0) {
                <div class="empty-state">
                  <div class="empty-icon-wrap"><mat-icon>mark_email_read</mat-icon></div>
                  <p class="empty-title">{{ 'requests.allCaughtUp' | translate }}</p>
                  <p class="empty-sub">{{ 'requests.noDocumentsWaiting' | translate }}</p>
                </div>
              } @else {
                @if (awaitingMe().length > 0) {
                  <div class="section-header">
                    <span class="section-label">{{ 'requests.needsSignature' | translate }}</span>
                    <span class="count-chip urgent">{{ awaitingMe().length }}</span>
                  </div>
                  <div class="list-surface">
                    @for (r of awaitingMe(); track r.documentId; let last = $last) {
                      <div class="list-row action-row" [class.no-border]="last">
                        <div class="row-icon-wrap action-icon">
                          <mat-icon>edit_document</mat-icon>
                        </div>
                        <div class="row-body">
                          <span class="row-title">{{ r.title }}</span>
                          <span class="row-meta">
                            {{ 'requests.from' | translate }} <strong class="meta-strong">{{ r.ownerName }}</strong>
                            @if (r.receivedAt) { &nbsp;·&nbsp; {{ r.receivedAt | date:'MMM d, y' }} }
                          </span>
                        </div>
                        <div class="row-end">
                          @if (remaining(r.expiresAt); as rem) {
                            <span class="sla-chip sla-{{ rem.level }}"
                                  [matTooltip]="r.expiresAt ? ('requests.deadline' | translate: { date: (r.expiresAt | date:'medium') }) : ''"
                                  matTooltipPosition="left">
                              <mat-icon>{{ rem.expired ? 'error_outline' : 'schedule' }}</mat-icon>
                              {{ rem.text }}
                            </span>
                          }
                          <button mat-flat-button class="sign-btn" (click)="openSigning(r)">
                            <mat-icon>draw</mat-icon> {{ 'requests.signNow' | translate }}
                          </button>
                        </div>
                      </div>
                    }
                  </div>
                }

                @if (otherReceived().length > 0) {
                  <div class="section-header" [style.margin-top]="awaitingMe().length ? '24px' : '0'">
                    <span class="section-label">{{ 'requests.history' | translate }}</span>
                    <span class="count-chip">{{ otherReceived().length }}</span>
                  </div>
                  <div class="list-surface">
                    @for (r of otherReceived(); track r.documentId; let last = $last) {
                      <div class="list-row" [class.no-border]="last">
                        <div class="row-icon-wrap" [style.background]="statusBg(r.myStatus)" [style.color]="statusColor(r.myStatus)">
                          <mat-icon>{{ statusIcon(r.myStatus) }}</mat-icon>
                        </div>
                        <div class="row-body">
                          <span class="row-title faded">{{ r.title }}</span>
                          <span class="row-meta">
                            {{ 'requests.from' | translate }} {{ r.ownerName }}
                            @if (r.signedAt) { &nbsp;·&nbsp; {{ 'requests.signed' | translate }} {{ r.signedAt | date:'MMM d, y' }} }
                          </span>
                        </div>
                        <div class="row-end">
                          <span class="status-chip" [style.background]="statusBg(r.myStatus)" [style.color]="statusColor(r.myStatus)">
                            {{ statusLabel(r.myStatus) }}
                          </span>
                          <button mat-icon-button [matTooltip]="'requests.openDocument' | translate" matTooltipPosition="left" (click)="openSigning(r)">
                            <mat-icon>open_in_new</mat-icon>
                          </button>
                        </div>
                      </div>
                    }
                  </div>
                }
              }
            </div>
          </mat-tab>

          <!-- ── Sent by Me ── -->
          <mat-tab>
            <ng-template mat-tab-label>
              <mat-icon class="tab-icon">send</mat-icon>
              {{ 'requests.tabs.sentByMe' | translate }}
              @if (sent().length > 0) {
                <span class="tab-badge muted">{{ sent().length }}</span>
              }
            </ng-template>

            <div class="tab-body">
              @if (sent().length === 0) {
                <div class="empty-state">
                  <div class="empty-icon-wrap"><mat-icon>send</mat-icon></div>
                  <p class="empty-title">{{ 'requests.nothingSentYet' | translate }}</p>
                  <p class="empty-sub">{{ 'requests.uploadDocumentSub' | translate }}</p>
                  <a mat-stroked-button routerLink="/documents/upload">{{ 'requests.uploadDocument' | translate }}</a>
                </div>
              } @else {
                <div class="list-surface">
                  @for (s of sent(); track s.documentId; let last = $last) {
                    <div class="sent-item" [class.no-border]="last && expandedId() !== s.documentId">

                      <!-- Clickable summary row -->
                      <div class="sent-row" (click)="toggleExpand(s.documentId)">
                        <div class="sent-pdf-badge">
                          <mat-icon>picture_as_pdf</mat-icon>
                        </div>
                        <div class="row-body">
                          <div class="sent-title-line">
                            <a [routerLink]="['/documents', s.documentId]" class="row-title"
                               (click)="$event.stopPropagation()">{{ s.title }}</a>
                            <span class="status-chip"
                                  [style.background]="statusBg(s.status)"
                                  [style.color]="statusColor(s.status)">
                              {{ statusLabel(s.status) }}
                            </span>
                          </div>
                          <div class="sent-meta-line">
                            <span class="row-meta">
                              {{ (s.sentAt ?? s.createdAt) | date:'MMM d, y' }}
                              &nbsp;·&nbsp; {{ (s.totalRecipients !== 1 ? 'requests.recipientsCountPlural' : 'requests.recipientsCount') | translate: { count: s.totalRecipients } }}
                              @if (s.workflowType) { &nbsp;·&nbsp; {{ s.workflowType }} }
                            </span>
                            @if (remainingForSent(s); as rem) {
                              <span class="sla-chip sla-{{ rem.level }}">
                                <mat-icon>{{ rem.expired ? 'error_outline' : 'schedule' }}</mat-icon>
                                {{ rem.text }}
                              </span>
                            }
                          </div>
                          <!-- Slim progress bar -->
                          <div class="progress-bar-wrap">
                            <div class="progress-bar-track">
                              <div class="progress-bar-fill"
                                   [style.width.%]="progressPercent(s)"
                                   [style.background]="s.status === 'Completed' ? '#34a853' : '#1a73e8'">
                              </div>
                            </div>
                            <span class="progress-label">
                              @if (s.currentRecipientName && s.status !== 'Completed') {
                                <mat-icon class="waiting-icon">hourglass_top</mat-icon>
                                {{ s.currentRecipientName }}
                              } @else {
                                {{ 'requests.completedRecipients' | translate: { completed: s.completedRecipients, total: s.totalRecipients } }}
                              }
                            </span>
                          </div>
                        </div>
                        <button mat-icon-button class="expand-btn"
                                [matTooltip]="(expandedId() === s.documentId ? 'requests.collapse' : 'requests.showRecipients') | translate"
                                matTooltipPosition="left"
                                (click)="$event.stopPropagation(); toggleExpand(s.documentId)">
                          <mat-icon>{{ expandedId() === s.documentId ? 'expand_less' : 'expand_more' }}</mat-icon>
                        </button>
                      </div>

                      <!-- Expanded: recipient list -->
                      @if (expandedId() === s.documentId) {
                        <div class="recipients-panel">
                          @for (r of s.recipients; track r.email; let i = $index; let last = $last) {
                            <div class="recip-row">
                              <div class="recip-col">
                                <div class="recip-node" [style.background]="statusColor(r.status)" [class.pulse]="r.isCurrent">
                                  <mat-icon>{{ statusIcon(r.status) }}</mat-icon>
                                </div>
                                @if (!last) { <div class="recip-line" [class.done]="r.status === 'Completed'"></div> }
                              </div>
                              <div class="recip-info">
                                <span class="recip-name">{{ r.name }}</span>
                                <span class="recip-email">{{ r.email }}</span>
                              </div>
                              <span class="recip-status" [style.color]="statusColor(r.status)">
                                @if (r.isCurrent) { {{ 'requests.signingNow' | translate }} }
                                @else { {{ statusLabel(r.status) }} }
                                @if (r.signedAt) { &nbsp;·&nbsp; {{ 'requests.signed' | translate }} {{ r.signedAt | date:'MMM d' }} }
                              </span>
                            </div>
                          }
                        </div>
                      }
                    </div>
                  }
                </div>
              }
            </div>
          </mat-tab>

        </mat-tab-group>
      }
    </div>
  `,
  styleUrl: './my-requests.component.scss'
})
export class MyRequestsComponent implements OnInit {
  private readonly documentService = inject(DocumentService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly sent = signal<SentRequest[]>([]);
  readonly received = signal<ReceivedRequest[]>([]);
  readonly expandedId = signal<string | null>(null);

  readonly awaitingMe = computed(() => this.received().filter(r => r.isMyTurn));
  readonly otherReceived = computed(() => this.received().filter(r => !r.isMyTurn));
  readonly actionRequiredCount = computed(() => this.awaitingMe().length);

  statusLabel(status: string): string {
    const key = STATUS_LABEL_KEYS[status];
    return key ? this.translate.instant(`requests.status.${key}`) : status;
  }
  statusColor(status: string): string { return STATUS_COLORS[status] ?? '#64748b'; }
  statusBg(status: string): string { return `${this.statusColor(status)}20`; }

  statusIcon(status: string): string {
    switch (status) {
      case 'Completed': return 'check_circle';
      case 'Rejected': return 'cancel';
      case 'InProgress': return 'hourglass_top';
      case 'Expired': return 'schedule';
      case 'Skipped': return 'skip_next';
      case 'Delegated': return 'forward';
      case 'ChangesRequested': return 'published_with_changes';
      default: return 'radio_button_unchecked';
    }
  }

  progressPercent(s: SentRequest): number {
    if (s.totalRecipients === 0) return 0;
    return Math.round((s.completedRecipients / s.totalRecipients) * 100);
  }

  remaining(expiresAt?: string): RemainingTime | null {
    return remainingTime(expiresAt);
  }

  remainingForSent(s: SentRequest): RemainingTime | null {
    // ChangesRequested pauses the flow (the SLA clock restarts on revision upload) — no countdown.
    if (s.status === 'Completed' || s.status === 'Rejected' || s.status === 'Cancelled' || s.status === 'ChangesRequested') return null;
    return remainingTime(s.expiresAt);
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.documentService.getMyRequests().subscribe({
      next: (data: MyRequests) => {
        this.sent.set(data.sent);
        this.received.set(data.received);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  toggleExpand(id: string): void {
    this.expandedId.update(cur => cur === id ? null : id);
  }

  openSigning(r: ReceivedRequest): void {
    const url = `${window.location.origin}/sign/${r.secureToken}`;
    window.open(url, '_blank');
  }
}
