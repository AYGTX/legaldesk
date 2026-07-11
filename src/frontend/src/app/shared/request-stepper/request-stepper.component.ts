import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

/** Generic step indicator for a request-creation flow — labels are pre-translated by the caller. */
@Component({
  selector: 'sf-request-stepper',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="rs-steps">
      @for (label of steps(); track $index; let i = $index; let last = $last) {
        <div class="rs-step" [class.active]="i === currentIndex()" [class.done]="i < currentIndex()">
          <div class="rs-num">{{ i < currentIndex() ? '✓' : i + 1 }}</div>
          <span>{{ label }}</span>
        </div>
        @if (!last) {
          <div class="rs-line"></div>
        }
      }
    </div>
  `,
  styles: [`
    .rs-steps { display: flex; align-items: center; gap: 0; margin-bottom: 32px; }
    .rs-step { display: flex; align-items: center; gap: 8px; font-size: 13px; color: #5f6368; }
    .rs-step.active { color: #0b57d0; font-weight: 500; }
    .rs-step.done { color: #1e6b2e; }
    .rs-num { width: 24px; height: 24px; border-radius: 50%; border: 2px solid currentColor; display: flex; align-items: center; justify-content: center; font-size: 12px; font-weight: 600; flex-shrink: 0; }
    .rs-step.active .rs-num { background: #0b57d0; color: #fff; border-color: #0b57d0; }
    .rs-step.done .rs-num { background: #1e6b2e; color: #fff; border-color: #1e6b2e; }
    .rs-line { flex: 1; height: 1px; background: #c7d0dc; margin: 0 12px; }
  `]
})
export class RequestStepperComponent {
  readonly steps = input.required<string[]>();
  readonly currentIndex = input.required<number>();
}
