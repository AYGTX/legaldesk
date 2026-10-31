import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { UserService } from '../../core/services/user.service';
import { formatSla } from '../../core/utils/sla';
import {
  ApprovalType, DocumentDetail, FieldDto, Review, RecipientInputDto, RequestType, UserDto, WorkflowType
} from '../../core/models';
import { RequestStepperComponent } from '../../shared/request-stepper/request-stepper.component';
import { ReadinessVerdictComponent } from '../../shared/readiness-verdict/readiness-verdict.component';
import { ReviewService } from '../../core/services/review.service';
import { ReadinessChecklistComponent } from '../../shared/readiness-checklist/readiness-checklist.component';
import { StageLanesComponent } from '../../shared/stage-lanes/stage-lanes.component';
import { normalizeStages } from '../../shared/stage-lanes/stage-lanes.util';
import { WorkflowTypeToggleComponent } from '../../shared/workflow-type-toggle/workflow-type-toggle.component';
import { PdfPreviewComponent, PreviewBox, PreviewPageSize } from '../../shared/pdf-preview/pdf-preview.component';
import { PdfTextEditorDialogComponent, PdfTextEditorDialogData } from './pdf-text-editor-dialog.component';

/** 0 request · 1 AI review · 2 resolve · 3 participants · 4 send · 5 sent */
type Step = 0 | 1 | 2 | 3 | 4 | 5;
type ReviewState = 'idle' | 'running' | 'done' | 'failed';

interface Participant {
  kind: 'user' | 'external';
  userId: string;
  name: string;
  email: string;
  approvalType: ApprovalType;
  roleLabel: string;
  /** Stage number in a staged workflow: people on the same stage act at the same time. */
  stage: number;
}

const EXTERNAL = '__external';
const ROLES: ApprovalType[] = ['Signature', 'Approval', 'Review', 'ViewOnly'];
/** An A4 page in points — used until the real page size is known. */
const A4: PreviewPageSize = { width: 595.28, height: 841.89 };
/** Signature boxes, in points (R-M3-1). */
const BOX = { height: 60, maxWidth: 200, margin: 40, bottomMargin: 67, rowGap: 20 };

/** Where signer number `index` of `count` gets her signature box on a page of the given size: a tidy two-column grid at the foot. */
function signatureBox(index: number, size: PreviewPageSize): { x: number; y: number; width: number; height: number } {
    const width = Math.min(BOX.maxWidth, (size.width - BOX.margin * 3) / 2);
    const col = index % 2;
    const row = Math.floor(index / 2);
    return {
      x: BOX.margin + col * (width + BOX.margin),
      y: size.height - BOX.bottomMargin - BOX.height - row * (BOX.height + BOX.rowGap),
      width,
      height: BOX.height
    };
}

/**
 * The guided way to put a contract into circulation: create the request and upload the file, let the AI review it
 * before anyone else sees it, resolve what it found, choose who takes part, and send. The contract is a draft on the
 * server from the first step on, so leaving and coming back (`?documentId=`) resumes where it stopped.
 *
 * Nothing here is new machinery — it strings together the upload, the pre-flight review, the recipients and the
 * send that already exist, in the order a person actually does them.
 */
