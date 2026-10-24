import { Component, ContentChild, TemplateRef, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { StageItem, normalizeStages } from './stage-lanes.util';

/**
 * A staged workflow as lanes: one lane per stage, people on the same stage act at the same time and the next stage
 * waits for the previous one. Drag an item into a lane to move it there, or into a gap between lanes to start a new
 * stage. Used by the request wizard and by the PDF template designer, so both edit a workflow the same way.
 *
 * The card shown for each item comes from the parent: `<ng-template #card let-item>…</ng-template>`.
 * The component never edits its input: it emits the new, renumbered list on every change (the items'
 * `stage` is updated in place, as {@link normalizeStages} documents).
 */
@Component({
  selector: 'sf-stage-lanes',
  standalone: true,
  imports: [CommonModule, DragDropModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="stages" cdkDropListGroup>
      <div class="stage-gap" [class.active]="dragging()" cdkDropList (cdkDropListDropped)="dropInNewStage($event, 0)">
        <span>{{ 'workflow.stages.newStageHere' | translate }}</span>
      </div>
      @for (lane of lanes(); track lane.stage; let li = $index) {
        <section class="stage-lane">
          <header class="stage-head">
            <span class="stage-num">{{ lane.stage }}</span>
            <strong>{{ 'workflow.stages.stage' | translate }} {{ lane.stage }}</strong>
            @if (lane.items.length > 1) {
              <span class="stage-sub">{{ 'workflow.stages.together' | translate }}</span>
            }
          </header>
          <div class="stage-body" cdkDropList (cdkDropListDropped)="dropInLane($event, lane.stage)">
            @for (item of lane.items; track item) {
              <div class="stage-card" cdkDrag [cdkDragData]="item"
                   (cdkDragStarted)="dragging.set(true)" (cdkDragEnded)="dragging.set(false)">
                <span class="drag-handle" cdkDragHandle [matTooltip]="'workflow.stages.drag' | translate">
                  <mat-icon>drag_indicator</mat-icon>
                </span>
                <div class="stage-card-body">
                  <ng-container *ngTemplateOutlet="cardTpl ?? null; context: { $implicit: item }"></ng-container>
                </div>
              </div>
            }
          </div>
        </section>
        <div class="stage-gap" [class.active]="dragging()" cdkDropList (cdkDropListDropped)="dropInNewStage($event, li + 1)">
          <span>{{ 'workflow.stages.newStageHere' | translate }}</span>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .stage-lane { border: 1px solid #e8edf2; border-radius: 10px; background: #fff; }
    .stage-head { display: flex; align-items: center; gap: 8px; padding: 6px 10px 0; font-size: 12.5px; color: #5f6368; }
    .stage-num {
      flex: 0 0 22px; height: 22px; border-radius: 50%; background: #e8f0fe; color: #174ea6;
      font-size: 12px; font-weight: 600; display: inline-flex; align-items: center; justify-content: center;
    }
    .stage-sub { margin-left: auto; font-size: 11.5px; color: #80868b; }
    .stage-body { padding: 2px 6px 6px; min-height: 40px; }
    .stage-body.cdk-drop-list-receiving { background: #f6faff; border-radius: 0 0 12px 12px; }
    .stage-card { display: flex; gap: 6px; align-items: flex-start; border-radius: 8px; background: #f8f9fa; padding: 6px; margin: 4px 0; }
    .stage-card-body { flex: 1; min-width: 0; }
    .drag-handle { cursor: grab; color: #9aa0a6; display: inline-flex; margin-top: 2px; }
    .drag-handle:active { cursor: grabbing; }
    .stage-gap { height: 12px; border-radius: 10px; transition: height .15s; display: flex; align-items: center; justify-content: center; overflow: hidden; }
    .stage-gap span { display: none; font-size: 12px; color: #1a73e8; }
    .stage-gap.active { height: 38px; border: 1px dashed #c2d7fb; }
    .stage-gap.active span { display: block; }
    .stage-gap.cdk-drop-list-receiving { background: #e8f0fe; }
    .cdk-drag-preview { box-shadow: 0 6px 18px rgba(0,0,0,.2); background: #fff; border-radius: 10px; padding: 8px; display: flex; gap: 8px; }
    .cdk-drag-placeholder { opacity: .35; }
    .cdk-drag-animating { transition: transform 200ms cubic-bezier(0, 0, 0.2, 1); }
  `]
})
export class StageLanesComponent {
  readonly items = input.required<StageItem[]>();
  /** The list after a drop, renumbered 1..n and sorted by stage. */
  readonly changed = output<StageItem[]>();

  @ContentChild('card', { read: TemplateRef }) cardTpl?: TemplateRef<unknown>;

  /** True while an item is being dragged: the gaps between stages open up as drop targets. */
  readonly dragging = signal(false);

  // A method, not a computed: parents edit the items' `stage` in place, which a signal could not see.
  lanes(): { stage: number; items: StageItem[] }[] {
    const all = this.items();
    const stages = [...new Set(all.map(i => i.stage))].sort((a, b) => a - b);
    return stages.map(stage => ({ stage, items: all.filter(i => i.stage === stage) }));
  }

  /** Dropped on a stage: the item joins it, at the position it was dropped. */
  dropInLane(event: CdkDragDrop<unknown>, stage: number): void {
    const item = event.item.data as StageItem;
    const others = this.items().filter(i => i.stage === stage && i !== item);
    const index = Math.max(0, Math.min(event.currentIndex, others.length));

    const rest = this.items().filter(i => i !== item);
    item.stage = stage;
    if (others.length === 0) rest.push(item);
    else if (index < others.length) rest.splice(rest.indexOf(others[index]), 0, item);
    else rest.splice(rest.indexOf(others[others.length - 1]) + 1, 0, item);

    this.changed.emit(normalizeStages(rest));
  }

  /** Dropped in a gap: a new stage is created there. Gap 0 is before the first stage, gap n after stage n. */
  dropInNewStage(event: CdkDragDrop<unknown>, gap: number): void {
    const item = event.item.data as StageItem;
    item.stage = gap + 0.5;
    this.changed.emit(normalizeStages(this.items()));
  }
}
