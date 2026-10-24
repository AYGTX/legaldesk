import { Component, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowType } from '../../core/models';

const OPTIONS: { type: WorkflowType; icon: string }[] = [
  { type: 'Sequential', icon: 'linear_scale' },
  { type: 'Parallel', icon: 'account_tree' },
  { type: 'Mixed', icon: 'device_hub' }
];

/**
 * How the people on a workflow act: one after the other, all at once, or in stages. The same three choices, with the
 * same names and explanation, wherever a workflow is set up (request wizard, PDF template designer).
 */
@Component({
  selector: 'sf-workflow-type-toggle',
  standalone: true,
  imports: [MatIconModule, TranslatePipe],
  template: `
    <div class="wf-toggle" [class.compact]="compact()" role="radiogroup">
      @for (o of options; track o.type) {
        <button type="button" role="radio" [attr.aria-checked]="value() === o.type"
                [class.active]="value() === o.type" (click)="valueChange.emit(o.type)"
                [attr.title]="('workflow.hint.' + o.type) | translate">
          <mat-icon>{{ o.icon }}</mat-icon> {{ ('workflow.type.' + o.type) | translate }}
        </button>
      }
    </div>
    @if (!compact()) {
      <p class="wf-hint"><mat-icon>info_outline</mat-icon> {{ ('workflow.hint.' + value()) | translate }}</p>
    }
  `,
  styles: [`
    :host { display: block; }
    .wf-toggle { display: inline-flex; flex-wrap: wrap; gap: 2px; background: #f1f3f4; border-radius: 8px; padding: 2px; margin: 8px 0 6px; }
    .wf-toggle button { display: flex; align-items: center; gap: 4px; border: none; background: transparent; padding: 6px 12px; border-radius: 6px; font-size: 13px; cursor: pointer; color: #5f6368; font-family: inherit; }
    .wf-toggle button.active { background: #fff; color: #0b57d0; box-shadow: 0 1px 2px rgba(0,0,0,.15); }
    .wf-toggle mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .wf-toggle.compact { display: grid; grid-template-columns: repeat(3, 1fr); }
    .wf-toggle.compact button { flex-direction: column; justify-content: flex-start; gap: 2px; padding: 6px 4px; font-size: 11.5px; text-align: center; line-height: 1.2; }
    .wf-hint { display: flex; align-items: flex-start; gap: 6px; margin: 0 0 12px; font-size: 12.5px; color: #5f6368; }
    .wf-hint mat-icon { font-size: 16px; width: 16px; height: 16px; flex-shrink: 0; margin-top: 1px; }
  `]
})
export class WorkflowTypeToggleComponent {
  readonly value = input.required<WorkflowType>();
  readonly valueChange = output<WorkflowType>();
  readonly options = OPTIONS;
  /** Narrow panels: three equal columns, icon above the name. */
  readonly compact = input(false);
}