@Component({
  selector: 'sf-new-contract-request',
  standalone: true,
  imports: [
    CommonModule, RouterLink, FormsModule, StageLanesComponent, WorkflowTypeToggleComponent,
    MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, MatSnackBarModule,
    RequestStepperComponent, ReadinessVerdictComponent, ReadinessChecklistComponent, PdfPreviewComponent, TranslatePipe
  ],
  template: `
    <div class="nc-page" [class.wide]="step() === 2 || step() === 3">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'newContractRequest.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'newContractRequest.subtitle' | translate }}</p>
        </div>
      </div>

      @if (step() < 5) {
        <sf-request-stepper [steps]="stepLabels()" [currentIndex]="step()" />
      }

      @switch (step()) {

        <!-- 1 · The request: what it is and the file -->
        @case (0) {
          <div class="nc-card">
            @if (!file()) {
              <div class="drop-zone" [class.drag-over]="isDragOver()"
                   (dragover)="onDragOver($event)" (dragleave)="isDragOver.set(false)"
                   (drop)="onDrop($event)" (click)="fileInput.click()">
                <mat-icon class="drop-icon">upload_file</mat-icon>
                <h3>{{ 'newContractRequest.drop.title' | translate }}</h3>
                <p>{{ 'newContractRequest.drop.browse' | translate }}</p>
                <p class="hint">{{ 'newContractRequest.drop.hint' | translate }}</p>
                <input #fileInput type="file" accept=".pdf,application/pdf" hidden (change)="onFileSelected($event)" />
              </div>
            } @else {
              <div class="file-row">
                <mat-icon class="pdf-icon">picture_as_pdf</mat-icon>
                <div class="file-info">
                  <span class="file-name">{{ file()!.name }}</span>
                  <span class="file-size">{{ formatSize(file()!.size) }}</span>
                </div>
                <button mat-icon-button (click)="file.set(null)" [disabled]="busy()"><mat-icon>close</mat-icon></button>
              </div>

              <label class="nc-label" for="nc-title">{{ 'newContractRequest.fields.title' | translate }}</label>
              <input id="nc-title" class="nc-input" type="text" [(ngModel)]="title" [disabled]="busy()" />

              <label class="nc-label" for="nc-type">{{ 'newContractRequest.fields.type' | translate }}</label>
              <select id="nc-type" class="nc-input" [(ngModel)]="requestTypeId" [disabled]="busy()">
                <option [ngValue]="null">{{ 'newContractRequest.fields.typeNone' | translate }}</option>
                @for (rt of requestTypes(); track rt.id) {
                  <option [ngValue]="rt.id">{{ rt.name }} ({{ sla(rt) }})</option>
                }
              </select>
              <p class="hint"><mat-icon>policy</mat-icon> {{ 'newContractRequest.fields.typeHint' | translate }}</p>
            }

            @if (error()) { <div class="nc-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }

            <div class="nc-actions">
              <a mat-button routerLink="/contracts">{{ 'common.cancel' | translate }}</a>
              <span class="spacer"></span>
              <button mat-flat-button color="primary" (click)="startUpload()" [disabled]="!canUpload() || busy()">
                <mat-icon>auto_awesome</mat-icon>
                {{ (busy() ? 'newContractRequest.uploading' : 'newContractRequest.uploadAndReview') | translate }}
              </button>
            </div>
          </div>
        }

        <!-- 2 · The AI reads it before anyone else does -->
        @case (1) {
          <div class="nc-card center">
            @if (review() === 'failed') {
              <mat-icon class="big warn">report_problem</mat-icon>
              <h2>{{ 'newContractRequest.review.failedTitle' | translate }}</h2>
              <p>{{ error() || ('newContractRequest.review.failedHint' | translate) }}</p>
              <div class="nc-actions center-actions">
                <button mat-stroked-button (click)="toParticipants()">{{ 'newContractRequest.review.skip' | translate }}</button>
                <button mat-flat-button color="primary" (click)="runReview()">
                  <mat-icon>refresh</mat-icon> {{ 'newContractRequest.review.retry' | translate }}
                </button>
              </div>
            } @else {
              <mat-icon class="big">auto_awesome</mat-icon>
              <h2>{{ 'newContractRequest.review.title' | translate }}</h2>
              <p>{{ 'newContractRequest.review.note' | translate }}</p>
              <mat-progress-bar mode="indeterminate"></mat-progress-bar>
            }
          </div>
        }

        <!-- 3 · Resolve what it found -->
        @case (2) {
          @if (readiness(); as r) {
            <sf-readiness-verdict
              [verdict]="r.verdict" [score]="r.readinessScore"
              [riskScore]="r.riskScore"
              [openCount]="r.openCount" [criticalCount]="r.criticalCount"
              [highCount]="r.highCount" [handledCount]="r.handledCount">
            </sf-readiness-verdict>

            <div class="resolve-layout">
              <div class="nc-card">
                <sf-readiness-checklist [documentId]="documentId()!" [items]="r.items" [fixedItems]="r.fixedItems" [fromOcr]="r.textSource === 'Ocr'" (changed)="readiness.set($event)">
                </sf-readiness-checklist>
              </div>

              <div class="nc-card fix-card">
                <h3>{{ 'newContractRequest.resolve.fixTitle' | translate }}</h3>
                <p class="hint">{{ 'newContractRequest.resolve.fixHint' | translate }}</p>
                <button mat-stroked-button (click)="editText()" [disabled]="busy()">
                  <mat-icon>edit_note</mat-icon> {{ 'newContractRequest.resolve.editText' | translate }}
                </button>
                <button mat-stroked-button (click)="fixInput.click()" [disabled]="busy()">
                  <mat-icon>upload_file</mat-icon> {{ 'newContractRequest.resolve.uploadFix' | translate }}
                </button>
                <input #fixInput type="file" accept="application/pdf" hidden (change)="onFixFile($event)" />
                <button mat-stroked-button (click)="runReview(true)" [disabled]="busy()">
                  <mat-icon>refresh</mat-icon> {{ 'newContractRequest.resolve.rerun' | translate }}
                </button>
                @if (error()) { <div class="nc-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }
              </div>
            </div>
          } @else {
            <div class="nc-card center">
              <p>{{ 'newContractRequest.resolve.none' | translate }}</p>
              <button mat-flat-button color="primary" (click)="runReview(true)">
                <mat-icon>auto_awesome</mat-icon> {{ 'newContractRequest.resolve.rerun' | translate }}
              </button>
            </div>
          }

          <div class="nc-actions">
            <span class="hint">
              @if (readiness()?.verdict === 'Blocked') { <mat-icon>block</mat-icon> {{ 'newContractRequest.resolve.blockedHint' | translate }} }
            </span>
            <span class="spacer"></span>
            <button mat-flat-button color="primary" (click)="step.set(3)">
              {{ 'newContractRequest.resolve.continue' | translate }} <mat-icon iconPositionEnd>arrow_forward</mat-icon>
            </button>
          </div>
        }

        <!-- 4 · Who takes part -->
        @case (3) {
          <div class="people-layout">
            <!-- The contract itself, with the signature boxes the participants below will get -->
            <div class="preview-col">
              <sf-pdf-preview [documentId]="documentId()!" [boxes]="previewBoxes()" (pageSizes)="pageSizes.set($event)" />
              <p class="hint"><mat-icon>draw</mat-icon> {{ 'newContractRequest.people.previewHint' | translate }}</p>
            </div>

            <div class="people-col">
          <div class="nc-card">
            <h3>{{ 'newContractRequest.people.title' | translate }}</h3>
            <p class="hint">{{ 'newContractRequest.people.hint' | translate }}</p>

            <sf-workflow-type-toggle [value]="workflowType" (valueChange)="setWorkflowType($event)"></sf-workflow-type-toggle>

            <ng-template #personFields let-p>
              <div class="person-fields">
                <div class="person-row">
                  <select class="nc-input" [ngModel]="p.kind === 'external' ? EXTERNAL : p.userId" (ngModelChange)="pickUser(p, $event)">
                    <option value="">{{ 'newContractRequest.people.pick' | translate }}</option>
                    @for (u of users(); track u.id) { <option [value]="u.id">{{ u.fullName }} — {{ u.email }}</option> }
                    <option [value]="EXTERNAL">{{ 'newContractRequest.people.external' | translate }}</option>
                  </select>
                  <select class="nc-input role" [(ngModel)]="p.approvalType">
                    @for (role of roles; track role) { <option [value]="role">{{ ('newContractRequest.people.roles.' + role) | translate }}</option> }
                  </select>
                </div>
                @if (p.kind === 'external') {
                  <div class="person-row">
                    <input class="nc-input" type="text" [(ngModel)]="p.name" [placeholder]="'newContractRequest.people.name' | translate" />
                    <input class="nc-input" type="email" [(ngModel)]="p.email" [placeholder]="'newContractRequest.people.email' | translate" />
                  </div>
                }
                <input class="nc-input" type="text" [(ngModel)]="p.roleLabel" [placeholder]="'newContractRequest.people.roleLabel' | translate" />
              </div>
            </ng-template>

            @if (workflowType === 'Mixed') {
              <sf-stage-lanes [items]="participants" (changed)="participants = $any($event)">
                <ng-template #card let-p>
                  <div class="person-card">
                    <ng-container *ngTemplateOutlet="personFields; context: { $implicit: p }"></ng-container>
                    <div class="person-actions">
                      <button mat-icon-button (click)="removeParticipant(participants.indexOf(p))" [disabled]="participants.length === 1"><mat-icon>delete_outline</mat-icon></button>
                    </div>
                  </div>
                </ng-template>
              </sf-stage-lanes>
            } @else {
              @for (p of participants; track $index; let i = $index) {
                <div class="person">
                  <span class="person-order">{{ workflowType === 'Parallel' ? '•' : i + 1 }}</span>
                  <ng-container *ngTemplateOutlet="personFields; context: { $implicit: p }"></ng-container>
                  <div class="person-actions">
                    <button mat-icon-button (click)="moveParticipant(i, -1)" [disabled]="i === 0"><mat-icon>keyboard_arrow_up</mat-icon></button>
                    <button mat-icon-button (click)="moveParticipant(i, 1)" [disabled]="i === participants.length - 1"><mat-icon>keyboard_arrow_down</mat-icon></button>
                    <button mat-icon-button (click)="removeParticipant(i)" [disabled]="participants.length === 1"><mat-icon>delete_outline</mat-icon></button>
                  </div>
                </div>
              }
            }

            <button mat-stroked-button (click)="addParticipant()"><mat-icon>person_add</mat-icon> {{ 'newContractRequest.people.add' | translate }}</button>

            @if (signerNotLast()) {
              <div class="nc-warn">
                <mat-icon>warning_amber</mat-icon>
                <span>{{ 'newContractRequest.people.signerFirstWarning' | translate }}</span>
                <button mat-stroked-button type="button" (click)="orderByRole()">{{ 'newContractRequest.people.orderByRole' | translate }}</button>
              </div>
            }
            @if (hasSigner()) {
              <p class="hint"><mat-icon>draw</mat-icon> {{ 'newContractRequest.people.signaturesAuto' | translate }}</p>
            }
            @if (error()) { <div class="nc-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }
          </div>

          <div class="nc-actions">
            <button mat-button (click)="step.set(2)"><mat-icon>arrow_back</mat-icon> {{ 'newContractRequest.back' | translate }}</button>
            <span class="spacer"></span>
            <button mat-flat-button color="primary" (click)="saveParticipants()" [disabled]="busy()">
              {{ (busy() ? 'newContractRequest.saving' : 'newContractRequest.people.save') | translate }}
              <mat-icon iconPositionEnd>arrow_forward</mat-icon>
            </button>
          </div>
            </div>
          </div>
        }

        <!-- 5 · Check and send -->
        @case (4) {
          <div class="nc-card">
            <h3>{{ 'newContractRequest.send.title' | translate }}</h3>

            <dl class="summary">
              <dt>{{ 'newContractRequest.send.contract' | translate }}</dt>
              <dd>{{ doc()?.title }}<span class="muted"> · {{ doc()?.fileName }} · {{ doc()?.pageCount }} p.</span></dd>
              <dt>{{ 'newContractRequest.fields.type' | translate }}</dt>
              <dd>{{ doc()?.requestTypeName || ('newContractRequest.fields.typeNone' | translate) }}
                @if (selectedType(); as t) { <span class="muted"> · {{ 'newContractRequest.send.deadline' | translate }} {{ sla(t) }}</span> }
              </dd>
              <dt>{{ 'newContractRequest.send.review' | translate }}</dt>
              <dd>
                <span class="verdict v-{{ readiness()?.verdict || 'NotEvaluated' }}">{{ ('newContractRequest.send.verdict.' + (readiness()?.verdict || 'NotEvaluated')) | translate }}</span>
                @if (readiness(); as r) {
                  @if (r.openCount > 0) { <span class="muted"> · {{ 'newContractRequest.send.openItems' | translate: { count: r.openCount } }}</span> }
                }
              </dd>
              <dt>{{ 'newContractRequest.people.title' | translate }}</dt>
              <dd>
                @for (r of doc()?.recipients ?? []; track r.id; let i = $index) {
                  <div class="sum-person">
                    <span class="person-order">{{ workflowType === 'Parallel' ? '•' : workflowType === 'Mixed' ? r.order : i + 1 }}</span>
                    {{ r.name }} <span class="muted">— {{ ('newContractRequest.people.roles.' + r.approvalType) | translate }}</span>
                  </div>
                }
              </dd>
            </dl>

            @if (readiness()?.verdict === 'Blocked') {
              <div class="nc-error"><mat-icon>block</mat-icon> {{ 'newContractRequest.send.blocked' | translate }}</div>
            } @else if (readiness()?.verdict === 'NeedsWork') {
              <!-- Sending with open high-risk findings takes a reason, kept in the audit trail (R-M8-8). -->
              <label class="ack ack-reason">
                {{ 'newContractRequest.send.reasonLabel' | translate: { count: readiness()!.openCount } }}
                <textarea rows="2" maxlength="1000" [(ngModel)]="sendReason"></textarea>
              </label>
            }
            @if (error()) { <div class="nc-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }
          </div>

          <div class="nc-actions">
            <button mat-button (click)="step.set(3)"><mat-icon>arrow_back</mat-icon> {{ 'newContractRequest.back' | translate }}</button>
            <a mat-button [routerLink]="['/contracts', documentId(), 'designer']">
              <mat-icon>draw</mat-icon> {{ 'newContractRequest.send.adjustFields' | translate }}
            </a>
            <span class="spacer"></span>
            <button mat-flat-button color="primary" (click)="send()" [disabled]="!canSend() || sending()">
              <mat-icon>send</mat-icon> {{ (sending() ? 'newContractRequest.send.sending' : 'newContractRequest.send.button') | translate }}
            </button>
          </div>
        }

        @case (5) {
          <div class="nc-card center">
            <mat-icon class="big ok">check_circle</mat-icon>
            <h2>{{ 'newContractRequest.sent.title' | translate }}</h2>
            <p>{{ 'newContractRequest.sent.hint' | translate }}</p>
            <div class="nc-actions center-actions">
              <a mat-flat-button color="primary" [routerLink]="['/contracts', documentId()]">{{ 'newContractRequest.sent.open' | translate }}</a>
              <a mat-stroked-button routerLink="/contracts/new" (click)="reload()">{{ 'newContractRequest.sent.another' | translate }}</a>
            </div>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .nc-card { border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; padding: 20px; margin-bottom: 16px; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .nc-card.center { text-align: center; display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 40px 24px; }
    .nc-card h2, .nc-card h3 { margin: 0 0 6px; font-weight: 500; }
    .big { font-size: 44px; width: 44px; height: 44px; color: #0b57d0; }
    .big.warn { color: #b06000; } .big.ok { color: #137333; }
    .nc-card mat-progress-bar { width: min(420px, 100%); margin: 12px 0; }
    .drop-zone { border: 2px dashed #c7d0dc; border-radius: 12px; padding: 40px 20px; text-align: center; cursor: pointer; color: #5f6368; transition: background .15s, border-color .15s; }
    .drop-zone:hover, .drop-zone.drag-over { border-color: #0b57d0; background: #f0f4f9; }
    .drop-zone h3 { color: #1f1f1f; }
    .drop-icon { font-size: 44px; width: 44px; height: 44px; color: #0b57d0; }
    .hint { display: flex; align-items: center; gap: 6px; font-size: 12px; color: #5f6368; margin: 6px 0; }
    .hint mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .file-row { display: flex; align-items: center; gap: 12px; padding: 10px 12px; border: 1px solid #e0e4ea; border-radius: 10px; margin-bottom: 14px; }
    .pdf-icon { color: #d93025; }
    .file-info { flex: 1; display: flex; flex-direction: column; }
    .file-name { font-weight: 500; font-size: 14px; } .file-size { font-size: 12px; color: #5f6368; }
    .nc-label { display: block; font-size: 12px; font-weight: 500; color: #5f6368; margin: 12px 0 4px; }
    .nc-input { width: 100%; box-sizing: border-box; padding: 9px 10px; border: 1px solid #c7d0dc; border-radius: 6px; font-size: 14px; font-family: inherit; background: #fff; outline: none; }
    .nc-input:focus { border-color: #0b57d0; }
    .nc-actions { display: flex; align-items: center; gap: 8px; margin-top: 12px; }
    .nc-actions.center-actions { justify-content: center; }
    .spacer { flex: 1; }
    .nc-error { display: flex; align-items: center; gap: 6px; margin-top: 12px; padding: 8px 10px; border-radius: 8px; background: #fce8e6; color: #b3261e; font-size: 13px; }
    .resolve-layout { display: grid; grid-template-columns: minmax(0, 1fr) 300px; gap: 16px; margin-top: 16px; align-items: start; }
    @media (max-width: 900px) { .resolve-layout { grid-template-columns: 1fr; } }
    .people-layout { display: grid; grid-template-columns: minmax(0, 1.1fr) minmax(0, 1fr); gap: 16px; align-items: start; }
    @media (max-width: 1000px) { .people-layout { grid-template-columns: 1fr; } }
    .preview-col { position: sticky; top: 8px; }
    .fix-card { display: flex; flex-direction: column; gap: 8px; }
    .person { display: flex; gap: 10px; align-items: flex-start; padding: 10px 0; border-top: 1px solid #eef1f5; }
    .person-order { flex: 0 0 24px; height: 24px; border-radius: 50%; background: #e8f0fe; color: #174ea6; font-size: 12px; font-weight: 600; display: inline-flex; align-items: center; justify-content: center; margin-top: 6px; }
    .person-fields { flex: 1; display: flex; flex-direction: column; gap: 6px; min-width: 0; }
    .person-row { display: flex; gap: 6px; } .person-row .role { max-width: 170px; }
    .person-actions { display: flex; }
    .nc-warn { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-top: 10px; padding: 10px 12px; border-radius: 8px; background: #fef7e0; color: #b06000; font-size: 13px; }
    .nc-warn mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
    .nc-warn span { flex: 1; min-width: 200px; }
    .person-card { display: flex; gap: 6px; align-items: flex-start; }
    .person-card .person-fields { flex: 1; min-width: 0; }
    .summary { display: grid; grid-template-columns: 140px 1fr; gap: 8px 16px; margin: 12px 0; font-size: 14px; }
    .summary dt { color: #5f6368; } .summary dd { margin: 0; }
    .sum-person { display: flex; align-items: center; gap: 8px; margin-bottom: 4px; } .sum-person .person-order { margin-top: 0; }
    .muted { color: #5f6368; font-size: 13px; }
    .verdict { font-weight: 600; } .v-Ready { color: #137333; } .v-NeedsWork { color: #b06000; } .v-Blocked { color: #b3261e; }
    .ack { display: flex; align-items: center; gap: 8px; margin-top: 12px; font-size: 14px; }
  `]
})
export class NewContractRequestComponent implements OnInit {
  private readonly documents = inject(DocumentService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly userService = inject(UserService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly EXTERNAL = EXTERNAL;
  readonly roles = ROLES;
  readonly sla = (t: RequestType) => formatSla(t.slaHours);

  readonly step = signal<Step>(0);
  readonly documentId = signal<string | null>(null);
  readonly doc = signal<DocumentDetail | null>(null);
  readonly readiness = signal<Review | null>(null);
  private readonly reviews = inject(ReviewService);
  readonly review = signal<ReviewState>('idle');
  readonly requestTypes = signal<RequestType[]>([]);
  readonly users = signal<UserDto[]>([]);

  readonly file = signal<File | null>(null);
  readonly isDragOver = signal(false);
  readonly busy = signal(false);
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);

  /** Page sizes reported by the document preview, so signature boxes are laid out on the real page. */
  readonly pageSizes = signal<PreviewPageSize[]>([]);
  private lastPageSize(): PreviewPageSize { return this.pageSizes().at(-1) ?? A4; }

  private boxesKey = '';
  private boxesCache: PreviewBox[] = [];

  /**
   * What the document preview draws: the boxes already on the contract if it has any, otherwise where each signer
   * below will get hers. Rebuilt only when it would actually change, so the preview is not redrawn on every refresh.
   */
  previewBoxes(): PreviewBox[] {
    const existing = this.doc()?.fields ?? [];
    const lastPage = Math.max(1, this.doc()?.pageCount ?? this.pageSizes().length);

    const boxes: PreviewBox[] = existing.length > 0
      ? existing.map(f => ({ page: f.page, x: f.x, y: f.y, width: f.width, height: f.height, label: f.label ?? '' }))
      : this.participants
          .filter(p => p.approvalType === 'Signature')
          .map((p, i) => ({ page: lastPage, ...signatureBox(i, this.lastPageSize()), label: p.name.trim() || this.translate.instant('newContractRequest.people.unnamedSigner') }));

    const key = JSON.stringify(boxes);
    if (key !== this.boxesKey) { this.boxesKey = key; this.boxesCache = boxes; }
    return this.boxesCache;
  }

  title = '';
  requestTypeId: string | null = null;
  workflowType: WorkflowType = 'Sequential';
  participants: Participant[] = [{ ...this.blankParticipant(), approvalType: 'Review' }];
  /** Why the contract goes out with open high-risk findings; required in that case. */
  sendReason = '';

  readonly stepLabels = computed(() => {
    this.step();   // re-read the language-dependent labels whenever the step changes
    return ['request', 'review', 'resolve', 'people', 'send']
      .map(k => this.translate.instant(`newContractRequest.steps.${k}`));
  });

  // Plain functions, not computed(): they read form fields (title, acknowledgement) that are ordinary properties.
  readonly canUpload = () => !!this.file() && this.title.trim().length > 0;
  readonly selectedType = computed(() => this.requestTypes().find(t => t.name === this.doc()?.requestTypeName) ?? null);
  readonly canSend = () => {
    const verdict = this.readiness()?.verdict;
    return !!verdict && verdict !== 'Blocked' && verdict !== 'NotEvaluated'
      && (verdict !== 'NeedsWork' || this.sendReason.trim().length > 0);
  };

  ngOnInit(): void {
    this.requestTypeService.getActive().subscribe({ next: t => this.requestTypes.set(t), error: () => {} });
    this.userService.getUsers({ pageSize: 200 }).subscribe({ next: r => this.users.set(r.items.filter(u => u.isActive)), error: () => {} });

    const existing = this.route.snapshot.queryParamMap.get('documentId');
    if (existing) this.resume(existing);
  }

  reload(): void {
    // Same route, so the component is reused: start over from a clean slate.
    this.step.set(0);
    this.documentId.set(null);
    this.doc.set(null);
    this.readiness.set(null);
    this.review.set('idle');
    this.file.set(null);
    this.title = '';
    this.requestTypeId = null;
    this.participants = [this.blankParticipant()];
    this.sendReason = '';
    this.error.set(null);
  }

  // ---------------------------------------------------------------- 1 · request

  onDragOver(event: DragEvent): void { event.preventDefault(); this.isDragOver.set(true); }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver.set(false);
    this.accept(event.dataTransfer?.files?.[0]);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.accept(input.files?.[0]);
    input.value = '';
  }

