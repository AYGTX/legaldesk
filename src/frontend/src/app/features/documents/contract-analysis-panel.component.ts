import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ClauseDialogComponent, ClauseDialogData } from '../clauses/clause-dialog.component';
import { ContractRiskAnalysis, RiskFinding, RiskSeverity, RISK_SEVERITY_ORDER } from '../../core/models';
import { ClauseCategory } from '../../core/models';

/** Best-effort mapping from a free-text finding clause type to the clause library's category. */
const CATEGORY_KEYWORDS: [RegExp, ClauseCategory][] = [
  [/confiden/i, 'Confidentiality'],
  [/liab/i, 'Liability'],
  [/terminat/i, 'Termination'],
  [/data protect|privacy|gdpr/i, 'DataProtection'],
  [/intellectual property|\bip\b|copyright|trademark/i, 'IntellectualProperty'],
  [/payment|invoic|pricing|fee/i, 'Payment'],
  [/force majeure/i, 'ForceMajeure'],
  [/governing law|jurisdiction/i, 'GoverningLaw'],
  [/complian/i, 'Compliance'],
  [/warrant/i, 'Warranty'],
  [/non-?compete|exclusiv/i, 'NonCompete']
];

function guessClauseCategory(clauseType: string): ClauseCategory {
  return CATEGORY_KEYWORDS.find(([pattern]) => pattern.test(clauseType))?.[1] ?? 'Other';
}

