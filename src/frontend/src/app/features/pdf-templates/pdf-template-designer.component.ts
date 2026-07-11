import {
  Component, OnInit, OnDestroy, inject, signal, computed, ElementRef, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CdkDragDrop, CdkDrag, DragDropModule } from '@angular/cdk/drag-drop';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { v4 as uuidv4 } from 'uuid';
import { PdfTemplateService } from '../../core/services/pdf-template.service';
import { UserService } from '../../core/services/user.service';
import {
  PdfFieldType, PdfFieldFillRole, PdfTemplateFieldDto, PDF_AUTO_FILL_OPTIONS
} from '../../core/models/pdf-template.models';
import { WorkflowType, UserDto } from '../../core/models';

interface TemplateField {
  tempId: string;
  id?: string;
  fieldKey: string;
  label: string;
  type: PdfFieldType;
  fillRole: PdfFieldFillRole;
  page: number;
  x: number; y: number; width: number; height: number;
  isRequired: boolean;
  autoFillFrom?: string;
  defaultValue?: string;
  optionsJson?: string;
  preAssignedApproverId?: string;
  workflowStage: number;
}

const PDF_FIELD_DEFS: { type: PdfFieldType; icon: string; width: number; height: number }[] = [
  { type: 'Text', icon: 'short_text', width: 180, height: 36 },
  { type: 'Date', icon: 'calendar_today', width: 140, height: 36 },
  { type: 'Number', icon: 'pin', width: 120, height: 36 },
  { type: 'Checkbox', icon: 'check_box', width: 28, height: 28 },
  { type: 'Signature', icon: 'draw', width: 200, height: 56 },
  { type: 'Dropdown', icon: 'arrow_drop_down_circle', width: 180, height: 36 }
];

const ROLE_COLORS: Record<PdfFieldFillRole, string> = {
  Requester: '#0b57d0',
  Approver: '#188038',
  System: '#e37400'
};

