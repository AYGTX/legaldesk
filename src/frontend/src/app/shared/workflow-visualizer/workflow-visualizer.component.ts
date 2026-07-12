import { Component, input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { WorkflowParticipant } from '../../core/models';

/**
 * Visualizes a sequential signing workflow as an ordered chain of participants.
 * Each node shows the signing order, the participant, and their status; connectors
 * turn green as earlier steps complete, and the active step pulses.
 */
@Component({
  selector: 'sf-workflow-visualizer',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="wf" [class.vertical]="orientation() === 'vertical'">
      <div class="wf-header" [class.hidden]="!showHeader()">
        <mat-icon>account_tree</mat-icon>
        <span>{{ 'shared.workflowVisualizer.header' | translate }}</span>
      </div>

      <div class="wf-track">
        @for (p of participants(); track p.order; let i = $index; let last = $last) {
          <div class="wf-step" [class.current]="p.isCurrent" [class.me]="p.isMe">
            <div class="wf-node" [style.background]="color(p)" [class.pulse]="p.isCurrent"
                 [matTooltip]="tooltip(p)">
              @if (p.status === 'Completed') {
                <mat-icon>check</mat-icon>
              } @else if (p.status === 'Rejected') {
                <mat-icon>close</mat-icon>
              } @else if (p.status === 'Skipped') {
                <mat-icon>redo</mat-icon>
              } @else if (p.status === 'ChangesRequested') {
                <mat-icon>published_with_changes</mat-icon>
              } @else {
                <span class="wf-order">{{ p.order }}</span>
              }
            </div>
            <div class="wf-label">
              <span class="wf-name">{{ p.name }}@if (p.isMe) { <span class="you">{{ 'shared.workflowVisualizer.you' | translate }}</span> }</span>
              <span class="wf-status" [style.color]="color(p)">{{ statusText(p) }}</span>
            </div>
          </div>

          @if (!last) {
            <div class="wf-connector" [class.done]="p.status === 'Completed'"></div>
          }
        }
      </div>
    </div>
  `,
  styleUrl: './workflow-visualizer.component.scss'
})
export class WorkflowVisualizerComponent {
  private readonly translate = inject(TranslateService);

  readonly participants = input.required<WorkflowParticipant[]>();
  readonly orientation = input<'horizontal' | 'vertical'>('horizontal');
  readonly showHeader = input<boolean>(true);

  color(p: WorkflowParticipant): string {
    if (p.status === 'Completed') return '#22c55e';
    if (p.status === 'Rejected') return '#ef4444';
    if (p.status === 'ChangesRequested') return '#d97706';
    if (p.status === 'Skipped' || p.status === 'Expired') return '#94a3b8';
    if (p.isCurrent) return '#6366f1';
    return '#cbd5e1';
  }

  statusText(p: WorkflowParticipant): string {
    switch (p.status) {
      case 'Completed': return this.translate.instant('shared.workflowVisualizer.status.signed');
      case 'Rejected': return this.translate.instant('shared.workflowVisualizer.status.declined');
      case 'Skipped': return this.translate.instant('shared.workflowVisualizer.status.skipped');
      case 'Expired': return this.translate.instant('shared.workflowVisualizer.status.expired');
      case 'ChangesRequested': return this.translate.instant('shared.workflowVisualizer.status.changesRequested');
      default: return p.isCurrent
        ? this.translate.instant('shared.workflowVisualizer.status.signingNow')
        : this.translate.instant('shared.workflowVisualizer.status.waiting');
    }
  }

  tooltip(p: WorkflowParticipant): string {
    if (p.signedAt) {
      return this.translate.instant('shared.workflowVisualizer.tooltipStepWithDate', {
        order: p.order, name: p.name, status: this.statusText(p),
        date: new Date(p.signedAt).toLocaleDateString()
      });
    }
    return this.translate.instant('shared.workflowVisualizer.tooltipStep', {
      order: p.order, name: p.name, status: this.statusText(p)
    });
  }
}
