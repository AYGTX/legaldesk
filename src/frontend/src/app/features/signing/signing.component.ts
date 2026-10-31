import {
  Component, OnInit, OnDestroy, inject, signal, computed, effect,
  ViewChild, ElementRef, AfterViewInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { DelegateDialogComponent, DelegateDialogData } from './delegate-dialog.component';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatStepperModule } from '@angular/material/stepper';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ACCOUNT_SESSION, SigningService } from '../../core/services/signing.service';
import { ReviewGateComponent } from '../../shared/review-gate/review-gate.component';
import { ConfirmService } from '../../core/services/confirm.service';
import { SigningSession, AssignedField, FilledField, SignatureType, ChecklistItemInfo, SigningIntro } from '../../core/models';
import { SignatureDialogComponent } from './signature-dialog.component';
import { WorkflowVisualizerComponent } from '../../shared/workflow-visualizer/workflow-visualizer.component';
import { CommentsPanelComponent } from '../../shared/comments-panel/comments-panel.component';
import { remainingTime, RemainingTime } from '../../core/utils/sla';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'sf-signing',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatDialogModule, MatFormFieldModule, MatInputModule,
    MatStepperModule, MatChipsModule, MatTooltipModule, MatSnackBarModule,
    WorkflowVisualizerComponent, CommentsPanelComponent, ReviewGateComponent, TranslatePipe
  ],
  template: `
    <div class="signing-layout">
      <!-- Top bar -->
      <div class="signing-topbar">
        <div class="brand">
          <mat-icon class="brand-icon">gavel</mat-icon>
          <span>{{ appName }}</span>
        </div>
        <div class="doc-info">
          @if (session()) {
            <span class="doc-name">{{ session()!.documentTitle }}</span>
            @if (isNonSigner()) {
              <span class="field-progress">{{ roleBannerText() }}</span>
            } @else {
              <span class="field-progress">
                {{ fieldsCompletedText() }}
              </span>
            }
          }
        </div>
        @if (remaining(); as r) {
          <span class="sla-pill" [class]="'rem-' + r.level" [matTooltip]="'signing.topbar.slaTooltip' | translate">
            <mat-icon>{{ r.expired ? 'error' : 'timer' }}</mat-icon> {{ r.text }}
          </span>
        }
        @if (linkRemaining(); as lr) {
          <span class="sla-pill link-pill" [class]="'rem-' + lr.level" [matTooltip]="'signing.topbar.linkTooltip' | translate">
            <mat-icon>{{ lr.expired ? 'link_off' : 'link' }}</mat-icon>
            {{ 'signing.topbar.linkPillLabel' | translate: { time: lr.text } }}
          </span>
        }
        @if (session() && session()!.isMyTurn && !session()!.isCompleted && !session()!.isRejected && !session()!.isExpired && session()!.documentStatus !== 'ChangesRequested' && session()!.recipient.approvalType !== 'ViewOnly') {
          @if (reviewMode()) {
            <div class="topbar-actions">
              <button mat-stroked-button (click)="backToFields()">
                <mat-icon>arrow_back</mat-icon> {{ 'signing.topbar.backToFields' | translate }}
              </button>
              <button mat-flat-button color="primary" (click)="approve()" [disabled]="approving() || !checklistComplete() || !gateCleared()"
                      [matTooltip]="!checklistComplete() ? ('signing.checklist.incompleteTooltip' | translate) : ''">
                <mat-icon>verified</mat-icon>
                {{ (approving() ? 'signing.topbar.submitting' : 'signing.topbar.approveSubmit') | translate }}
              </button>
            </div>
          } @else {
            <div class="topbar-actions">
              @if (isNonSigner()) {
                <button mat-stroked-button class="request-changes-btn" (click)="openRequestChangesDialog()">
                  <mat-icon>published_with_changes</mat-icon> {{ 'signing.topbar.requestChanges' | translate }}
                </button>
              }
              @if (canDelegate()) {
                <button mat-stroked-button (click)="openDelegateDialog()">
                  <mat-icon>forward_to_inbox</mat-icon> {{ 'signing.delegate.button' | translate }}
                </button>
              }
              <button mat-stroked-button color="warn" (click)="openRejectDialog()">
                <mat-icon>cancel</mat-icon> {{ 'signing.topbar.decline' | translate }}
              </button>
              <button mat-flat-button color="primary" (click)="finish()"
                      [disabled]="!allFieldsSigned() || finishing() || !gateCleared()">
                <mat-icon>rate_review</mat-icon>
                {{ (finishing() ? 'signing.topbar.loadingEllipsis' : (isNonSigner() ? 'signing.topbar.reviewApprove' : 'signing.topbar.reviewSubmit')) | translate }}
              </button>
            </div>
          }
        }
      </div>

      @if (needsCode()) {
        <!-- R-M5-3: the link shows what is asked; a 6-digit code sent to the participant opens the contract. -->
        <div class="status-screen verify-screen">
          <mat-icon>mark_email_read</mat-icon>
          <h2>{{ 'signing.verify.title' | translate }}</h2>
          @if (intro(); as i) {
            <p>{{ 'signing.verify.intro' | translate: { sender: i.senderName, title: i.documentTitle, name: i.participantName } }}</p>
            <p class="verify-email">{{ 'signing.verify.sentTo' | translate: { email: i.maskedEmail } }}</p>
          }
          @if (!codeSent()) {
            <button mat-flat-button color="primary" (click)="sendCode()" [disabled]="codeBusy()">
              {{ 'signing.verify.send' | translate }}
            </button>
          } @else {
            <label class="code-label" for="signing-code">{{ 'signing.verify.enter' | translate }}</label>
            <input id="signing-code" class="code-input" inputmode="numeric" maxlength="6" autocomplete="one-time-code"
                   [value]="code()" (input)="code.set($any($event.target).value)" (keyup.enter)="verifyCode()" />
            <div class="verify-actions">
              <button mat-flat-button color="primary" (click)="verifyCode()" [disabled]="codeBusy() || code().trim().length !== 6">
                {{ 'signing.verify.confirm' | translate }}
              </button>
              <button mat-button (click)="sendCode()" [disabled]="codeBusy()">{{ 'signing.verify.resend' | translate }}</button>
            </div>
          }
          @if (codeError()) { <p class="code-error">{{ codeError() }}</p> }
          <p class="verify-note">{{ 'signing.verify.note' | translate }}</p>
        </div>
      } @else if (loading()) {
        <div class="loading-state"><mat-spinner diameter="48"></mat-spinner><p>{{ 'signing.loadingDocument' | translate }}</p></div>
      } @else if (session()) {

        @if (showReviewGate()) {
          <sf-review-gate [gate]="session()!.reviewGate!" [busy]="acknowledging()" (confirmed)="acknowledgeReview()" />
        }

        @if (session()!.recipient.approvalType === 'ViewOnly') {
          <!-- Viewer experience: read + comment, never a workflow gate -->
          <div class="status-screen viewer">
            <mat-icon>visibility</mat-icon>
            <h2>{{ 'signing.viewer.title' | translate }}</h2>
            <p>{{ 'signing.viewer.body' | translate: { title: session()!.documentTitle } }}</p>
            @if (session()!.isHtmlTemplate && session()!.renderedHtmlContent) {
              <div class="viewer-doc contract-doc" [innerHTML]="session()!.renderedHtmlContent" (click)="onHtmlClick($event)"></div>
            } @else {
              <button mat-flat-button color="primary" (click)="downloadDocument()" [disabled]="downloading()">
                <mat-icon>open_in_new</mat-icon>
                {{ (downloading() ? 'signing.status.downloading' : 'signing.viewer.openDocument') | translate }}
              </button>
            }
            <div class="waiting-flow">
              <sf-workflow-visualizer [participants]="session()!.participants" [showHeader]="false"></sf-workflow-visualizer>
            </div>
            <div class="waiting-comments">
              <sf-comments-panel [token]="token"></sf-comments-panel>
            </div>
          </div>

        } @else if (session()!.documentStatus === 'ChangesRequested' || session()!.documentStatus === 'Draft') {
          <!-- Revision loop: paused after a change request, or back in Draft while the owner revises -->
          <div class="status-screen changes">
            <mat-icon>published_with_changes</mat-icon>
            @if (session()!.recipient.status === 'ChangesRequested') {
              <h2>{{ 'signing.status.changesRequestedOwnTitle' | translate }}</h2>
              <p>{{ 'signing.status.changesRequestedOwnBody' | translate }}</p>
            } @else if (session()!.documentStatus === 'Draft') {
              <h2>{{ 'signing.status.revisionInProgressTitle' | translate }}</h2>
              <p>{{ 'signing.status.revisionInProgressBody' | translate }}</p>
            } @else {
              <h2>{{ 'signing.status.changesRequestedTitle' | translate }}</h2>
              <p>{{ 'signing.status.changesRequestedBody' | translate }}</p>
            }
            <div class="waiting-comments">
              <sf-comments-panel [token]="token"></sf-comments-panel>
            </div>
          </div>

        } @else if (signingStep() === 'summary' && session()!.isMyTurn && !session()!.isCompleted && !session()!.isRejected && !session()!.isExpired) {
          <!-- Pre-signing Summary Screen -->
          <div class="summary-screen">
            <div class="summary-card">
              <div class="summary-card-header">
                <mat-icon class="summary-brand-icon">draw</mat-icon>
                <div>
                  <h1 class="summary-doc-title">{{ session()!.documentTitle }}</h1>
                  <p class="summary-meta">{{ summaryMetaText() }}</p>
                </div>
              </div>

              <div class="summary-divider"></div>

              <div class="summary-body">
                <h2 class="summary-section-title">
                  <mat-icon>auto_awesome</mat-icon>
                  {{ 'signing.summary.title' | translate }}
                </h2>
                @if (summaryLoading()) {
                  <div class="summary-skeleton">
                    <div class="skeleton-line"></div>
                    <div class="skeleton-line w80"></div>
                    <div class="skeleton-line w90"></div>
                    <div class="skeleton-line w70"></div>
                  </div>
                } @else if (summary()) {
                  <p class="summary-text">{{ summary() }}</p>
                } @else {
                  <p class="summary-unavailable">{{ 'signing.summary.unavailable' | translate }}</p>
                }
              </div>

              @if (session()!.message) {
                <div class="summary-message">
                  <mat-icon>message</mat-icon>
                  <p>{{ session()!.message }}</p>
                </div>
              }

              <div class="summary-footer">
                <div class="data-processing-notice">
                  <mat-icon class="notice-icon">privacy_tip</mat-icon>
                  <div class="notice-body">
                    <p class="notice-title">{{ 'signing.summary.gdprTitle' | translate }}</p>
                    <p class="notice-text">
                      {{ 'signing.summary.gdprText' | translate }}
                    </p>
                  </div>
                </div>
                <div class="summary-actions">
                  @if (isNonSigner()) {
                    <button mat-stroked-button class="request-changes-btn" (click)="openRequestChangesDialog()">
                      <mat-icon>published_with_changes</mat-icon> {{ 'signing.topbar.requestChanges' | translate }}
                    </button>
                  }
                  @if (canDelegate()) {
                    <button mat-stroked-button (click)="openDelegateDialog()">
                      <mat-icon>forward_to_inbox</mat-icon> {{ 'signing.delegate.button' | translate }}
                    </button>
                  }
                  <button mat-stroked-button color="warn" (click)="openRejectDialog()">
                    <mat-icon>cancel</mat-icon> {{ 'signing.topbar.decline' | translate }}
                  </button>
                  <button mat-flat-button color="primary" (click)="proceedToSign()">
                    {{ (isNonSigner() ? 'signing.summary.proceedToReview' : 'signing.summary.proceedToSign') | translate }} <mat-icon>arrow_forward</mat-icon>
                  </button>
                </div>
              </div>
            </div>
          </div>

        } @else if (session()!.isCompleted) {
          <div class="status-screen completed">
            <mat-icon>check_circle</mat-icon>
            <h2>{{ 'signing.status.completedTitle' | translate }}</h2>
            <p>{{ 'signing.status.completedBody' | translate: { title: session()!.documentTitle } }}</p>
            <p class="sub">{{ 'signing.status.completedSub' | translate }}</p>
            <div class="status-actions">
              <button mat-flat-button color="primary" (click)="downloadDocument()" [disabled]="downloading()">
                <mat-icon>download</mat-icon>
                {{ (downloading() ? 'signing.status.downloading' : 'signing.status.downloadCopy') | translate }}
              </button>
              <button mat-stroked-button (click)="closeTab()">
                <mat-icon>close</mat-icon> {{ 'common.close' | translate }}
              </button>
            </div>
          </div>
        } @else if (session()!.isRejected) {
          <div class="status-screen rejected">
            <mat-icon>cancel</mat-icon>
            <h2>{{ 'signing.status.rejectedTitle' | translate }}</h2>
            <p>{{ 'signing.status.rejectedBody' | translate }}</p>
            <div class="status-actions">
              <button mat-stroked-button (click)="closeTab()">
                <mat-icon>close</mat-icon> {{ 'common.close' | translate }}
              </button>
            </div>
          </div>
        } @else if (session()!.isExpired) {
          <div class="status-screen expired">
            <mat-icon>schedule</mat-icon>
            <h2>{{ 'signing.status.expiredTitle' | translate }}</h2>
            <p>{{ 'signing.status.expiredBody' | translate }}</p>
          </div>
        } @else if (!session()!.isMyTurn) {
          <div class="status-screen waiting">
            <mat-icon>hourglass_top</mat-icon>
            <h2>{{ 'signing.status.waitingTitle' | translate }}</h2>
            <p>
              {{ 'signing.status.waitingIntro' | translate }}
              @if (session()!.waitingForName) {
                {{ 'signing.status.waitingForName' | translate: { name: session()!.waitingForName } }}
              } @else {
                {{ 'signing.status.waitingGeneric' | translate }}
              }
            </p>
            <p class="sub">{{ 'signing.status.waitingSub' | translate }}</p>
            <div class="waiting-flow">
              <sf-workflow-visualizer [participants]="session()!.participants" [showHeader]="false"></sf-workflow-visualizer>
            </div>
            <!-- Participants can discuss the document before it's their turn to sign -->
            <div class="waiting-comments">
              <sf-comments-panel [token]="token"></sf-comments-panel>
            </div>
          </div>
        } @else if (reviewMode()) {
          <!-- Review Mode -->
          <div class="review-layout">
            <div class="review-header">
              <mat-icon class="review-icon">rate_review</mat-icon>
              <div>
                <h2>{{ 'signing.review.title' | translate }}</h2>
                <p>{{ 'signing.review.intro' | translate }}</p>
              </div>
            </div>

            <div class="review-fields">
              @if (session()!.fields.length === 0 && isNonSigner()) {
                <p class="review-empty-note">{{ 'signing.review.noFieldsRole' | translate }}</p>
              }
              @for (field of session()!.fields; track field.id) {
                <div class="review-field-card" [class.unsigned]="!field.isSigned">
                  <div class="rfc-meta">
                    <span class="rfc-type">{{ field.fieldType }}</span>
                    <span class="rfc-page">{{ 'signing.pageLabel' | translate: { page: field.page } }}</span>
                    @if (field.isRequired) {
                      <span class="rfc-required">{{ 'common.required' | translate }}</span>
                    }
                  </div>
                  <div class="rfc-value">
                    @if (field.isSigned && field.signedValue) {
                      @if (field.signedValue.startsWith('data:')) {
                        <img [src]="field.signedValue" class="rfc-sig-img" [attr.alt]="'signing.signatureImageAlt' | translate" />
                      } @else {
                        <span class="rfc-text-val">{{ field.signedValue }}</span>
                      }
                      <mat-icon class="rfc-check">check_circle</mat-icon>
                    } @else {
                      <span class="rfc-empty">{{ 'signing.review.notFilled' | translate }}</span>
                    }
                  </div>
                </div>
              }
            </div>

            @if (checklistItems().length > 0) {
              <div class="review-checklist">
                <h3><mat-icon>checklist</mat-icon>{{ 'signing.checklist.title' | translate }}</h3>
                @for (item of checklistItems(); track item.id) {
                  <label class="checklist-item">
                    <input type="checkbox" [checked]="item.isChecked" (change)="toggleChecklistItem(item)" />
                    <span [class.done]="item.isChecked">{{ item.text }}</span>
                  </label>
                }
              </div>
            }

            <div class="review-footer">
              <p class="review-legal" [innerHTML]="'signing.review.legalNotice' | translate"></p>
              <div class="review-actions">
                <button mat-stroked-button (click)="backToFields()">
                  <mat-icon>arrow_back</mat-icon> {{ 'signing.topbar.backToFields' | translate }}
                </button>
                <button mat-flat-button color="primary" (click)="approve()" [disabled]="approving() || !checklistComplete()"
                        [matTooltip]="!checklistComplete() ? ('signing.checklist.incompleteTooltip' | translate) : ''">
                  <mat-icon>verified</mat-icon>
                  {{ (approving() ? 'signing.topbar.submitting' : 'signing.topbar.approveSubmit') | translate }}
                </button>
              </div>
            </div>
          </div>
        } @else if (session()!.isHtmlTemplate) {
          <!-- HTML Template Signing View -->
          <div class="html-signing-body">
            <div class="html-doc-panel">
              <div class="html-doc-content contract-doc" [innerHTML]="session()!.renderedHtmlContent" (click)="onHtmlClick($event)"></div>
            </div>
            <div class="html-sig-panel">
              @if (session()!.fields.length > 0) {
                <div class="html-sig-header">
                  <span class="material-symbols-outlined">draw</span>
                  <div>
                    <h3>Your signature</h3>
                    <p>Sign below to complete this request</p>
                  </div>
                </div>
              } @else if (isNonSigner()) {
                <div class="role-info-card">
                  <mat-icon>{{ session()!.recipient.approvalType === 'Approval' ? 'fact_check' : 'rate_review' }}</mat-icon>
                  <p>{{ ('signing.roleNav.' + session()!.recipient.approvalType) | translate }}</p>
                </div>
              }
              @for (field of session()!.fields; track field.id) {
                <div class="html-sig-field" [class.signed]="field.isSigned">
                  @if (field.isSigned && field.signedValue) {
                    <div class="html-sig-done">
                      <img [src]="field.signedValue" class="html-sig-img" alt="Signature" />
                      <div class="html-sig-done-label">
                        <mat-icon>check_circle</mat-icon> Signed
                      </div>
                    </div>
                  } @else {
                    <div class="html-sig-prompt" (click)="clickField(field)">
                      <mat-icon>draw</mat-icon>
                      <span>Click to sign</span>
                    </div>
                  }
                </div>
              }
              @if (session()!.message) {
                <div class="html-doc-message">
                  <mat-icon>message</mat-icon>
                  <p>{{ session()!.message }}</p>
                </div>
              }
              <div class="html-sig-actions">
                @if (isNonSigner()) {
                  <button mat-stroked-button class="request-changes-btn" (click)="openRequestChangesDialog()">
                    <mat-icon>published_with_changes</mat-icon> {{ 'signing.topbar.requestChanges' | translate }}
                  </button>
                }
                @if (canDelegate()) {
                  <button mat-stroked-button (click)="openDelegateDialog()">
                    <mat-icon>forward_to_inbox</mat-icon> {{ 'signing.delegate.button' | translate }}
                  </button>
                }
                <button mat-stroked-button color="warn" (click)="openRejectDialog()">
                  <mat-icon>cancel</mat-icon> {{ 'signing.topbar.decline' | translate }}
                </button>
                <button mat-flat-button color="primary" (click)="finish()"
                        [disabled]="!allFieldsSigned() || finishing()">
                  <mat-icon>rate_review</mat-icon>
                  {{ (finishing() ? 'signing.topbar.submitting' : (isNonSigner() ? 'signing.topbar.reviewApprove' : 'signing.topbar.reviewSubmit')) | translate }}
                </button>
              </div>

              <div class="nav-comments">
                <sf-comments-panel [token]="token"></sf-comments-panel>
              </div>
            </div>
          </div>

        } @else {
          <div class="signing-body">
            <!-- Field Navigator -->
            <div class="field-navigator">
              @if (session()!.participants.length > 1) {
                <div class="nav-workflow">
                  <sf-workflow-visualizer [participants]="session()!.participants"
                                          orientation="vertical" [showHeader]="false"></sf-workflow-visualizer>
                </div>
              }
              @if (session()!.fields.length === 0 && isNonSigner()) {
                <div class="role-info-card">
                  <mat-icon>{{ session()!.recipient.approvalType === 'Approval' ? 'fact_check' : 'rate_review' }}</mat-icon>
                  <p>{{ ('signing.roleNav.' + session()!.recipient.approvalType) | translate }}</p>
                </div>
              }
              <div class="nav-header" [class.nav-hidden]="session()!.fields.length === 0">
                <h3>{{ 'signing.fieldNav.title' | translate }}</h3>
                <div class="progress-bar-wrapper">
                  <div class="progress-bar-fill" [style.width.%]="progressPercent()"></div>
                </div>
                <span class="progress-text">{{ signedCount() }} / {{ session()!.fields.length }}</span>
              </div>
              <div class="field-list">
                @for (field of session()!.fields; track field.id) {
                  <div class="field-nav-item" [class.signed]="field.isSigned" [class.current]="currentFieldId() === field.id"
                       (click)="scrollToField(field)">
                    <mat-icon class="nav-status-icon">
                      {{ field.isSigned ? 'check_circle' : 'radio_button_unchecked' }}
                    </mat-icon>
                    <div class="nav-field-info">
                      <span class="nav-type">{{ field.fieldType }}</span>
                      <span class="nav-page">{{ 'signing.pageLabel' | translate: { page: field.page } }}</span>
                    </div>
                    @if (!field.isSigned && field.isRequired) {
                      <span class="required-badge">{{ 'common.required' | translate }}</span>
                    }
                  </div>
                }
              </div>

              @if (session()!.message) {
                <div class="doc-message">
                  <mat-icon>message</mat-icon>
                  <p>{{ session()!.message }}</p>
                </div>
              }

              <div class="nav-comments">
                <sf-comments-panel [token]="token" [pageCount]="session()!.pageCount"
                                    [currentPage]="currentPage()" (pageJump)="jumpToPage($event)"></sf-comments-panel>
              </div>
            </div>

            <!-- PDF Viewer -->
            <div class="pdf-viewer-area" #viewerArea>
              @if (pdfLoading()) {
                <div class="pdf-loading"><mat-spinner diameter="40"></mat-spinner></div>
              }
              <div class="pdf-scroll-container" #pdfScrollContainer>
                @for (pageNum of pageNumbers(); track pageNum) {
                  <div class="pdf-page-wrapper" [attr.data-page]="pageNum">
                    <div class="page-label">{{ 'signing.pageLabel' | translate: { page: pageNum } }}</div>
                    <div class="pdf-page-canvas-wrapper" [style.width.px]="pageWidth()" [style.height.px]="pageHeight()">
                      <canvas [id]="'pdf-canvas-' + pageNum"></canvas>

                      <!-- Values other participants already filled in -->
                      @for (f of filledOnPage(pageNum); track f.id) {
                        <div class="filled-field"
                             [style.left.px]="f.x" [style.top.px]="f.y"
                             [style.width.px]="f.width" [style.height.px]="f.height"
                             [style.font-family]="f.fontFamily" [style.font-size.px]="f.fontSize" [style.color]="f.fontColor"
                             [matTooltip]="f.signerName ?? ''">
                          @if (isMark(f)) {
                            <mat-icon class="filled-mark">verified</mat-icon>
                            <span class="filled-sig">{{ f.signerName || f.value }}</span>
                          } @else {
                            <span class="filled-text">{{ f.value }}</span>
                          }
                        </div>
                      }

                      <!-- Fields on this page -->
                      @for (field of fieldsOnPage(pageNum); track field.id) {
                        <div class="sign-field-overlay"
                             [id]="'field-' + field.id"
                             [class.signed]="field.isSigned"
                             [class.current-field]="currentFieldId() === field.id"
                             [style.left.px]="field.x" [style.top.px]="field.y"
                             [style.width.px]="field.width" [style.height.px]="field.height"
                             (click)="clickField(field)">
                          @if (field.isSigned && field.signedValue) {
                            @if (field.fieldType === 'Radio') {
                              <mat-icon class="signed-radio">radio_button_checked</mat-icon>
                            } @else if (field.signedValue.startsWith('data:')) {
                              <img [src]="field.signedValue" class="signed-image" [attr.alt]="'signing.signatureImageAlt' | translate" />
                            } @else {
                              <span class="signed-text">{{ field.signedValue }}</span>
                            }
                            <mat-icon class="signed-check">check_circle</mat-icon>
                          } @else {
                            <div class="sign-field-prompt">
                              <mat-icon>{{ fieldIcon(field.fieldType) }}</mat-icon>
                              <span>{{ (field.isRequired ? 'common.required' : 'common.optional') | translate }}</span>
                              <span class="field-type-label">{{ field.fieldType }}</span>
                            </div>
                          }
                        </div>
                      }
                    </div>
                  </div>
                }
              </div>
            </div>
          </div>
        }
      } @else if (handedOver(); as name) {
        <div class="status-screen">
          <mat-icon>forward_to_inbox</mat-icon>
          <h2>{{ 'signing.delegate.doneTitle' | translate }}</h2>
          <p>{{ 'signing.delegate.done' | translate: { name: name } }}</p>
        </div>
      } @else if (error()) {
        <div class="status-screen error">
          <mat-icon>error_outline</mat-icon>
          <h2>{{ 'signing.status.invalidLinkTitle' | translate }}</h2>
          <p>{{ error() }}</p>
        </div>
      }
    </div>
  `,
  styleUrl: './signing.component.scss'
})
export class SigningComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('pdfScrollContainer') scrollContainer!: ElementRef<HTMLDivElement>;

  readonly appName = environment.appName;

  /** Bumped every 30s so the countdown pills re-render live without waiting for another event. */
  private readonly tick = signal(0);
  private tickInterval?: ReturnType<typeof setInterval>;

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly signingService = inject(SigningService);
  private readonly confirmService = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly pdfLoading = signal(true);
  readonly session = signal<SigningSession | null>(null);
  readonly error = signal<string | null>(null);
  /** The step was handed over: who has it now. */
  readonly handedOver = signal<string | null>(null);
  readonly currentFieldId = signal<string | null>(null);
  readonly finishing = signal(false);
  readonly reviewMode = signal(false);
  readonly approving = signal(false);
  readonly signingStep = signal<'summary' | 'signing'>('summary');
  readonly summary = signal<string | null>(null);
  readonly summaryLoading = signal(true);
  readonly downloading = signal(false);
  // The e-mailed code step (R-M5-3).
  readonly needsCode = signal(false);
  readonly intro = signal<SigningIntro | null>(null);
  readonly codeSent = signal(false);
  readonly code = signal('');
  readonly codeBusy = signal(false);
  readonly codeError = signal<string | null>(null);

  /** This page draws PDFs at 1.5 pixels per point; the API speaks points (R-M3-1). */
  private static readonly CanvasScale = 1.5;

  constructor() {
    // A session that ended while the page was open: ask for a new code rather than failing every action.
    effect(() => {
      const lost = this.signingService.sessionLost();
      if (lost && lost.token === this.token) this.showCodeStep();
    });
  }

  /** Review/Approval participants gate the workflow without filling fields. */
  readonly isNonSigner = computed(() => {
    const t = this.session()?.recipient.approvalType;
    return t === 'Review' || t === 'Approval';
  });

  /** "Reviewing · Legal team" style topbar text for non-signing participants. */
  roleBannerText(): string {
    const s = this.session();
    if (!s) return '';
    const base = this.translate.instant('signing.roleBanner.' + s.recipient.approvalType);
    return s.recipient.roleLabel ? `${base} · ${s.recipient.roleLabel}` : base;
  }

  /** Prerequisites the owner attached to this Review/Approval recipient. */
  readonly checklistItems = computed(() => this.session()?.recipient.checklistItems ?? []);
  readonly checklistComplete = computed(() => this.checklistItems().every(i => i.isChecked));

  toggleChecklistItem(item: ChecklistItemInfo): void {
    const checked = !item.isChecked;
    this.signingService.toggleChecklistItem(this.token, item.id, checked).subscribe({
      next: () => this.session.update(cur => cur && {
        ...cur,
        recipient: {
          ...cur.recipient,
          checklistItems: cur.recipient.checklistItems?.map(i => i.id === item.id ? { ...i, isChecked: checked } : i)
        }
      }),
      error: () => this.snackBar.open(this.translate.instant('signing.checklist.toggleFailed'), 'OK', { duration: 3000 })
    });
  }

  readonly scale = signal(1.0);
  readonly pageWidth = signal(794);
  readonly pageHeight = signal(1123);
  /** Template-visible: the comments panel binds this to reach the token-based endpoints. */
  token = '';

  readonly pageNumbers = computed(() => {
    const s = this.session();
    if (!s) return [];
    return Array.from({ length: s.pageCount }, (_, i) => i + 1);
  });

  /** Page of the field currently in focus — used to bookmark new comments to where the user is looking. */
  readonly currentPage = computed(() => {
    const id = this.currentFieldId();
    return this.session()?.fields.find(f => f.id === id)?.page ?? 1;
  });

  readonly signedCount = computed(() =>
    this.session()?.fields.filter(f => f.isSigned).length ?? 0
  );

  readonly allFieldsSigned = computed(() => {
    const s = this.session();
    if (!s) return false;
    // Radio groups are single-choice: a required group is satisfied by ANY signed member,
    // so its individual members are excluded from the every-required-field check.
    // Mirrors the backend's RequiredFieldRules.
    const standaloneOk = s.fields
      .filter(f => f.isRequired && !(f.fieldType === 'Radio' && f.groupName))
      .every(f => f.isSigned);
    const groups = new Map<string, { required: boolean; signed: boolean }>();
    for (const f of s.fields) {
      if (f.fieldType !== 'Radio' || !f.groupName) continue;
      const g = groups.get(f.groupName) ?? { required: false, signed: false };
      g.required ||= f.isRequired;
      g.signed ||= f.isSigned;
      groups.set(f.groupName, g);
    }
    return standaloneOk && [...groups.values()].every(g => !g.required || g.signed);
  });

  /** True when the field is a radio whose group already has a selected option. */
  private isRadioGroupSatisfied(field: AssignedField): boolean {
    if (field.fieldType !== 'Radio' || !field.groupName) return false;
    return this.session()?.fields.some(f => f.groupName === field.groupName && f.isSigned) ?? false;
  }

  readonly progressPercent = computed(() => {
    const total = this.session()?.fields.length ?? 0;
    if (!total) return 0;
    return (this.signedCount() / total) * 100;
  });

  readonly fieldsCompletedText = computed(() => {
    const total = this.session()?.fields.length ?? 0;
    const count = this.signedCount();
    const unit = this.translate.instant(total === 1 ? 'signing.field' : 'signing.fields');
    return this.translate.instant('signing.fieldsCompleted', { count, total, unit });
  });

  readonly summaryMetaText = computed(() => {
    const s = this.session();
    if (!s) return '';
    const count = s.pageCount;
    const unit = this.translate.instant(count === 1 ? 'signing.summary.page' : 'signing.summary.pages');
    const name = s.participants[0]?.name ?? this.translate.instant('signing.summary.documentOwnerFallback');
    const pageCountText = this.translate.instant('signing.summary.pageCount', { count, unit });
    const sentByText = this.translate.instant('signing.summary.sentBy', { name });
    return `${pageCountText} · ${sentByText}`;
  });

  fieldsOnPage(page: number): AssignedField[] {
    return this.session()?.fields.filter(f => f.page === page) ?? [];
  }

  filledOnPage(page: number): FilledField[] {
    return this.session()?.filledFields?.filter(f => f.page === page) ?? [];
  }

  /** A drawn or stamped signature stores no text, only the kind of mark: show who signed instead. */
  isMark(f: FilledField): boolean {
    return /^(signature|initial|stamp)/i.test(f.fieldType) || ['Drawn', 'Typed', 'Uploaded', 'Stamp'].includes(f.value ?? '');
  }

  remaining(): RemainingTime | null {
    this.tick();
    const s = this.session();
    if (!s || s.isCompleted || s.isRejected) return null;
    return remainingTime(s.expiresAt);
  }

  /** Countdown for this specific signing link (Recipient.TokenExpiresAt) — distinct from the document's SLA deadline above. */
  linkRemaining(): RemainingTime | null {
    this.tick();
    const s = this.session();
    if (!s || s.isCompleted || s.isRejected || !s.isMyTurn) return null;
    return remainingTime(s.linkExpiresAt);
  }

  fieldIcon(type: string): string {
    const icons: Record<string, string> = {
      Signature: 'draw', Initials: 'text_fields', Date: 'calendar_today',
      Text: 'short_text', Checkbox: 'check_box', Radio: 'radio_button_unchecked',
      Name: 'person', Email: 'email'
    };
    return icons[type] ?? 'edit';
  }

  ngOnInit(): void {
    this.tickInterval = setInterval(() => this.tick.update(v => v + 1), 30000);
    this.token = this.route.snapshot.paramMap.get('token')!;

    // Signed in to LegalDesk as the participant: the account opens the session (no code).
    const recipientId = this.route.snapshot.queryParamMap.get('r');
    if (this.token === ACCOUNT_SESSION && recipientId) {
      this.signingService.openAccountSession(recipientId).subscribe({
        next: () => this.loadSession(),
        error: err => this.refused(err)
      });
      return;
    }

    if (this.signingService.sessionSecret(this.token)) this.loadSession();
    else this.showCodeStep();
  }

  private loadSession(): void {
    this.needsCode.set(false);
    this.loading.set(true);
    this.signingService.getSession(this.token).subscribe({
      next: raw => {
        const s = this.inCanvasPixels(raw);
        this.session.set(s);
        this.loading.set(false);
        if (!s.isCompleted && !s.isRejected && !s.isExpired && s.isMyTurn) {
          // HTML template docs have no PDF — skip AI summary fetch
          if (!s.isHtmlTemplate) {
            this.signingService.getDocumentSummary(this.token).subscribe({
              next: res => { this.summary.set(res.summary); this.summaryLoading.set(false); },
              error: () => this.summaryLoading.set(false)
            });
          } else {
            this.summaryLoading.set(false);
          }
          const firstUnsigned = s.fields.find(f => !f.isSigned);
          if (firstUnsigned) this.currentFieldId.set(firstUnsigned.id);
        }
      },
      error: err => {
        const code = err.error?.code as string | undefined;
        if (code === 'session.required' || code === 'session.expired') this.showCodeStep();
        else this.refused(err);
      }
    });
  }

  /** Field boxes come in points; this page draws at 1.5 pixels per point. */
  private inCanvasPixels(s: SigningSession): SigningSession {
    const k = SigningComponent.CanvasScale;
    const scaleBox = <T extends { x: number; y: number; width: number; height: number }>(f: T): T =>
      ({ ...f, x: f.x * k, y: f.y * k, width: f.width * k, height: f.height * k });
    return { ...s, fields: s.fields.map(scaleBox), filledFields: s.filledFields?.map(scaleBox) };
  }

  private showCodeStep(): void {
    if (this.token === ACCOUNT_SESSION) {
      this.error.set(this.translate.instant('signing.refused.session.expired'));
      this.loading.set(false);
      return;
    }
    this.session.set(null);
    this.loading.set(false);
    this.needsCode.set(true);
    this.codeSent.set(false);
    this.code.set('');
    this.codeError.set(null);
    if (!this.intro()) {
      this.signingService.getIntro(this.token).subscribe({
        next: i => this.intro.set(i),
        error: err => { this.needsCode.set(false); this.refused(err); }
      });
    }
  }

  sendCode(): void {
    this.codeBusy.set(true);
    this.codeError.set(null);
    this.signingService.sendCode(this.token).subscribe({
      next: () => { this.codeBusy.set(false); this.codeSent.set(true); },
      error: err => { this.codeBusy.set(false); this.codeError.set(this.refusalText(err)); if (err.error?.code === 'code.too_soon') this.codeSent.set(true); }
    });
  }

  verifyCode(): void {
    const code = this.code().trim();
    if (code.length !== 6) return;
    this.codeBusy.set(true);
    this.codeError.set(null);
    this.signingService.verifyCode(this.token, code).subscribe({
      next: () => { this.codeBusy.set(false); this.loadSession(); },
      error: err => { this.codeBusy.set(false); this.code.set(''); this.codeError.set(this.refusalText(err)); }
    });
  }

  /** The page explains a refusal by its code (R-M5-2); otherwise the server's sentence. */
  private refusalText(err: { error?: { code?: string; detail?: string } }): string {
    const code = err.error?.code;
    if (code) {
      const key = `signing.refused.${code}`;
      const text = this.translate.instant(key);
      if (text !== key) return text;
    }
    return err.error?.detail ?? this.translate.instant('signing.errors.invalidOrExpiredLink');
  }

  private refused(err: { error?: { code?: string; detail?: string } }): void {
    this.loading.set(false);
    this.needsCode.set(false);
    this.error.set(this.refusalText(err));
  }

  ngOnDestroy(): void {
    if (this.tickInterval) clearInterval(this.tickInterval);
  }

  proceedToSign(): void {
    const s = this.session();
    if (!s) return;
    this.signingStep.set('signing');
    if (s.isHtmlTemplate) return; // HTML template — no PDF to load; the signing UI renders inline
    const allPreSigned = s.fields.length > 0 && s.fields.filter(f => f.isRequired).every(f => f.isSigned);
    if (allPreSigned) {
      this.reviewMode.set(true);
    } else {
      setTimeout(() => this.loadPdf(s.storagePath), 100);
    }
  }

  ngAfterViewInit(): void {}

  /** Kept so the pages can be drawn again after review mode removed their canvases. */
  private pdfDoc: { numPages: number; getPage(n: number): Promise<any> } | null = null;

  private async renderPages(): Promise<void> {
    const pdfDoc = this.pdfDoc;
    if (!pdfDoc) return;
    for (let i = 1; i <= pdfDoc.numPages; i++) {
      const page = await pdfDoc.getPage(i);
      const viewport = page.getViewport({ scale: 1.5 });
      if (i === 1) {
        this.scale.set(1.5);
        this.pageWidth.set(viewport.width);
        this.pageHeight.set(viewport.height);
      }
      const canvas = document.getElementById(`pdf-canvas-${i}`) as HTMLCanvasElement;
      if (!canvas) continue;
      canvas.width = viewport.width;
      canvas.height = viewport.height;
      const ctx = canvas.getContext('2d')!;
      await page.render({ canvasContext: ctx, viewport, canvas }).promise;
    }
  }

  /** Back from the review screen: the PDF viewer is created anew, so its pages must be drawn again. */
  backToFields(): void {
    this.reviewMode.set(false);
    if (this.session()?.isHtmlTemplate) return;
    this.pdfLoading.set(true);
    setTimeout(() => this.loadPdf(this.session()?.storagePath ?? ''), 100);
  }

  private async loadPdf(storagePath: string): Promise<void> {
    try {
      const pdfjsLib = await import('pdfjs-dist');
      pdfjsLib.GlobalWorkerOptions.workerSrc = new URL(
        'pdfjs-dist/build/pdf.worker.mjs', import.meta.url
      ).toString();

      // Fetch via the configured API base URL (works in dev where the SPA and API are on different ports)
      if (!this.pdfDoc) {
        const blob = await firstValueFrom(this.signingService.streamDocument(this.token));
        const data = await blob.arrayBuffer();
        this.pdfDoc = await pdfjsLib.getDocument({ data }).promise;
      }
      await this.renderPages();
      this.pdfLoading.set(false);
    } catch {
      this.pdfLoading.set(false);
    }
  }

  scrollToField(field: AssignedField): void {
    this.currentFieldId.set(field.id);
    setTimeout(() => {
      const el = document.getElementById(`field-${field.id}`);
      el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }, 50);
  }

  /** Jumps the PDF viewer to the page a bookmarked comment points at. */
  jumpToPage(page: number): void {
    const el = this.scrollContainer?.nativeElement.querySelector(`[data-page="${page}"]`);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  // ---- Review gate ---------------------------------------------------------------------------

  readonly acknowledging = signal(false);

  /** The participant has a task but has not yet confirmed they read the contract (and its review). */
  readonly gateCleared = () => {
    const gate = this.session()?.reviewGate;
    return !gate?.required || gate.acknowledged;
  };

  /** Shown on the participant's turn, while there is still something for them to do. */
  readonly showReviewGate = () => {
    const s = this.session();
    return !!s?.reviewGate?.required
      && s.isMyTurn && !s.isCompleted && !s.isRejected && !s.isExpired
      && s.documentStatus !== 'ChangesRequested';
  };

  acknowledgeReview(): void {
    this.acknowledging.set(true);
    this.signingService.acknowledgeReview(this.token).subscribe({
      next: () => {
        this.acknowledging.set(false);
        this.session.update(s => s?.reviewGate ? { ...s, reviewGate: { ...s.reviewGate, acknowledged: true } } : s);
      },
      error: () => {
        this.acknowledging.set(false);
        this.snackBar.open(this.translate.instant('signing.gate.failed'), this.translate.instant('signing.dismiss'), { duration: 6000 });
      }
    });
  }

  /**
   * A signature placeholder in an HTML contract is drawn where the template put it. The one that is the viewer's own
   * (`sig-mine`) is the same action as the "Click to sign" panel beside the text, so it opens the same dialog.
   */
  onHtmlClick(event: Event): void {
    const slot = (event.target as HTMLElement | null)?.closest('.sig-slot.sig-mine') as HTMLElement | null;
    if (!slot || !this.session()?.isMyTurn) return;

    const field = this.session()?.fields.find(f => f.id === slot.id.replace('sig-slot-', ''));
    if (field) this.clickField(field);
  }

  clickField(field: AssignedField): void {
    if (field.isSigned) return;

    // Acting on the request starts with confirming the document was read; the card above the contract is where.
    if (!this.gateCleared()) {
      this.snackBar.open(this.translate.instant('signing.gate.mustConfirm'), this.translate.instant('signing.dismiss'), { duration: 5000 });
      return;
    }

    this.currentFieldId.set(field.id);

    if (field.fieldType === 'Signature' || field.fieldType === 'Initials' || field.fieldType === 'Stamp') {
      this.openSignatureDialog(field);
    } else if (field.fieldType === 'Date') {
      // The server writes the date, in the organisation's time zone and format (R-M5-7).
      this.signTextualField(field, '');
    } else if (field.fieldType === 'Checkbox') {
      this.signTextualField(field, 'true');
    } else if (field.fieldType === 'Radio') {
      this.signRadioField(field);
    } else {
      this.openTextDialog(field);
    }
  }

  /**
   * Single-choice group: the backend voids the previously selected option in the same group,
   * so mirror that locally before recording the new choice.
   */
  private signRadioField(field: AssignedField): void {
    this.signingService.signField(this.token, field.id, {
      signatureType: 'Typed',
      typedText: 'true'
    }).subscribe({
      next: res => {
        if (field.groupName) {
          this.session.update(s => s && {
            ...s,
            fields: s.fields.map(f =>
              f.id !== field.id && f.fieldType === 'Radio' && f.groupName === field.groupName
                ? { ...f, isSigned: false, signedValue: undefined }
                : f)
          });
        }
        this.applyFieldResult(field.id, 'true', res.allFieldsSigned);
      },
      error: () => this.snackBar.open(
        this.translate.instant('signing.errors.saveFieldFailed'),
        this.translate.instant('signing.dismiss'),
        { duration: 4000 }
      )
    });
  }

  private openSignatureDialog(field: AssignedField): void {
    const ref = this.dialog.open(SignatureDialogComponent, {
      width: '640px',
      data: { fieldType: field.fieldType }
    });
    ref.afterClosed().subscribe((result: { dataUrl: string; type: string } | null) => {
      if (!result) return;
      const signatureType: SignatureType =
        result.type === 'uploaded' ? 'Uploaded'
        : result.type === 'typed' ? 'Typed'
        : 'Drawn';
      this.signingService.signField(this.token, field.id, {
        signatureType,
        imageBase64: result.dataUrl
      }).subscribe({
        next: res => this.applyFieldResult(field.id, result.dataUrl, res.allFieldsSigned),
        error: () => this.snackBar.open(
          this.translate.instant('signing.errors.saveSignatureFailed'),
          this.translate.instant('signing.dismiss'),
          { duration: 4000 }
        )
      });
    });
  }

  private openTextDialog(field: AssignedField): void {
    this.confirmService.prompt({
      message: this.translate.instant('signing.enterFieldPrompt', { fieldType: field.fieldType })
    }).subscribe(value => {
      if (value !== null && value.trim()) this.signTextualField(field, value.trim());
    });
  }

  private signTextualField(field: AssignedField, value: string): void {
    this.signingService.signField(this.token, field.id, {
      signatureType: 'Typed',
      typedText: value
    }).subscribe({
      next: res => this.applyFieldResult(field.id, res.signedValue ?? value, res.allFieldsSigned),
      error: () => this.snackBar.open(
        this.translate.instant('signing.errors.saveFieldFailed'),
        this.translate.instant('signing.dismiss'),
        { duration: 4000 }
      )
    });
  }

  private applyFieldResult(fieldId: string, value: string, allFieldsSigned = false): void {
    this.session.update(s => {
      if (!s) return s;
      return {
        ...s,
        fields: s.fields.map(f =>
          f.id === fieldId ? { ...f, isSigned: true, signedValue: value } : f
        )
      };
    });

    if (allFieldsSigned) {
      this.currentFieldId.set(null);
      this.reviewMode.set(true);
      return;
    }

    // Skip unsigned radios whose group already has a selection — they're not pending work.
    const next = this.session()?.fields.find(f => !f.isSigned && !this.isRadioGroupSatisfied(f));
    if (next) {
      this.currentFieldId.set(next.id);
      this.scrollToField(next);
    }
  }

  approve(): void {
    this.approving.set(true);
    this.signingService.approveSigning(this.token).subscribe({
      next: res => {
        this.approving.set(false);
        this.reviewMode.set(false);
        this.session.update(s => s ? { ...s, isCompleted: true } : s);
      },
      error: () => {
        this.approving.set(false);
        this.snackBar.open(
          this.translate.instant('signing.errors.submitFailed'),
          this.translate.instant('signing.dismiss'),
          { duration: 6000 }
        );
      }
    });
  }

  finish(): void {
    // Kept for compatibility — entering review mode replaces the old instant-finish flow.
    this.reviewMode.set(true);
  }

  /** A team step is shared by its members already; a viewer has no step to hand over. */
  canDelegate(): boolean {
    const r = this.session()?.recipient;
    return !!r && r.approvalType !== 'ViewOnly' && r.email !== 'pool@legaldesk.invalid';
  }

  openDelegateDialog(): void {
    const s = this.session();
    if (!s) return;
    this.dialog.open(DelegateDialogComponent, {
      data: { token: this.token, ownEmail: s.recipient.email } satisfies DelegateDialogData,
      width: '520px', maxWidth: '96vw'
    }).afterClosed().subscribe((result?: { name: string }) => {
      if (!result) return;
      // This link no longer acts: say so plainly instead of leaving buttons that would be refused.
      this.session.set(null);
      this.handedOver.set(result.name);
    });
  }

  openRequestChangesDialog(): void {
    this.confirmService.prompt({
      message: this.translate.instant('signing.requestChangesPrompt'),
      inputType: 'textarea'
    }).subscribe(reason => {
      if (reason === null || !reason.trim()) return;
      this.requestChanges(reason.trim());
    });
  }

  private requestChanges(reason: string): void {
    this.signingService.requestChanges(this.token, reason).subscribe({
      next: () => this.session.update(s => s
        ? { ...s, documentStatus: 'ChangesRequested', recipient: { ...s.recipient, status: 'ChangesRequested' } }
        : s),
      error: err => this.snackBar.open(
        err.error?.detail ?? this.translate.instant('signing.errors.requestChangesFailed'),
        this.translate.instant('signing.dismiss'),
        { duration: 4000 }
      )
    });
  }

  openRejectDialog(): void {
    this.confirmService.prompt({
      message: this.translate.instant('signing.declineReasonPrompt'),
      inputType: 'textarea',
      required: true,
      disableClose: true
    }).subscribe(reason => {
      if (reason === null || !reason.trim()) return; // Cancelled, or nothing written
      this.reject(reason.trim());
    });
  }

  private reject(reason: string): void {
    this.signingService.rejectDocument(this.token, reason).subscribe({
      next: () => this.session.update(s => s ? { ...s, isRejected: true } : s),
      error: () => this.snackBar.open(
        this.translate.instant('signing.errors.declineFailed'),
        this.translate.instant('signing.dismiss'),
        { duration: 4000 }
      )
    });
  }

  downloadDocument(): void {
    this.downloading.set(true);
    this.signingService.streamDocument(this.token).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.session()?.documentTitle ?? this.translate.instant('signing.untitledDocument')}.pdf`;
        a.click();
        URL.revokeObjectURL(url);
        this.downloading.set(false);
      },
      error: () => {
        this.downloading.set(false);
        this.snackBar.open(
          this.translate.instant('signing.errors.downloadFailed'),
          this.translate.instant('signing.dismiss'),
          { duration: 4000 }
        );
      }
    });
  }

  /** Browsers only let a script close tabs it opened itself, so fall back to leaving the page. */
  closeTab(): void {
    window.close();
    setTimeout(() => {
      if (!window.closed) this.router.navigate(['/contracts']);
    }, 150);
  }
}
