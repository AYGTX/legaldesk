import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatStepperModule } from '@angular/material/stepper';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { HubService } from '../../core/services/hub.service';
import { AuthService } from '../../core/auth/auth.service';
import { DocumentDetail, WorkflowParticipant, AiSummaryDto } from '../../core/models';
import { WorkflowVisualizerComponent } from '../../shared/workflow-visualizer/workflow-visualizer.component';
import { CommentsPanelComponent } from '../../shared/comments-panel/comments-panel.component';
import { remainingTime, RemainingTime } from '../../core/utils/sla';

@Component({
  selector: 'sf-document-detail',
  standalone: true,
  imports: [
    CommonModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatStepperModule, MatChipsModule, MatDividerModule, MatTooltipModule, MatDialogModule,
    MatSnackBarModule, WorkflowVisualizerComponent, CommentsPanelComponent, TranslatePipe
  ],
  template: `
    <div class="detail-page">
      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="48"></mat-spinner></div>
      } @else if (doc()) {
        <!-- Header -->
        <div class="page-header">
          <div class="header-left">
            <button mat-icon-button routerLink="/documents"><mat-icon>arrow_back</mat-icon></button>
            <div>
              <h1 class="doc-title">{{ doc()!.title }}</h1>
              <div class="doc-meta">
                <span class="status-badge" [style.background]="statusBg()" [style.color]="statusColor()">
                  {{ statusLabel() }}
                </span>
                @if (doc()!.requestTypeName) {
                  <span class="type-chip" [style.background]="(doc()!.requestTypeColor || '#1a73e8') + '20'"
                        [style.color]="doc()!.requestTypeColor || '#1a73e8'">
                    <mat-icon>{{ doc()!.requestTypeIcon || 'category' }}</mat-icon> {{ doc()!.requestTypeName }}
                  </span>
                }
                <span class="meta-item"><mat-icon>description</mat-icon> {{ (doc()!.pageCount === 1 ? 'documents.detail.pageCountSingular' : 'documents.detail.pageCountPlural') | translate: { count: doc()!.pageCount } }}</span>
                <span class="meta-item"><mat-icon>schedule</mat-icon> {{ doc()!.createdAt | date:'MMM d, y' }}</span>
                @if (remaining(); as r) {
                  <span class="remaining" [class]="'rem-' + r.level">
                    <mat-icon>{{ r.expired ? 'error' : 'timer' }}</mat-icon> {{ r.text }}
                  </span>
                }
              </div>
            </div>
          </div>

          <div class="header-actions">
            @if (doc()!.status === 'Draft') {
              <button mat-stroked-button [routerLink]="['/documents', doc()!.id, 'designer']">
                <mat-icon>edit</mat-icon> {{ 'documents.detail.designFields' | translate }}
              </button>
              <button mat-flat-button color="primary" (click)="send()" [disabled]="sending()">
                <mat-icon>send</mat-icon>
                @if (sending()) { {{ 'documents.detail.sending' | translate }} } @else { {{ 'documents.detail.sendForSignature' | translate }} }
              </button>
            }
            @if (doc()!.status === 'Expired' && auth.isAdmin()) {
              <button mat-flat-button color="primary" (click)="reopen()" [disabled]="reopening()">
                <mat-icon>restart_alt</mat-icon>
                @if (reopening()) { {{ 'documents.detail.reopening' | translate }} } @else { {{ 'documents.detail.reopenRequest' | translate }} }
              </button>
            }
            <button mat-icon-button (click)="download()"
              [matTooltip]="(doc()!.finalStoragePath ? 'documents.detail.downloadSigned' : 'documents.detail.downloadOriginal') | translate">
              <mat-icon>download</mat-icon>
            </button>
            <button mat-icon-button [routerLink]="['/documents', doc()!.id, 'audit']" [matTooltip]="'documents.detail.auditTrail' | translate">
              <mat-icon>history</mat-icon>
            </button>
          </div>
        </div>

        <!-- Completion banner -->
        @if (doc()!.status === 'Completed') {
          <div class="completion-banner">
            <div class="cb-left">
              <mat-icon class="cb-icon">verified</mat-icon>
              <div>
                <strong>{{ 'documents.detail.completedTitle' | translate }}</strong>
                <span class="cb-sub">
                  {{ 'documents.detail.allSignedOn' | translate: { date: (doc()!.completedAt | date:'MMMM d, y') } }}
                  @if (doc()!.verificationCode) { · {{ 'documents.detail.verificationCode' | translate }}: <code>{{ doc()!.verificationCode }}</code> }
                </span>
              </div>
            </div>
            <button mat-flat-button class="cb-download-btn" (click)="download()">
              <mat-icon>download</mat-icon> {{ 'documents.detail.downloadSigned' | translate }}
            </button>
          </div>
        }

        <div class="detail-layout">
          <!-- Left: Recipients & Workflow -->
          <div class="left-column">
            <!-- Sequential workflow visualizer -->
            @if (workflowParticipants().length > 0) {
              <mat-card class="workflow-card">
                <mat-card-header><mat-card-title>{{ 'documents.detail.signingWorkflow' | translate }}</mat-card-title></mat-card-header>
                <mat-card-content>
                  <sf-workflow-visualizer [participants]="workflowParticipants()"></sf-workflow-visualizer>
                </mat-card-content>
              </mat-card>
            }

            <!-- Recipients -->
            <mat-card class="recipients-card">
              <mat-card-header><mat-card-title>{{ 'documents.detail.recipientsTitle' | translate }}</mat-card-title></mat-card-header>
              <mat-card-content>
                @for (r of doc()!.recipients; track r.id) {
                  <div class="recipient-row">
                    <div class="recipient-avatar">{{ initials(r.name) }}</div>
                    <div class="recipient-info">
                      <span class="recipient-name">{{ r.name }}</span>
                      <span class="recipient-email">{{ r.email }}</span>
                      @if (r.approvalType !== 'Signature' || r.roleLabel) {
                        <span class="role-chip" [class]="'role-' + r.approvalType.toLowerCase()">
                          <mat-icon>{{ roleIcon(r.approvalType) }}</mat-icon>
                          {{ r.roleLabel || (('documents.detail.roles.' + r.approvalType) | translate) }}
                        </span>
                      }
                    </div>
                    @if (r.approvalType === 'ViewOnly') {
                      <div class="recipient-status rs-viewer">
                        <mat-icon>visibility</mat-icon> {{ 'documents.detail.roles.ViewOnly' | translate }}
                      </div>
                    } @else {
                    <div class="recipient-status" [class]="'rs-' + r.status.toLowerCase()">
                      @switch (r.status) {
                        @case ('Completed') { <mat-icon>check_circle</mat-icon> {{ (r.approvalType === 'Review' || r.approvalType === 'Approval' ? 'documents.detail.recipientStatus.approved' : 'documents.detail.recipientStatus.signed') | translate }} }
                        @case ('Rejected') { <mat-icon>cancel</mat-icon> {{ 'documents.detail.recipientStatus.rejected' | translate }} }
                        @case ('InProgress') { <mat-icon>hourglass_top</mat-icon> {{ 'documents.detail.recipientStatus.inProgress' | translate }} }
                        @case ('Skipped') { <mat-icon>skip_next</mat-icon> {{ 'documents.detail.recipientStatus.skipped' | translate }} }
                        @case ('Delegated') { <mat-icon>forward</mat-icon> {{ 'documents.detail.recipientStatus.delegated' | translate }} }
                        @default { <mat-icon>schedule</mat-icon> {{ 'documents.detail.recipientStatus.pending' | translate }} }
                      }
                    </div>
                    }
                    @if (r.signedAt) {
                      <span class="recipient-time">{{ r.signedAt | date:'MMM d' }}</span>
                    }
                    <button mat-icon-button class="copy-link-btn" (click)="copyLink(r.secureToken)" [matTooltip]="'documents.detail.copySigningLink' | translate">
                      <mat-icon>link</mat-icon>
                    </button>
                  </div>
                  <mat-divider></mat-divider>
                }
                @if (doc()!.recipients.length === 0) {
                  <div class="empty-recipients">
                    <mat-icon>people</mat-icon>
                    <p>{{ 'documents.detail.noRecipients' | translate }}</p>
                  </div>
                }
              </mat-card-content>
            </mat-card>

            <!-- Comments / discussion -->
            <mat-card class="comments-card">
              <mat-card-content>
                <sf-comments-panel [documentId]="doc()!.id" [canModerate]="canModerate()"></sf-comments-panel>
              </mat-card-content>
            </mat-card>

            <!-- Real-time progress -->
            @if (hub.lastProgress()?.documentId === doc()!.id) {
              <mat-card class="progress-card">
                <mat-card-content>
                  <div class="progress-info">
                    <mat-icon class="pulse">sync</mat-icon>
                    <div>
                      <strong>{{ hub.lastProgress()!.message }}</strong>
                      <span class="progress-steps">
                        {{ 'documents.detail.stepsSigned' | translate: { completed: hub.lastProgress()!.completedSteps, total: hub.lastProgress()!.totalSteps } }}
                      </span>
                    </div>
                  </div>
                </mat-card-content>
              </mat-card>
            }
          </div>

          <!-- Right: Fields summary -->
          <div class="right-column">
            <mat-card>
              <mat-card-header><mat-card-title>{{ 'documents.detail.signatureFields' | translate }}</mat-card-title></mat-card-header>
              <mat-card-content>
                <div class="fields-summary">
                  <div class="field-stat">
                    <span class="field-count">{{ doc()!.fields.length }}</span>
                    <span class="field-label">{{ 'documents.detail.fields.total' | translate }}</span>
                  </div>
                  <div class="field-stat">
                    <span class="field-count signed">{{ signedCount() }}</span>
                    <span class="field-label">{{ 'documents.detail.fields.signed' | translate }}</span>
                  </div>
                  <div class="field-stat">
                    <span class="field-count pending">{{ doc()!.fields.length - signedCount() }}</span>
                    <span class="field-label">{{ 'documents.detail.fields.pending' | translate }}</span>
                  </div>
                </div>

                @for (field of doc()!.fields.slice(0, 5); track field.id) {
                  <div class="field-row">
                    <mat-icon class="field-icon" [class.signed-icon]="field.isSigned">
                      {{ field.isSigned ? 'check_circle' : 'radio_button_unchecked' }}
                    </mat-icon>
                    <div>
                      <span class="field-type">{{ field.fieldType }}</span>
                      <span class="field-page">{{ 'documents.detail.fieldPage' | translate: { page: field.page } }}</span>
                    </div>
                  </div>
                }
                @if (doc()!.fields.length > 5) {
                  <p class="more-fields">{{ 'documents.detail.moreFields' | translate: { count: doc()!.fields.length - 5 } }}</p>
                }
              </mat-card-content>
            </mat-card>

            <!-- AI Summary -->
            <mat-card class="ai-summary-card">
              <mat-card-header>
                <mat-card-title class="ai-summary-title">
                  <mat-icon class="ai-icon">auto_awesome</mat-icon> {{ 'documents.detail.aiSummary' | translate }}
                </mat-card-title>
              </mat-card-header>
              <mat-card-content>
                @if (summaryLoading()) {
                  <div class="summary-skeleton">
                    <div class="skeleton-line long"></div>
                    <div class="skeleton-line"></div>
                    <div class="skeleton-line medium"></div>
                  </div>
                } @else if (summary()) {
                  <p class="summary-text">{{ summary()!.summary }}</p>
                  <span class="summary-meta">{{ 'documents.detail.generated' | translate: { date: (summary()!.generatedAt | date:'MMM d, y') } }}</span>
                } @else if (summaryError()) {
                  <div class="summary-empty">
                    <mat-icon>smart_toy</mat-icon>
                    <p>{{ 'documents.detail.summaryError' | translate }}</p>
                    <button mat-stroked-button (click)="generateSummary()">{{ 'common.retry' | translate }}</button>
                  </div>
                } @else {
                  <div class="summary-empty">
                    <mat-icon>smart_toy</mat-icon>
                    <p>{{ 'documents.detail.noSummary' | translate }}</p>
                    <button mat-stroked-button (click)="generateSummary()">{{ 'documents.detail.generateSummary' | translate }}</button>
                  </div>
                }
              </mat-card-content>
            </mat-card>
          </div>
        </div>
      } @else if (error()) {
        <div class="error-state">
          <mat-icon>error_outline</mat-icon>
          <p>{{ error() }}</p>
          <button mat-stroked-button routerLink="/documents">{{ 'documents.detail.backToDocuments' | translate }}</button>
        </div>
      }
    </div>
  `,
  styleUrl: './document-detail.component.scss'
})
export class DocumentDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly documentService = inject(DocumentService);
  readonly hub = inject(HubService);
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  private readonly snack = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly doc = signal<DocumentDetail | null>(null);
  readonly error = signal<string | null>(null);
  readonly sending = signal(false);
  readonly summary = signal<AiSummaryDto | null>(null);
  readonly summaryLoading = signal(false);
  readonly summaryError = signal(false);

  readonly signedCount = () => this.doc()?.fields.filter(f => f.isSigned).length ?? 0;
  readonly reopening = signal(false);

  /** Owner and admins can resolve/delete any comment thread. */
  readonly canModerate = () =>
    this.auth.isAdmin() || this.doc()?.owner.id === this.auth.user()?.id;

  remaining(): RemainingTime | null {
    const d = this.doc();
    if (!d || d.status === 'Completed' || d.status === 'Rejected' || d.status === 'Draft') return null;
    return remainingTime(d.expiresAt);
  }

  copyLink(secureToken: string): void {
    const url = `${window.location.origin}/sign/${secureToken}`;
    navigator.clipboard.writeText(url).then(
      () => this.snack.open(this.translate.instant('documents.detail.linkCopied'), 'OK', { duration: 2500 }),
      () => this.snack.open(this.translate.instant('documents.detail.linkCopyFailed'), 'OK', { duration: 3000 })
    );
  }

  reopen(): void {
    const d = this.doc();
    if (!d) return;
    this.reopening.set(true);
    this.documentService.reopen(d.id).subscribe({
      next: () => { this.reopening.set(false); this.load(); },
      error: err => { this.reopening.set(false); alert(err.error?.detail ?? this.translate.instant('documents.detail.reopenFailed')); }
    });
  }

  readonly workflowParticipants = (): WorkflowParticipant[] => {
    // Viewers aren't workflow gates — keep them out of the signing-progress visualizer.
    const recipients = (this.doc()?.recipients ?? []).filter(r => r.approvalType !== 'ViewOnly');
    if (recipients.length === 0) return [];
    const ordered = [...recipients].sort((a, b) => a.order - b.order);
    const current = ordered.find(r =>
      r.status !== 'Completed' && r.status !== 'Rejected' && r.status !== 'Skipped');
    return ordered.map(r => ({
      order: r.order,
      name: r.name,
      status: r.status as WorkflowParticipant['status'],
      isCurrent: r.id === current?.id && this.doc()?.status !== 'Completed',
      signedAt: r.signedAt
    }));
  };

  private documentId = '';

  readonly STATUS_COLORS: Record<string, string> = {
    Draft: '#5f6368', Sent: '#1a73e8', Completed: '#22c55e',
    Rejected: '#ef4444', Expired: '#f97316', WaitingForOthers: '#8b5cf6'
  };

  statusColor(): string { return this.STATUS_COLORS[this.doc()?.status ?? ''] ?? '#5f6368'; }
  statusBg(): string { return `${this.statusColor()}20`; }
  statusLabel(): string { return this.translate.instant(`documents.detail.status.${this.doc()?.status ?? ''}`); }
  initials(name: string): string {
    return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);
  }

  roleIcon(approvalType: string): string {
    switch (approvalType) {
      case 'Review': return 'rate_review';
      case 'Approval': return 'fact_check';
      case 'ViewOnly': return 'visibility';
      case 'Stamp': return 'approval';
      default: return 'draw';
    }
  }

  ngOnInit(): void {
    this.documentId = this.route.snapshot.paramMap.get('id')!;
    this.load();
    this.hub.joinDocument(this.documentId);
  }

  ngOnDestroy(): void {
    this.hub.leaveDocument(this.documentId);
  }

  load(): void {
    this.loading.set(true);
    this.documentService.getDocument(this.documentId).subscribe({
      next: doc => {
        this.doc.set(doc);
        this.loading.set(false);
        if (doc.aiSummary && doc.aiSummaryGeneratedAt) {
          this.summary.set({ summary: doc.aiSummary, generatedAt: doc.aiSummaryGeneratedAt });
        }
      },
      error: () => { this.error.set(this.translate.instant('documents.detail.notFound')); this.loading.set(false); }
    });
  }

  generateSummary(): void {
    this.summaryLoading.set(true);
    this.summaryError.set(false);
    this.documentService.getDocumentSummary(this.documentId).subscribe({
      next: s => { this.summary.set(s); this.summaryLoading.set(false); },
      error: () => { this.summaryError.set(true); this.summaryLoading.set(false); }
    });
  }

  send(): void {
    if (!confirm(this.translate.instant('documents.detail.confirmSend'))) return;
    this.sending.set(true);
    this.documentService.send(this.documentId).subscribe({
      next: () => { this.sending.set(false); this.load(); },
      error: err => {
        this.sending.set(false);
        alert(err.error?.detail ?? this.translate.instant('documents.detail.sendFailed'));
      }
    });
  }

  download(): void {
    this.documentService.download(this.documentId).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${this.doc()?.title ?? 'document'}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    });
  }
}