  private accept(file?: File): void {
    if (!file) return;
    if (!file.name.toLowerCase().endsWith('.pdf')) {
      this.error.set(this.translate.instant('newContractRequest.drop.pdfOnly'));
      return;
    }
    this.error.set(null);
    this.file.set(file);
    if (!this.title.trim()) this.title = file.name.replace(/\.pdf$/i, '');
  }

  formatSize(bytes: number): string {
    if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  startUpload(): void {
    const file = this.file();
    if (!file || !this.canUpload()) return;

    this.busy.set(true);
    this.error.set(null);
    const body = new FormData();
    body.append('file', file);
    body.append('title', this.title.trim());
    if (this.requestTypeId) body.append('requestTypeId', this.requestTypeId);

    this.documents.upload(body).subscribe({
      next: result => {
        this.busy.set(false);
        this.documentId.set(result.documentId);
        this.step.set(1);
        this.runReview();
      },
      error: err => {
        this.busy.set(false);
        this.error.set(err.error?.detail ?? this.translate.instant('newContractRequest.uploadFailed'));
      }
    });
  }

  // ---------------------------------------------------------------- 2 · AI review

  /** Starts (or restarts) the review. It carries on in the background if the user moves to the participants. */
  runReview(fromResolve = false): void {
    const id = this.documentId();
    if (!id) return;

    this.error.set(null);
    this.review.set('running');
    if (fromResolve) { this.busy.set(true); this.step.set(1); }

    // Harvey reviews in the background; the page waits for the result.
    this.reviews.run(id).subscribe({
      next: review => {
        this.readiness.set(review);
        this.review.set('done');
        this.busy.set(false);
        // Land on the findings — unless the user has already moved on to choosing people.
        if (this.step() === 1) this.step.set(2);
      },
      error: (err: Error) => {
        this.busy.set(false);
        this.review.set('failed');
        this.error.set(err.message);
      }
    });
  }

  /** Only reachable when the review could not run — while it is running, nothing else is offered. */
  toParticipants(): void { this.step.set(3); }

  // ---------------------------------------------------------------- 3 · resolve

  editText(): void {
    const id = this.documentId();
    if (!id) return;
    this.dialog.open(PdfTextEditorDialogComponent, {
      data: { documentId: id, documentTitle: this.title || (this.doc()?.title ?? '') } satisfies PdfTextEditorDialogData,
      width: '1000px', maxWidth: '96vw', disableClose: true
    }).afterClosed().subscribe(result => {
      // A new version clears the old verdict, so the review runs again on what was just changed.
      if (result) this.runReview(true);
    });
  }

  onFixFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const id = this.documentId();
    if (!file || !id) return;

    this.busy.set(true);
    this.error.set(null);
    this.documents.uploadRevision(id, file).subscribe({
      next: () => {
        this.snack.open(this.translate.instant('newContractRequest.resolve.fixUploaded'), 'OK', { duration: 3000 });
        this.runReview(true);
      },
      error: err => {
        this.busy.set(false);
        this.error.set(err.error?.detail ?? this.translate.instant('newContractRequest.uploadFailed'));
      }
    });
  }

