import { Component, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ReviewGate } from '../../core/models';

/**
 * "Before you continue": what a participant has to take in before acting on a request. The contract itself is the
 * page below; this card brings the AI review of it, for participants who are allowed to see it, and asks them to
 * confirm they have read both. Once confirmed it shrinks to a line, which can be opened again to re-read the review.
 */
@Component({
  selector: 'sf-review-gate',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <section class="rg-card" [class.rg-done]="gate().acknowledged">
      <div class="rg-head">
        <mat-icon class="rg-icon">{{ gate().acknowledged ? 'task_alt' : 'fact_check' }}</mat-icon>
        <div class="rg-title">
          @if (gate().acknowledged) {
            <strong>{{ (gate().review ? 'signing.gate.done' : 'signing.gate.doneNoReview') | translate }}</strong>
          } @else {
            <strong>{{ 'signing.gate.title' | translate }}</strong>
            <span>{{ 'signing.gate.subtitle' | translate }}</span>
          }
        </div>
        @if (gate().acknowledged && gate().review) {
          <button mat-button (click)="expanded.set(!expanded())">
            {{ (expanded() ? 'signing.gate.hideReview' : 'signing.gate.showReview') | translate }}
          </button>
        }
      </div>

      @if (!gate().acknowledged || expanded()) {
        @if (gate().review; as r) {
          <div class="rg-verdict-row">
            <span class="rg-verdict v-{{ r.verdict }}">{{ ('signing.gate.verdict.' + r.verdict) | translate }}</span>
            @if (r.score !== null && r.score !== undefined) { <span class="rg-chip">{{ 'signing.gate.readiness' | translate }} {{ r.score }}</span> }
            @if (r.riskScore !== null && r.riskScore !== undefined) { <span class="rg-chip">{{ 'signing.gate.risk' | translate }} {{ r.riskScore }}</span> }
            @if (r.complianceScore !== null && r.complianceScore !== undefined) { <span class="rg-chip">{{ 'signing.gate.compliance' | translate }} {{ r.complianceScore }}</span> }
            <span class="rg-count">{{ 'signing.gate.openCount' | translate: { count: r.openCount, critical: r.criticalCount } }}</span>
          </div>

          @if (r.items.length === 0) {
            <p class="rg-note"><mat-icon>task_alt</mat-icon> {{ 'signing.gate.noFindings' | translate }}</p>
          }
          <ul class="rg-items">
            @for (item of visibleItems(); track $index) {
              <li class="rg-item" [class.handled]="item.status !== 'Open'">
                <div class="rg-item-top">
                  <span class="rg-sev sev-{{ item.severity }}">{{ item.severity }}</span>
                  <strong>{{ item.title }}</strong>
                  @if (item.status !== 'Open') {
                    <span class="rg-status">{{ ('signing.gate.status.' + item.status) | translate }}</span>
                  }
                </div>
                <p>{{ item.detail }}</p>
                @if (item.status !== 'Open' && item.resolutionNote) { <p class="rg-reason">“{{ item.resolutionNote }}”</p> }
              </li>
            }
          </ul>
          @if (r.items.length > collapsedCount) {
            <button mat-button (click)="showAll.set(!showAll())">
              {{ (showAll() ? 'signing.gate.showFewer' : 'signing.gate.showAll') | translate: { count: r.items.length } }}
            </button>
          }
        } @else if (gate().reviewRestricted) {
          <p class="rg-note"><mat-icon>lock</mat-icon> {{ 'signing.gate.restricted' | translate }}</p>
        } @else {
          <p class="rg-note"><mat-icon>info</mat-icon> {{ 'signing.gate.noReview' | translate }}</p>
        }
      }

      @if (!gate().acknowledged) {
        <label class="rg-confirm">
          <input type="checkbox" [checked]="checked()" (change)="checked.set($any($event.target).checked)" />
          {{ (gate().review ? 'signing.gate.confirmWithReview' : 'signing.gate.confirm') | translate }}
        </label>
        <div class="rg-actions">
          <button mat-flat-button color="primary" [disabled]="!checked() || busy()" (click)="confirmed.emit()">
            <mat-icon>check</mat-icon> {{ 'signing.gate.continue' | translate }}
          </button>
        </div>
      }
    </section>
  `,
  styles: [`
    .rg-card { margin: 12px auto; max-width: 960px; padding: 16px 20px; border: 1px solid #c2d7fb; border-radius: 12px; background: #f4f8ff; }
    .rg-card.rg-done { border-color: #cfe8d5; background: #f4fbf6; padding: 10px 16px; }
    .rg-head { display: flex; align-items: center; gap: 12px; }
    .rg-icon { color: #0b57d0; font-size: 28px; width: 28px; height: 28px; }
    .rg-done .rg-icon { color: #137333; }
    .rg-title { flex: 1; display: flex; flex-direction: column; gap: 2px; font-size: 13px; color: #3c4043; }
    .rg-title strong { font-size: 15px; color: #202124; }
    .rg-verdict-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin: 12px 0 6px; }
    .rg-verdict { font-weight: 600; font-size: 13px; padding: 2px 10px; border-radius: 12px; background: #fff; border: 1px solid #dadce0; }
    .v-Ready { color: #137333; border-color: #137333; } .v-NeedsWork { color: #b06000; border-color: #b06000; } .v-Blocked { color: #b3261e; border-color: #b3261e; }
    .rg-chip { font-size: 12px; color: #5f6368; background: #fff; border-radius: 10px; padding: 2px 8px; border: 1px solid #e0e4ea; }
    .rg-count { font-size: 12px; color: #5f6368; }
    .rg-items { list-style: none; margin: 8px 0 0; padding: 0; display: flex; flex-direction: column; gap: 8px; max-height: 320px; overflow: auto; }
    .rg-item { background: #fff; border: 1px solid #e0e4ea; border-radius: 8px; padding: 8px 12px; font-size: 13px; }
    .rg-item.handled { opacity: .75; }
    .rg-item p { margin: 4px 0 0; color: #3c4043; }
    .rg-item-top { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .rg-sev { font-size: 11px; font-weight: 600; text-transform: uppercase; padding: 1px 8px; border-radius: 8px; background: #eee; color: #444; }
    .sev-Critical { background: #fce8e6; color: #b3261e; } .sev-High { background: #feefe3; color: #b45309; } .sev-Medium { background: #fef7e0; color: #b06000; }
    .rg-status { font-size: 11px; color: #137333; background: #e6f4ea; padding: 1px 8px; border-radius: 8px; }
    .rg-reason { font-style: italic; color: #5f6368 !important; }
    .rg-note { display: flex; align-items: center; gap: 8px; margin: 10px 0 0; font-size: 13px; color: #5f6368; }
    .rg-note mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .rg-confirm { display: flex; align-items: center; gap: 8px; margin-top: 14px; font-size: 14px; color: #202124; cursor: pointer; }
    .rg-actions { display: flex; justify-content: flex-end; margin-top: 8px; }
  `]
})
export class ReviewGateComponent {
  readonly gate = input.required<ReviewGate>();
  readonly busy = input(false);
  readonly confirmed = output<void>();

  readonly collapsedCount = 4;
  readonly checked = signal(false);
  readonly expanded = signal(false);
  readonly showAll = signal(false);

  visibleItems() {
    const items = this.gate().review?.items ?? [];
    return this.showAll() ? items : items.slice(0, this.collapsedCount);
  }
}