@Component({
  selector: 'sf-contract-analysis-panel',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatExpansionModule, MatTooltipModule, MatSnackBarModule, TranslatePipe
  ],
  template: `
    <mat-card class="analysis-card">
      <mat-card-header>
        <mat-card-title class="analysis-title">
          <mat-icon class="ai-icon">gavel</mat-icon> {{ 'documents.detail.analysis.title' | translate }}
        </mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <div class="analysis-toolbar">
          <button mat-flat-button color="primary" [disabled]="analyzing()" (click)="runAnalysis()">
            @if (analyzing()) {
              <mat-spinner diameter="18"></mat-spinner>
            } @else {
              <ng-container>{{ (riskAnalysis() ? 'documents.detail.analysis.rerun' : 'documents.detail.analysis.run') | translate }}</ng-container>
            }
          </button>
          @if (generatedAt) {
            <span class="summary-meta">{{ 'documents.detail.generated' | translate: { date: (generatedAt | date:'MMM d, y') } }}</span>
          }
        </div>

        @if (analysisError()) {
          <div class="analysis-error"><mat-icon>error_outline</mat-icon><span>{{ analysisError() }}</span></div>
        }

        @if (riskAnalysis(); as ra) {
          @if (ra.profile; as p) {
            <div class="profile-grid">
              <div class="profile-item">
                <span class="profile-label">{{ 'documents.detail.analysis.profile.contractType' | translate }}</span>
                <span class="profile-value">{{ p.contractType ?? notStated() }}</span>
              </div>
              <div class="profile-item">
                <span class="profile-label">{{ 'documents.detail.analysis.profile.parties' | translate }}</span>
                <span class="profile-value">{{ p.parties ?? notStated() }}</span>
              </div>
              <div class="profile-item">
                <span class="profile-label">{{ 'documents.detail.analysis.profile.purpose' | translate }}</span>
                <span class="profile-value">{{ p.purpose ?? notStated() }}</span>
              </div>
              <div class="profile-item">
                <span class="profile-label">{{ 'documents.detail.analysis.profile.jurisdiction' | translate }}</span>
                <span class="profile-value">{{ p.jurisdiction ?? notStated() }}</span>
              </div>
              <div class="profile-item">
                <span class="profile-label">{{ 'documents.detail.analysis.profile.governingLaw' | translate }}</span>
                <span class="profile-value">{{ p.governingLaw ?? notStated() }}</span>
              </div>
              <div class="profile-item">
                <span class="profile-label">{{ 'documents.detail.analysis.profile.term' | translate }}</span>
                <span class="profile-value">{{ p.term ?? notStated() }}</span>
              </div>
            </div>
          }

          @if (ra.limitations && ra.limitations.length > 0) {
            <div class="limitations-banner">
              <mat-icon>info</mat-icon>
              <ul>
                @for (l of ra.limitations; track l) { <li>{{ l }}</li> }
              </ul>
            </div>
          }

          <div class="risk-score-row">
            <span class="risk-score-badge" [ngClass]="riskLevelClass(ra.riskScore)">
              {{ 'documents.detail.riskScore' | translate: { score: ra.riskScore } }}
            </span>
          </div>

          @for (group of groupedFindings(); track group.severity) {
            <div class="severity-group">
              <h4 class="severity-group-title" [ngClass]="severityClass(group.severity)">
                {{ ('documents.detail.severity' + group.severity) | translate }} ({{ group.findings.length }})
              </h4>
              <mat-accordion multi>
                @for (finding of group.findings; track finding.clauseType + finding.explanation) {
                  <mat-expansion-panel>
                    <mat-expansion-panel-header>
                      <mat-panel-title class="finding-title">
                        <span class="finding-clause">{{ finding.clauseType }}</span>
                        @if (finding.findingType) {
                          <span class="finding-type-chip">{{ ('documents.detail.analysis.findingType.' + finding.findingType) | translate }}</span>
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
                          <button mat-stroked-button (click)="addToLibrary(finding)">
                            <mat-icon>library_add</mat-icon> {{ 'documents.detail.analysis.addToLibrary' | translate }}
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
        } @else if (!analyzing()) {
          <div class="summary-empty">
            <mat-icon>gavel</mat-icon>
            <p>{{ 'documents.detail.noRiskFindings' | translate }}</p>
          </div>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .analysis-card { border: 1px solid #fde2d2; background: #fff8f3; }
    .analysis-title { display: flex; align-items: center; gap: 6px; font-size: 15px; }
    .ai-icon { color: #7c3aed; font-size: 18px; width: 18px; height: 18px; }
    .analysis-toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
    .summary-meta { font-size: 12px; color: #9ca3af; }
    .analysis-error { display: flex; align-items: center; gap: 8px; background: #fee2e2; color: #991b1b; border-radius: 8px; padding: 10px 12px; margin-bottom: 12px; font-size: 13px; }
    .summary-empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 16px 0; color: #9ca3af; text-align: center; }
    .summary-empty mat-icon { font-size: 28px; width: 28px; height: 28px; }
    .summary-empty p { margin: 0; font-size: 13px; }

    .profile-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px 20px; margin-bottom: 14px; padding-bottom: 14px; border-bottom: 1px solid #f0e0d0; }
    .profile-item { display: flex; flex-direction: column; gap: 2px; }
    .profile-label { font-size: 11px; color: #9ca3af; text-transform: uppercase; letter-spacing: 0.04em; }
    .profile-value { font-size: 13px; color: #374151; }

    .limitations-banner { display: flex; gap: 8px; background: #eff6ff; color: #1e40af; border-radius: 8px; padding: 10px 12px; margin-bottom: 14px; font-size: 12.5px; }
    .limitations-banner mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
    .limitations-banner ul { margin: 0; padding-left: 16px; }

    .risk-score-row { display: flex; align-items: center; gap: 10px; margin-bottom: 14px; }
    .risk-score-badge { font-weight: 600; border-radius: 8px; white-space: nowrap; font-size: 13px; padding: 4px 10px; }
    .risk-low { background: #e6f4ea; color: #1e6b2e; }
    .risk-medium { background: #fef3c7; color: #92400e; }
    .risk-high { background: #fee2e2; color: #991b1b; }
    .risk-critical { background: #7f1d1d; color: #fff; }

    .severity-group { margin-bottom: 10px; }
    .severity-group-title { font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; margin: 0 0 6px; padding: 2px 0; }
    .severity-group-title.severity-critical { color: #7f1d1d; }
    .severity-group-title.severity-high { color: #991b1b; }
    .severity-group-title.severity-medium { color: #92400e; }
    .severity-group-title.severity-low { color: #1e6b2e; }

    .finding-title { display: flex; align-items: center; gap: 8px; }
    .finding-clause { font-size: 13px; font-weight: 600; color: #374151; }
    .finding-type-chip { font-size: 10.5px; font-weight: 500; color: #6b7280; background: #f3f4f6; border-radius: 6px; padding: 1px 6px; }
    .finding-explanation { margin: 0 0 8px; font-size: 12.5px; color: #6b7280; line-height: 1.5; }
    .finding-block { margin-bottom: 10px; }
    .finding-block-label { display: block; font-size: 11px; font-weight: 700; color: #9ca3af; text-transform: uppercase; letter-spacing: 0.04em; margin-bottom: 2px; }
    .finding-block p { margin: 0; font-size: 13px; color: #374151; line-height: 1.55; }
    .proposed-language { white-space: pre-wrap; font-family: 'Consolas', 'Courier New', monospace; font-size: 12.5px; background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 6px; padding: 10px; margin: 0 0 8px; line-height: 1.5; }
    .finding-actions { display: flex; gap: 8px; }
    .finding-reference { font-size: 11.5px; color: #9ca3af; margin: 6px 0 0; }
  `]
})
export class ContractAnalysisPanelComponent {
  @Input({ required: true }) documentId!: string;
  @Input() riskAnalysisJson: string | null | undefined = null;
  @Input() generatedAt: string | null | undefined = null;
  /** Fires after a successful analysis so the parent can reload the document (obligation may have changed too). */
  @Output() analyzed = new EventEmitter<void>();