@Component({
  selector: 'sf-pdf-template-designer',
  standalone: true,
  imports: [
    CommonModule, RouterLink, FormsModule, DragDropModule,
    MatToolbarModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatCheckboxModule, MatTooltipModule,
    MatProgressSpinnerModule, MatSnackBarModule, TranslatePipe
  ],
  template: `
    <div class="dz">
      <mat-toolbar class="dz-toolbar">
        <button mat-icon-button routerLink="/pdf-templates" [matTooltip]="'common.back' | translate"><mat-icon>arrow_back</mat-icon></button>
        <input class="tpl-name-input" [(ngModel)]="name" [placeholder]="'pdfTemplates.designer.namePlaceholder' | translate" />
        <span class="flex-1"></span>

        <div class="wf-selector">
          <button class="wf-btn" [class.active]="workflowType() === 'Sequential'" (click)="workflowType.set('Sequential')" [matTooltip]="'designer.workflow.sequentialHint' | translate"><mat-icon>linear_scale</mat-icon></button>
          <button class="wf-btn" [class.active]="workflowType() === 'Parallel'" (click)="workflowType.set('Parallel')" [matTooltip]="'designer.workflow.parallelHint' | translate"><mat-icon>account_tree</mat-icon></button>
          <button class="wf-btn" [class.active]="workflowType() === 'Mixed'" (click)="workflowType.set('Mixed')" [matTooltip]="'designer.workflow.mixedHint' | translate"><mat-icon>device_hub</mat-icon></button>
        </div>

        <button class="grid-toggle" [class.active]="snapToGrid()" (click)="snapToGrid.set(!snapToGrid())"
                [matTooltip]="'pdfTemplates.designer.snapToGrid' | translate">
          <mat-icon>grid_on</mat-icon>
        </button>

        @if (doc()) {
          <div class="page-controls">
            <button mat-icon-button (click)="prevPage()" [disabled]="currentPage() <= 1"><mat-icon>chevron_left</mat-icon></button>
            <span class="page-indicator">{{ 'designer.toolbar.pageIndicator' | translate: { n: currentPage(), total: pageCount() } }}</span>
            <button mat-icon-button (click)="nextPage()" [disabled]="currentPage() >= pageCount()"><mat-icon>chevron_right</mat-icon></button>
          </div>
        }

        <button mat-flat-button class="save-btn" [disabled]="saving() || !name.trim()" (click)="save()">
          <mat-icon>save</mat-icon> {{ saving() ? ('designer.toolbar.saving' | translate) : ('common.save' | translate) }}
        </button>
      </mat-toolbar>

      <div class="dz-body">
        <!-- Field palette -->
        <div class="palette" cdkDropList #paletteList="cdkDropList"
             [cdkDropListConnectedTo]="canvasList ? [canvasList] : []" cdkDropListSortingDisabled>
          <div class="palette-title">{{ 'pdfTemplates.designer.fieldTypes' | translate }}</div>
          @for (fd of fieldDefs; track fd.type) {
            <div class="palette-chip" [cdkDragData]="fd" cdkDrag>
              <div *cdkDragPreview class="drag-preview"><mat-icon>{{ fd.icon }}</mat-icon></div>
              <mat-icon>{{ fd.icon }}</mat-icon>
              <span>{{ ('pdfTemplates.fieldTypes.' + fd.type) | translate }}</span>
            </div>
          }
          <div class="legend">
            <div class="legend-title">{{ 'pdfTemplates.designer.fillRoleLegend' | translate }}</div>
            <div class="legend-item"><span class="dot" [style.background]="roleColors.Requester"></span>{{ 'pdfTemplates.fillRoles.Requester' | translate }}</div>
            <div class="legend-item"><span class="dot" [style.background]="roleColors.Approver"></span>{{ 'pdfTemplates.fillRoles.Approver' | translate }}</div>
            <div class="legend-item"><span class="dot" [style.background]="roleColors.System"></span>{{ 'pdfTemplates.fillRoles.System' | translate }}</div>
          </div>
        </div>

        <!-- Canvas -->
        <div class="canvas-area">
          @if (pdfLoading()) { <div class="pdf-loading"><mat-spinner diameter="48"></mat-spinner></div> }
          <div class="pdf-wrap" #pdfWrapper
               cdkDropList #canvasList="cdkDropList" cdkDropListSortingDisabled
               (cdkDropListDropped)="onDrop($event)"
               [style.width.px]="canvasWidth()" [style.height.px]="canvasHeight()"
               (click)="onCanvasClick($event)">
            <canvas #pdfCanvas class="pdf-canvas"></canvas>
            @if (snapToGrid()) {
              <div class="grid-overlay" [style.background-size]="gridSize + 'px ' + gridSize + 'px'"></div>
            }
            @for (f of pageFields(); track f.tempId) {
              <div class="placed-field" [class.selected]="selected()?.tempId === f.tempId"
                   [style.left.px]="f.x" [style.top.px]="f.y" [style.width.px]="f.width" [style.height.px]="f.height"
                   [style.border-color]="roleColor(f)" [style.background]="roleColor(f) + '18'"
                   cdkDrag (cdkDragEnded)="onFieldDragEnd($event, f)" (click)="selectField(f, $event)"
                   [matTooltip]="f.label + ' · ' + (('pdfTemplates.fillRoles.' + f.fillRole) | translate)">
                <div class="pf-header" [style.background]="roleColor(f)">
                  <mat-icon>{{ iconFor(f.type) }}</mat-icon><span>{{ f.label }}</span>
                </div>
                <div class="resize-handle" (mousedown)="startResize($event, f)"></div>
                <button class="pf-del" (click)="deleteField(f, $event)"><mat-icon>close</mat-icon></button>
              </div>
            }
          </div>
        </div>

        <!-- Properties panel -->
        @if (selected(); as sel) {
          <div class="props">
            <div class="props-head">
              <h3>{{ 'pdfTemplates.designer.fieldProperties' | translate }}</h3>
              <button mat-icon-button (click)="selected.set(null)"><mat-icon>close</mat-icon></button>
            </div>

            <label class="pl">{{ 'pdfTemplates.designer.label' | translate }}</label>
            <input class="pi" [ngModel]="sel.label" (ngModelChange)="updateProp('label', $event)" />

            <label class="pl">{{ 'pdfTemplates.designer.fillRole' | translate }}</label>
            <div class="role-select">
              @for (r of fillRoles; track r) {
                <button class="role-btn" [class.active]="sel.fillRole === r" [style.--rc]="roleColors[r]"
                        (click)="updateProp('fillRole', r)">{{ ('pdfTemplates.fillRoles.' + r) | translate }}</button>
              }
            </div>

            <label class="pl">{{ 'common.required' | translate }}</label>
            <mat-checkbox [checked]="sel.isRequired" (change)="updateProp('isRequired', $event.checked)">
              {{ 'pdfTemplates.designer.requiredHint' | translate }}
            </mat-checkbox>

            <label class="pl">{{ 'pdfTemplates.designer.autoFill' | translate }}</label>
            <mat-form-field appearance="outline" class="ff">
              <mat-select [value]="sel.autoFillFrom ?? ''" (selectionChange)="updateProp('autoFillFrom', $event.value || undefined)">
                <mat-option value="">{{ 'pdfTemplates.designer.autoFillNone' | translate }}</mat-option>
                @for (o of autoFillOptions; track o.value) {
                  <mat-option [value]="o.value">{{ o.labelKey | translate }}</mat-option>
                }
              </mat-select>
            </mat-form-field>

            @if (sel.type === 'Dropdown') {
              <label class="pl">{{ 'pdfTemplates.designer.options' | translate }}</label>
              <input class="pi" [ngModel]="optionsText(sel)" (ngModelChange)="updateOptions($event)"
                     [placeholder]="'pdfTemplates.designer.optionsPlaceholder' | translate" />
            }

            @if (sel.fillRole !== 'Approver') {
              <label class="pl">{{ 'pdfTemplates.designer.defaultValue' | translate }}</label>
              <input class="pi" [ngModel]="sel.defaultValue ?? ''" (ngModelChange)="updateProp('defaultValue', $event || undefined)" />
            }

            @if (sel.fillRole === 'Approver') {
              <label class="pl">{{ 'pdfTemplates.designer.preAssignedApprover' | translate }}</label>
              <mat-form-field appearance="outline" class="ff">
                <mat-select [value]="sel.preAssignedApproverId ?? ''" (selectionChange)="updateProp('preAssignedApproverId', $event.value || undefined)">
                  <mat-option value="">{{ 'pdfTemplates.designer.approverAtRequestTime' | translate }}</mat-option>
                  @for (u of users(); track u.id) { <mat-option [value]="u.id">{{ u.fullName }}</mat-option> }
                </mat-select>
              </mat-form-field>

              @if (workflowType() === 'Mixed') {
                <label class="pl">{{ 'pdfTemplates.designer.workflowStage' | translate }}</label>
                <input class="pi" type="number" min="1" [ngModel]="sel.workflowStage" (ngModelChange)="updateProp('workflowStage', +$event || 1)" />
              }
            }

            <div class="pos-grid">
              <div><label class="pl">X</label><input class="pi" type="number" [ngModel]="sel.x" (ngModelChange)="updateProp('x', +$event)" /></div>
              <div><label class="pl">Y</label><input class="pi" type="number" [ngModel]="sel.y" (ngModelChange)="updateProp('y', +$event)" /></div>
              <div><label class="pl">W</label><input class="pi" type="number" [ngModel]="sel.width" (ngModelChange)="updateProp('width', +$event)" /></div>
              <div><label class="pl">H</label><input class="pi" type="number" [ngModel]="sel.height" (ngModelChange)="updateProp('height', +$event)" /></div>
            </div>

            <button mat-stroked-button color="warn" class="del-btn" (click)="deleteField(sel, $event)">
              <mat-icon>delete</mat-icon> {{ 'pdfTemplates.designer.removeField' | translate }}
            </button>
          </div>
        } @else {
          <div class="props empty-props">
            <mat-icon>touch_app</mat-icon>
            <p>{{ 'pdfTemplates.designer.selectOrDragHint' | translate }}</p>
            <div class="default-approver-box">
              <label class="pl">{{ 'pdfTemplates.designer.defaultApprover' | translate }}</label>
              <mat-form-field appearance="outline" class="ff">
                <mat-select [value]="defaultApproverId() ?? ''" (selectionChange)="defaultApproverId.set($event.value || undefined)">
                  <mat-option value="">{{ 'pdfTemplates.designer.noDefaultApprover' | translate }}</mat-option>
                  @for (u of users(); track u.id) { <mat-option [value]="u.id">{{ u.fullName }}</mat-option> }
                </mat-select>
              </mat-form-field>
              <mat-checkbox [checked]="isActive()" (change)="isActive.set($event.checked)">{{ 'pdfTemplates.designer.active' | translate }}</mat-checkbox>
              <mat-checkbox [checked]="hideIdentities()" (change)="hideIdentities.set($event.checked)">{{ 'designer.anon.hideIdentities' | translate }}</mat-checkbox>
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; height: 100vh; overflow: hidden; }
    .dz { display: flex; flex-direction: column; flex: 1; min-height: 0; }
    .dz-toolbar { gap: 8px; background: #fff; border-bottom: 1px solid #e0e0e0; }
    .tpl-name-input { border: none; font-size: 16px; font-weight: 500; outline: none; padding: 6px 8px; border-radius: 6px; min-width: 240px; }
    .tpl-name-input:focus { background: #f1f3f4; }
    .flex-1 { flex: 1; }
    .wf-selector { display: flex; gap: 2px; background: #f1f3f4; border-radius: 8px; padding: 2px; }
    .wf-btn { border: none; background: transparent; border-radius: 6px; padding: 6px; cursor: pointer; color: #5f6368; display: flex; }
    .wf-btn.active { background: #fff; color: #0b57d0; box-shadow: 0 1px 2px rgba(0,0,0,0.15); }
    .grid-toggle { border: 1px solid #dadce0; background: #fff; border-radius: 8px; padding: 6px; cursor: pointer; color: #5f6368; display: flex; }
    .grid-toggle.active { border-color: #0b57d0; background: #e8f0fe; color: #0b57d0; }
    .page-controls { display: flex; align-items: center; }
    .page-indicator { font-size: 13px; color: #5f6368; }
    .save-btn { background: #0b57d0 !important; color: #fff !important; border-radius: 18px !important; }

    .dz-body { flex: 1; display: flex; overflow: hidden; }
    .palette { width: 160px; border-right: 1px solid #e0e0e0; padding: 12px; overflow-y: auto; background: #fafafa; flex-shrink: 0; }
    .palette-title { font-size: 11px; font-weight: 600; text-transform: uppercase; color: #80868b; margin-bottom: 8px; }
    .palette-chip { display: flex; align-items: center; gap: 8px; padding: 8px 10px; border: 1px solid #dadce0; border-radius: 8px; background: #fff; margin-bottom: 6px; cursor: grab; font-size: 13px; }
    .palette-chip:hover { border-color: #0b57d0; }
    .drag-preview { background: #0b57d0; color: #fff; padding: 8px; border-radius: 8px; }
    .legend { margin-top: 16px; }
    .legend-title { font-size: 11px; font-weight: 600; text-transform: uppercase; color: #80868b; margin-bottom: 6px; }
    .legend-item { display: flex; align-items: center; gap: 6px; font-size: 12px; color: #5f6368; margin-bottom: 4px; }
    .legend-item .dot, .role-btn .dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }

    .canvas-area { flex: 1; min-height: 0; overflow: auto; padding: 24px; display: flex; justify-content: center; background: #eef1f4; position: relative; }
    .pdf-loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; }
    .pdf-wrap { position: relative; background: #fff; box-shadow: 0 2px 12px rgba(0,0,0,0.15); }
    .pdf-canvas { display: block; }
    .grid-overlay { position: absolute; inset: 0; pointer-events: none; z-index: 1;
      background-image: linear-gradient(to right, rgba(11,87,208,0.12) 1px, transparent 1px),
                        linear-gradient(to bottom, rgba(11,87,208,0.12) 1px, transparent 1px); }
    .placed-field { position: absolute; z-index: 2; border: 2px solid; border-radius: 4px; cursor: move; box-sizing: border-box; }
    .placed-field.selected { box-shadow: 0 0 0 2px #fff, 0 0 0 4px currentColor; }
    .pf-header { display: flex; align-items: center; gap: 4px; color: #fff; font-size: 11px; padding: 1px 4px; height: 18px; overflow: hidden; white-space: nowrap; }
    .pf-header mat-icon { font-size: 13px; width: 13px; height: 13px; }
    .resize-handle { position: absolute; right: -4px; bottom: -4px; width: 12px; height: 12px; background: #fff; border: 2px solid currentColor; border-radius: 50%; cursor: nwse-resize; }
    .pf-del { position: absolute; top: -10px; right: -10px; width: 20px; height: 20px; border-radius: 50%; border: none; background: #d93025; color: #fff; cursor: pointer; display: none; align-items: center; justify-content: center; }
    .pf-del mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .placed-field.selected .pf-del { display: flex; }

    .props { width: 300px; border-left: 1px solid #e0e0e0; padding: 16px; overflow-y: auto; background: #fff; flex-shrink: 0; }
    .props-head { display: flex; align-items: center; justify-content: space-between; }
    .props-head h3 { font-size: 15px; margin: 0; }
    .pl { display: block; font-size: 12px; font-weight: 500; color: #5f6368; margin: 14px 0 4px; }
    .pi { width: 100%; height: 36px; padding: 0 10px; border: 1px solid #c7d0dc; border-radius: 6px; font-size: 13px; font-family: inherit; box-sizing: border-box; outline: none; }
    .pi:focus { border-color: #0b57d0; }
    .ff { width: 100%; }
    .role-select { display: flex; gap: 4px; }
    .role-btn { flex: 1; border: 1px solid #c7d0dc; background: #fff; border-radius: 6px; padding: 6px 4px; font-size: 12px; cursor: pointer; font-family: inherit; }
    .role-btn.active { border-color: var(--rc); background: var(--rc); color: #fff; }
    .pos-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 8px; }
    .del-btn { width: 100%; margin-top: 16px; }
    .empty-props { display: flex; flex-direction: column; align-items: center; color: #80868b; text-align: center; }
    .empty-props > mat-icon { font-size: 40px; width: 40px; height: 40px; margin: 24px 0 8px; opacity: 0.4; }
    .default-approver-box { width: 100%; margin-top: 24px; border-top: 1px solid #e8edf2; padding-top: 16px; text-align: left; display: flex; flex-direction: column; gap: 8px; }
  `]
})
export class PdfTemplateDesignerComponent implements OnInit, OnDestroy {
  @ViewChild('pdfCanvas') pdfCanvasRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('pdfWrapper') pdfWrapperRef!: ElementRef<HTMLDivElement>;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly svc = inject(PdfTemplateService);
  private readonly userService = inject(UserService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly fieldDefs = PDF_FIELD_DEFS;
  readonly fillRoles: PdfFieldFillRole[] = ['Requester', 'Approver', 'System'];
  readonly roleColors = ROLE_COLORS;
  readonly autoFillOptions = PDF_AUTO_FILL_OPTIONS;

  readonly doc = signal<boolean>(false);
  readonly pdfLoading = signal(true);
  readonly currentPage = signal(1);
  readonly pageCount = signal(1);
  readonly saving = signal(false);
  readonly selected = signal<TemplateField | null>(null);
  readonly fields = signal<TemplateField[]>([]);
  readonly users = signal<UserDto[]>([]);

  readonly workflowType = signal<WorkflowType>('Sequential');
  readonly hideIdentities = signal(false);
  readonly isActive = signal(true);
  readonly defaultApproverId = signal<string | undefined>(undefined);

  readonly snapToGrid = signal(true);
  readonly gridSize = 10;

  name = '';
  templateId = '';
  readonly canvasWidth = signal(794);
  readonly canvasHeight = signal(1123);
  private pdfDoc: unknown = null;

  readonly pageFields = computed(() => this.fields().filter(f => f.page === this.currentPage()));

  roleColor(f: TemplateField): string { return ROLE_COLORS[f.fillRole]; }
  iconFor(type: PdfFieldType): string { return PDF_FIELD_DEFS.find(d => d.type === type)?.icon ?? 'edit'; }
  optionsText(f: TemplateField): string {
    if (!f.optionsJson) return '';
    try { return (JSON.parse(f.optionsJson) as string[]).join(', '); } catch { return ''; }
  }

  ngOnInit(): void {
    this.templateId = this.route.snapshot.paramMap.get('id')!;

    this.userService.getUsers({ pageSize: 200 }).subscribe({
      next: r => this.users.set(r.items),
      error: () => {}
    });

    this.svc.getById(this.templateId).subscribe({
      next: t => {
        this.name = t.name;
        this.pageCount.set(t.pageCount);
        this.workflowType.set(t.workflowType);
        this.hideIdentities.set(t.hideRecipientIdentities);
        this.isActive.set(t.isActive);
        this.defaultApproverId.set(t.defaultApproverId);
        this.fields.set(t.fields.map(f => ({
          tempId: uuidv4(),
          id: f.id,
          fieldKey: f.fieldKey,
          label: f.label,
          type: f.type,
          fillRole: f.fillRole,
          page: f.page,
          x: f.x, y: f.y, width: f.width, height: f.height,
          isRequired: f.isRequired,
          autoFillFrom: f.autoFillFrom,
          defaultValue: f.defaultValue,
          optionsJson: f.optionsJson,
          preAssignedApproverId: f.preAssignedApproverId,
          workflowStage: f.workflowStage
        })));
        this.doc.set(true);
        this.loadPdf();
      },
      error: () => this.pdfLoading.set(false)
    });

    document.addEventListener('keydown', this.handleKeydown);
  }

  ngOnDestroy(): void { document.removeEventListener('keydown', this.handleKeydown); }

  private async loadPdf(): Promise<void> {
    try {
      const pdfjsLib = await import('pdfjs-dist');
      pdfjsLib.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/build/pdf.worker.mjs', import.meta.url).toString();
      const blob = await firstValueFrom(this.svc.stream(this.templateId));
      const data = await blob.arrayBuffer();
      this.pdfDoc = await pdfjsLib.getDocument({ data }).promise;
      this.pdfLoading.set(false);
      setTimeout(() => this.renderPage(this.currentPage()), 0);
    } catch {
      this.pdfLoading.set(false);
    }
  }

  private async renderPage(pageNum: number): Promise<void> {
    if (!this.pdfDoc || !this.pdfCanvasRef) return;
    const pdfjsLib = await import('pdfjs-dist');
    const pdfDocTyped = this.pdfDoc as Awaited<ReturnType<typeof pdfjsLib.getDocument>['promise']>;
    const page = await pdfDocTyped.getPage(pageNum);
    const viewport = page.getViewport({ scale: 1.5 });
    const canvas = this.pdfCanvasRef.nativeElement;
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    this.canvasWidth.set(viewport.width);
    this.canvasHeight.set(viewport.height);
    const ctx = canvas.getContext('2d')!;
    await page.render({ canvasContext: ctx, viewport, canvas }).promise;
  }

  async prevPage(): Promise<void> { if (this.currentPage() > 1) { this.currentPage.update(p => p - 1); await this.renderPage(this.currentPage()); } }
  async nextPage(): Promise<void> { if (this.currentPage() < this.pageCount()) { this.currentPage.update(p => p + 1); await this.renderPage(this.currentPage()); } }

  /** Rounds a coordinate to the nearest grid line when snapping is on, else to the nearest pixel. */
  private snap(v: number): number {
    return this.snapToGrid() ? Math.round(v / this.gridSize) * this.gridSize : Math.round(v);
  }

  /** Keeps a field's top-left within the page so it can never be placed/dragged off the edge. */
  private clampPos(v: number, size: number, extent: number): number {
    return Math.max(0, Math.min(v, Math.max(0, extent - size)));
  }

  onDrop(event: CdkDragDrop<unknown>): void {
    const def = event.item.data as typeof PDF_FIELD_DEFS[number];
    if (!def?.type) return;
    // Measure the canvas itself (the visible page) live, so origin + extent share one coordinate space
    // regardless of scroll position or any stale size binding.
    const canvasRect = this.pdfCanvasRef.nativeElement.getBoundingClientRect();
    const x = this.clampPos(this.snap(event.dropPoint.x - canvasRect.left), def.width, canvasRect.width);
    const y = this.clampPos(this.snap(event.dropPoint.y - canvasRect.top), def.height, canvasRect.height);
    const field: TemplateField = {
      tempId: uuidv4(),
      fieldKey: this.uniqueKey(def.type.toLowerCase()),
      label: this.translate.instant('pdfTemplates.fieldTypes.' + def.type),
      type: def.type,
      fillRole: def.type === 'Signature' ? 'Approver' : 'Requester',
      page: this.currentPage(),
      x, y, width: def.width, height: def.height,
      isRequired: true,
      workflowStage: 1
    };
    this.fields.update(fs => [...fs, field]);
    this.selectField(field);
  }

  onFieldDragEnd(event: { source: CdkDrag; distance: { x: number; y: number } }, field: TemplateField): void {
    this.fields.update(fs => fs.map(f => f.tempId === field.tempId
      ? { ...f,
          x: this.clampPos(this.snap(f.x + event.distance.x), f.width, this.canvasWidth()),
          y: this.clampPos(this.snap(f.y + event.distance.y), f.height, this.canvasHeight()) } : f));
    this.syncSelected(field.tempId);
    event.source.reset();
  }

  onCanvasClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).closest('.placed-field')) return;
    this.selected.set(null);
  }

  selectField(field: TemplateField, event?: MouseEvent): void { event?.stopPropagation(); this.selected.set(field); }

  deleteField(field: TemplateField, event: Event): void {
    event.stopPropagation();
    this.fields.update(fs => fs.filter(f => f.tempId !== field.tempId));
    if (this.selected()?.tempId === field.tempId) this.selected.set(null);
  }

  updateProp<K extends keyof TemplateField>(prop: K, value: TemplateField[K]): void {
    const sel = this.selected();
    if (!sel) return;
    this.fields.update(fs => fs.map(f => {
      if (f.tempId !== sel.tempId) return f;
      const next = { ...f, [prop]: value };
      // Keep manual position/size edits inside the page bounds too.
      next.width = Math.min(Math.max(24, next.width), this.canvasWidth());
      next.height = Math.min(Math.max(18, next.height), this.canvasHeight());
      next.x = this.clampPos(next.x, next.width, this.canvasWidth());
      next.y = this.clampPos(next.y, next.height, this.canvasHeight());
      return next;
    }));
    this.syncSelected(sel.tempId);
  }

  updateOptions(text: string): void {
    const arr = text.split(',').map(s => s.trim()).filter(Boolean);
    this.updateProp('optionsJson', arr.length ? JSON.stringify(arr) : undefined);
  }

  private syncSelected(tempId: string): void {
    this.selected.set(this.fields().find(f => f.tempId === tempId) ?? null);
  }

  private uniqueKey(base: string): string {
    const existing = new Set(this.fields().map(f => f.fieldKey));
    let key = base; let i = 1;
    while (existing.has(key)) key = `${base}_${++i}`;
    return key;
  }

  startResize(event: MouseEvent, field: TemplateField): void {
    event.stopPropagation(); event.preventDefault();
    const startX = event.clientX, startY = event.clientY, startW = field.width, startH = field.height;
    const onMove = (e: MouseEvent) => {
      this.fields.update(fs => fs.map(f => f.tempId === field.tempId
        ? { ...f,
            width: Math.min(Math.max(24, this.snap(startW + e.clientX - startX)), this.canvasWidth() - f.x),
            height: Math.min(Math.max(18, this.snap(startH + e.clientY - startY)), this.canvasHeight() - f.y) } : f));
      this.syncSelected(field.tempId);
    };
    const onUp = () => { document.removeEventListener('mousemove', onMove); document.removeEventListener('mouseup', onUp); };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  }

  private handleKeydown = (event: KeyboardEvent): void => {
    if (event.key === 'Delete' && this.selected() && !(event.target as HTMLElement).matches('input,textarea')) {
      this.deleteField(this.selected()!, event);
    }
  };

  save(): void {
    if (!this.name.trim()) return;
    this.saving.set(true);
    const fields: PdfTemplateFieldDto[] = this.fields().map((f, i) => ({
      id: f.id,
      fieldKey: f.fieldKey,
      label: f.label,
      type: f.type,
      fillRole: f.fillRole,
      page: f.page,
      x: f.x, y: f.y, width: f.width, height: f.height,
      isRequired: f.isRequired,
      order: i,
      autoFillFrom: f.autoFillFrom,
      defaultValue: f.defaultValue,
      optionsJson: f.optionsJson,
      preAssignedApproverId: f.preAssignedApproverId,
      workflowStage: f.workflowStage
    }));

    this.svc.update(this.templateId, {
      name: this.name.trim(),
      defaultApproverId: this.defaultApproverId(),
      isActive: this.isActive(),
      fields,
      workflowType: this.workflowType(),
      hideRecipientIdentities: this.hideIdentities()
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.snack.open(this.translate.instant('pdfTemplates.designer.saved'), 'OK', { duration: 3000 });
      },
      error: (err: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.saving.set(false);
        const msg = err.error?.errors ? Object.values(err.error.errors).flat().join(' ')
          : err.error?.detail ?? this.translate.instant('pdfTemplates.designer.saveFailed');
        this.snack.open(msg, 'OK', { duration: 6000 });
      }
    });
  }
}
