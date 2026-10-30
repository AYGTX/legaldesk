import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { Review, ReviewItem, RequestType } from '../../core/models';
import { ReviewService } from '../../core/services/review.service';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { PdfEditorRecommendation, PdfTextEditorDialogComponent, PdfTextEditorDialogData } from '../documents/pdf-text-editor-dialog.component';
import { ReadinessVerdictComponent } from '../../shared/readiness-verdict/readiness-verdict.component';
import { ReadinessChecklistComponent } from '../../shared/readiness-checklist/readiness-checklist.component';

type ReviewStage = 'drop' | 'analysing' | 'verdict';

/**
 * Contract review — the pre-flight desk. Drop a contract here and it is uploaded, analysed and
 * graded before anyone is asked to sign anything: the verdict says whether it is fit to go out, and
 * the checklist says exactly what to change if it is not.
 *
 * Deliberately separate from /contracts/upload, which commits straight to a signature circuit. The
 * document created here is an ordinary Draft, so promoting it later is just the normal flow.
 */
@Component({
  selector: 'sf-contract-review',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatProgressBarModule, MatSelectModule, MatTooltipModule, MatSnackBarModule, MatDialogModule,
    ReadinessVerdictComponent, ReadinessChecklistComponent, TranslatePipe
  ],
  template: `
    <div class="review-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'review.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'review.subtitle' | translate }}</p>
        </div>
        @if (stage() === 'verdict') {
          <button mat-stroked-button (click)="reset()">
            <mat-icon>add</mat-icon> {{ 'review.reviewAnother' | translate }}
          </button>
        }
      </div>

      @switch (stage()) {
        @case ('drop') {
          <div class="review-layout">
            <mat-card class="upload-card">
              @if (!selectedFile()) {
                <div class="drop-zone" [class.drag-over]="isDragOver()"
                     (dragover)="onDragOver($event)" (dragleave)="isDragOver.set(false)"
                     (drop)="onDrop($event)" (click)="fileInput.click()">
                  <mat-icon class="drop-icon">fact_check</mat-icon>
                  <h3>{{ 'review.dropHere' | translate }}</h3>
                  <p>{{ 'review.orBrowse' | translate }}</p>
                  <p class="file-hint">{{ 'review.hint' | translate }}</p>
                  <input #fileInput type="file" accept=".pdf" hidden (change)="onFileSelected($event)" />
                </div>
              } @else {
                <div class="file-preview">
                  <mat-icon class="pdf-icon">picture_as_pdf</mat-icon>
                  <div class="file-info">
                    <span class="file-name">{{ selectedFile()!.name }}</span>
                    <span class="file-size">{{ formatSize(selectedFile()!.size) }}</span>
                  </div>
                  <button mat-icon-button (click)="selectedFile.set(null)" [matTooltip]="'documents.upload.remove' | translate">
                    <mat-icon>close</mat-icon>
                  </button>
                </div>

                <form [formGroup]="form" class="review-form">
                  <mat-form-field appearance="outline" class="full-width">
                    <mat-label>{{ 'documents.upload.documentTitle' | translate }}</mat-label>
                    <input matInput formControlName="title" [placeholder]="'documents.upload.titlePlaceholder' | translate" />
                    @if (form.get('title')?.hasError('required') && form.get('title')?.touched) {
                      <mat-error>{{ 'documents.upload.titleRequired' | translate }}</mat-error>
                    }
                  </mat-form-field>

                  @if (requestTypes().length > 0) {
                    <mat-form-field appearance="outline" class="full-width">
                      <mat-label>{{ 'documents.upload.requestType' | translate }}</mat-label>
                      <mat-select formControlName="requestTypeId">
                        <mat-option [value]="null">{{ 'documents.upload.none' | translate }}</mat-option>
                        @for (rt of requestTypes(); track rt.id) {
                          <mat-option [value]="rt.id">
                            <mat-icon class="opt-icon" [style.color]="rt.color || '#6366f1'">{{ rt.icon || 'description' }}</mat-icon>
                            {{ rt.name }}
                          </mat-option>
                        }
                      </mat-select>
                    </mat-form-field>
                    <p class="scope-hint">
                      <mat-icon>policy</mat-icon> {{ 'review.requestTypeHint' | translate }}
                    </p>
                  }
                </form>

                @if (error()) {
                  <div class="error-banner"><mat-icon>error_outline</mat-icon> {{ error() }}</div>
                }

                <div class="review-actions">
                  <button mat-stroked-button (click)="selectedFile.set(null)">{{ 'common.cancel' | translate }}</button>
                  <button mat-flat-button color="primary" (click)="startReview()" [disabled]="form.invalid">
                    <mat-icon>auto_awesome</mat-icon> {{ 'review.analyse' | translate }}
                  </button>
                </div>
              }
            </mat-card>

            <div class="review-tips">
              <h3>{{ 'review.whatWeCheck' | translate }}</h3>
              <ol>
                <li>
                  <mat-icon>gavel</mat-icon>
                  <div>
                    <strong>{{ 'review.checks.clauses.title' | translate }}</strong>
                    <p>{{ 'review.checks.clauses.desc' | translate }}</p>
                  </div>
                </li>
                <li>
                  <mat-icon>policy</mat-icon>
                  <div>
                    <strong>{{ 'review.checks.regulatory.title' | translate }}</strong>
                    <p>{{ 'review.checks.regulatory.desc' | translate }}</p>
                  </div>
                </li>
                <li>
                  <mat-icon>event_available</mat-icon>
                  <div>
                    <strong>{{ 'review.checks.obligations.title' | translate }}</strong>
                    <p>{{ 'review.checks.obligations.desc' | translate }}</p>
                  </div>
                </li>
              </ol>
              <p class="disclaimer">
                <mat-icon>info</mat-icon> {{ 'review.disclaimer' | translate }}
              </p>
            </div>
          </div>
        }

        @case ('analysing') {
          <mat-card class="analysing-card">
            <mat-icon class="analysing-icon">auto_awesome</mat-icon>
            <h2>{{ 'review.analysing.title' | translate }}</h2>
            <p class="analysing-step">{{ analysingStep() | translate }}</p>
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
            <p class="analysing-note">{{ 'review.analysing.note' | translate }}</p>
          </mat-card>
        }

        @case ('verdict') {
          @if (readiness(); as r) {
            <sf-readiness-verdict
              [verdict]="r.verdict" [score]="r.readinessScore"
              [riskScore]="r.riskScore"
              [openCount]="r.openCount" [criticalCount]="r.criticalCount"
              [highCount]="r.highCount" [handledCount]="r.handledCount">
            </sf-readiness-verdict>

            @if (sendMode()) {
              <div class="send-banner">
                <mat-icon>mark_email_read</mat-icon>
                <span>{{ 'review.send.intro' | translate }}</span>
              </div>
            }

            <div class="verdict-body">
              <mat-card class="checklist-card">
                <mat-card-content>
                  <sf-readiness-checklist
                    [documentId]="documentId()!"
                    [items]="r.items"
                    [fixedItems]="r.fixedItems" [fromOcr]="r.textSource === 'Ocr'"
                    (changed)="readiness.set($event)">
                  </sf-readiness-checklist>
                </mat-card-content>
              </mat-card>

              <mat-card class="next-card">
                <mat-card-content>
                  <h3 class="next-title">{{ 'review.next.title' | translate }}</h3>

                  @if (sendMode()) {
                    <button mat-flat-button color="primary" class="next-btn"
                            [disabled]="r.verdict === 'Blocked' || sending()"
                            [matTooltip]="r.verdict === 'Blocked' ? ('review.next.blockedHint' | translate) : ''"
                            (click)="send()">
                      <mat-icon>send</mat-icon> {{ (sending() ? 'review.send.sending' : 'review.send.button') | translate }}
                    </button>
                  } @else {
                  <button mat-flat-button color="primary" class="next-btn"
                          [disabled]="r.verdict === 'Blocked'"
                          [matTooltip]="r.verdict === 'Blocked' ? ('review.next.blockedHint' | translate) : ''"
                          [routerLink]="['/contracts', documentId(), 'designer']">
                    <mat-icon>send</mat-icon> {{ 'review.next.promote' | translate }}
                  </button>
                  }

                  <button mat-stroked-button class="next-btn" (click)="revisionInput.click()"
                          [disabled]="uploadingRevision()">
                    <mat-icon>upload_file</mat-icon>
                    {{ (uploadingRevision() ? 'review.next.uploading' : 'review.next.uploadRevision') | translate }}
                  </button>
                  <input type="file" accept="application/pdf" hidden #revisionInput
                         (change)="onRevisionFile($event)" />

                  <button mat-stroked-button class="next-btn" (click)="editText(r.items)">
                    <mat-icon>edit_note</mat-icon> {{ 'review.next.editText' | translate }}
                  </button>

                  <button mat-stroked-button class="next-btn" (click)="rerun()" [disabled]="rerunning()">
                    <mat-icon>refresh</mat-icon>
                    {{ (rerunning() ? 'review.analysing.title' : 'review.next.rerun') | translate }}
                  </button>

                  <a mat-button class="next-link" [routerLink]="['/contracts', documentId()]">
                    {{ 'review.next.openContract' | translate }}
                  </a>

                  @if (error()) {
                    <div class="error-banner"><mat-icon>error_outline</mat-icon> {{ error() }}</div>
                  }
                </mat-card-content>
              </mat-card>
            </div>
          }
        }
      }
    </div>
  `,
  styleUrl: './contract-review.component.scss'
})
export class ContractReviewComponent implements OnInit {
  private readonly documentService = inject(DocumentService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly fb = inject(FormBuilder);
  private readonly translate = inject(TranslateService);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly reviews = inject(ReviewService);

  /** The request came from a template and is a draft nobody has been told about: this page reviews it, then sends it. */
  readonly sendMode = signal(false);
  readonly sending = signal(false);

  readonly stage = signal<ReviewStage>('drop');
  readonly selectedFile = signal<File | null>(null);
  readonly isDragOver = signal(false);
  readonly error = signal<string | null>(null);
  readonly requestTypes = signal<RequestType[]>([]);
  readonly documentId = signal<string | null>(null);
  readonly readiness = signal<Review | null>(null);
  readonly rerunning = signal(false);
  readonly uploadingRevision = signal(false);
  readonly analysingStep = signal('review.analysing.uploading');

  readonly form = this.fb.group({
    title: ['', Validators.required],
    requestTypeId: [null as string | null]
  });

  ngOnInit(): void {
    // Arriving with a document id (a template request just filled in): review that draft straight away.
    const existing = this.route.snapshot.queryParamMap.get('documentId');
    if (existing) {
      this.documentId.set(existing);
      this.sendMode.set(true);
      this.analysingStep.set('review.analysing.reviewing');
      this.stage.set('analysing');
      this.documentService.getDocument(existing).subscribe({
        next: doc => this.form.patchValue({ title: doc.title }),
        error: () => {}
      });
      this.evaluate();
    }

    this.requestTypeService.getActive().subscribe({
      next: t => this.requestTypes.set(t),
      error: () => this.requestTypes.set([])
    });
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) this.setFile(file);
  }

  onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) this.setFile(file);
  }

  private setFile(file: File): void {
    if (file.type !== 'application/pdf') {
      this.error.set(this.translate.instant('documents.upload.onlyPdf'));
      return;
    }
    if (file.size > 50 * 1024 * 1024) {
      this.error.set(this.translate.instant('documents.upload.fileTooLarge'));
      return;
    }
    this.error.set(null);
    this.selectedFile.set(file);
    if (!this.form.value.title) {
      this.form.patchValue({ title: file.name.replace(/\.pdf$/i, '') });
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  /** Upload, then immediately review: the point of this page is that nobody has to press "analyse". */
  startReview(): void {
    if (!this.selectedFile() || this.form.invalid) return;
    this.stage.set('analysing');
    this.analysingStep.set('review.analysing.uploading');
    this.error.set(null);

    const formData = new FormData();
    formData.append('file', this.selectedFile()!);
    formData.append('title', this.form.value.title!);
    if (this.form.value.requestTypeId) {
      formData.append('requestTypeId', this.form.value.requestTypeId);
    }

    this.documentService.upload(formData).subscribe({
      next: result => {
        this.documentId.set(result.documentId);
        this.analysingStep.set('review.analysing.reviewing');
        this.evaluate();
      },
      error: err => this.failTo('drop', err)
    });
  }

  /** Harvey reviews in the background; the page waits for the run's result. */
  private evaluate(): void {
    this.reviews.run(this.documentId()!).subscribe({
      next: review => {
        this.readiness.set(review);
        this.rerunning.set(false);
        this.stage.set('verdict');
      },
      // The contract is uploaded either way: show what is known (the last review, if any) so it is not orphaned.
      error: (err: Error) => {
        this.rerunning.set(false);
        this.documentService.getReview(this.documentId()!).subscribe({
          next: review => this.readiness.set(review),
          error: () => {}
        });
        this.stage.set(this.documentId() ? 'verdict' : 'drop');
        this.error.set(err.message);
      }
    });
  }

  rerun(): void {
    if (!this.documentId()) return;
    this.rerunning.set(true);
    this.error.set(null);
    this.analysingStep.set('review.analysing.reviewing');
    this.stage.set('analysing');
    this.evaluate();
  }

  onRevisionFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || !this.documentId()) return;

    this.uploadingRevision.set(true);
    this.error.set(null);
    this.documentService.uploadRevision(this.documentId()!, file).subscribe({
      next: () => {
        this.uploadingRevision.set(false);
        this.snack.open(this.translate.instant('review.next.revisionUploaded'), 'OK', { duration: 3000 });
        // The revision cleared the old verdict — re-grade against the new file.
        this.rerun();
      },
      error: err => {
        this.uploadingRevision.set(false);
        this.error.set(err.error?.detail ?? this.translate.instant('review.failed'));
      }
    });
  }

  /**
   * Opens the click-to-edit view of the contract with the open findings beside it. A save creates a new version
   * of the contract, so the review is run again against it.
   */
  editText(items: ReviewItem[]): void {
    const recommendations: PdfEditorRecommendation[] = items
      .filter(i => i.status === 'Open')
      .map(i => ({
        id: i.id, severity: i.severity, title: i.clauseReference ? `${i.title} · ${i.clauseReference}` : i.title, detail: i.detail,
        whyItMatters: i.articles.length ? i.articles.map(a => `${a.citation} : « ${a.quote} »`).join('\n') : i.whyItMatters ?? undefined,
        recommendedAction: i.recommendedAction ?? undefined, proposedLanguage: i.proposedLanguage ?? undefined
      }));
    this.dialog.open(PdfTextEditorDialogComponent, {
      data: {
        documentId: this.documentId()!,
        documentTitle: this.form.value.title ?? '',
        recommendations
      } satisfies PdfTextEditorDialogData,
      width: '1360px',
      maxWidth: '98vw',
      disableClose: true
    }).afterClosed().subscribe(result => {
      if (result) this.rerun();
    });
  }

  /**
   * Starts the circuit through the review gate: refused when not reviewed or blocked, and with open high-risk findings
   * the reader gives a reason that is kept in the audit trail.
   */
  send(): void {
    if (!this.documentId()) return;
    this.sending.set(true);
    this.error.set(null);
    this.reviews.send(this.documentId()!).subscribe({
      next: sent => {
        this.sending.set(false);
        if (!sent) return;
        this.snack.open(this.translate.instant('review.send.sent'), 'OK', { duration: 4000 });
        this.router.navigate(['/contracts', this.documentId()]);
      },
      error: (err: Error) => {
        this.sending.set(false);
        this.error.set(err.message);
      }
    });
  }

  reset(): void {
    this.stage.set('drop');
    this.selectedFile.set(null);
    this.documentId.set(null);
    this.readiness.set(null);
    this.error.set(null);
    this.form.reset({ title: '', requestTypeId: null });
  }

  private failTo(stage: ReviewStage, err: { error?: { detail?: string } }): void {
    this.rerunning.set(false);
    this.stage.set(stage);
    this.error.set(err.error?.detail ?? this.translate.instant('review.failed'));
  }
}
