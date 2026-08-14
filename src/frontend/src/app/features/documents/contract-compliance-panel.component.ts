import { Component, computed, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ComplianceAnalysis, ComplianceStatus } from '../../core/models';

/** Gaps first: they are the only status that asks the reader to do something. */
const COMPLIANCE_STATUS_ORDER: ComplianceStatus[] = ['Gap', 'Unclear', 'Addressed'];

@Component({
  selector: 'sf-contract-compliance-panel',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatExpansionModule, MatTooltipModule, MatSnackBarModule, TranslatePipe
  ],
  template: `
    <mat-card class="compliance-card" [class.wide]="wide()">
      <mat-card-header>
        <mat-card-title class="compliance-title">
          <mat-icon class="ai-icon">policy</mat-icon> {{ 'documents.detail.compliance.title' | translate }}
        </mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <div class="compliance-toolbar">
          <button mat-flat-button color="primary" [disabled]="analyzing()" (click)="runAnalysis()">
            @if (analyzing()) {
              <mat-spinner diameter="18"></mat-spinner>
            } @else {
              <ng-container>{{ (complianceAnalysis() ? 'documents.detail.compliance.rerun' : 'documents.detail.compliance.run') | translate }}</ng-container>
            }
          </button>
          @if (analyzing()) {
            <span class="compliance-meta">{{ 'documents.detail.compliance.running' | translate }}</span>
          } @else if (generatedAt()) {
            <span class="compliance-meta">{{ 'documents.detail.generated' | translate: { date: (generatedAt() | date:'MMM d, y') } }}</span>
          }
        </div>

        @if (analysisError()) {
          <div class="compliance-error"><mat-icon>error_outline</mat-icon><span>{{ analysisError() }}</span></div>
        }

        @if (complianceAnalysis(); as ca) {
          @if (ca.limitations?.length) {
            <div class="limitations-banner">
              <mat-icon>info</mat-icon>
              <ul>
                @for (l of ca.limitations; track l) { <li>{{ l }}</li> }
              </ul>
            </div>
          }

          <div class="compliance-score-row">
            <span class="compliance-score-badge" [ngClass]="scoreClass(ca.complianceScore)">
              {{ 'documents.detail.compliance.score' | translate: { score: ca.complianceScore } }}
            </span>
          </div>

          @if (ca.findings.length === 0) {
            <div class="compliance-empty">
              <mat-icon>rule</mat-icon>
              <p>{{ 'documents.detail.compliance.noneTracked' | translate }}</p>
            </div>
          } @else {
            @for (group of groupedFindings(); track group.status) {
              <div class="status-group">
                <h4 class="status-group-title" [ngClass]="'status-' + group.status.toLowerCase()">
                  {{ ('documents.detail.compliance.status.' + group.status) | translate }} ({{ group.findings.length }})
                </h4>
                <mat-accordion multi>
                  @for (finding of group.findings; track finding.regulationId) {
                    <mat-expansion-panel [expanded]="wide() && group.status === 'Gap'">
                      <mat-expansion-panel-header>
                        <mat-panel-title class="finding-title">
                          <span class="finding-regulation">{{ finding.regulationTitle }}</span>
                          @if (finding.severity) {
                            <span class="finding-severity-chip" [ngClass]="'severity-' + finding.severity.toLowerCase()">
                              {{ ('documents.detail.severity' + finding.severity) | translate }}
                            </span>
                          }
                        </mat-panel-title>
                      </mat-expansion-panel-header>

                      <p class="finding-explanation">{{ finding.explanation }}</p>

                      @if (finding.whyItMatters) {
                        <div class="finding-block">
                          <span class="finding-block-label">{{ 'documents.detail.analysis.whyItMatters' | translate }}</span>
                          <p>{{ finding.whyItMatters }}</p>
                        </div>
                      }
                      @if (finding.recommendedAction) {
                        <div class="finding-block">
                          <span class="finding-block-label">{{ 'documents.detail.analysis.recommendedAction' | translate }}</span>
                          <p>{{ finding.recommendedAction }}</p>
                        </div>
                      }
                      @if (finding.proposedLanguage) {
                        <div class="finding-block">
                          <span class="finding-block-label">{{ 'documents.detail.analysis.proposedLanguage' | translate }}</span>
                          <pre class="proposed-language">{{ finding.proposedLanguage }}</pre>
                          <div class="finding-actions">
                            <button mat-stroked-button (click)="copyLanguage(finding.proposedLanguage)">
                              <mat-icon>content_copy</mat-icon> {{ 'documents.detail.analysis.copy' | translate }}
                            </button>
                          </div>
                        </div>
                      }
                      @if (finding.contractReference) {
                        <p class="finding-reference">{{ 'documents.detail.analysis.reference' | translate }}: {{ finding.contractReference }}</p>
                      }
                    </mat-expansion-panel>
                  }
                </mat-accordion>
              </div>
            }
          }
        } @else if (!analyzing()) {
          <div class="compliance-empty">
            <mat-icon>policy</mat-icon>
            <p>{{ 'documents.detail.compliance.noRun' | translate }}</p>
          </div>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .compliance-card { border: 1px solid #dbe6fd; background: #f5f8ff; }
    .compliance-title { display: flex; align-items: center; gap: 6px; font-size: 15px; }
    .ai-icon { color: #1a56db; font-size: 18px; width: 18px; height: 18px; }
    .compliance-toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
    .compliance-meta { font-size: 12px; color: #9ca3af; }
    .compliance-error { display: flex; align-items: center; gap: 8px; background: #fee2e2; color: #991b1b; border-radius: 8px; padding: 10px 12px; margin-bottom: 12px; font-size: 13px; }
    .compliance-empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 16px 0; color: #9ca3af; text-align: center; }
    .compliance-empty mat-icon { font-size: 28px; width: 28px; height: 28px; }
    .compliance-empty p { margin: 0; font-size: 13px; }

    .limitations-banner { display: flex; gap: 8px; background: #eff6ff; color: #1e40af; border-radius: 8px; padding: 10px 12px; margin-bottom: 14px; font-size: 12.5px; }
    .limitations-banner mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
    .limitations-banner ul { margin: 0; padding-left: 16px; }

    .compliance-score-row { display: flex; align-items: center; gap: 10px; margin-bottom: 14px; }
    .compliance-score-badge { font-weight: 600; border-radius: 8px; white-space: nowrap; font-size: 13px; padding: 4px 10px; }
    .score-high { background: #e6f4ea; color: #1e6b2e; }
    .score-medium { background: #fef3c7; color: #92400e; }
    .score-low { background: #fee2e2; color: #991b1b; }

    .status-group { margin-bottom: 10px; }
    .status-group-title { font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; margin: 0 0 6px; padding: 2px 0; }
    .status-group-title.status-addressed { color: #1e6b2e; }
    .status-group-title.status-gap { color: #92400e; }
    .status-group-title.status-unclear { color: #6b7280; }

    .finding-title { display: flex; align-items: center; gap: 8px; }
    .finding-regulation { font-size: 13px; font-weight: 600; color: #374151; }
    .finding-severity-chip { font-size: 10.5px; font-weight: 700; border-radius: 6px; padding: 1px 6px; white-space: nowrap; flex-shrink: 0; }
    .severity-critical { background: #7f1d1d; color: #fff; }
    .severity-high { background: #fee2e2; color: #991b1b; }
    .severity-medium { background: #fef3c7; color: #92400e; }
    .severity-low { background: #e6f4ea; color: #1e6b2e; }

    .finding-explanation { margin: 0 0 8px; font-size: 12.5px; color: #6b7280; line-height: 1.5; }
    .finding-block { margin-bottom: 10px; }
    .finding-block-label { display: block; font-size: 11px; font-weight: 700; color: #9ca3af; text-transform: uppercase; letter-spacing: 0.04em; margin-bottom: 2px; }
    .finding-block p { margin: 0; font-size: 13px; color: #374151; line-height: 1.55; }
    .proposed-language { white-space: pre-wrap; font-family: 'Consolas', 'Courier New', monospace; font-size: 12.5px; background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 6px; padding: 10px; margin: 0 0 8px; line-height: 1.5; }
    .finding-actions { display: flex; gap: 8px; }
    .finding-reference { font-size: 11.5px; color: #9ca3af; margin: 6px 0 0; }
  `]
})
export class ContractCompliancePanelComponent {
  // Signal inputs, not @Input() properties: `complianceAnalysis` and `groupedFindings` below are
  // computed values, and a computed that reads plain properties has nothing to invalidate it — it
  // would cache the first result forever and go stale the moment the parent reloads.
  readonly documentId = input.required<string>();
  readonly complianceAnalysisJson = input<string | null | undefined>(null);
  readonly generatedAt = input<string | null | undefined>(null);
  /** Full-width placement: gaps start open instead of collapsed. */
  readonly wide = input(false);
  readonly analyzed = output<void>();

