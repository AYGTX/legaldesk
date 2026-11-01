import { Component, computed, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { KeyTerms, RenewalNote } from '../../core/models/contract-lifecycle-ai.model';
import { LifecycleService } from '../../core/services/lifecycle.service';

/**
 * Harvey's help across the contract's life: reads the key data (each value with the sentence it comes from, checked
 * word for word) so the term can be applied in one click, and writes a short decision note before the next deadline.
 */
@Component({
  selector: 'sf-contract-lifecycle-ai-panel',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe],
  template: `
    <mat-card class="lc-card">
      <mat-card-header>
        <mat-card-title class="lc-title">
          <mat-icon class="lc-icon">auto_awesome</mat-icon> {{ 'documents.detail.lifecycleAi.title' | translate }}
        </mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <div class="lc-actions">
          <button mat-stroked-button (click)="extract()" [disabled]="extracting()">
            <mat-icon>fact_check</mat-icon> {{ 'documents.detail.lifecycleAi.extract' | translate }}
          </button>
          <button mat-stroked-button (click)="writeNote()" [disabled]="writing() || !hasEndDate()"
            [matTooltip]="hasEndDate() ? '' : ('documents.detail.lifecycleAi.needEndDate' | translate)">
            <mat-icon>event_repeat</mat-icon> {{ 'documents.detail.lifecycleAi.note' | translate }}
          </button>
          @if (extracting() || writing()) { <mat-spinner diameter="20"></mat-spinner> }
        </div>

        @if (error()) {
          <div class="lc-error"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
        }

        @if (terms(); as kt) {
          <h4 class="lc-sub">{{ 'documents.detail.lifecycleAi.keyData' | translate }}</h4>
          @if (kt.terms.length === 0) {
            <p class="lc-muted">{{ 'documents.detail.lifecycleAi.nothingFound' | translate }}</p>
          } @else {
            <table class="lc-table">
              @for (t of kt.terms; track t.field) {
                <tr>
                  <th>{{ ('documents.detail.lifecycleAi.field.' + t.field) | translate }}</th>
                  <td>
                    <div class="lc-value">{{ t.value }}</div>
                    @if (t.quote) { <div class="lc-quote">« {{ t.quote }} »</div> }
                  </td>
                </tr>
              }
            </table>
            @if (canEdit() && hasSuggestedTerm()) {
              <div class="lc-apply">
                @if (appliedOk()) {
                  <span class="lc-ok"><mat-icon>check_circle</mat-icon> {{ 'documents.detail.lifecycleAi.applied' | translate }}</span>
                } @else {
                  <button mat-flat-button color="primary" (click)="applyTerm()" [disabled]="applying()">
                    <mat-icon>check</mat-icon> {{ 'documents.detail.lifecycleAi.apply' | translate }}
                  </button>
                  <span class="lc-muted">{{ 'documents.detail.lifecycleAi.applyHint' | translate }}</span>
                }
              </div>
            }
          }
        }

        @if (note(); as n) {
          <h4 class="lc-sub">{{ 'documents.detail.lifecycleAi.noteTitle' | translate }}</h4>
          <span class="lc-reco" [ngClass]="'reco-' + n.recommendation">
            {{ ('documents.detail.lifecycleAi.reco.' + n.recommendation) | translate }}
          </span>
          @if (n.noticeDeadline) {
            <span class="lc-muted lc-deadline">{{ 'documents.detail.lifecycleAi.deadline' | translate: { date: (n.noticeDeadline | date: 'dd/MM/yyyy') } }}</span>
          }
          <p class="lc-summary">{{ n.summary }}</p>
          @if (n.reasons.length) {
            <ul class="lc-reasons">@for (r of n.reasons; track $index) { <li>{{ r }}</li> }</ul>
          }
          @for (c of n.citations; track $index) { <div class="lc-quote">« {{ c.quote }} »</div> }
          <p class="lc-note">{{ 'documents.detail.lifecycleAi.disclaimer' | translate }}</p>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .lc-card { margin-bottom: 16px; }
    .lc-title { display: flex; align-items: center; gap: 8px; font-size: 16px; }
    .lc-icon { color: #6750a4; }
    .lc-actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin: 8px 0; }
    .lc-error { display: flex; gap: 6px; align-items: center; color: #991b1b; font-size: 13px; margin: 8px 0; }
    .lc-sub { margin: 16px 0 8px; font-size: 14px; color: #374151; }
    .lc-muted { color: #9ca3af; font-size: 13px; }
    .lc-table { width: 100%; border-collapse: collapse; }
    .lc-table th { text-align: left; vertical-align: top; width: 30%; padding: 8px 12px 8px 0; font-weight: 500; color: #6b7280; font-size: 13px; }
    .lc-table td { padding: 8px 0; border-bottom: 1px solid #f3f4f6; }
    .lc-value { font-weight: 500; color: #111827; }
    .lc-quote { font-size: 12px; color: #6b7280; font-style: italic; margin-top: 2px; }
    .lc-ok { display: inline-flex; align-items: center; gap: 6px; color: #1e6b2e; font-weight: 500; }
    .lc-apply { display: flex; gap: 12px; align-items: center; margin-top: 12px; flex-wrap: wrap; }
    .lc-reco { display: inline-block; font-weight: 600; font-size: 13px; padding: 4px 10px; border-radius: 8px; }
    .reco-Renew { background: #e6f4ea; color: #1e6b2e; }
    .reco-Renegotiate { background: #fef3c7; color: #92400e; }
    .reco-Terminate { background: #fee2e2; color: #991b1b; }
    .reco-Review { background: #f3f4f6; color: #374151; }
    .lc-deadline { margin-left: 10px; }
    .lc-summary { margin: 10px 0 6px; color: #374151; }
    .lc-reasons { margin: 0 0 8px; padding-left: 20px; color: #374151; font-size: 13px; }
    .lc-note { font-size: 11px; color: #9ca3af; margin-top: 10px; }
  `]
})
export class ContractLifecycleAiPanelComponent {
  private readonly documents = inject(DocumentService);
  private readonly lifecycle = inject(LifecycleService);
  private readonly translate = inject(TranslateService);

