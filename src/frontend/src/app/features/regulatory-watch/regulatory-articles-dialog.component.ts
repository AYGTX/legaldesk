import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { RegulatoryWatchService } from '../../core/services/regulatory-watch.service';
import { RegulatoryText } from '../../core/models';

export interface RegulatoryArticlesDialogData {
  text: RegulatoryText;
}

interface EditableArticle {
  number: string;
  text: string;
}

/**
 * Where a law becomes law in the app: its articles, word for word, and the sign-off that they match the official text.
 * Paste the published text, check how it was cut into articles, correct what needs it, save — and then a person who
 * has compared the articles with the source verifies them. Changing an article afterwards withdraws that sign-off.
 */
@Component({
  selector: 'sf-regulatory-articles-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title class="ra-title">
      <mat-icon>gavel</mat-icon>
      {{ 'regulatoryWatch.articles.title' | translate }}
      <span class="ra-law">{{ data.text.title }}</span>
    </h2>

    <mat-dialog-content class="ra-content">
      <div class="ra-source">
        <div><strong>{{ 'regulatoryWatch.articles.reference' | translate }}:</strong>
          {{ data.text.reference || ('regulatoryWatch.articles.missing' | translate) }}</div>
        <div><strong>{{ 'regulatoryWatch.articles.source' | translate }}:</strong>
          @if (data.text.sourceUrl) { <a [href]="data.text.sourceUrl" target="_blank" rel="noopener noreferrer">{{ data.text.sourceUrl }}</a> }
          @else { {{ 'regulatoryWatch.articles.missing' | translate }} }
        </div>
        @if (verified()) {
          <div class="ra-verified"><mat-icon>verified</mat-icon>
            {{ 'regulatoryWatch.articles.verifiedBy' | translate: { name: data.text.verifiedByName || '—', date: (data.text.verifiedAt | date:'mediumDate') } }}
          </div>
        } @else {
          <div class="ra-unverified"><mat-icon>report</mat-icon> {{ 'regulatoryWatch.articles.notVerified' | translate }}</div>
        }
      </div>

      <section class="ra-import">
        <label class="ra-label" for="ra-raw">{{ 'regulatoryWatch.articles.pasteLabel' | translate }}</label>
        <textarea id="ra-raw" class="ra-raw" rows="5" [(ngModel)]="raw" [placeholder]="'regulatoryWatch.articles.pastePlaceholder' | translate"></textarea>
        <div class="ra-import-actions">
          <button mat-stroked-button (click)="split()" [disabled]="!raw.trim() || splitting()">
            <mat-icon>content_cut</mat-icon> {{ 'regulatoryWatch.articles.split' | translate }}
          </button>
          @if (articles().length > 0 && raw.trim()) { <span class="ra-hint">{{ 'regulatoryWatch.articles.splitReplaces' | translate }}</span> }
        </div>

        @if (warnings().length > 0) {
          <ul class="ra-warnings">
            @for (w of warnings(); track $index) { <li><mat-icon>warning_amber</mat-icon> {{ w }}</li> }
          </ul>
        }
        @if (preamble()) {
          <details class="ra-preamble">
            <summary>{{ 'regulatoryWatch.articles.preamble' | translate }}</summary>
            <pre>{{ preamble() }}</pre>
          </details>
        }
      </section>

      @if (loading()) {
        <div class="ra-state"><mat-spinner diameter="26"></mat-spinner></div>
      } @else {
        <div class="ra-list-head">
          <strong>{{ 'regulatoryWatch.articles.count' | translate: { count: articles().length } }}</strong>
          <button mat-button (click)="add()"><mat-icon>add</mat-icon> {{ 'regulatoryWatch.articles.add' | translate }}</button>
        </div>

        @for (a of articles(); track $index; let i = $index) {
          <div class="ra-row" [class.ra-dup]="isDuplicate(a)">
            <input class="ra-number" [ngModel]="a.number" (ngModelChange)="edit(i, 'number', $event)"
                   [placeholder]="'regulatoryWatch.articles.numberPlaceholder' | translate" />
            <textarea class="ra-text" rows="3" [ngModel]="a.text" (ngModelChange)="edit(i, 'text', $event)"
                      [placeholder]="'regulatoryWatch.articles.textPlaceholder' | translate"></textarea>
            <button mat-icon-button (click)="remove(i)" [attr.aria-label]="'common.delete' | translate"><mat-icon>delete_outline</mat-icon></button>
          </div>
        }
        @if (articles().length === 0) { <p class="ra-empty">{{ 'regulatoryWatch.articles.empty' | translate }}</p> }
      }

      @if (error()) { <div class="ra-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }

      @if (canVerify()) {
        <label class="ra-confirm">
          <input type="checkbox" [checked]="confirmed()" (change)="confirmed.set($any($event.target).checked)" />
          {{ 'regulatoryWatch.articles.confirm' | translate }}
        </label>
      }
    </mat-dialog-content>

    <mat-dialog-actions class="ra-actions">
      <span class="ra-hint">{{ 'regulatoryWatch.articles.editWithdraws' | translate }}</span>
      <span class="ra-spacer"></span>
      <button mat-button (click)="close()">{{ 'common.close' | translate }}</button>
      <button mat-stroked-button (click)="save()" [disabled]="!canSave() || busy()">
        <mat-icon>save</mat-icon> {{ 'regulatoryWatch.articles.save' | translate }}
      </button>
      <button mat-flat-button color="primary" (click)="verify()" [disabled]="!canVerify() || !confirmed() || busy()">
        <mat-icon>verified</mat-icon> {{ 'regulatoryWatch.articles.verify' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .ra-title { display: flex; align-items: center; gap: 8px; }
    .ra-law { font-size: 13px; font-weight: 400; color: #5f6368; margin-left: 8px; }
    .ra-content { min-width: min(820px, 90vw); max-height: 72vh; }
    .ra-source { display: flex; flex-direction: column; gap: 4px; padding: 10px 12px; margin-bottom: 12px; border-radius: 10px; background: #f1f3f4; font-size: 13px; }
    .ra-source a { color: #0b57d0; word-break: break-all; }
    .ra-verified, .ra-unverified { display: flex; align-items: center; gap: 6px; font-weight: 500; }
    .ra-verified { color: #137333; } .ra-unverified { color: #b06000; }
    .ra-verified mat-icon, .ra-unverified mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .ra-label { display: block; font-size: 12px; font-weight: 500; color: #5f6368; margin-bottom: 4px; }
    .ra-raw, .ra-text { width: 100%; box-sizing: border-box; border: 1px solid #c7d0dc; border-radius: 6px; padding: 8px 10px; font: inherit; font-size: 13px; resize: vertical; }
    .ra-import-actions { display: flex; align-items: center; gap: 12px; margin: 8px 0; }
    .ra-hint { font-size: 12px; color: #5f6368; }
    .ra-warnings { list-style: none; margin: 8px 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
    .ra-warnings li { display: flex; align-items: center; gap: 6px; padding: 6px 10px; border-radius: 8px; background: #fef7e0; color: #b06000; font-size: 12px; }
    .ra-warnings mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .ra-preamble { margin: 6px 0; font-size: 12px; color: #5f6368; } .ra-preamble pre { white-space: pre-wrap; margin: 6px 0 0; }
    .ra-list-head { display: flex; align-items: center; justify-content: space-between; margin: 12px 0 6px; }
    .ra-row { display: grid; grid-template-columns: 90px 1fr auto; gap: 8px; align-items: start; margin-bottom: 8px; }
    .ra-number { box-sizing: border-box; border: 1px solid #c7d0dc; border-radius: 6px; padding: 8px; font: inherit; font-size: 13px; width: 100%; }
    .ra-dup .ra-number { border-color: #d93025; background: #fce8e6; }
    .ra-empty { color: #80868b; font-size: 13px; }
    .ra-state { display: flex; justify-content: center; padding: 16px; }
    .ra-error { display: flex; align-items: center; gap: 6px; margin-top: 10px; padding: 8px 10px; border-radius: 8px; background: #fce8e6; color: #b3261e; font-size: 13px; }
    .ra-confirm { display: flex; align-items: center; gap: 8px; margin-top: 12px; font-size: 14px; }
    .ra-actions { padding: 12px 24px; }
    .ra-spacer { flex: 1; }
  `]
})
export class RegulatoryArticlesDialogComponent implements OnInit {
  readonly data = inject<RegulatoryArticlesDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<RegulatoryArticlesDialogComponent, boolean>>(MatDialogRef);
  private readonly service = inject(RegulatoryWatchService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly splitting = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly articles = signal<EditableArticle[]>([]);
  readonly warnings = signal<string[]>([]);
  readonly preamble = signal('');
  readonly confirmed = signal(false);
  /** Edited since it was last saved — only saved articles can be verified. */
  private readonly dirty = signal(false);
  /** Anything changed that the parent list should refresh for. */
  private changed = false;

  raw = '';

  readonly verified = computed(() => !!this.data.text.verifiedAt && !this.dirty());

  readonly canSave = computed(() => this.dirty() && this.articles().length > 0 && this.valid());
  readonly canVerify = computed(() =>
    !this.dirty() && this.articles().length > 0 && !this.data.text.verifiedAt
    && !!this.data.text.reference && !!this.data.text.sourceUrl);

  ngOnInit(): void {
    this.service.getArticles(this.data.text.id).subscribe({
      next: list => {
        this.articles.set(list.map(a => ({ number: a.number, text: a.text })));
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.error.set(this.translate.instant('regulatoryWatch.articles.loadFailed')); }
    });
  }

  private valid(): boolean {
    const list = this.articles();
    const numbers = list.map(a => a.number.trim().toLowerCase());
    return list.every(a => a.number.trim() && a.text.trim()) && new Set(numbers).size === numbers.length;
  }

  isDuplicate(a: EditableArticle): boolean {
    const n = a.number.trim().toLowerCase();
    return !!n && this.articles().filter(x => x.number.trim().toLowerCase() === n).length > 1;
  }

  split(): void {
    this.splitting.set(true);
    this.error.set(null);
    this.service.previewArticles(this.raw).subscribe({
      next: result => {
        this.splitting.set(false);
        this.warnings.set(result.warnings);
        this.preamble.set(result.preamble);
        this.articles.set(result.articles.map(a => ({ number: a.number, text: a.text })));
        this.dirty.set(true);
        this.confirmed.set(false);
      },
      error: err => { this.splitting.set(false); this.error.set(err.error?.detail ?? this.translate.instant('regulatoryWatch.articles.splitFailed')); }
    });
  }

  edit(index: number, field: keyof EditableArticle, value: string): void {
    this.articles.update(list => list.map((a, i) => i === index ? { ...a, [field]: value } : a));
    this.dirty.set(true);
    this.confirmed.set(false);
  }

  add(): void {
    this.articles.update(list => [...list, { number: '', text: '' }]);
    this.dirty.set(true);
  }

  remove(index: number): void {
    this.articles.update(list => list.filter((_, i) => i !== index));
    this.dirty.set(true);
    this.confirmed.set(false);
  }

  save(): void {
    this.busy.set(true);
    this.error.set(null);
    this.service.saveArticles(this.data.text.id, this.articles().map(a => ({ number: a.number.trim(), text: a.text.trim() }))).subscribe({
      next: () => {
        this.busy.set(false);
        this.dirty.set(false);
        this.changed = true;
        // Saving withdraws any earlier sign-off, here as on the server.
        this.data.text.verifiedAt = undefined;
        this.data.text.verifiedByName = undefined;
      },
      error: err => { this.busy.set(false); this.error.set(this.messageOf(err, 'regulatoryWatch.articles.saveFailed')); }
    });
  }

  verify(): void {
    this.busy.set(true);
    this.error.set(null);
    this.service.verify(this.data.text.id).subscribe({
      next: () => { this.busy.set(false); this.ref.close(true); },
      error: err => { this.busy.set(false); this.error.set(this.messageOf(err, 'regulatoryWatch.articles.verifyFailed')); }
    });
  }

  close(): void { this.ref.close(this.changed); }

  private messageOf(err: { error?: { detail?: string; errors?: Record<string, string[]> } }, fallbackKey: string): string {
    return err.error?.errors ? Object.values(err.error.errors).flat().join(' ')
      : err.error?.detail ?? this.translate.instant(fallbackKey);
  }
}
