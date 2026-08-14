import { Component, computed, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ComplianceAnalysis } from '../../core/models';

@Component({
  selector: 'sf-contract-compliance-panel',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatTooltipModule, MatSnackBarModule, TranslatePipe
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
          @if (generatedAt()) {
            <span class="compliance-meta">{{ 'documents.detail.generated' | translate: { date: (generatedAt() | date:'MMM d, y') } }}</span>
          }
        </div>

        @if (analysisError()) {
          <div class="compliance-error"><mat-icon>error_outline</mat-icon><span>{{ analysisError() }}</span></div>
        }

        @if (complianceAnalysis(); as ca) {
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
            <ul class="finding-list">
              @for (finding of ca.findings; track finding.regulationId) {
                <li class="finding-row">
                  <span class="finding-status-chip" [ngClass]="'status-' + finding.status.toLowerCase()">
                    {{ ('documents.detail.compliance.status.' + finding.status) | translate }}
                  </span>
                  <div class="finding-text">
                    <span class="finding-regulation">{{ finding.regulationTitle }}</span>
                    <span class="finding-explanation">{{ finding.explanation }}</span>
                  </div>
                </li>
              }
            </ul>
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

    .compliance-score-row { display: flex; align-items: center; gap: 10px; margin-bottom: 14px; }
    .compliance-score-badge { font-weight: 600; border-radius: 8px; white-space: nowrap; font-size: 13px; padding: 4px 10px; }
    .score-high { background: #e6f4ea; color: #1e6b2e; }
    .score-medium { background: #fef3c7; color: #92400e; }
    .score-low { background: #fee2e2; color: #991b1b; }

    .finding-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .wide .finding-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); }
    .finding-row { display: flex; align-items: flex-start; gap: 10px; background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; padding: 10px 12px; }
    .finding-status-chip { font-size: 10.5px; font-weight: 700; border-radius: 6px; padding: 2px 8px; white-space: nowrap; flex-shrink: 0; }
    .status-addressed { background: #e6f4ea; color: #1e6b2e; }
    .status-gap { background: #fef3c7; color: #92400e; }
    .status-unclear { background: #f3f4f6; color: #6b7280; }
    .finding-text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .finding-regulation { font-size: 13px; font-weight: 600; color: #374151; }
    .finding-explanation { font-size: 12.5px; color: #6b7280; line-height: 1.5; }
  `]
})
export class ContractCompliancePanelComponent {
  // Signal inputs, not @Input() properties: `complianceAnalysis` below is a computed, and a
  // computed that reads plain properties has nothing to invalidate it — it would cache the first
  // result forever and go stale the moment the parent reloads.
  readonly documentId = input.required<string>();
  readonly complianceAnalysisJson = input<string | null | undefined>(null);
  readonly generatedAt = input<string | null | undefined>(null);
  /** Full-width placement: the regulation list goes two-up instead of a single narrow column. */
  readonly wide = input(false);
  readonly analyzed = output<void>();

  private readonly documentService = inject(DocumentService);
  private readonly translate = inject(TranslateService);

  readonly analyzing = signal(false);
  readonly analysisError = signal<string | null>(null);

  readonly complianceAnalysis = computed<ComplianceAnalysis | null>(() => {
    const json = this.complianceAnalysisJson();
    if (!json) return null;
    try { return JSON.parse(json) as ComplianceAnalysis; } catch { return null; }
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
}
