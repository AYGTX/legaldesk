import { Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { ReadinessVerdict } from '../../core/models';

/**
 * The headline answer to "is this contract fit to send?" — a verdict badge, a readiness score and
 * the risk/compliance sub-scores behind it.
 *
 * Two shapes from one component: the full hero used on the contract review page, and a `compact`
 * band that sits above the fold on the document detail page. The verdict sentence is assembled from
 * counts here rather than sent by the API, so it comes out in the reader's language.
 */
@Component({
  selector: 'sf-readiness-verdict',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="verdict" [class.compact]="compact()" [ngClass]="verdictClass()">
      <div class="verdict-main">
        <div class="verdict-icon-wrap">
          <mat-icon class="verdict-icon">{{ verdictIcon() }}</mat-icon>
        </div>
        <div class="verdict-text">
          <span class="verdict-label">{{ ('shared.readiness.verdict.' + verdict()) | translate }}</span>
          <span class="verdict-sentence">{{ sentence() | translate: sentenceParams() }}</span>
        </div>
      </div>

      @if (verdict() !== 'NotEvaluated') {
        <div class="scores">
          <div class="score-tile primary" [matTooltip]="'shared.readiness.scoreHint' | translate">
            <span class="score-value">{{ score() ?? '—' }}</span>
            <span class="score-label">{{ 'shared.readiness.scoreLabel' | translate }}</span>
          </div>
          @if (riskScore() != null) {
            <div class="score-tile">
              <span class="score-value">{{ riskScore() }}</span>
              <span class="score-label">{{ 'shared.readiness.riskLabel' | translate }}</span>
            </div>
          }
          @if (complianceScore() != null) {
            <div class="score-tile">
              <span class="score-value">{{ complianceScore() }}</span>
              <span class="score-label">{{ 'shared.readiness.complianceLabel' | translate }}</span>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .verdict {
      display: flex; align-items: center; justify-content: space-between; gap: 20px; flex-wrap: wrap;
      border-radius: 14px; padding: 18px 22px; border: 1px solid transparent;
    }
    .verdict.compact { padding: 12px 16px; border-radius: 12px; }

    .verdict-main { display: flex; align-items: center; gap: 14px; min-width: 0; flex: 1; }
    .verdict-icon-wrap {
      display: flex; align-items: center; justify-content: center; flex-shrink: 0;
      width: 44px; height: 44px; border-radius: 50%; background: rgba(255, 255, 255, 0.65);
    }
    .compact .verdict-icon-wrap { width: 34px; height: 34px; }
    .verdict-icon { font-size: 24px; width: 24px; height: 24px; }
    .compact .verdict-icon { font-size: 20px; width: 20px; height: 20px; }

    .verdict-text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .verdict-label { font-size: 17px; font-weight: 700; letter-spacing: -0.01em; }
    .compact .verdict-label { font-size: 15px; }
    .verdict-sentence { font-size: 13px; opacity: 0.85; line-height: 1.45; }
    .compact .verdict-sentence { font-size: 12.5px; }

    .scores { display: flex; gap: 10px; flex-shrink: 0; }
    .score-tile {
      display: flex; flex-direction: column; align-items: center; justify-content: center;
      min-width: 72px; padding: 8px 12px; border-radius: 10px;
      background: rgba(255, 255, 255, 0.7);
    }
    .compact .score-tile { min-width: 60px; padding: 6px 10px; }
    .score-tile.primary { background: rgba(255, 255, 255, 0.95); }
    .score-value { font-size: 22px; font-weight: 700; line-height: 1.1; }
    .compact .score-value { font-size: 18px; }
    .score-label { font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.04em; opacity: 0.75; margin-top: 2px; }

    .v-ready { background: #e6f4ea; border-color: #b7e1c1; color: #12522a; }
    .v-needswork { background: #fff8e6; border-color: #f5dfa0; color: #7a4b00; }
    .v-blocked { background: #fdecea; border-color: #f5c2bd; color: #7f1d1d; }
    .v-notevaluated { background: #f2f5fa; border-color: #dbe2ec; color: #3c4858; }

    :host-context(html.dark) .verdict-icon-wrap,
    :host-context(html.dark) .score-tile { background: rgba(0, 0, 0, 0.18); }
  `]
})
export class ReadinessVerdictComponent {
  readonly verdict = input<ReadinessVerdict>('NotEvaluated');
  readonly score = input<number | null | undefined>(null);
  readonly riskScore = input<number | null | undefined>(null);
  readonly complianceScore = input<number | null | undefined>(null);
  readonly openCount = input(0);
  readonly criticalCount = input(0);
  readonly highCount = input(0);
  readonly handledCount = input(0);
  readonly compact = input(false);

  readonly verdictClass = computed(() => `v-${this.verdict().toLowerCase()}`);

  readonly verdictIcon = computed(() => {
    switch (this.verdict()) {
      case 'Ready': return 'verified';
      case 'NeedsWork': return 'build_circle';
      case 'Blocked': return 'gpp_maybe';
      default: return 'fact_check';
    }
  });

  /**
   * Picks the sentence that describes *why* the verdict is what it is. "Ready" splits on whether
   * the contract was clean to begin with or was cleaned up — the second is worth saying out loud.
   */
  readonly sentence = computed(() => {
    switch (this.verdict()) {
      case 'Blocked': return 'shared.readiness.sentence.blocked';
      case 'NeedsWork': return 'shared.readiness.sentence.needsWork';
      case 'Ready': return this.handledCount() > 0
        ? 'shared.readiness.sentence.readyAfterFixes'
        : 'shared.readiness.sentence.readyClean';
      default: return 'shared.readiness.sentence.notEvaluated';
    }
  });

  readonly sentenceParams = computed(() => ({
    critical: this.criticalCount(),
    high: this.highCount(),
    open: this.openCount(),
    handled: this.handledCount()
  }));
}
