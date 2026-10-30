import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { HubService } from '../../core/services/hub.service';
import { AuthService } from '../../core/auth/auth.service';
import { DocumentDetail, WorkflowParticipant, AiSummaryDto, Review } from '../../core/models';
import { ReviewService } from '../../core/services/review.service';
import { ReadinessChecklistComponent } from '../../shared/readiness-checklist/readiness-checklist.component';
import { CommentsPanelComponent } from '../../shared/comments-panel/comments-panel.component';
import { ContractChatPanelComponent } from './contract-chat-panel.component';
import { ContractTermPanelComponent } from './contract-term-panel.component';
import { ContractLifecycleAiPanelComponent } from './contract-lifecycle-ai-panel.component';
import { VersionCompareDialogComponent, VersionCompareDialogData } from './version-compare-dialog.component';
import { PdfTextEditorDialogComponent, PdfTextEditorDialogData } from './pdf-text-editor-dialog.component';
import { TemplateRevisionDialogComponent, TemplateRevisionDialogData } from './template-revision-dialog.component';
import { remainingTime, RemainingTime } from '../../core/utils/sla';

@Component({
  selector: 'sf-document-detail',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatStepperModule, MatChipsModule, MatDividerModule, MatTooltipModule, MatDialogModule,
    MatTabsModule, MatSnackBarModule, CommentsPanelComponent,
    ContractChatPanelComponent, ContractTermPanelComponent, ContractLifecycleAiPanelComponent,
    ReadinessChecklistComponent, TranslatePipe
  ],
  template: `
    <div class="detail-page">
      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="48"></mat-spinner></div>
      } @else if (doc()) {
        <!-- Header -->
        <div class="page-header">
          <div class="header-left">
            <button mat-icon-button routerLink="/contracts"><mat-icon>arrow_back</mat-icon></button>
            <div>
              <h1 class="doc-title">{{ doc()!.title }}</h1>
              <div class="doc-meta">
                <span class="status-badge" [style.background]="statusBg()" [style.color]="statusColor()">
                  {{ statusLabel() }}
                </span>
                @if (doc()!.version > 1) {
                  <span class="version-chip" [matTooltip]="'documents.detail.versionTooltip' | translate">v{{ doc()!.version }}</span>
                }
                @if (doc()!.requestTypeName) {
                  <span class="type-chip" [style.background]="(doc()!.requestTypeColor || '#1a73e8') + '20'"
                        [style.color]="doc()!.requestTypeColor || '#1a73e8'">
                    <mat-icon>{{ doc()!.requestTypeIcon || 'category' }}</mat-icon> {{ doc()!.requestTypeName }}
                  </span>
                }
                <span class="meta-item"><mat-icon>description</mat-icon> {{ (doc()!.pageCount === 1 ? 'documents.detail.pageCountSingular' : 'documents.detail.pageCountPlural') | translate: { count: doc()!.pageCount } }}</span>
                <span class="meta-item"><mat-icon>schedule</mat-icon> {{ doc()!.createdAt | date:'MMM d, y' }}</span>
                <span class="meta-item file-name" [matTooltip]="doc()!.fileName"><mat-icon>attach_file</mat-icon> <span class="file-text">{{ doc()!.fileName }}</span></span>
                @if (remaining(); as r) {
                  <span class="remaining" [class]="'rem-' + r.level">
                    <mat-icon>{{ r.expired ? 'error' : 'timer' }}</mat-icon> {{ r.text }}
                  </span>
                }
                @if (activeViewers().length > 0) {
                  <span class="presence-stack" [matTooltip]="presenceTooltip()">
                    @for (v of activeViewers().slice(0, 4); track v.userId) {
                      <span class="presence-avatar">{{ initials(v.name) }}</span>
                    }
                    <span class="presence-label">
                      <span class="presence-dot"></span>
                      {{ (activeViewers().length === 1 ? 'documents.detail.presence.viewingOne' : 'documents.detail.presence.viewingMany') | translate: { count: activeViewers().length } }}
                    </span>
                  </span>
                }
              </div>
            </div>
          </div>

          <div class="header-actions">
            @if (doc()!.status === 'Draft') {
              @if (isOwner() && !doc()!.templateKind) {
                <a mat-stroked-button [routerLink]="['/contracts/new']" [queryParams]="{ documentId: doc()!.id }">
                  <mat-icon>checklist</mat-icon> {{ 'documents.detail.continueSetup' | translate }}
                </a>
              }
              @if (canEditText()) {
                <button mat-stroked-button (click)="editText()">
                  <mat-icon>edit_note</mat-icon> {{ 'documents.detail.editText.button' | translate }}
                </button>
              }
              <button mat-stroked-button [routerLink]="['/contracts', doc()!.id, 'designer']">
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
            <button mat-icon-button [routerLink]="['/contracts', doc()!.id, 'audit']" [matTooltip]="'documents.detail.auditTrail' | translate">
              <mat-icon>history</mat-icon>
            </button>
          </div>
        </div>

        <!--
          One status card instead of several banners: where the contract stands, the stages it goes through,
          and the two scores. Clicking the scores opens the review tab where the detail lives.
        -->
        <section class="status-rail">
          <div class="rail-main">
            <div class="rail-head">
              <span class="rail-icon" [class]="'tone-' + railTone()"><mat-icon>{{ railIcon() }}</mat-icon></span>
              <div>
                <p class="rail-title">{{ railTitle() }}</p>
                @if (railSub()) { <p class="rail-sub">{{ railSub() }}</p> }
              </div>
            </div>
            <ol class="rail-steps">
              @for (s of railSteps(); track s.key; let last = $last; let i = $index) {
                <li class="rail-step" [class.last]="last">
                  <span class="rail-dot" [class]="'dot-' + s.state">
                    @switch (s.state) {
                      @case ('done') { <mat-icon>check</mat-icon> }
                      @case ('error') { <mat-icon>close</mat-icon> }
                      @case ('warn') { <mat-icon>pause</mat-icon> }
                      @default { {{ i + 1 }} }
                    }
                  </span>
                  <span class="rail-step-text">
                    <span class="rail-step-label">{{ s.label }}</span>
                    @if (s.note) { <span class="rail-step-note" [class.note-error]="s.state === 'error'">{{ s.note }}</span> }
                  </span>
                  @if (!last) { <span class="rail-line" [class.line-done]="s.state === 'done'"></span> }
                </li>
              }
            </ol>
            @if (canReview() && doc()!.readinessVerdict !== 'NotEvaluated') {
              <!-- The one place to run the review again: it refreshes the findings, the adjustments and the verdict together. -->
              <div class="rail-actions">
                <button mat-stroked-button (click)="runReview()" [disabled]="reviewing()">
                  <mat-icon>refresh</mat-icon>
                  {{ (reviewing() ? 'documents.detail.readiness.running' : 'documents.detail.readiness.rerun') | translate }}
                </button>
              </div>
            }
          </div>

          @if (doc()!.readinessVerdict !== 'NotEvaluated' && (riskScore() !== null || doc()!.readinessScore != null)) {
            <div class="rail-score-block">
            <div class="rail-scores" (click)="showChecklist()">
              @if (riskScore() !== null) {
                <ng-container *ngTemplateOutlet="ring; context: { label: ('documents.detail.rail.risk' | translate), value: riskScore(), color: ringColor(riskScore()!, false) }"></ng-container>
              }
              @if (doc()!.readinessScore != null) {
                <ng-container *ngTemplateOutlet="ring; context: { label: ('documents.detail.rail.fitness' | translate), value: doc()!.readinessScore, color: ringColor(doc()!.readinessScore!, true) }"></ng-container>
              }
            </div>
            @if (riskScore() !== null) {
              <!-- The risk score rates the text Harvey read; deciding on findings does not re-score it, a new version does. -->
              <p class="rail-score-note">
                {{ 'documents.detail.rail.riskAnalysed' | translate: { date: (doc()!.readinessEvaluatedAt | date:'dd/MM/yyyy') } }}
                @if ((readiness()?.handledCount ?? 0) > 0) {
                  <span class="rail-score-hint">{{ 'documents.detail.rail.riskStale' | translate: { count: readiness()!.handledCount } }}</span>
                }
              </p>
            }
            </div>
          }
        </section>
        <ng-template #ring let-label="label" let-value="value" let-color="color">
          <div class="ring">
            <div class="ring-chart">
              <svg viewBox="0 0 64 64" width="64" height="64">
                <circle cx="32" cy="32" r="26" class="ring-track" />
                <circle cx="32" cy="32" r="26" class="ring-value" [attr.stroke]="color"
                        stroke-dasharray="163.36" [attr.stroke-dashoffset]="163.36 * (1 - value / 100)" />
              </svg>
              <span class="ring-num">{{ value }}</span>
            </div>
            <span class="ring-label">{{ label }}</span>
          </div>
        </ng-template>

        <!-- Changes-requested banner: a participant paused the flow; the owner answers with a revised PDF -->
        @if (doc()!.status === 'ChangesRequested' && isOwner()) {
          <div class="changes-banner">
            <div class="cb-left">
              <mat-icon class="crb-icon">published_with_changes</mat-icon>
              <div>
                <strong>{{ 'documents.detail.changesRequestedTitle' | translate }}</strong>
                <span class="crb-sub">
                  @if (changeRequester(); as cr) {
                    {{ 'documents.detail.changesRequestedBy' | translate: { name: cr.name } }}
                    @if (cr.declineReason) { — “{{ cr.declineReason }}” }
                  } @else {
                    {{ 'documents.detail.changesRequestedHint' | translate }}
                  }
                </span>
              </div>
            </div>
            @if (isOwner() && doc()!.templateKind) {
              <div class="revision-form">
                <button mat-flat-button class="crb-upload-btn" (click)="reviseAnswers()">
                  <mat-icon>edit_note</mat-icon> {{ 'documents.detail.reviseAnswers.button' | translate }}
                </button>
              </div>
            } @else if (isOwner()) {
              <div class="revision-form">
                <input class="note-input" #noteInput
                       [placeholder]="'documents.detail.changeNotePlaceholder' | translate"
                       [disabled]="uploadingRevision()" />
                <input type="file" accept="application/pdf" hidden #revisionInput
                       (change)="onRevisionFile($event, noteInput.value)" />
                @if (canEditText()) {
                  <button mat-stroked-button (click)="editText()" [disabled]="uploadingRevision()">
                    <mat-icon>edit_note</mat-icon> {{ 'documents.detail.editText.button' | translate }}
                  </button>
                }
                <button mat-flat-button class="crb-upload-btn" (click)="revisionInput.click()" [disabled]="uploadingRevision()">
                  <mat-icon>upload_file</mat-icon>
                  {{ (uploadingRevision() ? 'documents.detail.uploadingRevision' : 'documents.detail.uploadRevision') | translate }}
                </button>
              </div>
            }
          </div>
        }

        <!-- What the owner changed between versions of a template contract -->
        @if (revisionsNewestFirst().length > 0) {
          <div class="rev-history">
            <div class="rev-title"><mat-icon>history</mat-icon> {{ 'documents.detail.revisionHistory.title' | translate }}</div>
            @for (rev of revisionsNewestFirst(); track rev.toVersion; let first = $first) {
              <div class="rev-item" [class.rev-latest]="first">
                <div class="rev-head">
                  <strong>v{{ rev.fromVersion }} → v{{ rev.toVersion }}</strong>
                  @if (rev.changedByName) { <span>· {{ rev.changedByName }}</span> }
                  <span>· {{ rev.createdAt | date:'medium' }}</span>
                  @if (first) { <span class="rev-badge">{{ 'documents.detail.revisionHistory.latest' | translate }}</span> }
                </div>
                @if (rev.changeNote) { <div class="rev-note">{{ rev.changeNote }}</div> }
                <table class="rev-table">
                  @for (c of rev.changes; track c.fieldKey) {
                    <tr>
                      <td class="rev-field">{{ c.label }}</td>
                      <td class="rev-old">{{ c.oldValue || ('documents.detail.revisionHistory.empty' | translate) }}</td>
                      <td class="rev-arrow">→</td>
                      <td class="rev-new">{{ c.newValue || ('documents.detail.revisionHistory.empty' | translate) }}</td>
                    </tr>
                  }
                </table>
              </div>
            }
          </div>
        }

        <!-- Participant action: the signing screen is one click away, once they have read everything here -->
        @if (myAction(); as action) {
          <div class="my-action-banner">
            <mat-icon>{{ action.icon }}</mat-icon>
            <div class="my-action-text">
              <strong>{{ 'documents.detail.myAction.title' | translate }}</strong>
              <span>{{ 'documents.detail.myAction.hint' | translate }}</span>
            </div>
            <button mat-flat-button color="primary" (click)="continueToAction(action.token)">
              <mat-icon>{{ action.icon }}</mat-icon> {{ action.labelKey | translate }}
            </button>
          </div>
        }

        <!--
          Intelligence band. The legal read on the contract is the first thing on the page, above
          the fold — before this it sat at the bottom of a 340px sidebar and was routinely missed.
          Clicking anywhere on it opens the Intelligence tab where the detail lives.
        -->
        @if (doc()!.readinessVerdict === 'NotEvaluated') {
          <div class="intel-band cta">
            <div class="cta-text">
              <mat-icon>fact_check</mat-icon>
              <div>
                <strong>{{ 'documents.detail.readiness.ctaTitle' | translate }}</strong>
                <span>{{ 'documents.detail.readiness.ctaHint' | translate }}</span>
              </div>
            </div>
            @if (canReview()) {
              <button mat-flat-button color="primary" (click)="runReview()" [disabled]="reviewing()">
                <mat-icon>auto_awesome</mat-icon>
                {{ (reviewing() ? 'documents.detail.readiness.running' : 'documents.detail.readiness.run') | translate }}
              </button>
            }
          </div>
        }

        <mat-tab-group class="detail-tabs" [selectedIndex]="tabIndex()" (selectedIndexChange)="tabIndex.set($event)">
          <!-- 1. Summary: what the contract is, what to fix first, what comes next -->
          <mat-tab>
            <ng-template mat-tab-label>
              <mat-icon class="tab-icon">dashboard</mat-icon> {{ 'documents.detail.tabs.summary' | translate }}
              @if ((readiness()?.openCount ?? 0) > 0) { <span class="tab-badge">{{ readiness()!.openCount }}</span> }
            </ng-template>
            <div class="summary-grid">
            <div class="intel-layout">
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
              @if (readiness(); as r) {
                <mat-card class="readiness-card" id="readiness-checklist">
                  <mat-card-content>
                    <!-- The verdict itself is the band above the tabs; here, only what to do about it. -->
                    <sf-readiness-checklist
                      [documentId]="doc()!.id"
                      [items]="r.items"
                      [fixedItems]="r.fixedItems"
                      [readonly]="!canReview() || doc()!.status !== 'Draft'"
                      (changed)="onReadinessChanged($event)">
                    </sf-readiness-checklist>
                  </mat-card-content>
                </mat-card>
              }
            <!-- Contract term & lifecycle -->
            <sf-contract-term-panel [wide]="true"
              [documentId]="doc()!.id"
              [contractStartDate]="doc()!.contractStartDate"
              [contractEndDate]="doc()!.contractEndDate"
              [autoRenews]="doc()!.autoRenews"
              [noticePeriodDays]="doc()!.noticePeriodDays"
              [termConfirmed]="doc()!.termConfirmed"
              [canEdit]="isOwner() || auth.isAdmin()"
              (saved)="load()">
            </sf-contract-term-panel>
            </div>
            <aside class="summary-aside">
              <mat-card>
                <mat-card-content>
                  <h3 class="eyebrow">{{ 'documents.detail.next.title' | translate }}</h3>
                  <ul class="next-list">
                    @for (a of nextActions(); track a.key) {
                      <li>
                        <button class="next-item" (click)="a.go()">
                          <span class="next-ico" [class]="'tone-' + a.tone"><mat-icon>{{ a.icon }}</mat-icon></span>
                          <span class="next-text">
                            <span class="next-title">{{ a.title }}</span>
                            <span class="next-sub">{{ a.sub }}</span>
                          </span>
                        </button>
                      </li>
                    } @empty {
                      <li class="next-empty">{{ 'documents.detail.next.none' | translate }}</li>
                    }
                  </ul>
                </mat-card-content>
              </mat-card>
              @if (readiness(); as r) {
                <mat-card>
                  <mat-card-content>
                    <h3 class="eyebrow">{{ 'documents.detail.next.points' | translate }}</h3>
                    <p class="points-count">{{ r.openCount }} <span>{{ 'documents.detail.next.open' | translate }}</span></p>
                    <p class="points-sub">{{ 'documents.detail.next.handled' | translate: { count: r.handledCount } }}</p>
                  </mat-card-content>
                </mat-card>
              }
            </aside>
            </div>
          </mat-tab>

          <!-- 2. Harvey: ask the contract, key data, renewal note -->
          <mat-tab>
            <ng-template mat-tab-label>
              <mat-icon class="tab-icon">auto_awesome</mat-icon> {{ 'documents.detail.tabs.harvey' | translate }}
            </ng-template>
            <div class="harvey-grid">
            <!-- Ask the contract -->
            <sf-contract-chat-panel [documentId]="doc()!.id"></sf-contract-chat-panel>
            <!-- Harvey: key data and renewal note -->
            <sf-contract-lifecycle-ai-panel
              [documentId]="doc()!.id"
              [hasEndDate]="!!doc()!.contractEndDate"
              [canEdit]="isOwner() || auth.isAdmin()"
              (applied)="load()">
            </sf-contract-lifecycle-ai-panel>
            </div>
          </mat-tab>

          <!-- 3. Signing and follow-up: workflow, recipients, fields, comments, versions, obligations -->
          <mat-tab>
            <ng-template mat-tab-label>
              <mat-icon class="tab-icon">draw</mat-icon> {{ 'documents.detail.tabs.signing' | translate }}
            </ng-template>
        <div class="detail-layout">
          <!-- Left: Recipients & Workflow -->
          <div class="left-column">
            <!-- Recipients -->
            <mat-card class="recipients-card">
              <mat-card-header>
                <mat-card-title>{{ 'documents.detail.circuit.title' | translate }}</mat-card-title>
                @if (doc()!.workflowType === 'Sequential') {
                  <mat-card-subtitle>{{ 'documents.detail.circuit.sequential' | translate }}</mat-card-subtitle>
                }
              </mat-card-header>
              <mat-card-content>
                @for (r of doc()!.recipients; track r.id; let isLast = $last) {
                  <div class="circuit-item" [class.is-last]="isLast">
                  <div class="recipient-avatar" [class]="'recipient-avatar av-' + (stoppedFor(r) ? 'skipped' : r.status.toLowerCase())">{{ initials(r.name) }}</div>
                  <div class="circuit-body">
                  <div class="recipient-row">
                    <div class="recipient-info">
                      <div class="recipient-line">
                        <span class="recipient-name">{{ r.name }}</span>
                        <span class="role-chip" [class]="'role-' + r.approvalType.toLowerCase()">
                          {{ r.roleLabel || (('documents.detail.roles.' + r.approvalType) | translate) }}
                        </span>
                      </div>
                      <span class="recipient-email">{{ r.email }}</span>
                      @if (r.declineReason && (r.status === 'Rejected' || r.status === 'ChangesRequested')) {
                        <span class="recipient-note">“{{ r.declineReason }}”</span>
                      } @else if (r.status === 'Pending' && r.order > 1 && !stoppedFor(r)) {
                        <span class="recipient-wait">{{ 'documents.detail.circuit.notifiedAfter' | translate: { step: r.order - 1 } }}</span>
                      }
                    </div>
                    @if (r.approvalType === 'ViewOnly') {
                      <div class="recipient-status rs-viewer">
                        <mat-icon>visibility</mat-icon> {{ 'documents.detail.roles.ViewOnly' | translate }}
                      </div>
                    } @else {
                    <div class="recipient-status" [class]="'rs-' + (stoppedFor(r) ? 'skipped' : r.status.toLowerCase())">
                      @switch (stoppedFor(r) ?? r.status) {
                        @case ('Rejected_') { <mat-icon>block</mat-icon> {{ 'documents.detail.recipientStatus.notRequired' | translate }} }
                        @case ('Expired_') { <mat-icon>schedule</mat-icon> {{ 'documents.detail.recipientStatus.expired' | translate }} }
                        @case ('Completed') { <mat-icon>check_circle</mat-icon> {{ (r.approvalType === 'Review' || r.approvalType === 'Approval' ? 'documents.detail.recipientStatus.approved' : 'documents.detail.recipientStatus.signed') | translate }} }
                        @case ('Rejected') { <mat-icon>cancel</mat-icon> {{ 'documents.detail.recipientStatus.rejected' | translate }} }
                        @case ('ChangesRequested') { <mat-icon>published_with_changes</mat-icon> {{ 'documents.detail.recipientStatus.changesRequested' | translate }} }
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
                    @if (r.secureToken) {
                      <button mat-icon-button class="copy-link-btn" (click)="copyLink(r.secureToken)" [matTooltip]="'documents.detail.copySigningLink' | translate">
                        <mat-icon>link</mat-icon>
                      </button>
                    }
                  </div>
                  @if (r.approvalType === 'Review' || r.approvalType === 'Approval') {
                    @if (isOwner() && doc()!.status === 'Draft') {
                      <div class="checklist-block">
                        <span class="checklist-label"><mat-icon>checklist</mat-icon>{{ 'documents.detail.checklist.title' | translate }}</span>
                        @for (item of r.checklistItems; track item.id; let idx = $index) {
                          <div class="checklist-edit-row">
                            <span class="checklist-text">{{ item.text }}</span>
                            <button mat-icon-button class="checklist-remove-btn" (click)="removeChecklistItem(r.id, idx)">
                              <mat-icon>close</mat-icon>
                            </button>
                          </div>
                        }
                        <div class="checklist-add-row">
                          <input class="checklist-input" [(ngModel)]="newChecklistText[r.id]"
                                 [placeholder]="'documents.detail.checklist.addPlaceholder' | translate"
                                 (keydown.enter)="addChecklistItem(r.id)" />
                          <button mat-icon-button (click)="addChecklistItem(r.id)"><mat-icon>add</mat-icon></button>
                        </div>
                      </div>
                    } @else if (r.checklistItems && r.checklistItems.length > 0) {
                      <div class="checklist-block readonly">
                        <span class="checklist-label"><mat-icon>checklist</mat-icon>{{ 'documents.detail.checklist.title' | translate }}</span>
                        @for (item of r.checklistItems; track item.id) {
                          <div class="checklist-view-row">
                            <mat-icon [class.checked]="item.isChecked">{{ item.isChecked ? 'check_box' : 'check_box_outline_blank' }}</mat-icon>
                            <span [class.done]="item.isChecked">{{ item.text }}</span>
                          </div>
                        }
                      </div>
                    }
                  }
                  </div>
                  </div>
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
                <sf-comments-panel [documentId]="doc()!.id" [canModerate]="canModerate()"
                                    [pageCount]="doc()!.pageCount"></sf-comments-panel>
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
                <div class="fields-progress">
                  <span class="fp-count">{{ signedCount() }}</span>
                  <span class="fp-total">/ {{ signingFields().length }} {{ 'documents.detail.fields.signaturesSigned' | translate }}</span>
                </div>
                <div class="fp-bar"><div class="fp-fill" [style.width.%]="signingFields().length ? signedCount() * 100 / signingFields().length : 0"></div></div>
                @if (doc()!.fields.length > signingFields().length) {
                  <p class="fp-other">{{ 'documents.detail.fields.otherFields' | translate: { count: doc()!.fields.length - signingFields().length } }}</p>
                }

                @for (field of (showAllFields() ? doc()!.fields : doc()!.fields.slice(0, 5)); track field.id) {
                  <div class="field-row">
                    <mat-icon class="field-icon" [class.signed-icon]="field.isSigned">
                      {{ field.isSigned ? 'check_circle' : 'radio_button_unchecked' }}
                    </mat-icon>
                    <div class="field-text">
                      <span class="field-type" [matTooltip]="field.label || ''">{{ field.label || (('designer.fieldTypes.' + field.fieldType) | translate) }}</span>
                      <span class="field-meta">
                        {{ ('designer.fieldTypes.' + field.fieldType) | translate }}
                        · {{ 'documents.detail.fieldPage' | translate: { page: field.page } }}
                        · {{ recipientNameOf(field.recipientId) ?? ('documents.detail.fields.filledByOwner' | translate) }}
                      </span>
                    </div>
                  </div>
                }
                @if (doc()!.fields.length > 5) {
                  <button mat-button class="more-fields" (click)="showAllFields.set(!showAllFields())">
                    <mat-icon>{{ showAllFields() ? 'expand_less' : 'expand_more' }}</mat-icon>
                    @if (showAllFields()) { {{ 'documents.detail.fields.showLess' | translate }} }
                    @else { {{ 'documents.detail.moreFields' | translate: { count: doc()!.fields.length - 5 } }} }
                  </button>
                }
              </mat-card-content>
            </mat-card>

            <!-- Version history (only present once a revision was uploaded) -->
            @if ((doc()!.versions?.length ?? 0) > 0) {
              <mat-card class="versions-card">
                <mat-card-header>
                  <mat-card-title>{{ 'documents.detail.versionsTitle' | translate }}</mat-card-title>
                </mat-card-header>
                <mat-card-content>
                  @if (!doc()!.templateKind) {
                    <button mat-stroked-button class="compare-versions-btn" (click)="compareVersions()">
                      <mat-icon>compare_arrows</mat-icon>
                      {{ 'documents.detail.compare.open' | translate }}
                    </button>
                  }

                  <div class="version-row">
                    <span class="version-badge current">v{{ doc()!.version }}</span>
                    <div class="version-info">
                      <span class="version-note">{{ 'documents.detail.currentVersion' | translate }}</span>
                    </div>
                    <button mat-icon-button (click)="downloadVersion(doc()!.version)"
                      [matTooltip]="'documents.detail.compare.downloadVersion' | translate: { version: doc()!.version }">
                      <mat-icon>download</mat-icon>
                    </button>
                  </div>
                  @for (v of doc()!.versions; track v.id) {
                    <div class="version-row">
                      <span class="version-badge">v{{ v.versionNumber }}</span>
                      <div class="version-info">
                        <span class="version-note">{{ v.changeNote || ('documents.detail.noChangeNote' | translate) }}</span>
                        <span class="version-meta">
                          @if (v.changedByName) { {{ v.changedByName }} · }
                          {{ v.createdAt | date:'MMM d, y' }}
                        </span>
                      </div>
                      <button mat-icon-button (click)="downloadVersion(v.versionNumber)"
                        [matTooltip]="'documents.detail.compare.downloadVersion' | translate: { version: v.versionNumber }">
                        <mat-icon>download</mat-icon>
                      </button>
                    </div>
                  }
                </mat-card-content>
              </mat-card>
            }

            <!-- Obligation Tracking -->
            <mat-card class="obligation-card">
              <mat-card-header>
                <mat-card-title class="ai-summary-title">
                  <mat-icon class="ai-icon">event_available</mat-icon> {{ 'documents.detail.obligation.title' | translate }}
                </mat-card-title>
              </mat-card-header>
              <mat-card-content>
                @if (editingObligation()) {
                  <div class="obligation-edit-form">
                    <label class="obligation-field">
                      <span>{{ 'documents.detail.obligation.dueDate' | translate }}</span>
                      <input type="date" [(ngModel)]="obligationDueDateInput" />
                    </label>
                    <label class="obligation-field">
                      <span>{{ 'documents.detail.obligation.type' | translate }}</span>
                      <input type="text" [(ngModel)]="obligationTypeInput" />
                    </label>
                    <div class="obligation-edit-actions">
                      <button mat-stroked-button (click)="cancelEditObligation()">{{ 'common.cancel' | translate }}</button>
                      <button mat-flat-button color="primary" [disabled]="savingObligation() || !obligationDueDateInput || !obligationTypeInput"
                        (click)="saveObligation()">{{ 'documents.detail.obligation.save' | translate }}</button>
                    </div>
                  </div>
                } @else if (doc()!.obligationDueDate) {
                  <div class="obligation-details">
                    <div class="obligation-row">
                      <span class="obligation-label">{{ 'documents.detail.obligation.dueDate' | translate }}</span>
                      <span class="obligation-value">{{ doc()!.obligationDueDate | date:'MMM d, y' }}</span>
                    </div>
                    <div class="obligation-row">
                      <span class="obligation-label">{{ 'documents.detail.obligation.type' | translate }}</span>
                      <span class="obligation-value">{{ doc()!.obligationType }}</span>
                    </div>
                    @if (!doc()!.obligationConfirmed) {
                      <div class="obligation-suggested-chip">
                        <mat-icon>auto_awesome</mat-icon> {{ 'documents.detail.obligation.suggested' | translate }}
                      </div>
                      @if (doc()!.obligationRationale) {
                        <p class="obligation-rationale">{{ doc()!.obligationRationale }}</p>
                      }
                    }
                    @if (isOwner() || auth.isAdmin()) {
                      <button mat-stroked-button (click)="startEditObligation()">{{ 'documents.detail.obligation.edit' | translate }}</button>
                    }
                  </div>
                } @else {
                  <div class="summary-empty">
                    <mat-icon>event_available</mat-icon>
                    <p>{{ 'documents.detail.obligation.none' | translate }}</p>
                  </div>
                }
              </mat-card-content>
            </mat-card>
          </div>
        </div>
          </mat-tab>
        </mat-tab-group>
      } @else if (error()) {
        <div class="error-state">
          <mat-icon>error_outline</mat-icon>
          <p>{{ error() }}</p>
          <button mat-stroked-button routerLink="/contracts">{{ 'documents.detail.backToDocuments' | translate }}</button>
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
  private readonly confirmService = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);

  readonly loading = signal(true);
  readonly doc = signal<DocumentDetail | null>(null);
  readonly error = signal<string | null>(null);
  readonly sending = signal(false);
  readonly summary = signal<AiSummaryDto | null>(null);
  readonly summaryLoading = signal(false);
  readonly summaryError = signal(false);

  /** 0 = Summary, 1 = Review, 2 = Harvey, 3 = Signing and follow-up. */
  readonly tabIndex = signal(0);
  readonly readiness = signal<Review | null>(null);
  readonly reviewing = signal(false);
  private readonly reviews = inject(ReviewService);

  /** Computed by the API from Harvey's current findings. */
  readonly riskScore = computed(() => this.doc()?.riskScore ?? null);

  /** Fields someone actually signs; text, date and other filled-in fields are not part of the signing progress. */
  readonly signingFields = () => this.doc()?.fields.filter(f => ['Signature', 'Initials', 'Stamp'].includes(f.fieldType)) ?? [];
  readonly signedCount = () => this.signingFields().filter(f => f.isSigned).length;
  readonly reopening = signal(false);
  readonly uploadingRevision = signal(false);

  readonly editingObligation = signal(false);
  readonly savingObligation = signal(false);
  obligationDueDateInput = '';
  obligationTypeInput = '';

  /** Owner and admins can resolve/delete any comment thread. */
  readonly canModerate = () =>
    this.auth.isAdmin() || this.doc()?.owner.id === this.auth.user()?.id;

  readonly isOwner = () => this.doc()?.owner.id === this.auth.user()?.id;

  /** Running the review and resolving its items are owner/admin actions; everyone else reads it. */
  readonly canReview = () => this.isOwner() || this.auth.isAdmin();

  /** The participant whose change request paused the document. */
  readonly changeRequester = () =>
    this.doc()?.recipients.find(r => r.status === 'ChangesRequested') ?? null;

  readonly rejecter = () =>
    this.doc()?.recipients.find(r => r.status === 'Rejected') ?? null;

  // ── Status card ──────────────────────────────────────────────────────────────────────────

  private tr(key: string, params?: object): string {
    return this.translate.instant(`documents.detail.${key}`, params);
  }

  railTone(): 'error' | 'warn' | 'success' | 'info' | 'neutral' {
    switch (this.doc()?.status) {
      case 'Rejected': case 'Expired': return 'error';
      case 'ChangesRequested': return 'warn';
      case 'Completed': return 'success';
      case 'Draft': case 'Cancelled': return 'neutral';
      default: return 'info';
    }
  }

  railIcon(): string {
    switch (this.doc()?.status) {
      case 'Rejected': return 'cancel';
      case 'ChangesRequested': return 'published_with_changes';
      case 'Completed': return 'verified';
      case 'Expired': return 'schedule';
      case 'Draft': return 'edit_note';
      case 'Cancelled': return 'block';
      default: return 'hourglass_top';
    }
  }

  railTitle(): string {
    switch (this.doc()?.status) {
      case 'Rejected': return this.tr('rejectedTitle');
      case 'ChangesRequested': return this.tr('changesRequestedTitle');
      case 'Completed': return this.tr('completedTitle');
      default: return this.statusLabel();
    }
  }

  railSub(): string {
    const d = this.doc();
    if (!d) return '';
    switch (d.status) {
      case 'Rejected': {
        const rj = this.rejecter();
        if (!rj) return this.tr('rejectedHint');
        return this.tr('rejectedBy', { name: rj.name }) +
          (rj.declineReason ? ` — “${rj.declineReason}”` : ` — ${this.tr('rejectedNoReason')}`);
      }
      case 'ChangesRequested': {
        const cr = this.changeRequester();
        if (!cr) return this.tr('changesRequestedHint');
        return this.tr('changesRequestedBy', { name: cr.name }) + (cr.declineReason ? ` — “${cr.declineReason}”` : '');
      }
      case 'Completed': {
        const date = d.completedAt ? new Date(d.completedAt).toLocaleDateString(this.translate.getCurrentLang() ?? undefined) : '';
        return this.tr('allSignedOn', { date }) + (d.verificationCode ? ` · ${d.verificationCode}` : '');
      }
      case 'Draft': return this.tr('rail.draftHint');
      case 'Expired': case 'Cancelled': return '';
      default: {
        const current = this.workflowParticipants().find(p => p.isCurrent);
        return current ? this.tr('rail.waitingOn', { name: current.name }) : '';
      }
    }
  }

  railSteps(): { key: string; label: string; state: 'done' | 'current' | 'error' | 'warn' | 'todo'; note?: string }[] {
    const d = this.doc();
    if (!d) return [];
    const r = this.readiness();
    const reviewed = d.readinessVerdict !== 'NotEvaluated';
    const gates = d.recipients.filter(x => x.approvalType !== 'ViewOnly');
    const doneCount = gates.filter(x => x.status === 'Completed').length;

    let signature: { state: 'done' | 'current' | 'error' | 'warn' | 'todo'; note?: string };
    switch (d.status) {
      case 'Rejected':
        signature = { state: 'error', note: this.rejecter() ? this.tr('rail.declinedBy', { name: this.rejecter()!.name }) : undefined };
        break;
      case 'ChangesRequested': signature = { state: 'warn', note: this.tr('rail.paused') }; break;
      case 'Completed': signature = { state: 'done', note: gates.length ? `${doneCount}/${gates.length}` : undefined }; break;
      case 'Draft': case 'Cancelled': case 'Expired': signature = { state: 'todo' }; break;
      default: signature = { state: 'current', note: gates.length ? `${doneCount}/${gates.length}` : undefined };
    }

    return [
      { key: 'imported', label: this.tr('rail.imported'), state: 'done' },
      {
        key: 'review', label: this.tr('rail.review'), state: reviewed ? 'done' : 'todo',
        note: reviewed && r ? (r.openCount > 0 ? this.tr('rail.reviewOpen', { count: r.openCount }) : this.tr('rail.reviewHandled')) : undefined
      },
      { key: 'signature', label: this.tr('rail.signature'), ...signature },
      { key: 'active', label: this.tr('rail.active'), state: d.status === 'Completed' ? 'done' : 'todo' }
    ];
  }

  readonly showAllFields = signal(false);

  /** Who a field belongs to, for the fields summary. */
  recipientNameOf(recipientId?: string | null): string | null {
    return recipientId ? this.doc()?.recipients.find(r => r.id === recipientId)?.name ?? null : null;
  }

  /** The review lives in the Summary tab: go there and bring the checklist into view. */
  showChecklist(): void {
    this.tabIndex.set(0);
    setTimeout(() => document.getElementById('readiness-checklist')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  /** Green when the score is good: a high risk score is bad, a high readiness score is good. */
  ringColor(value: number, highIsGood: boolean): string {
    const good = highIsGood ? value : 100 - value;
    if (good >= 70) return 'var(--g-green)';
    if (good >= 40) return 'var(--g-yellow)';
    return 'var(--g-red)';
  }

  /** The few things worth doing next on this contract, most pressing first. */
  nextActions(): { key: string; tone: 'danger' | 'warning' | 'muted'; icon: string; title: string; sub: string; go: () => void }[] {
    const d = this.doc();
    if (!d) return [];
    const out: { key: string; tone: 'danger' | 'warning' | 'muted'; icon: string; title: string; sub: string; go: () => void }[] = [];

    if (d.status === 'Rejected') {
      out.push({ key: 'declined', tone: 'danger', icon: 'cancel', title: this.tr('next.fixDecline'), sub: this.tr('next.fixDeclineSub'), go: () => this.tabIndex.set(2) });
    }
    const r = this.readiness();
    const severe = (r?.criticalCount ?? 0) + (r?.highCount ?? 0);
    if (severe > 0) {
      out.push({ key: 'risks', tone: 'warning', icon: 'warning', title: this.tr('next.risks', { count: severe }), sub: this.tr('next.risksSub'), go: () => this.showChecklist() });
    }
    if (!d.contractEndDate && this.canReview()) {
      out.push({
        key: 'term', tone: 'muted', icon: 'event', title: this.tr('next.setTerm'), sub: this.tr('next.setTermSub'),
        go: () => document.querySelector('sf-contract-term-panel')?.scrollIntoView({ behavior: 'smooth', block: 'center' })
      });
    }
    return out;
  }

  /** Other authenticated users viewing this document right now (self excluded). */
  readonly activeViewers = computed(() => {
    const p = this.hub.presence();
    if (!p || p.documentId !== this.documentId) return [];
    const myId = this.auth.user()?.id;
    return p.viewers.filter(v => v.userId !== myId);
  });

  presenceTooltip(): string {
    return this.activeViewers().map(v => v.name).join(', ');
  }

  remaining(): RemainingTime | null {
    const d = this.doc();
    // ChangesRequested pauses the flow (the SLA clock restarts on revision upload) — no countdown.
    if (!d || d.status === 'Completed' || d.status === 'Rejected' || d.status === 'Draft' || d.status === 'ChangesRequested') return null;
    return remainingTime(d.expiresAt);
  }

  onRevisionFile(event: Event, changeNote: string): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.uploadingRevision.set(true);
    this.documentService.uploadRevision(this.documentId, file, changeNote).subscribe({
      next: r => {
        this.uploadingRevision.set(false);
        this.snack.open(this.translate.instant('documents.detail.revisionUploaded', { version: r.version }), 'OK', { duration: 5000 });
        this.load();
      },
      error: err => {
        this.uploadingRevision.set(false);
        this.snack.open(err.error?.detail ?? this.translate.instant('documents.detail.revisionFailed'), 'OK', { duration: 6000 });
      }
    });
  }

  /** Draft input for the "add checklist item" box, keyed by recipient id. */
  newChecklistText: Record<string, string> = {};

  addChecklistItem(recipientId: string): void {
    const text = (this.newChecklistText[recipientId] ?? '').trim();
    if (!text) return;
    const recipient = this.doc()?.recipients.find(r => r.id === recipientId);
    if (!recipient) return;
    const items = [...(recipient.checklistItems ?? []).map(i => i.text), text];
    this.newChecklistText[recipientId] = '';
    this.saveChecklist(recipientId, items);
  }

  removeChecklistItem(recipientId: string, index: number): void {
    const recipient = this.doc()?.recipients.find(r => r.id === recipientId);
    if (!recipient?.checklistItems) return;
    const items = recipient.checklistItems.map(i => i.text).filter((_, i) => i !== index);
    this.saveChecklist(recipientId, items);
  }

  private saveChecklist(recipientId: string, items: string[]): void {
    this.documentService.setChecklist(this.documentId, recipientId, items).subscribe({
      next: () => this.load(),
      error: err => this.snack.open(err.error?.detail ?? this.translate.instant('documents.detail.checklist.saveFailed'), 'OK', { duration: 4000 })
    });
  }

  private static readonly ACTIONS: Record<string, { labelKey: string; icon: string }> = {
    Signature: { labelKey: 'requests.signNow', icon: 'draw' },
    Stamp: { labelKey: 'requests.signNow', icon: 'draw' },
    Approval: { labelKey: 'requests.approveNow', icon: 'fact_check' },
    Review: { labelKey: 'requests.reviewNow', icon: 'rate_review' },
    ViewOnly: { labelKey: 'requests.viewNow', icon: 'visibility' }
  };

  /**
   * What the signed-in participant has to do next, if it is their turn: their own recipient row, an active
   * contract and a workflow step of theirs that is in progress (the same rule "My requests" uses).
   */
  readonly myAction = () => {
    const d = this.doc();
    const email = this.auth.user()?.email?.toLowerCase();
    if (!d || !email) return null;
    if (!['InProgress', 'Pending', 'WaitingForOthers'].includes(d.status)) return null;

    const me = d.recipients.find(r => r.email.toLowerCase() === email && r.secureToken);
    if (!me) return null;

    const myTurn = d.workflow?.steps?.length
      ? d.workflow.steps.some(s => s.recipientId === me.id && s.status === 'InProgress')
      : !d.recipients.some(o => o.order < me.order && (o.status === 'Pending' || o.status === 'InProgress'));
    if (!myTurn || !['Pending', 'InProgress'].includes(me.status)) return null;

    const action = DocumentDetailComponent.ACTIONS[me.approvalType] ?? DocumentDetailComponent.ACTIONS['Signature'];
    return { ...action, token: me.secureToken };
  };

  continueToAction(token: string): void {
    this.router.navigate(['/sign', token]);
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
      error: err => { this.reopening.set(false); this.snack.open(err.error?.detail ?? this.translate.instant('documents.detail.reopenFailed'), 'OK', { duration: 4000 }); }
    });
  }

  startEditObligation(): void {
    const d = this.doc();
    this.obligationDueDateInput = d?.obligationDueDate ? d.obligationDueDate.substring(0, 10) : '';
    this.obligationTypeInput = d?.obligationType ?? '';
    this.editingObligation.set(true);
  }

  cancelEditObligation(): void {
    this.editingObligation.set(false);
  }

  saveObligation(): void {
    if (!this.obligationDueDateInput || !this.obligationTypeInput) return;
    this.savingObligation.set(true);
    this.documentService.updateObligation(this.documentId, this.obligationDueDateInput, this.obligationTypeInput.trim()).subscribe({
      next: () => { this.savingObligation.set(false); this.editingObligation.set(false); this.load(); },
      error: err => {
        this.savingObligation.set(false);
        this.snack.open(err.error?.detail ?? this.translate.instant('documents.detail.obligation.saveFailed'), 'OK', { duration: 4000 });
      }
    });
  }

  readonly workflowParticipants = (): WorkflowParticipant[] => {
    // Viewers aren't workflow gates — keep them out of the signing-progress visualizer.
    const recipients = (this.doc()?.recipients ?? []).filter(r => r.approvalType !== 'ViewOnly');
    if (recipients.length === 0) return [];
    const ordered = [...recipients].sort((a, b) => a.order - b.order);
    const current = ordered.find(r =>
      r.status !== 'Completed' && r.status !== 'Rejected' && r.status !== 'Skipped');
    // A declined contract is over: no later step is "signing now".
    const over = this.doc()?.status === 'Completed' || this.doc()?.status === 'Rejected';
    return ordered.map(r => ({
      order: r.order,
      name: r.name,
      status: r.status as WorkflowParticipant['status'],
      isCurrent: r.id === current?.id && !over,
      signedAt: r.signedAt
    }));
  };

  private documentId = '';

  readonly STATUS_COLORS: Record<string, string> = {
    Draft: '#5f6368', Sent: '#1a73e8', Completed: '#22c55e',
    Rejected: '#ef4444', Expired: '#f97316', WaitingForOthers: '#8b5cf6',
    ChangesRequested: '#f59e0b'
  };

  /**
   * A participant who never got to act on a contract that ended without them (declined, cancelled, expired) is not
   * "pending" any more: nothing is expected from them. Returns the marker for the status to show instead, else null.
   */
  stoppedFor(r: { status: string }): 'Rejected_' | 'Expired_' | null {
    if (r.status !== 'Pending' && r.status !== 'InProgress') return null;
    const ended = this.doc()?.status;
    if (ended === 'Rejected' || ended === 'Cancelled') return 'Rejected_';
    if (ended === 'Expired') return 'Expired_';
    return null;
  }

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
    // Only the first load shows the spinner: a refresh after an action (saving the term, an analysis) updates the
    // page in place, so open panels keep their state and the reader keeps their place.
    if (!this.doc()) this.loading.set(true);
    this.documentService.getDocument(this.documentId).subscribe({
      next: doc => {
        this.doc.set(doc);
        this.loading.set(false);
        if (doc.aiSummary && doc.aiSummaryGeneratedAt) {
          this.summary.set({ summary: doc.aiSummary, generatedAt: doc.aiSummaryGeneratedAt });
        }
        this.loadReadiness();
      },
      error: () => { this.error.set(this.translate.instant('documents.detail.notFound')); this.loading.set(false); }
    });
  }

  /** The findings are a separate read: the detail payload only carries the headline verdict. A review still running
   *  (started at upload, or in another tab) is followed until it ends. */
  private loadReadiness(): void {
    this.documentService.getReview(this.documentId).subscribe({
      next: r => {
        this.readiness.set(r);
        const run = r.latestRun;
        if (run && (run.status === 'Queued' || run.status === 'Running') && !this.reviewing()) {
          this.reviewing.set(true);
          this.reviews.waitFor(this.documentId, run.id).subscribe({
            next: () => { this.reviewing.set(false); this.load(); },
            error: (err: Error) => { this.reviewing.set(false); this.snack.open(err.message, 'OK', { duration: 6000 }); this.load(); }
          });
        }
      },
      error: () => this.readiness.set(null)
    });
  }

  onReadinessChanged(review: Review): void {
    this.readiness.set(review);
    // The verdict on the band comes off the document, which the API just re-graded.
    const doc = this.doc();
    if (doc) {
      this.doc.set({
        ...doc, readinessVerdict: review.verdict,
        readinessScore: review.readinessScore ?? undefined, riskScore: review.riskScore ?? undefined
      });
    }
  }

  /** Harvey's review of the current version, in the background; the page follows it to the end. */
  runReview(): void {
    // The first review lands on the summary; a re-run leaves the reader on the tab they were looking at.
    const first = this.doc()?.readinessVerdict === 'NotEvaluated';
    this.reviewing.set(true);
    this.reviews.run(this.documentId).subscribe({
      next: r => {
        this.reviewing.set(false);
        this.readiness.set(r);
        if (first) this.tabIndex.set(0);
        this.load();
      },
      error: (err: Error) => {
        this.reviewing.set(false);
        this.snack.open(err.message, 'OK', { duration: 6000 });
        this.load();
      }
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
    this.confirmService.confirm({
      message: this.translate.instant('documents.detail.confirmSend')
    }).subscribe(ok => {
      if (!ok) return;
      this.doSend();
    });
  }

  /**
   * Through the review gate: refused when this version is not reviewed or has an open critical finding (the reader is
   * taken to the findings); with open high-risk findings the reader gives a reason, kept in the audit trail.
   */
  private doSend(): void {
    this.sending.set(true);
    this.reviews.send(this.documentId).subscribe({
      next: sent => { this.sending.set(false); if (sent) this.load(); },
      error: (err: Error & { code?: string | null }) => {
        this.sending.set(false);
        if (err.code === 'readiness' || err.code === 'readinessRequired') this.showChecklist();
        this.snack.open(err.message, 'OK', { duration: 6000 });
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

  /** The version is in the filename: two drafts sharing one name is how the wrong one gets circulated. */
  downloadVersion(versionNumber: number): void {
    this.documentService.downloadVersion(this.documentId, versionNumber).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.doc()?.title ?? 'document'}-v${versionNumber}.pdf`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.snack.open(this.translate.instant('documents.detail.compare.downloadFailed'), 'OK', { duration: 4000 })
    });
  }

  /** Owner of a PDF contract that is still editable: a draft, or one a participant sent back. */
  readonly canEditText = () => {
    const d = this.doc();
    return !!d && this.isOwner()
      && (d.status === 'Draft' || d.status === 'ChangesRequested')
      && !d.templateKind
      && d.fileName.toLowerCase().endsWith('.pdf');
  };

  readonly revisionsNewestFirst = () => [...(this.doc()?.templateRevisions ?? [])].reverse();

  /** Opens the form the owner fills again; the contract returns to the reviewers once it is sent. */
  reviseAnswers(): void {
    const detail = this.doc();
    if (!detail) return;

    this.dialog.open(TemplateRevisionDialogComponent, {
      data: { documentId: this.documentId, documentTitle: detail.title } satisfies TemplateRevisionDialogData,
      width: '640px',
      maxWidth: '96vw',
      disableClose: true
    }).afterClosed().subscribe(result => {
      if (result) this.load();
    });
  }

  editText(): void {
    const detail = this.doc();
    if (!detail) return;

    this.dialog.open(PdfTextEditorDialogComponent, {
      data: { documentId: this.documentId, documentTitle: detail.title } satisfies PdfTextEditorDialogData,
      width: '1000px',
      maxWidth: '96vw',
      disableClose: true
    }).afterClosed().subscribe(result => {
      if (result) this.load();
    });
  }

  compareVersions(): void {
    const detail = this.doc();
    if (!detail) return;

    this.dialog.open(VersionCompareDialogComponent, {
      data: {
        documentId: this.documentId,
        documentTitle: detail.title,
        currentVersion: detail.version,
        versions: detail.versions ?? []
      } satisfies VersionCompareDialogData,
      maxWidth: '95vw'
    });
  }
}