  // ---------------------------------------------------------------- 4 · participants

  private blankParticipant(): Participant {
    return { kind: 'user', userId: '', name: '', email: '', approvalType: 'Signature', roleLabel: '', stage: 1 };
  }

  /** Who has to act before whom: the people who check go first, the people who sign go last. */
  private static readonly ROLE_RANK: Record<ApprovalType, number> = { ViewOnly: 1, Review: 1, Reject: 1, Approval: 2, Signature: 3, Stamp: 3 };
  private rank(p: Participant): number { return NewContractRequestComponent.ROLE_RANK[p.approvalType] ?? 1; }

  /**
   * True when a signer could sign before the contract has been checked: they sit ahead of, or on the same stage as,
   * someone who reviews or approves (or everyone acts at once).
   */
  signerNotLast(): boolean {
    const signers = this.participants.filter(p => this.rank(p) === 3);
    const checkers = this.participants.filter(p => p.approvalType === 'Review' || p.approvalType === 'Approval');
    if (!signers.length || !checkers.length) return false;
    if (this.workflowType === 'Parallel') return true;
    if (this.workflowType === 'Mixed') {
      return signers.some(s => checkers.some(c => c.stage >= s.stage));
    }
    const firstSigner = Math.min(...signers.map(s => this.participants.indexOf(s)));
    return checkers.some(c => this.participants.indexOf(c) > firstSigner);
  }

