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
import { CitationStatus, ComplianceAnalysis, ComplianceStatus } from '../../core/models';

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

          @if (ca.relevantLaw?.length) {
            <div class="relevant-law">
              <h4 class="relevant-law-title">
                <mat-icon>gavel</mat-icon>
                {{ 'documents.detail.compliance.relevantLaw.title' | translate: { count: ca.relevantLaw!.length } }}
              </h4>
              <p class="relevant-law-intro">{{ 'documents.detail.compliance.relevantLaw.intro' | translate }}</p>
              <mat-accordion multi>
                @for (match of ca.relevantLaw; track $index) {
                  <mat-expansion-panel>
                    <mat-expansion-panel-header>
                      <mat-panel-title class="clause-title">
                        <span class="clause-text">{{ clauseHeading(match.clause) }}</span>
                        <span class="clause-cites">
                          @for (a of match.articles; track a.articleId) {
                            <span class="law-chip">{{ shortCitation(a.citation) }}</span>
                          }
                        </span>
                      </mat-panel-title>
                    </mat-expansion-panel-header>
                    <blockquote class="clause-quote">{{ match.clause }}</blockquote>
                    @for (a of match.articles; track a.articleId) {
                      <div class="law-article">
                        <div class="law-article-head">
                          <mat-icon class="citation-verified">verified</mat-icon>
                          <span class="legal-citation">{{ a.citation }}</span>
                        </div>
                        <p class="law-article-text">{{ a.text }}</p>
                      </div>
                    }
                  </mat-expansion-panel>
                }
              </mat-accordion>
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
                          @if (finding.citationStatus) {
                            <mat-icon class="citation-icon" [ngClass]="'citation-' + finding.citationStatus.toLowerCase()"
                                      [matTooltip]="('documents.detail.compliance.citation.' + finding.citationStatus) | translate">
                              {{ citationIcon(finding.citationStatus) }}
                            </mat-icon>
                          }
                          @if (finding.severity) {
                            <span class="finding-severity-chip" [ngClass]="'severity-' + finding.severity.toLowerCase()">
                              {{ ('documents.detail.severity' + finding.severity) | translate }}
                            </span>
                          }
                        </mat-panel-title>
                      </mat-expansion-panel-header>

                      <p class="finding-explanation">{{ finding.explanation }}</p>

                      @if (finding.legalCitation && finding.citationStatus) {
                        <div class="finding-block legal-basis" [ngClass]="'citation-' + finding.citationStatus.toLowerCase()">
                          <span class="finding-block-label">{{ 'documents.detail.compliance.legalBasis' | translate }}</span>
                          <div class="legal-basis-head">
                            <span class="legal-citation">{{ finding.legalCitation }}</span>
                            <span class="citation-chip">
                              <mat-icon>{{ citationIcon(finding.citationStatus) }}</mat-icon>
                              {{ ('documents.detail.compliance.citation.' + finding.citationStatus) | translate }}
                            </span>
                          </div>
                          @if (finding.legalQuote) {
                            <blockquote class="legal-quote" [class.rejected]="finding.citationStatus === 'Rejected'">{{ finding.legalQuote }}</blockquote>
                          }
                          <p class="citation-hint">{{ ('documents.detail.compliance.citation.' + finding.citationStatus + 'Hint') | translate }}</p>
                        </div>
                      }

                      @if (finding.whyItMatters) {
                        <div class="finding-block">
                          <span class="finding-block-label">{{ 'documents.detail.analysis.whyItMatters' | translate }}</span>
                          <p class="pre-line">{{ finding.whyItMatters }}</p>
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

    .relevant-law { margin-bottom: 16px; }
    .relevant-law-title { display: flex; align-items: center; gap: 6px; font-size: 13px; font-weight: 700; color: #1e3a8a; margin: 0 0 2px; }
    .relevant-law-title mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .relevant-law-intro { margin: 0 0 8px; font-size: 12px; color: #6b7280; }
    .clause-title { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; min-width: 0; }
    .clause-text { font-size: 13px; font-weight: 600; color: #374151; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 100%; }
    .clause-cites { display: flex; flex-wrap: wrap; gap: 4px; }
    .law-chip { font-size: 10.5px; font-weight: 600; background: #e0e7ff; color: #1e3a8a; border-radius: 6px; padding: 1px 6px; white-space: nowrap; }
    .clause-quote { margin: 0 0 10px; padding: 6px 10px; font-size: 12.5px; color: #4b5563; background: #f9fafb; border-left: 3px solid #d1d5db; border-radius: 0 4px 4px 0; line-height: 1.5; }
    .law-article { border-left: 3px solid #1e6b2e; background: #fff; border-radius: 0 6px 6px 0; padding: 8px 10px; margin-bottom: 8px; }
    .law-article-head { display: flex; align-items: center; gap: 6px; margin-bottom: 4px; }
    .law-article-head mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .law-article-text { margin: 0; font-size: 12.5px; color: #374151; line-height: 1.55; white-space: pre-wrap; }

    .citation-icon { font-size: 16px; width: 16px; height: 16px; flex-shrink: 0; }
    .legal-basis { border-left: 3px solid #d1d5db; padding: 8px 10px; border-radius: 0 6px 6px 0; background: #fff; }
    .legal-basis-head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 6px; }
    .legal-citation { font-size: 13px; font-weight: 600; color: #374151; }
    .citation-chip { display: inline-flex; align-items: center; gap: 3px; font-size: 10.5px; font-weight: 700; border-radius: 6px; padding: 1px 6px; white-space: nowrap; }
    .citation-chip mat-icon { font-size: 13px; width: 13px; height: 13px; }
    .legal-quote { margin: 0 0 6px; padding: 6px 10px; font-size: 12.5px; font-style: italic; color: #374151; background: #f9fafb; border-radius: 4px; line-height: 1.5; }
    .legal-quote.rejected { text-decoration: line-through; color: #9ca3af; }
    .citation-hint { margin: 0; font-size: 11.5px; color: #6b7280; }
    .citation-verified.legal-basis { border-left-color: #1e6b2e; }
    .citation-verified .citation-chip, mat-icon.citation-verified { color: #1e6b2e; }
    .citation-verified .citation-chip { background: #e6f4ea; }
    .citation-unverified.legal-basis, .citation-pending.legal-basis { border-left-color: #d97706; }
    .citation-unverified .citation-chip, .citation-pending .citation-chip { background: #fef3c7; color: #92400e; }
    mat-icon.citation-unverified, mat-icon.citation-pending { color: #d97706; }
    .citation-rejected.legal-basis { border-left-color: #b91c1c; }
    .citation-rejected .citation-chip { background: #fee2e2; color: #991b1b; }
    mat-icon.citation-rejected { color: #b91c1c; }
    .pre-line { white-space: pre-line; }
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

  /** The clause's own title when it has one ("Article 8 - Force majeure"), else its opening words. */
  clauseHeading(clause: string): string {
    const title = clause.match(/^(.{3,90}?)(?:\.\s|:\s|$)/);
    const text = title ? title[1] : clause;
    return text.length > 90 ? text.slice(0, 90) + '…' : text;
  }

  /** Compact chip: "Loi n° 2004-63, art. 7", "COC, art. 282", "Code de commerce, art. 654". */
  shortCitation(citation: string): string {
    const plain = citation.replace(/\s*\([^)]*\)/g, '');
    const article = plain.match(/,\s*(art\.\s*.+)$/)?.[1] ?? '';
    const law = plain.match(/n°\s*([\d-]+)/);
    if (law) return `Loi ${law[1]}, ${article}`;
    const source = plain.replace(/,\s*art\..*$/, '')
      .replace('Code des obligations et des contrats', 'COC')
      .replace('Code des sociétés commerciales', 'CSC');
    return `${source}, ${article}`;
  }

  citationIcon(status: CitationStatus): string {
    switch (status) {
      case 'Verified': return 'verified';
      case 'Rejected': return 'gpp_bad';
      default: return 'help_outline';
    }
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
