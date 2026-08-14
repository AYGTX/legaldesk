import { Component, computed, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatTooltipModule } from '@angular/material/tooltip';
import { provideNativeDateAdapter } from '@angular/material/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { evaluateContractTerm, lifecycleStageTone } from '../../core/utils/contract-term';

@Component({
  selector: 'sf-contract-term-panel',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatCheckboxModule, MatDatepickerModule,
    MatTooltipModule, TranslatePipe
  ],
  template: `
    <mat-card class="term-card" [class.wide]="wide()">
      <mat-card-header>
        <mat-card-title class="term-title">
          <mat-icon class="term-icon">update</mat-icon> {{ 'documents.detail.contractTerm.title' | translate }}
        </mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @if (editing()) {
          <div class="term-form">
            <div class="term-form-row">
              <mat-form-field appearance="outline">
                <mat-label>{{ 'documents.detail.contractTerm.startDate' | translate }}</mat-label>
                <input matInput [matDatepicker]="startPicker" [(ngModel)]="startInput" />
                <mat-datepicker-toggle matSuffix [for]="startPicker"></mat-datepicker-toggle>
                <mat-datepicker #startPicker></mat-datepicker>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'documents.detail.contractTerm.endDate' | translate }}</mat-label>
                <input matInput [matDatepicker]="endPicker" [(ngModel)]="endInput" [min]="startInput" />
                <mat-datepicker-toggle matSuffix [for]="endPicker"></mat-datepicker-toggle>
                <mat-datepicker #endPicker></mat-datepicker>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'documents.detail.contractTerm.noticePeriod' | translate }}</mat-label>
                <input matInput type="number" min="0" max="365" [(ngModel)]="noticeInput" />
                <span matTextSuffix>{{ 'documents.detail.contractTerm.days' | translate }}</span>
              </mat-form-field>
            </div>

            <mat-checkbox [(ngModel)]="autoRenewsInput">
              {{ 'documents.detail.contractTerm.autoRenews' | translate }}
            </mat-checkbox>

            @if (saveError()) {
              <div class="term-error"><mat-icon>error_outline</mat-icon><span>{{ saveError() }}</span></div>
            }

            <div class="term-form-actions">
              <button mat-stroked-button (click)="cancelEdit()">{{ 'common.cancel' | translate }}</button>
              <button mat-flat-button color="primary" [disabled]="saving() || !endInput" (click)="save()">
                {{ 'documents.detail.contractTerm.save' | translate }}
              </button>
            </div>
          </div>
        } @else if (contractEndDate()) {
          <div class="term-body">
            <div class="term-stage-row">
              <span class="term-stage-chip" [ngClass]="'tone-' + tone()">
                {{ ('documents.detail.contractTerm.stage.' + status().stage) | translate }}
              </span>
              @if (autoRenews()) {
                <span class="term-renew-chip" [matTooltip]="'documents.detail.contractTerm.autoRenewsHint' | translate">
                  <mat-icon>autorenew</mat-icon> {{ 'documents.detail.contractTerm.autoRenews' | translate }}
                </span>
              }
              @if (!termConfirmed()) {
                <span class="term-unset-chip">{{ 'documents.detail.contractTerm.unconfirmed' | translate }}</span>
              }
            </div>

            <div class="term-grid">
              @if (contractStartDate()) {
                <div class="term-cell">
                  <span class="term-label">{{ 'documents.detail.contractTerm.startDate' | translate }}</span>
                  <span class="term-value">{{ contractStartDate() | date:'MMM d, y' }}</span>
                </div>
              }
              <div class="term-cell">
                <span class="term-label">{{ 'documents.detail.contractTerm.endDate' | translate }}</span>
                <span class="term-value">{{ contractEndDate() | date:'MMM d, y' }}</span>
              </div>
              @if (status().noticeDeadline) {
                <div class="term-cell">
                  <span class="term-label">
                    {{ 'documents.detail.contractTerm.noticeDeadline' | translate }}
                    <mat-icon class="term-derived" [matTooltip]="'documents.detail.contractTerm.derivedHint' | translate">info_outline</mat-icon>
                  </span>
                  <span class="term-value" [class.term-value-danger]="status().daysToNoticeDeadline !== null && status().daysToNoticeDeadline! <= 0">
                    {{ status().noticeDeadline | date:'MMM d, y' }}
                  </span>
                </div>
              }
              <div class="term-cell">
                <span class="term-label">{{ 'documents.detail.contractTerm.remaining' | translate }}</span>
                <span class="term-value">
                  @if (status().daysToEnd !== null && status().daysToEnd! >= 0) {
                    {{ 'documents.detail.contractTerm.daysRemaining' | translate: { days: status().daysToEnd } }}
                  } @else {
                    {{ 'documents.detail.contractTerm.endedAgo' | translate: { days: -(status().daysToEnd ?? 0) } }}
                  }
                </span>
              </div>
            </div>

            @if (canEdit()) {
              <button mat-stroked-button (click)="startEdit()">{{ 'documents.detail.contractTerm.edit' | translate }}</button>
            }
          </div>
        } @else {
          <div class="term-empty">
            <mat-icon>update</mat-icon>
            <p>{{ 'documents.detail.contractTerm.none' | translate }}</p>
            @if (canEdit()) {
              <button mat-flat-button color="primary" (click)="startEdit()">
                {{ 'documents.detail.contractTerm.record' | translate }}
              </button>
            }
          </div>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .term-card { border: 1px solid #dcd5f2; background: #f8f6ff; }
    .term-title { display: flex; align-items: center; gap: 6px; font-size: 15px; }
    .term-icon { color: #6750a4; font-size: 18px; width: 18px; height: 18px; }

    .term-body { display: flex; flex-direction: column; gap: 14px; align-items: flex-start; }
    .term-stage-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .term-stage-chip { font-size: 11px; font-weight: 700; border-radius: 8px; padding: 4px 10px; }
    .tone-ok { background: #e6f4ea; color: #1e6b2e; }
    .tone-warn { background: #fef3c7; color: #92400e; }
    .tone-danger { background: #fee2e2; color: #991b1b; }
    .tone-neutral { background: #f3f4f6; color: #6b7280; }
    .term-renew-chip, .term-unset-chip { display: inline-flex; align-items: center; gap: 4px; font-size: 11px; font-weight: 600; border-radius: 8px; padding: 3px 8px; }
    .term-renew-chip { color: #6750a4; background: #ede9fe; }
    .term-renew-chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .term-unset-chip { color: #92400e; background: #fef3c7; }

    .term-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 24px; width: 100%; }
    .wide .term-grid { grid-template-columns: repeat(4, minmax(0, 1fr)); }
    .term-cell { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .term-label { display: inline-flex; align-items: center; gap: 4px; font-size: 12px; color: #9ca3af; }
    .term-derived { font-size: 13px; width: 13px; height: 13px; cursor: help; }
    .term-value { font-size: 14px; color: #374151; font-weight: 500; }
    .term-value-danger { color: #991b1b; }

    .term-empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 16px 0; color: #9ca3af; text-align: center; }
    .term-empty mat-icon { font-size: 28px; width: 28px; height: 28px; }
    .term-empty p { margin: 0; font-size: 13px; }

    .term-form { display: flex; flex-direction: column; gap: 12px; }
    .term-form-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; }
    .term-form-actions { display: flex; gap: 8px; justify-content: flex-end; }
    .term-error { display: flex; align-items: center; gap: 8px; background: #fee2e2; color: #991b1b; border-radius: 8px; padding: 10px 12px; font-size: 13px; }
  `]
})
export class ContractTermPanelComponent {
  // Signal inputs, not @Input() properties: `status` below is a computed, and a computed that
  // reads plain properties has nothing to invalidate it — it would cache the stage forever and go
  // stale the moment the parent reloads the contract after a save.
  readonly documentId = input.required<string>();
  readonly contractStartDate = input<string | null | undefined>(null);
  readonly contractEndDate = input<string | null | undefined>(null);
  readonly autoRenews = input(false);
  readonly noticePeriodDays = input<number | null | undefined>(null);
  readonly termConfirmed = input(false);
  /** Only the owner or an administrator may set the dates the alerts fire on. */
  readonly canEdit = input(false);
  /** Full-width placement: the term grid goes four-up instead of two. */
  readonly wide = input(false);
  readonly saved = output<void>();

