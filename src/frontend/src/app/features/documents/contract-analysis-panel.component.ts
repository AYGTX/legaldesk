import { Component, computed, inject, input, output, signal } from '@angular/core';
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
import { ContractRiskAnalysis, ReadinessItem, RiskFinding, RiskSeverity, RISK_SEVERITY_ORDER } from '../../core/models';
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
    <mat-card class="analysis-card" [class.wide]="wide()">
      <mat-card-header>
        <mat-card-title class="analysis-title">
          <mat-icon class="ai-icon">gavel</mat-icon> {{ (profileOnly() ? 'documents.detail.analysis.profileTitle' : 'documents.detail.analysis.title') | translate }}
        </mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <div class="analysis-toolbar">
          @if (canRun()) {
            <button mat-flat-button color="primary" [disabled]="analyzing()" (click)="runAnalysis()">
              @if (analyzing()) {
                <mat-spinner diameter="18"></mat-spinner>
              } @else {
                <ng-container>{{ (riskAnalysis() ? 'documents.detail.analysis.rerun' : 'documents.detail.analysis.run') | translate }}</ng-container>
              }
            </button>
          }
          @if (generatedAt()) {
            <span class="summary-meta">{{ 'documents.detail.generated' | translate: { date: (generatedAt() | date:'MMM d, y') } }}</span>
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

          <!-- The risk score is shown once, beside the readiness score at the top of the page. -->
          @if (profileOnly()) {
            <!-- Findings are the readiness checklist's job on the document page. -->
          } @else if (wide()) {
            <!-- Master / detail: the list of findings on the left, the selected one in full on the right -->
            <div class="md-layout">
              <div class="md-list">
                <div class="md-filters">
                  <button class="md-pill" [class.active]="filter() === 'all'" (click)="filter.set('all')">
                    {{ 'documents.detail.analysis.filterAll' | translate }} {{ flatFindings().length }}
                  </button>
                  @for (g of groupedFindings(); track g.severity) {
                    <button class="md-pill" [class.active]="filter() === g.severity" (click)="filter.set(g.severity)">
                      {{ ('documents.detail.severity' + g.severity) | translate }} {{ g.findings.length }}
                    </button>
                  }
                </div>
                <ul class="md-items">
                  @for (item of visibleFindings(); track item.key) {
                    <li>
                      <button class="md-item" [class.selected]="item.key === selected()?.key" (click)="selectedKey.set(item.key)">
                        <span class="md-dot" [ngClass]="severityClass(item.severity)"></span>
                        <span class="md-item-text">
                          <span class="md-item-title">{{ item.finding.clauseType }}</span>
                          @if (item.finding.findingType) {
                            <span class="md-item-sub">{{ ('documents.detail.analysis.findingType.' + item.finding.findingType) | translate }}</span>
                          }
                        </span>
                      </button>
                    </li>
                  }
                  @if (visibleFindings().length === 0) {
                    <li class="md-empty">{{ 'documents.detail.analysis.noneOpen' | translate }}</li>
                  }
                </ul>
                @if (handledFindings().length > 0) {
                  <button class="md-handled-toggle" (click)="showHandled.set(!showHandled())">
                    <mat-icon>{{ showHandled() ? 'expand_less' : 'expand_more' }}</mat-icon>
                    {{ 'documents.detail.analysis.handled' | translate: { count: handledFindings().length } }}
                  </button>
                  @if (showHandled()) {
                    <ul class="md-items">
                      @for (item of handledFindings(); track item.key) {
                        <li>
                          <button class="md-item handled" [class.selected]="item.key === selected()?.key" (click)="selectedKey.set(item.key)">
                            <mat-icon class="md-check">check_circle</mat-icon>
                            <span class="md-item-text">
                              <span class="md-item-title">{{ item.finding.clauseType }}</span>
                              <span class="md-item-sub">{{ ('documents.detail.analysis.status.' + itemFor(item.finding)!.status) | translate }}</span>
                            </span>
                          </button>
                        </li>
                      }
                    </ul>
                  }
                }
              </div>
              <div class="md-detail">
                @if (selected(); as sel) {
                  <div class="md-chips">
                    <span class="md-sev" [ngClass]="severityClass(sel.severity)">{{ ('documents.detail.severity' + sel.severity) | translate }}</span>
                    @if (sel.finding.findingType) {
                      <span class="finding-type-chip">{{ ('documents.detail.analysis.findingType.' + sel.finding.findingType) | translate }}</span>
                    }
                    @if (itemFor(sel.finding); as it) {
                      @if (it.status !== 'Open') {
                        <span class="status-chip"><mat-icon>check_circle</mat-icon> {{ ('documents.detail.analysis.status.' + it.status) | translate }}</span>
                      }
                    }
                  </div>
                  <h3 class="md-title">{{ sel.finding.clauseType }}</h3>
                  @if (itemFor(sel.finding)?.resolutionNote; as note) {
                    <p class="resolution-note"><mat-icon>notes</mat-icon> {{ note }}</p>
                  }
                  <p class="finding-explanation">{{ sel.finding.explanation }}</p>

                  @if (sel.finding.whyItMatters || sel.finding.recommendedAction) {
                    <div class="md-two-col">
                      @if (sel.finding.whyItMatters) {
                        <div class="finding-block">
                          <span class="finding-block-label">{{ 'documents.detail.analysis.whyItMatters' | translate }}</span>
                          <p class="pre-line">{{ sel.finding.whyItMatters }}</p>
                        </div>
                      }
                      @if (sel.finding.recommendedAction) {
                        <div class="finding-block">
                          <span class="finding-block-label">{{ 'documents.detail.analysis.recommendedAction' | translate }}</span>
                          <p>{{ sel.finding.recommendedAction }}</p>
                        </div>
                      }
                    </div>
                  }
                  @if (sel.finding.proposedLanguage) {
                    <div class="finding-block">
                      <span class="finding-block-label">{{ 'documents.detail.analysis.proposedLanguage' | translate }}</span>
                      <pre class="proposed-language">{{ sel.finding.proposedLanguage }}</pre>
                      <div class="finding-actions">
                        <button mat-stroked-button (click)="copyLanguage(sel.finding.proposedLanguage)">
                          <mat-icon>content_copy</mat-icon> {{ 'documents.detail.analysis.copy' | translate }}
                        </button>
                        <button mat-stroked-button (click)="addToLibrary(sel.finding)">
                          <mat-icon>library_add</mat-icon> {{ 'documents.detail.analysis.addToLibrary' | translate }}
                        </button>
                      </div>
                    </div>
                  }
                  @if (sel.finding.contractReference) {
                    <p class="finding-reference">{{ 'documents.detail.analysis.reference' | translate }}: {{ sel.finding.contractReference }}</p>
                  }
                }
              </div>
            </div>
          } @else {
          @for (group of groupedFindings(); track group.severity) {
            <div class="severity-group">
              <h4 class="severity-group-title" [ngClass]="severityClass(group.severity)">
                {{ ('documents.detail.severity' + group.severity) | translate }} ({{ group.findings.length }})
              </h4>
              <mat-accordion multi>
                @for (finding of group.findings; track finding.clauseType + finding.explanation) {
                  <mat-expansion-panel [expanded]="wide() && isSevere(group.severity)">
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
          @if (handledFindings().length > 0) {
            <div class="severity-group">
              <h4 class="severity-group-title handled-title">
                {{ 'documents.detail.analysis.handled' | translate: { count: handledFindings().length } }}
              </h4>
              <ul class="handled-list">
                @for (item of handledFindings(); track item.key) {
                  <li><mat-icon class="md-check">check_circle</mat-icon> {{ item.finding.clauseType }}
                    <span class="md-item-sub">· {{ ('documents.detail.analysis.status.' + itemFor(item.finding)!.status) | translate }}</span></li>
                }
              </ul>
            </div>
          }
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
    .wide .profile-grid { grid-template-columns: repeat(3, 1fr); gap: 14px 28px; }
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
    .severity-group-title.severity-possible { color: #b85d12; border-top: 1px dashed #ef8024; padding-top: 10px; }

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
    .pre-line { white-space: pre-line; }

    .md-layout { display: grid; grid-template-columns: 320px 1fr; gap: 16px; align-items: start; }
    @media (max-width: 900px) { .md-layout { grid-template-columns: 1fr; } }
    .md-list { background: #fff; border: 1px solid #f0e0d0; border-radius: 12px; padding: 8px; }
    .md-filters { display: flex; flex-wrap: wrap; gap: 4px; padding: 6px; }
    .md-pill { border: 0; background: transparent; cursor: pointer; font: inherit; font-size: 12px; color: #6b7280; border-radius: 99px; padding: 4px 10px; }
    .md-pill:hover { background: #f3f4f6; }
    .md-pill.active { background: #374151; color: #fff; }
    .md-items { list-style: none; margin: 0; padding: 0; max-height: 520px; overflow-y: auto; }
    .md-item { display: flex; align-items: center; gap: 10px; width: 100%; border: 0; background: transparent; cursor: pointer; font: inherit; text-align: left; border-radius: 8px; padding: 9px 10px; }
    .md-item:hover { background: #f9fafb; }
    .md-item.selected { background: #f3f4f6; }
    .md-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; background: #9ca3af; }
    .md-dot.severity-critical { background: #7f1d1d; }
    .md-dot.severity-high { background: #dc2626; }
    .md-dot.severity-medium { background: #d97706; }
    .md-dot.severity-low { background: #16a34a; }
    .md-dot.severity-possible { background: #ef8024; }
    .md-item-text { display: flex; flex-direction: column; min-width: 0; }
    .md-item-title { font-size: 13px; font-weight: 600; color: #374151; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .md-item-sub { font-size: 11.5px; color: #9ca3af; }
    .md-empty { font-size: 12.5px; color: #9ca3af; padding: 10px; }
    .md-handled-toggle { display: flex; align-items: center; gap: 4px; width: 100%; border: 0; border-top: 1px solid #f0e0d0; background: transparent; cursor: pointer; font: inherit; font-size: 12px; color: #6b7280; padding: 8px 6px 4px; margin-top: 4px; }
    .md-handled-toggle mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .md-item.handled .md-item-title { color: #9ca3af; text-decoration: line-through; }
    .md-check { color: #16a34a; font-size: 16px; width: 16px; height: 16px; flex-shrink: 0; vertical-align: middle; }
    .status-chip { display: inline-flex; align-items: center; gap: 4px; font-size: 11.5px; font-weight: 600; border-radius: 99px; padding: 2px 10px; background: #e6f4ea; color: #1e6b2e; }
    .status-chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .resolution-note { display: flex; gap: 6px; align-items: flex-start; font-size: 12.5px; color: #1e6b2e; background: #f0fdf4; border-radius: 8px; padding: 8px 10px; margin: 0 0 10px; }
    .resolution-note mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .handled-title { color: #1e6b2e; }
    .handled-list { list-style: none; margin: 0; padding: 0; font-size: 13px; color: #9ca3af; }
    .handled-list li { padding: 3px 0; }
    .md-detail { background: #fff; border: 1px solid #f0e0d0; border-radius: 12px; padding: 22px 24px; min-width: 0; }
    .md-chips { display: flex; align-items: center; gap: 8px; }
    .md-sev { font-size: 11.5px; font-weight: 600; border-radius: 99px; padding: 2px 10px; background: #f3f4f6; color: #374151; }
    .md-sev.severity-critical { background: #7f1d1d; color: #fff; }
    .md-sev.severity-high { background: #fee2e2; color: #991b1b; }
    .md-sev.severity-medium { background: #fef3c7; color: #92400e; }
    .md-sev.severity-low { background: #e6f4ea; color: #1e6b2e; }
    .md-sev.severity-possible { background: #fff1e6; color: #b85d12; }
    .md-title { font-size: 22px; font-weight: 700; color: #1f2937; margin: 10px 0 6px; }
    .md-two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin: 14px 0; }
    @media (max-width: 700px) { .md-two-col { grid-template-columns: 1fr; } }
  `]
})
export class ContractAnalysisPanelComponent {
  // Signal inputs, not @Input() properties: `riskAnalysis` and `groupedFindings` below are
  // computed values, and a computed that reads plain properties has nothing to invalidate it —
  // it would cache the first result forever and go stale the moment the parent reloads.
  readonly documentId = input.required<string>();
  readonly riskAnalysisJson = input<string | null | undefined>(null);
  readonly generatedAt = input<string | null | undefined>(null);
  /**
   * Full-width placement (the document's Intelligence tab) rather than the narrow sidebar: the
   * contract profile spreads out and severe findings start open instead of collapsed.
   */
  readonly wide = input(false);
  /** False when the page already offers the review (which refreshes this panel too): a second button would only fork it. */
  readonly canRun = input(true);
  /**
   * The pre-flight checklist items. A finding whose item was resolved or dismissed there is moved out of the
   * open list into a collapsed "handled" group, so this tab and the checklist never disagree.
   */
  readonly readinessItems = input<ReadinessItem[]>([]);
  /** Only what Harvey read the contract as (profile, limitations): the findings are listed by the checklist. */
  readonly profileOnly = input(false);
  /** Fires after a successful analysis so the parent can reload the document (obligation may have changed too). */
  readonly analyzed = output<void>();

  private readonly documentService = inject(DocumentService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly analyzing = signal(false);
  readonly analysisError = signal<string | null>(null);

  readonly riskAnalysis = computed<ContractRiskAnalysis | null>(() => {
    const json = this.riskAnalysisJson();
    if (!json) return null;
    try { return JSON.parse(json) as ContractRiskAnalysis; } catch { return null; }
  });

  /** Checklist item per finding, matched on the key the readiness evaluator builds (risk:clause|reference). */
  private readonly itemByKey = computed(() =>
    new Map(this.readinessItems().filter(i => i.category === 'Risk').map(i => [i.sourceKey, i])));

  itemFor(finding: RiskFinding): ReadinessItem | undefined {
    const key = finding.contractReference ? `risk:${finding.clauseType}|${finding.contractReference}` : `risk:${finding.clauseType}`;
    return this.itemByKey().get(key);
  }

  private isHandled(finding: RiskFinding): boolean {
    const status = this.itemFor(finding)?.status;
    return status === 'Resolved' || status === 'Dismissed';
  }

  /** Findings already fixed or dismissed in the checklist, shown apart and collapsed. */
  readonly handledFindings = computed(() =>
    (this.riskAnalysis()?.findings ?? []).filter(f => this.isHandled(f))
      .map((finding, i) => ({ key: `h-${i}`, severity: (finding.findingType === 'Possible' ? 'Possible' : finding.severity) as RiskSeverity | 'Possible', finding })));

  readonly showHandled = signal(false);

  // Certain findings by severity, then the meaning-based leads ("Possible") in a group of their own.
  readonly groupedFindings = computed(() => {
    const findings = (this.riskAnalysis()?.findings ?? []).filter(f => !this.isHandled(f));
    const certain = findings.filter(f => f.findingType !== 'Possible');
    const groups: { severity: RiskSeverity | 'Possible'; findings: RiskFinding[] }[] = RISK_SEVERITY_ORDER
      .map(severity => ({ severity, findings: certain.filter(f => f.severity === severity) }))
      .filter(group => group.findings.length > 0);
    const possible = findings.filter(f => f.findingType === 'Possible');
    if (possible.length) groups.push({ severity: 'Possible', findings: possible });
    return groups;
  });

  /** Every finding in one list, in severity order, for the master/detail layout. */
  readonly flatFindings = computed(() =>
    this.groupedFindings().flatMap((g, gi) =>
      g.findings.map((finding, i) => ({ key: `${gi}-${i}`, severity: g.severity, finding }))));

  readonly filter = signal<RiskSeverity | 'Possible' | 'all'>('all');
  readonly selectedKey = signal<string | null>(null);

  readonly visibleFindings = computed(() => {
    const f = this.filter();
    return f === 'all' ? this.flatFindings() : this.flatFindings().filter(x => x.severity === f);
  });

  /** The chosen finding, or the first visible one when nothing (or something filtered out) is chosen. */
  readonly selected = computed(() => {
    const list = this.visibleFindings();
    return list.find(x => x.key === this.selectedKey())
      ?? this.handledFindings().find(x => x.key === this.selectedKey())
      ?? list[0] ?? null;
  });

  notStated(): string {
    return this.translate.instant('documents.detail.analysis.notStated');
  }

  runAnalysis(): void {
    this.analyzing.set(true);
    this.analysisError.set(null);
    this.documentService.analyzeContract(this.documentId()).subscribe({
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

  isSevere(severity: RiskSeverity | 'Possible'): boolean {
    return severity === 'Critical' || severity === 'High';
  }

  severityClass(severity: RiskSeverity | 'Possible'): string {
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