  readonly documentId = input.required<string>();
  /** Kept for callers; the note asks the server, which says when no end date is recorded. */
  readonly hasEndDate = input(true);
  readonly canEdit = input(false);
  readonly applied = output<void>();

  readonly terms = signal<KeyTerms | null>(null);
  readonly note = signal<RenewalNote | null>(null);
  readonly extracting = signal(false);
  readonly writing = signal(false);
  readonly applying = signal(false);
  readonly appliedOk = signal(false);
  readonly error = signal<string | null>(null);

  readonly hasSuggestedTerm = computed(() => !!this.terms()?.suggested.endDate);

  private get lang(): string {
    return this.translate.currentLang() === 'en' ? 'en' : 'fr';
  }

  extract(): void {
    this.extracting.set(true);
    this.error.set(null);
    this.documents.extractKeyTerms(this.documentId(), this.lang).subscribe({
      next: kt => { this.terms.set(kt); this.appliedOk.set(false); this.extracting.set(false); },
      error: () => { this.error.set(this.translate.instant('documents.detail.lifecycleAi.failed')); this.extracting.set(false); }
    });
  }

  writeNote(): void {
    this.writing.set(true);
    this.error.set(null);
    this.documents.writeRenewalNote(this.documentId(), this.lang).subscribe({
      next: n => { this.note.set(n); this.writing.set(false); },
      error: () => { this.error.set(this.translate.instant('documents.detail.lifecycleAi.failed')); this.writing.set(false); }
    });
  }

  applyTerm(): void {
    const s = this.terms()?.suggested;
    if (!s?.endDate) return;
    this.applying.set(true);
    // A person applies what Harvey read (R-M12-9): the dates become the term only through this click.
    this.lifecycle.setTerm(this.documentId(), {
      startDate: s.startDate ?? null, endDate: s.endDate, renewalMode: s.autoRenews ? 'Automatic' : 'None',
      renewalMonths: s.autoRenews ? 12 : null, noticeAmount: s.noticePeriodDays ?? null, noticeUnit: 'Days', noticeForm: null
    }).subscribe({
      next: () => { this.applying.set(false); this.appliedOk.set(true); this.applied.emit(); },
      error: () => { this.applying.set(false); this.error.set(this.translate.instant('documents.detail.lifecycleAi.failed')); }
    });
  }
}