  /** Reviewers first, then approvers, then signers. "All at once" becomes a staged flow, since it cannot be ordered. */
  orderByRole(): void {
    if (this.workflowType === 'Sequential') {
      this.participants = [...this.participants].sort((a, b) => this.rank(a) - this.rank(b));
      return;
    }
    this.workflowType = 'Mixed';
    this.participants.forEach(p => p.stage = this.rank(p));
    this.normalizeStages();
  }

  /**
   * Switching to a staged workflow: from "one after the other" each person keeps their own stage, from
   * "all at once" everyone starts on the same stage. Either way they can be regrouped by dragging.
   */
  setWorkflowType(type: WorkflowType): void {
    if (type !== 'Mixed') { this.workflowType = type; return; }
    if (this.workflowType === 'Mixed') return;
    const together = this.workflowType === 'Parallel';
    this.workflowType = 'Mixed';
    this.participants.forEach((p, i) => p.stage = together ? 1 : i + 1);
  }

  /** Renumbers the stages 1..n in order, dropping empty ones, and keeps the list sorted by stage. */
  private normalizeStages(): void {
    this.participants = normalizeStages(this.participants);
  }

  hasSigner(): boolean {
    return this.participants.some(p => p.approvalType === 'Signature');
  }

  addParticipant(): void {
    const signerIndex = this.participants.findIndex(p => this.rank(p) === 3);

    // No signer yet: the next person signs, last. Otherwise a new reviewer joins before the signers.
    if (signerIndex < 0) {
      const stage = Math.max(0, ...this.participants.map(p => p.stage)) + 1;
      this.participants = [...this.participants, { ...this.blankParticipant(), approvalType: 'Signature', stage }];
      return;
    }

    const person: Participant = { ...this.blankParticipant(), approvalType: 'Review', stage: this.participants[signerIndex].stage - 0.5 };
    const next = [...this.participants];
    next.splice(signerIndex, 0, person);
    this.participants = next;
    if (this.workflowType === 'Mixed') this.normalizeStages();
  }