  private readonly documentService = inject(DocumentService);
  private readonly translate = inject(TranslateService);
  private readonly snack = inject(MatSnackBar);

  readonly analyzing = signal(false);
  readonly analysisError = signal<string | null>(null);

  readonly complianceAnalysis = computed<ComplianceAnalysis | null>(() => {
    const json = this.complianceAnalysisJson();
    if (!json) return null;
    try { return JSON.parse(json) as ComplianceAnalysis; } catch { return null; }
  });

  readonly groupedFindings = computed(() => {
    const findings = this.complianceAnalysis()?.findings ?? [];
    return COMPLIANCE_STATUS_ORDER
      .map(status => ({ status, findings: findings.filter(f => f.status === status) }))
      .filter(group => group.findings.length > 0);
  });

  runAnalysis(): void {
    this.analyzing.set(true);
    this.analysisError.set(null);
    this.documentService.analyzeCompliance(this.documentId()).subscribe({
      next: () => { this.analyzing.set(false); this.analyzed.emit(); },
      error: err => {
        this.analyzing.set(false);
        this.analysisError.set(err.error?.detail ?? this.translate.instant('documents.detail.compliance.failed'));
      }
    });
  }

  scoreClass(score: number): string {
    if (score >= 75) return 'score-high';
    if (score >= 40) return 'score-medium';
    return 'score-low';
  }

  copyLanguage(text: string): void {
    navigator.clipboard.writeText(text).then(
      () => this.snack.open(this.translate.instant('documents.detail.analysis.copied'), 'OK', { duration: 2500 }),
      () => this.snack.open(this.translate.instant('documents.detail.linkCopyFailed'), 'OK', { duration: 3000 })
    );
  }
}