  private readonly documentService = inject(DocumentService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly analyzing = signal(false);
  readonly analysisError = signal<string | null>(null);

  readonly riskAnalysis = computed<ContractRiskAnalysis | null>(() => {
    if (!this.riskAnalysisJson) return null;
    try { return JSON.parse(this.riskAnalysisJson) as ContractRiskAnalysis; } catch { return null; }
  });

  readonly groupedFindings = computed(() => {
    const findings = this.riskAnalysis()?.findings ?? [];
    return RISK_SEVERITY_ORDER
      .map(severity => ({ severity, findings: findings.filter(f => f.severity === severity) }))
      .filter(group => group.findings.length > 0);
  });

  notStated(): string {
    return this.translate.instant('documents.detail.analysis.notStated');
  }

  runAnalysis(): void {
    this.analyzing.set(true);
    this.analysisError.set(null);
    this.documentService.analyzeContract(this.documentId).subscribe({
      next: () => { this.analyzing.set(false); this.analyzed.emit(); },
      error: err => {
        this.analyzing.set(false);
        this.analysisError.set(err.error?.detail ?? this.translate.instant('documents.detail.analysis.failed'));
      }
    });
  }

  riskLevelClass(score: number): string {
    if (score >= 75) return 'risk-critical';
    if (score >= 50) return 'risk-high';
    if (score >= 25) return 'risk-medium';
    return 'risk-low';
  }

  severityClass(severity: RiskSeverity): string {
    return `severity-${severity.toLowerCase()}`;
  }

  copyLanguage(text: string): void {
    navigator.clipboard.writeText(text).then(
      () => this.snack.open(this.translate.instant('documents.detail.analysis.copied'), 'OK', { duration: 2500 }),
      () => this.snack.open(this.translate.instant('documents.detail.linkCopyFailed'), 'OK', { duration: 3000 })
    );
  }

  addToLibrary(finding: RiskFinding): void {
    const dialogRef = this.dialog.open(ClauseDialogComponent, {
      width: '640px',
      data: {
        clause: null,
        prefill: {
          title: finding.clauseType,
          body: finding.proposedLanguage ?? '',
          category: guessClauseCategory(finding.clauseType),
          riskLevel: finding.severity === 'Critical' ? 'High' : finding.severity
        }
      } as ClauseDialogData
    });

    dialogRef.afterClosed().subscribe(created => {
      if (created) this.snack.open(this.translate.instant('documents.detail.analysis.addedToLibrary'), 'OK', { duration: 3000 });
    });
  }
}