  private readonly documentService = inject(DocumentService);
  private readonly translate = inject(TranslateService);

  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  startInput: Date | null = null;
  endInput: Date | null = null;
  noticeInput: number | null = null;
  autoRenewsInput = false;

  /**
   * Recomputed from the inputs rather than fetched, using the same rules as the server-side sweep
   * (see evaluateContractTerm) — so the stage shown here is exactly the stage that will alert.
   */
  readonly status = computed(() =>
    evaluateContractTerm(this.contractStartDate(), this.contractEndDate(), this.noticePeriodDays()));

  readonly tone = computed(() => lifecycleStageTone(this.status().stage));

  startEdit(): void {
    const start = this.contractStartDate();
    const end = this.contractEndDate();
    this.startInput = start ? new Date(start) : null;
    this.endInput = end ? new Date(end) : null;
    this.noticeInput = this.noticePeriodDays() ?? null;
    this.autoRenewsInput = this.autoRenews();
    this.saveError.set(null);
    this.editing.set(true);
  }

  cancelEdit(): void {
    this.editing.set(false);
  }

  save(): void {
    if (!this.endInput) return;
    this.saving.set(true);
    this.saveError.set(null);

    this.documentService.updateContractTerm(this.documentId(), {
      startDate: this.toIsoDate(this.startInput),
      endDate: this.toIsoDate(this.endInput),
      autoRenews: this.autoRenewsInput,
      noticePeriodDays: this.noticeInput ?? null
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.saved.emit();
      },
      error: err => {
        this.saving.set(false);
        this.saveError.set(err.error?.detail ?? this.translate.instant('documents.detail.contractTerm.saveFailed'));
      }
    });
  }

  /**
   * Date-only, built from the local calendar fields. `toISOString()` would shift the date across a
   * day boundary for anyone behind UTC, which on a notice deadline is a real difference.
   */
  private toIsoDate(value: Date | null): string | null {
    if (!value) return null;
    const month = `${value.getMonth() + 1}`.padStart(2, '0');
    const day = `${value.getDate()}`.padStart(2, '0');
    return `${value.getFullYear()}-${month}-${day}`;
  }
}
