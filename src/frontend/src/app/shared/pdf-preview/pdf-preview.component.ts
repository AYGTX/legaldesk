import { Component, ElementRef, OnDestroy, ViewChild, ViewEncapsulation, AfterViewInit, effect, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';

/** A labelled rectangle drawn over a page, in PDF points from the page's top-left as displayed (R-M3-1). */
export interface PreviewBox {
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
  label: string;
}

/** One page's size in points, so callers can lay boxes out against the real page, not an assumed A4. */
export interface PreviewPageSize {
  width: number;
  height: number;
}

/**
 * Every page of a contract, one under the other, with optional boxes drawn on top — so a step that asks about the
 * document (who signs, where) can be done with the document in front of you. Read-only: nothing here changes the file.
 */
@Component({
  selector: 'sf-pdf-preview',
  standalone: true,
  imports: [CommonModule, MatProgressSpinnerModule, TranslatePipe],
  encapsulation: ViewEncapsulation.None,   // the pages and boxes are created in code, outside the template
  template: `
    <div class="pp-frame" #frame>
      @if (loading()) { <div class="pp-state"><mat-spinner diameter="28"></mat-spinner> {{ 'shared.pdfPreview.loading' | translate }}</div> }
      @if (failed()) { <div class="pp-state pp-failed">{{ 'shared.pdfPreview.failed' | translate }}</div> }
      <div #host></div>
    </div>
  `,
  styles: [`
    .pp-frame { background: #eceff3; border-radius: 12px; padding: 12px; max-height: 78vh; overflow: auto; }
    .pp-state { display: flex; align-items: center; justify-content: center; gap: 10px; padding: 32px; color: #5f6368; font-size: 13px; }
    .pp-failed { color: #b3261e; }
    .pp-page { position: relative; margin: 0 auto 12px; background: #fff; box-shadow: 0 1px 4px rgba(0,0,0,.2); }
    .pp-page canvas { display: block; }
    .pp-page-number { position: absolute; top: 6px; right: 8px; font-size: 11px; color: #80868b; background: rgba(255,255,255,.85); padding: 0 6px; border-radius: 8px; }
    .pp-box { position: absolute; box-sizing: border-box; display: flex; align-items: flex-end; padding: 2px 6px; overflow: hidden;
              border: 2px dashed #0b57d0; background: rgba(11,87,208,.10); color: #0b57d0; font-size: 11px; font-weight: 500; border-radius: 4px; }
  `]
})
export class PdfPreviewComponent implements AfterViewInit, OnDestroy {
  private readonly documents = inject(DocumentService);

  readonly documentId = input.required<string>();
  /** Re-read whenever it changes: the boxes are redrawn without reloading the document. */
  readonly boxes = input<PreviewBox[]>([]);
  /** Emitted once the pages are known. */
  readonly pageSizes = output<PreviewPageSize[]>();

  @ViewChild('frame', { static: true }) private frame!: ElementRef<HTMLElement>;
  @ViewChild('host', { static: true }) private host!: ElementRef<HTMLElement>;

  readonly loading = signal(true);
  readonly failed = signal(false);
  private readonly ready = signal(false);

  private pdfDoc: unknown = null;
  private pages: { wrapper: HTMLElement; scale: number }[] = [];

  constructor() {
    effect(() => {
      const boxes = this.boxes();
      if (this.ready()) this.drawBoxes(boxes);
    });
  }

  ngAfterViewInit(): void {
    this.documents.stream(this.documentId()).subscribe({
      next: blob => this.render(blob).catch(() => this.fail()),
      error: () => this.fail()
    });
  }

  ngOnDestroy(): void { (this.pdfDoc as { destroy?: () => void } | null)?.destroy?.(); }

  private fail(): void {
    this.loading.set(false);
    this.failed.set(true);
  }

  private async render(blob: Blob): Promise<void> {
    const pdfjsLib = await import('pdfjs-dist');
    pdfjsLib.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/build/pdf.worker.mjs', import.meta.url).toString();
    const pdf = await pdfjsLib.getDocument({ data: await blob.arrayBuffer() }).promise;
    this.pdfDoc = pdf;

    const available = Math.max(240, this.frame.nativeElement.clientWidth - 24);
    const dpr = window.devicePixelRatio || 1;
    const sizes: PreviewPageSize[] = [];

    for (let n = 1; n <= pdf.numPages; n++) {
      const page = await pdf.getPage(n);
      const natural = page.getViewport({ scale: 1 });
      const scale = Math.min(1.6, available / natural.width);
      const viewport = page.getViewport({ scale });

      const wrapper = document.createElement('div');
      wrapper.className = 'pp-page';
      wrapper.style.width = `${viewport.width}px`;
      wrapper.style.height = `${viewport.height}px`;

      const canvas = document.createElement('canvas');
      canvas.width = Math.floor(viewport.width * dpr);
      canvas.height = Math.floor(viewport.height * dpr);
      canvas.style.width = `${viewport.width}px`;
      canvas.style.height = `${viewport.height}px`;
      wrapper.appendChild(canvas);

      const label = document.createElement('span');
      label.className = 'pp-page-number';
      label.textContent = `${n} / ${pdf.numPages}`;
      wrapper.appendChild(label);

      this.host.nativeElement.appendChild(wrapper);
      this.pages.push({ wrapper, scale });
      sizes.push({ width: natural.width, height: natural.height });

      await page.render({
        canvasContext: canvas.getContext('2d')!,
        viewport,
        canvas,
        transform: dpr === 1 ? undefined : [dpr, 0, 0, dpr, 0, 0]
      }).promise;

      if (n === 1) this.loading.set(false);
    }

    this.loading.set(false);
    this.ready.set(true);
    this.pageSizes.emit(sizes);
  }

  private drawBoxes(boxes: PreviewBox[]): void {
    this.pages.forEach(({ wrapper, scale }, index) => {
      wrapper.querySelectorAll('.pp-box').forEach(el => el.remove());

      // Boxes are in points; the preview draws at its own scale.
      const k = scale;
      for (const box of boxes.filter(b => b.page === index + 1)) {
        const el = document.createElement('div');
        el.className = 'pp-box';
        el.style.left = `${box.x * k}px`;
        el.style.top = `${box.y * k}px`;
        el.style.width = `${box.width * k}px`;
        el.style.height = `${box.height * k}px`;
        el.textContent = box.label;   // textContent, never innerHTML: the label is a person's name
        wrapper.appendChild(el);
      }
    });
  }
}