  removeParticipant(index: number): void {
    if (this.participants.length > 1) this.participants = this.participants.filter((_, i) => i !== index);
    if (this.workflowType === 'Mixed') this.normalizeStages();
  }

  moveParticipant(index: number, direction: -1 | 1): void {
    const target = index + direction;
    if (target < 0 || target >= this.participants.length) return;
    const next = [...this.participants];
    [next[index], next[target]] = [next[target], next[index]];
    this.participants = next;
  }

  pickUser(p: Participant, value: string): void {
    if (value === EXTERNAL) {
      p.kind = 'external'; p.userId = ''; p.name = ''; p.email = '';
      return;
    }
    p.kind = 'user';
    p.userId = value;
    const user = this.users().find(u => u.id === value);
    p.name = user?.fullName ?? '';
    p.email = user?.email ?? '';
  }

  private participantsProblem(): string | null {
    const emails = new Set<string>();
    for (const p of this.participants) {
      if (!p.name.trim() || !p.email.trim()) return this.translate.instant('newContractRequest.people.incomplete');
      if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(p.email.trim())) return this.translate.instant('newContractRequest.people.badEmail', { email: p.email });
      const key = p.email.trim().toLowerCase();
      if (emails.has(key)) return this.translate.instant('newContractRequest.people.duplicate', { email: p.email });
      emails.add(key);
    }
    return null;
  }

  saveParticipants(): void {
    const id = this.documentId();
    if (!id) return;
    const problem = this.participantsProblem();
    if (problem) { this.error.set(problem); return; }

    this.busy.set(true);
    this.error.set(null);
    const recipients: RecipientInputDto[] = this.participants.map((p, i) => ({
      name: p.name.trim(),
      email: p.email.trim(),
      order: this.workflowType === 'Parallel' ? 1 : this.workflowType === 'Mixed' ? p.stage : i + 1,
      approvalType: p.approvalType,
      roleLabel: p.roleLabel.trim() || undefined
    }));

    this.documents.saveRecipients(id, this.workflowType, recipients).subscribe({
      next: () => this.documents.getDocument(id).subscribe({
        next: detail => this.placeSignatureBoxes(id, detail),
        error: () => this.failSave()
      }),
      error: err => this.failSave(err)
    });
  }

  /**
   * Each signer gets a signature box on the last page, in a tidy grid, so the contract can be sent without a visit
   * to the designer. A contract that already has fields (placed by hand earlier) is left exactly as it is.
   */
  private placeSignatureBoxes(id: string, detail: DocumentDetail): void {
    const signers = detail.recipients.filter(r => r.approvalType === 'Signature');
    const finish = (d: DocumentDetail) => { this.doc.set(d); this.finishParticipants(id); };

    if (detail.fields.length > 0 || signers.length === 0) { finish(detail); return; }

    const size = this.lastPageSize();
    const fields: FieldDto[] = signers.map((r, i) => ({
      fieldType: 'Signature',
      page: Math.max(1, detail.pageCount),
      ...signatureBox(i, size),
      recipientId: r.id,
      label: r.name,
      isRequired: true
    }));

    this.documents.saveFields(id, fields).subscribe({
      next: () => this.documents.getDocument(id).subscribe({ next: finish, error: () => finish(detail) }),
      error: err => this.failSave(err)
    });
  }

  private finishParticipants(id: string): void {
    // The verdict the sender sees on the last step is the stored one, including anything settled in the meantime.
    this.documents.getReview(id).subscribe({
      next: r => { this.readiness.set(r); this.busy.set(false); this.step.set(4); },
      error: () => { this.busy.set(false); this.step.set(4); }
    });
  }

  private failSave(err?: { error?: { detail?: string } }): void {
    this.busy.set(false);
    this.error.set(err?.error?.detail ?? this.translate.instant('newContractRequest.people.saveFailed'));
  }

  // ---------------------------------------------------------------- 5 · send

  send(): void {
    const id = this.documentId();
    if (!id || !this.canSend()) return;

    this.sending.set(true);
    this.error.set(null);
    // With open high-risk findings the API wants the sender's reason; it goes in the audit trail.
    this.documents.send(id, this.readiness()?.verdict === 'NeedsWork' ? this.sendReason.trim() : undefined).subscribe({
      next: () => { this.sending.set(false); this.step.set(5); },
      error: err => {
        this.sending.set(false);
        const errors = err.error?.errors as Record<string, string[]> | undefined;
        if (errors?.['readiness']) { this.error.set(this.translate.instant('newContractRequest.send.blocked')); return; }
        this.error.set(err.error?.detail ?? this.translate.instant('newContractRequest.send.failed'));
      }
    });
  }

  // ---------------------------------------------------------------- resume

  /** Re-opens a draft from the right step: its review if it has none, its findings if it has, its people if it has those. */
  private resume(id: string): void {
    this.busy.set(true);
    this.documents.getDocument(id).subscribe({
      next: detail => {
        if (detail.status !== 'Draft') { this.router.navigate(['/contracts', id]); return; }

        this.documentId.set(id);
        this.doc.set(detail);
        this.title = detail.title;
        this.requestTypeId = this.requestTypes().find(t => t.name === detail.requestTypeName)?.id ?? null;
        this.workflowType = detail.workflowType;

        if (detail.recipients.length > 0) {
          this.participants = detail.recipients.map(r => {
            const known = this.users().find(u => u.email.toLowerCase() === r.email.toLowerCase());
            return {
              kind: known ? 'user' : 'external', userId: known?.id ?? '', name: r.name, email: r.email,
              approvalType: r.approvalType, roleLabel: r.roleLabel ?? '', stage: r.order
            } as Participant;
          });
          if (this.workflowType === 'Mixed') this.normalizeStages();
        }

        this.documents.getReview(id).subscribe({
          next: readiness => {
            this.readiness.set(readiness);
            this.busy.set(false);
            if (readiness.verdict === 'NotEvaluated') { this.step.set(1); this.runReview(); }
            else { this.review.set('done'); this.step.set(detail.recipients.length > 0 ? 3 : 2); }
          },
          error: () => { this.busy.set(false); this.step.set(2); }
        });
      },
      error: () => { this.busy.set(false); this.router.navigate(['/contracts']); }
    });
  }
}
