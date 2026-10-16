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
import { DocumentDetail, WorkflowParticipant, AiSummaryDto, Readiness } from '../../core/models';
import { ReadinessVerdictComponent } from '../../shared/readiness-verdict/readiness-verdict.component';
import { ReadinessChecklistComponent } from '../../shared/readiness-checklist/readiness-checklist.component';
import { WorkflowVisualizerComponent } from '../../shared/workflow-visualizer/workflow-visualizer.component';
import { CommentsPanelComponent } from '../../shared/comments-panel/comments-panel.component';
import { ContractAnalysisPanelComponent } from './contract-analysis-panel.component';
import { ContractCompliancePanelComponent } from './contract-compliance-panel.component';
import { ContractTermPanelComponent } from './contract-term-panel.component';
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
    MatTabsModule, MatSnackBarModule, WorkflowVisualizerComponent, CommentsPanelComponent,
    ContractAnalysisPanelComponent, ContractCompliancePanelComponent, ContractTermPanelComponent,
    ReadinessVerdictComponent, ReadinessChecklistComponent, TranslatePipe
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
                <a mat-stroked-button [routerLink]="['/documents/new-request']" [queryParams]="{ documentId: doc()!.id }">
                  <mat-icon>checklist</mat-icon> {{ 'documents.detail.continueSetup' | translate }}
                </a>
              }
              @if (canEditText()) {
                <button mat-stroked-button (click)="editText()">
                  <mat-icon>edit_note</mat-icon> {{ 'documents.detail.editText.button' | translate }}
                </button>
              }
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

        <!-- Changes-requested banner: a participant paused the flow; the owner answers with a revised PDF -->
        @if (doc()!.status === 'ChangesRequested') {
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
        } @else {
          <div class="intel-band" (click)="tabIndex.set(1)">
            <sf-readiness-verdict [compact]="true"
              [verdict]="doc()!.readinessVerdict"
              [score]="doc()!.readinessScore"
              [riskScore]="riskScore()"
              [complianceScore]="complianceScore()"
              [openCount]="readiness()?.openCount ?? 0"
              [criticalCount]="readiness()?.criticalCount ?? 0"
              [highCount]="readiness()?.highCount ?? 0"
              [handledCount]="readiness()?.handledCount ?? 0">
            </sf-readiness-verdict>
          </div>
        }

        <mat-tab-group class="detail-tabs" [selectedIndex]="tabIndex()" (selectedIndexChange)="tabIndex.set($event)">
          <mat-tab>
            <ng-template mat-tab-label>
              <mat-icon class="tab-icon">assignment</mat-icon> {{ 'documents.detail.tabs.overview' | translate }}
            </ng-template>

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

          </div>
        </div>
          </mat-tab>

          <mat-tab>
            <ng-template mat-tab-label>
              <mat-icon class="tab-icon">gavel</mat-icon> {{ 'documents.detail.tabs.intelligence' | translate }}
              @if ((readiness()?.openCount ?? 0) > 0) {
                <span class="tab-badge">{{ readiness()!.openCount }}</span>
              }
            </ng-template>

            <!--
              Full page width here, not the 340px rail: the analysis panel carries a contract
              profile grid, severity-grouped findings and proposed wording, none of which fit in a
              sidebar.
            -->
            <div class="intel-layout">
              @if (readiness(); as r) {
                <mat-card class="readiness-card">
                  <mat-card-content>
                    <div class="readiness-head">
                      <sf-readiness-verdict
                        [verdict]="r.verdict" [score]="r.score"
                        [riskScore]="riskScore()" [complianceScore]="complianceScore()"
                        [openCount]="r.openCount" [criticalCount]="r.criticalCount"
                        [highCount]="r.highCount" [handledCount]="r.handledCount">
                      </sf-readiness-verdict>
                      @if (canReview()) {
                        <button mat-stroked-button (click)="runReview()" [disabled]="reviewing()">
                          <mat-icon>refresh</mat-icon>
                          {{ (reviewing() ? 'documents.detail.readiness.running' : 'documents.detail.readiness.rerun') | translate }}
                        </button>
                      }
                    </div>
                    <sf-readiness-checklist
                      [documentId]="doc()!.id"
                      [items]="r.items"
                      [readonly]="!canReview()"
                      (changed)="onReadinessChanged($event)">
                    </sf-readiness-checklist>
                  </mat-card-content>
                </mat-card>
              }

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

            <!-- Contract Risk Analysis -->
            <sf-contract-analysis-panel [wide]="true"
              [documentId]="doc()!.id"
              [riskAnalysisJson]="doc()!.aiRiskAnalysisJson"
              [generatedAt]="doc()!.aiRiskAnalysisGeneratedAt"
              (analyzed)="load()">
            </sf-contract-analysis-panel>

            <!-- Regulatory Compliance Check -->
            <sf-contract-compliance-panel [wide]="true"
              [documentId]="doc()!.id"
              [complianceAnalysisJson]="doc()!.aiComplianceAnalysisJson"
              [generatedAt]="doc()!.aiComplianceAnalysisGeneratedAt"
              (analyzed)="load()">
            </sf-contract-compliance-panel>

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
          </mat-tab>
        </mat-tab-group>
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
  private readonly confirmService = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);

  readonly loading = signal(true);
  readonly doc = signal<DocumentDetail | null>(null);
  readonly error = signal<string | null>(null);
  readonly sending = signal(false);
  readonly summary = signal<AiSummaryDto | null>(null);
  readonly summaryLoading = signal(false);
  readonly summaryError = signal(false);

  /** 0 = Overview, 1 = Legal intelligence. */
  readonly tabIndex = signal(0);
  readonly readiness = signal<Readiness | null>(null);
  readonly reviewing = signal(false);

  /** The stored analyses hold the sub-scores; parse them once rather than on every render. */
  readonly riskScore = computed(() => this.scoreFrom(this.doc()?.aiRiskAnalysisJson, 'riskScore'));
  readonly complianceScore = computed(() => this.scoreFrom(this.doc()?.aiComplianceAnalysisJson, 'complianceScore'));

  private scoreFrom(json: string | undefined, key: 'riskScore' | 'complianceScore'): number | null {
    if (!json) return null;
    try {
      const value = (JSON.parse(json) as Record<string, unknown>)[key];
      return typeof value === 'number' ? value : null;
    } catch {
      return null;
    }
  }

  readonly signedCount = () => this.doc()?.fields.filter(f => f.isSigned).length ?? 0;
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
    Rejected: '#ef4444', Expired: '#f97316', WaitingForOthers: '#8b5cf6',
    ChangesRequested: '#f59e0b'
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
        this.loadReadiness();
      },
      error: () => { this.error.set(this.translate.instant('documents.detail.notFound')); this.loading.set(false); }
    });
  }

  /** The to-do items are a separate read — the detail payload only carries the headline verdict. */
  private loadReadiness(): void {
    this.documentService.getReadiness(this.documentId).subscribe({
      next: r => this.readiness.set(r),
      error: () => this.readiness.set(null)
    });
  }

  onReadinessChanged(readiness: Readiness): void {
    this.readiness.set(readiness);
    // The verdict on the band comes off the document, which the API just re-graded.
    const doc = this.doc();
    if (doc) {
      this.doc.set({ ...doc, readinessVerdict: readiness.verdict, readinessScore: readiness.score });
    }
  }

  /** Full pre-flight review: the model legal review plus the regulatory check, then a re-grade. */
  runReview(): void {
    this.reviewing.set(true);
    this.documentService.evaluateReadiness(this.documentId).subscribe({
      next: r => {
        this.reviewing.set(false);
        this.readiness.set(r);
        this.tabIndex.set(1);
        // Reload for the refreshed risk/compliance JSON the panels below render from.
        this.load();
      },
      error: err => {
        this.reviewing.set(false);
        this.snack.open(err.error?.detail ?? this.translate.instant('documents.detail.readiness.failed'), 'OK', { duration: 5000 });
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
      this.doSend(false);
    });
  }

  /**
   * The API refuses to send a contract the pre-flight review flagged. A "needs work" verdict is an
   * acknowledgeable warning (re-sent with force); a "blocked" one is not — it points the owner at
   * the Intelligence tab instead.
   */
  private doSend(force: boolean): void {
    this.sending.set(true);
    this.documentService.send(this.documentId, force).subscribe({
      next: () => { this.sending.set(false); this.load(); },
      error: err => {
        this.sending.set(false);
        const errors = err.error?.errors as Record<string, string[]> | undefined;

        if (errors?.['readinessWarning']) {
          const open = this.readiness()?.openCount ?? 0;
          this.confirmService.confirm({
            message: this.translate.instant('documents.detail.readiness.sendAnyway', { count: open }),
            confirmLabel: this.translate.instant('documents.detail.readiness.sendAnywayConfirm')
          }).subscribe(ok => { if (ok) this.doSend(true); });
          return;
        }

        if (errors?.['readiness']) {
          this.tabIndex.set(1);
          this.snack.open(this.translate.instant('documents.detail.readiness.sendBlocked'), 'OK', { duration: 6000 });
          return;
        }

        this.snack.open(err.error?.detail ?? this.translate.instant('documents.detail.sendFailed'), 'OK', { duration: 4000 });
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
