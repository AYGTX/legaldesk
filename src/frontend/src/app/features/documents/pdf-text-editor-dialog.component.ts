import { AfterViewInit, Component, ElementRef, HostListener, OnDestroy, ViewChild, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { PdfTextEdit } from '../../core/models';

export interface PdfTextEditorDialogData {
  documentId: string;
  documentTitle: string;
}

/** One editable line, in PDF points with the origin at the top-left of the page (PDF.js at scale 1). */
interface EditorLine {
  id: string;
  page: number;
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
  fontSize: number;
}

interface RawTextItem { str: string; transform: number[]; width: number; height: number }

/** Page is drawn at this multiple of PDF points; overlay boxes use the same factor. */
const DISPLAY_SCALE = 1.4;
/** Items whose baselines differ by less than this (points) belong to the same line. */
const BASELINE_TOLERANCE = 2;

/**
 * Click-to-edit view of a contract PDF. PDF.js draws the page and reports where each piece of text sits;
 * the pieces are merged into lines and laid over the page as clickable boxes. Nothing is sent until the
 * user saves — then the changed lines go to the server, which applies them to the stored file and saves a
 * new version of the contract.
 */
@Component({
  selector: 'sf-pdf-text-editor-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title class="ed-title">
      <mat-icon>edit_note</mat-icon>
      {{ 'documents.detail.editText.title' | translate }}
      <span class="ed-doc">{{ data.documentTitle }}</span>
    </h2>

    <div class="ed-toolbar">
      <span class="ed-hint">{{ 'documents.detail.editText.hint' | translate }}</span>
      <span class="ed-spacer"></span>
      <button mat-icon-button (click)="undo()" [disabled]="!canUndo()"
              [attr.aria-label]="'documents.detail.editText.undo' | translate"
              [title]="'documents.detail.editText.undo' | translate">
        <mat-icon>undo</mat-icon>
      </button>
      <button mat-icon-button (click)="redo()" [disabled]="!canRedo()"
              [attr.aria-label]="'documents.detail.editText.redo' | translate"
              [title]="'documents.detail.editText.redo' | translate">
        <mat-icon>redo</mat-icon>
      </button>
      <span class="ed-sep"></span>
      <button mat-icon-button (click)="goTo(currentPage() - 1)" [disabled]="currentPage() <= 1 || loading()">
        <mat-icon>chevron_left</mat-icon>
      </button>
      <span class="ed-page">{{ 'documents.detail.editText.page' | translate: { n: currentPage(), total: pageCount() } }}</span>
      <button mat-icon-button (click)="goTo(currentPage() + 1)" [disabled]="currentPage() >= pageCount() || loading()">
        <mat-icon>chevron_right</mat-icon>
      </button>
      <span class="ed-count">{{ 'documents.detail.editText.changes' | translate: { count: editCount() } }}</span>
    </div>

    <mat-dialog-content class="ed-content">
      @if (loading()) {
        <div class="ed-state"><mat-spinner diameter="28"></mat-spinner> {{ 'documents.detail.editText.loading' | translate }}</div>
      }
      @if (!loading() && pageLines().length === 0) {
        <div class="ed-state ed-warn"><mat-icon>image_not_supported</mat-icon> {{ 'documents.detail.editText.noText' | translate }}</div>
      }
      <div class="ed-stage" [style.width.px]="stageWidth()" [style.height.px]="stageHeight()" [class.ed-hidden]="loading()">
        <canvas #canvas></canvas>
        @for (line of pageLines(); track line.id) {
          @if (editingId() === line.id) {
            <input class="ed-line ed-active" #lineInput
                   [style.left.px]="line.x * scale" [style.top.px]="line.y * scale"
                   [style.width.px]="inputWidth(line)" [style.height.px]="line.height * scale"
                   [style.font-size.px]="line.fontSize * scale"
                   [value]="textOf(line)"
                   (blur)="commit(line, $any($event.target).value)"
                   (keydown.enter)="$any($event.target).blur()"
                   (keydown.escape)="cancelEdit()" />
          } @else {
            <div class="ed-line" [class.ed-changed]="isChanged(line)"
                 [style.left.px]="line.x * scale" [style.top.px]="line.y * scale"
                 [style.width.px]="isChanged(line) ? null : line.width * scale" [style.min-width.px]="line.width * scale"
                 [style.height.px]="line.height * scale"
                 [style.font-size.px]="line.fontSize * scale"
                 (click)="startEdit(line)">
              @if (isChanged(line)) {
                <span class="ed-new">{{ textOf(line) }}</span>
                <button type="button" class="ed-revert" (click)="revert(line); $event.stopPropagation()"
                        [attr.aria-label]="'documents.detail.editText.revert' | translate">
                  <mat-icon>undo</mat-icon>
                </button>
              }
            </div>
          }
        }
      </div>
    </mat-dialog-content>

    <mat-dialog-actions class="ed-actions">
      <span class="ed-limits">{{ 'documents.detail.editText.limits' | translate }}</span>
      <span class="ed-spacer"></span>
      <button mat-button (click)="ref.close()" [disabled]="saving()">{{ 'documents.detail.editText.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="saving() || editCount() === 0">
        {{ (saving() ? 'documents.detail.editText.saving' : 'documents.detail.editText.save') | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .ed-title { display: flex; align-items: center; gap: 8px; }
    .ed-doc { font-size: 13px; font-weight: 400; color: #5f6368; margin-left: 8px; }
    .ed-toolbar { display: flex; align-items: center; gap: 6px; padding: 0 24px 8px; font-size: 13px; color: #5f6368; }
    .ed-spacer { flex: 1; }
    .ed-sep { width: 1px; height: 20px; background: #dadce0; margin: 0 6px; }
    .ed-page { min-width: 90px; text-align: center; }
    .ed-count { margin-left: 12px; font-weight: 500; color: #854F0B; }
    .ed-content { max-height: 70vh; background: #eceff3; padding: 16px !important; }
    .ed-state { display: flex; align-items: center; gap: 10px; padding: 24px; color: #5f6368; justify-content: center; }
    .ed-warn { color: #854F0B; }
    .ed-stage { position: relative; margin: 0 auto; background: #fff; box-shadow: 0 1px 4px rgba(0,0,0,.2); }
    .ed-hidden { visibility: hidden; height: 0 !important; }
    canvas { display: block; }
    .ed-line { position: absolute; box-sizing: border-box; border: 1px dashed transparent; border-radius: 2px; cursor: text; line-height: 1; }
    .ed-line:hover { border-color: #378ADD; background: rgba(55,138,221,.12); }
    .ed-changed { background: #fff; border-color: #BA7517; padding-right: 4px; }
    .ed-changed:hover { background: #FAEEDA; }
    .ed-new { display: block; white-space: pre; color: #111; font-family: Arial, sans-serif; line-height: 1.15; }
    .ed-line.ed-active, .ed-line.ed-active:hover { border: 1px solid #185FA5; background: #fff; color: #111; font-family: Arial, sans-serif; outline: none; padding: 0 2px; z-index: 2; box-shadow: 0 0 0 2px #fff; }
    .ed-revert { position: absolute; right: -22px; top: 0; width: 20px; height: 20px; padding: 0; border: none; border-radius: 50%; background: #FAEEDA; color: #854F0B; cursor: pointer; display: none; align-items: center; justify-content: center; }
    .ed-revert mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .ed-line:hover .ed-revert { display: flex; }
    .ed-actions { padding: 12px 24px; }
    .ed-limits { font-size: 12px; color: #5f6368; max-width: 60%; }
  `]
})
export class PdfTextEditorDialogComponent implements AfterViewInit, OnDestroy {
  readonly data = inject<PdfTextEditorDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<PdfTextEditorDialogComponent, { version: number } | undefined>>(MatDialogRef);
  private readonly documentService = inject(DocumentService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  @ViewChild('canvas') private canvasRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('lineInput') private set lineInputRef(el: ElementRef<HTMLInputElement> | undefined) {
    if (el) setTimeout(() => { el.nativeElement.focus(); el.nativeElement.select(); });
  }

  readonly scale = DISPLAY_SCALE;
  loading = signal(true);
  saving = signal(false);
  pageCount = signal(0);
  currentPage = signal(1);
  stageWidth = signal(0);
  stageHeight = signal(0);
  pageLines = signal<EditorLine[]>([]);
  editingId = signal<string | null>(null);

  /** line id → new text, only for lines the user changed. */
  private readonly edits = signal<Map<string, { line: EditorLine; text: string }>>(new Map());
  readonly editCount = computed(() => this.edits().size);

  /**
   * Undo history: every change to the edit map is stored as a whole snapshot. The maps are replaced, never
   * mutated, so a snapshot is just a reference. A new change clears the redo stack, like any editor.
   */
  private readonly undoStack = signal<Map<string, { line: EditorLine; text: string }>[]>([]);
  private readonly redoStack = signal<Map<string, { line: EditorLine; text: string }>[]>([]);
  readonly canUndo = computed(() => this.undoStack().length > 0 && !this.saving());
  readonly canRedo = computed(() => this.redoStack().length > 0 && !this.saving());

  private pdfDoc: unknown = null;
  private readonly linesCache = new Map<number, EditorLine[]>();

  async ngAfterViewInit(): Promise<void> {
    this.documentService.stream(this.data.documentId).subscribe({
      next: async blob => {
        try {
          const pdfjsLib = await import('pdfjs-dist');
          pdfjsLib.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/build/pdf.worker.mjs', import.meta.url).toString();
          this.pdfDoc = await pdfjsLib.getDocument({ data: await blob.arrayBuffer() }).promise;
          this.pageCount.set((this.pdfDoc as { numPages: number }).numPages);
          await this.renderPage(1);
        } catch {
          this.fail('documents.detail.editText.loadFailed');
        }
      },
      error: () => this.fail('documents.detail.editText.loadFailed')
    });
  }

  ngOnDestroy(): void {
    (this.pdfDoc as { destroy?: () => void } | null)?.destroy?.();
  }

  private fail(key: string): void {
    this.snack.open(this.translate.instant(key), 'OK', { duration: 5000 });
    this.ref.close();
  }

  async goTo(page: number): Promise<void> {
    if (page < 1 || page > this.pageCount() || page === this.currentPage()) return;
    this.editingId.set(null);
    await this.renderPage(page);
  }

  private async renderPage(pageNum: number): Promise<void> {
    this.loading.set(true);
    const doc = this.pdfDoc as { getPage(n: number): Promise<any> };
    const page = await doc.getPage(pageNum);

    const viewport = page.getViewport({ scale: DISPLAY_SCALE });
    const canvas = this.canvasRef.nativeElement;
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    this.stageWidth.set(viewport.width);
    this.stageHeight.set(viewport.height);
    await page.render({ canvasContext: canvas.getContext('2d')!, viewport, canvas }).promise;

    if (!this.linesCache.has(pageNum)) {
      const pageHeightPts = page.getViewport({ scale: 1 }).height;
      const content = await page.getTextContent();
      this.linesCache.set(pageNum, this.buildLines(pageNum, content.items as unknown[], pageHeightPts));
    }

    this.pageLines.set(this.linesCache.get(pageNum)!);
    this.currentPage.set(pageNum);
    this.loading.set(false);
  }

  /** Merges PDF.js text fragments into lines: same baseline, read left to right, split at wide gaps. */
  private buildLines(pageNum: number, rawItems: unknown[], pageHeight: number): EditorLine[] {
    const items = (rawItems as RawTextItem[])
      .filter(i => typeof i.str === 'string' && i.str.trim().length > 0)
      .map(i => {
        const fontSize = Math.hypot(i.transform[2], i.transform[3]) || i.height || 10;
        return { str: i.str, x: i.transform[4], baseline: pageHeight - i.transform[5], width: i.width, fontSize };
      })
      .sort((a, b) => a.baseline - b.baseline || a.x - b.x);

    const lines: EditorLine[] = [];
    let group: typeof items = [];

    const flush = () => {
      if (group.length === 0) return;
      let text = '';
      let end = 0;
      for (const g of group) {
        const gap = g.x - end;
        if (text && gap > g.fontSize * 0.15 && !text.endsWith(' ') && !g.str.startsWith(' ')) text += ' ';
        text += g.str;
        end = g.x + g.width;
      }
      const first = group[0];
      const fontSize = Math.max(...group.map(g => g.fontSize));
      lines.push({
        id: `${pageNum}:${lines.length}`,
        page: pageNum,
        text: text.trim(),
        x: first.x,
        y: first.baseline - fontSize * 0.85,
        width: Math.max(end - first.x, fontSize),
        height: fontSize * 1.15,
        fontSize
      });
      group = [];
    };

    for (const item of items) {
      const last = group[group.length - 1];
      const sameLine = last
        && Math.abs(item.baseline - last.baseline) <= BASELINE_TOLERANCE
        && item.x - (last.x + last.width) < item.fontSize * 2.5;
      if (!sameLine) flush();
      group.push(item);
    }
    flush();
    return lines;
  }

  textOf(line: EditorLine): string {
    return this.edits().get(line.id)?.text ?? line.text;
  }

  isChanged(line: EditorLine): boolean {
    return this.edits().has(line.id);
  }

  inputWidth(line: EditorLine): number {
    // Room to type: at least the original width, up to the right edge of the page.
    return Math.max(line.width * this.scale + 40, Math.min(this.stageWidth() - line.x * this.scale - 8, 600));
  }

  startEdit(line: EditorLine): void {
    this.editingId.set(line.id);
  }

  cancelEdit(): void {
    this.editingId.set(null);
  }

  commit(line: EditorLine, value: string): void {
    this.editingId.set(null);
    if (value === this.textOf(line)) return;   // nothing changed — not an undo step

    const next = new Map(this.edits());
    if (value === line.text) next.delete(line.id);
    else next.set(line.id, { line, text: value });
    this.applyChange(next);
  }

  revert(line: EditorLine): void {
    if (!this.edits().has(line.id)) return;
    const next = new Map(this.edits());
    next.delete(line.id);
    this.applyChange(next);
  }

  private applyChange(next: Map<string, { line: EditorLine; text: string }>): void {
    this.undoStack.update(stack => [...stack, this.edits()]);
    this.redoStack.set([]);
    this.edits.set(next);
  }

  undo(): void {
    const stack = this.undoStack();
    if (stack.length === 0 || this.saving()) return;
    this.editingId.set(null);
    this.redoStack.update(r => [...r, this.edits()]);
    this.edits.set(stack[stack.length - 1]);
    this.undoStack.set(stack.slice(0, -1));
  }

  redo(): void {
    const stack = this.redoStack();
    if (stack.length === 0 || this.saving()) return;
    this.editingId.set(null);
    this.undoStack.update(u => [...u, this.edits()]);
    this.edits.set(stack[stack.length - 1]);
    this.redoStack.set(stack.slice(0, -1));
  }

  /**
   * Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y. While a line is being typed in, the browser's own text undo handles the
   * keystroke — otherwise Ctrl+Z would throw away the whole line's edit instead of the last few characters.
   */
  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!(event.ctrlKey || event.metaKey) || this.editingId() !== null) return;

    const key = event.key.toLowerCase();
    if (key === 'z' && !event.shiftKey) { event.preventDefault(); this.undo(); }
    else if (key === 'y' || (key === 'z' && event.shiftKey)) { event.preventDefault(); this.redo(); }
  }

  save(): void {
    const payload: PdfTextEdit[] = [...this.edits().values()].map(({ line, text }) => ({
      page: line.page,
      x: line.x,
      y: line.y,
      width: line.width,
      height: line.height,
      fontSize: line.fontSize,
      text
    }));
    if (payload.length === 0) return;

    this.saving.set(true);
    this.documentService.editPdf(this.data.documentId, payload).subscribe({
      next: res => {
        this.saving.set(false);
        this.snack.open(this.translate.instant('documents.detail.editText.saved', { version: res.version }), 'OK', { duration: 5000 });
        this.ref.close({ version: res.version });
      },
      error: () => {
        this.saving.set(false);
        this.snack.open(this.translate.instant('documents.detail.editText.failed'), 'OK', { duration: 5000 });
      }
    });
  }
}
