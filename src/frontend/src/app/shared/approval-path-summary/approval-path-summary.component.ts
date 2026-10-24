import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ApprovalPath } from '../../features/pdf-templates/approval-path.util';

/** A read-only view of the approval path a template will produce, shown while the template is being set up. */
@Component({
  selector: 'sf-approval-path-summary',
  standalone: true,
  imports: [MatIconModule, TranslatePipe],
  template: `
    @if (path().steps.length === 0) {
      <p class="empty"><mat-icon>route</mat-icon> {{ 'pdfTemplates.designer.path.empty' | translate }}</p>
    } @else {
      <ol class="steps">
        @for (step of path().steps; track step.index) {
          <li class="step">
            <span class="badge">{{ step.index }}</span>
            <div class="signers">
              @if (step.signers.length > 1) {
                <span class="par"><mat-icon>call_split</mat-icon> {{ 'pdfTemplates.designer.path.parallel' | translate }}</span>
              }
              @for (s of step.signers; track s.tempId) {
                <span class="signer">
                  <strong>{{ s.label || '—' }}</strong>
                  <span class="who" [class.pending]="s.atRequestTime">@if (s.atRequestTime) { {{ 'pdfTemplates.designer.path.chosenAtRequest' | translate }} } @else { {{ names()[s.approverId ?? ''] ?? '' }} }</span>
                </span>
              }
            </div>
          </li>
        }
      </ol>
      <p class="summary">
        @if (path().requestTimeCount > 0) {
          {{ 'pdfTemplates.designer.path.requestTime' | translate: { count: path().requestTimeCount } }}
        } @else {
          {{ 'pdfTemplates.designer.path.allFixed' | translate }}
        }
      </p>
    }
    @for (w of path().warnings; track w.code) {
      <p class="warn"><mat-icon>warning_amber</mat-icon> {{ 'pdfTemplates.designer.path.warn.' + w.code | translate: w.params }}</p>
    }
  `,
  styles: [`
    :host { display: block; }
    .steps { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .step { display: flex; gap: 10px; align-items: flex-start; }
    .badge { flex: none; width: 22px; height: 22px; border-radius: 50%; background: #0b57d0; color: #fff; font-size: 12px; font-weight: 600; display: inline-flex; align-items: center; justify-content: center; margin-top: 1px; }
    .signers { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .signer { display: flex; flex-direction: column; font-size: 13px; line-height: 1.3; }
    .who { font-size: 12px; color: #5f6368; }
    .who.pending { color: #b06000; font-style: italic; }
    .par { display: inline-flex; align-items: center; gap: 4px; font-size: 11px; color: #5f6368; text-transform: uppercase; letter-spacing: 0.04em; }
    .par mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .summary { margin: 10px 0 0; font-size: 12px; color: #5f6368; }
    .empty, .warn { display: flex; align-items: flex-start; gap: 6px; margin: 6px 0 0; font-size: 12px; line-height: 1.4; }
    .empty { color: #5f6368; }
    .warn { color: #b06000; }
    .empty mat-icon, .warn mat-icon { font-size: 16px; width: 16px; height: 16px; flex: none; }
  `]
})
export class ApprovalPathSummaryComponent {
  readonly path = input.required<ApprovalPath>();
  /** id → display name, so a fixed approver shows as a person and not as a GUID. */
  readonly names = input<Record<string, string>>({});

}
