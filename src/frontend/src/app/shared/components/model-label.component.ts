import { Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { ModelLabel } from '../../core/models/assistant.models';

/** "Harvey assistant, model-generated", with the provider, model, prompt and date (R-M12-3). */
@Component({
  selector: 'sf-model-label',
  standalone: true,
  imports: [DatePipe, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="model-label" [matTooltip]="'assistant.labelHint' | translate">
      <mat-icon>auto_awesome</mat-icon>
      <span class="who">{{ 'assistant.label' | translate }}</span>
      @if (label(); as l) {
        <span class="meta">{{ l.provider }} · {{ l.model }} · {{ l.promptVersion }} · {{ l.generatedAt | date: 'dd/MM/yyyy HH:mm' }}</span>
      }
    </div>
  `,
  styles: [`
    .model-label { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; font-size: 12px; color: #6b7280; margin: 6px 0; }
    .model-label mat-icon { font-size: 15px; width: 15px; height: 15px; color: #7c4dff; }
    .who { font-weight: 600; color: #4b5563; }
  `]
})
export class ModelLabelComponent {
  readonly label = input<ModelLabel | null | undefined>(null);
}
