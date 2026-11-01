import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ContractSummary } from '../../core/models/assistant.models';
import { ModelLabelComponent } from './model-label.component';

/**
 * The assistant's summary, clause by clause: each point with the contract's own words, the clauses a limit kept from
 * being read, and how many points were dropped because their quote is not in the contract (R-M12-3, R-M12-4).
 */
@Component({
  selector: 'sf-assistant-summary',
  standalone: true,
  imports: [MatIconModule, TranslatePipe, ModelLabelComponent],
  template: `
    @let s = summary();
    <sf-model-label [label]="s.label"></sf-model-label>
    @if (s.points.length === 0) {
      <p class="muted">{{ 'assistant.summary.empty' | translate }}</p>
    }
    <ol class="points">
      @for (p of s.points; track $index) {
        <li>
          <span class="clause">{{ p.clause }}</span>
          <span class="point">{{ p.point }}</span>
          <blockquote class="quote"><mat-icon>verified</mat-icon><span>« {{ p.quote }} »</span></blockquote>
        </li>
      }
    </ol>
    @if (s.notRead.length) {
      <div class="warn">
        <mat-icon>warning_amber</mat-icon>
        <span>{{ 'assistant.summary.notRead' | translate: { count: s.notRead.length } }} {{ s.notRead.join(', ') }}</span>
      </div>
    }
    @if (s.dropped > 0) {
      <p class="muted">{{ 'assistant.summary.dropped' | translate: { count: s.dropped } }}</p>
    }
    <p class="muted">{{ 'assistant.summary.disclaimer' | translate }}</p>
  `,
  styles: [`
    .points { margin: 8px 0; padding-left: 20px; display: flex; flex-direction: column; gap: 10px; }
    .clause { display: block; font-size: 12px; font-weight: 600; color: #6b7280; }
    .point { display: block; font-size: 14px; color: #111827; }
    .quote { display: flex; gap: 6px; align-items: flex-start; margin: 4px 0 0; padding: 6px 8px; background: #f5f3ff;
      border-left: 3px solid #7c4dff; font-size: 13px; color: #374151; font-style: italic; }
    .quote mat-icon { font-size: 15px; width: 15px; height: 15px; color: #16a34a; flex: none; }
    .warn { display: flex; gap: 6px; align-items: flex-start; background: #fff7ed; color: #9a3412; padding: 8px; border-radius: 6px; font-size: 13px; }
    .warn mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; }
    .muted { color: #6b7280; font-size: 12px; margin: 6px 0; }
  `]
})
export class AssistantSummaryComponent {
  readonly summary = input.required<ContractSummary>();
}
