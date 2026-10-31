import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ClauseDiff, DocumentVersionInfo, VersionComparison } from '../../core/models';

export interface VersionCompareDialogData {
  documentId: string;
  documentTitle: string;
  currentVersion: number;
  versions: DocumentVersionInfo[];
}

/**
 * The redline between two versions of a contract — the first question a reviewer asks when a
 * counterparty returns a draft.
 *
 * Everything shown here is computed server-side. The component renders the marks it is given and
 * derives nothing, so the redline on screen is the one the audit trail recorded being consulted.
 */
@Component({
  selector: 'sf-version-compare-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatFormFieldModule, MatSlideToggleModule, MatProgressSpinnerModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title class="compare-title">
      <mat-icon>compare_arrows</mat-icon>
      {{ 'documents.detail.compare.title' | translate }}
    </h2>

    <mat-dialog-content class="compare-content">
      <div class="compare-picker">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'documents.detail.compare.from' | translate }}</mat-label>
          <mat-select [ngModel]="fromVersion()" (ngModelChange)="fromVersion.set($event); onSelectionChange()">
            @for (v of availableVersions; track v) {
              <mat-option [value]="v">{{ label(v) }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-icon class="picker-arrow">arrow_forward</mat-icon>

        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'documents.detail.compare.to' | translate }}</mat-label>
          <mat-select [ngModel]="toVersion()" (ngModelChange)="toVersion.set($event); onSelectionChange()">
            @for (v of availableVersions; track v) {
              <mat-option [value]="v">{{ label(v) }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <button mat-flat-button color="primary" [disabled]="loading() || sameVersion()" (click)="run()">
          @if (loading()) {
            <mat-spinner diameter="18"></mat-spinner>
          } @else {
            <ng-container>{{ 'documents.detail.compare.run' | translate }}</ng-container>
          }
        </button>
      </div>

      @if (sameVersion()) {
        <p class="compare-hint">{{ 'documents.detail.compare.pickTwo' | translate }}</p>
      }

      @if (error()) {
        <div class="compare-error"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
      }

      @if (loading()) {
        <p class="compare-hint">{{ 'documents.detail.compare.running' | translate }}</p>
      }

      @if (result(); as r) {
        <div class="compare-summary">
          <span class="stat added"><strong>{{ r.addedCount }}</strong> {{ 'documents.detail.compare.added' | translate }}</span>
          <span class="stat removed"><strong>{{ r.removedCount }}</strong> {{ 'documents.detail.compare.removed' | translate }}</span>
          <span class="stat modified"><strong>{{ r.modifiedCount }}</strong> {{ 'documents.detail.compare.modified' | translate }}</span>
          @if (r.movedCount) {
            <span class="stat moved"><strong>{{ r.movedCount }}</strong> {{ 'documents.detail.compare.moved' | translate }}</span>
          }
          <span class="stat unchanged"><strong>{{ r.unchangedCount }}</strong> {{ 'documents.detail.compare.unchanged' | translate }}</span>
        </div>

        @if (r.addedCount + r.removedCount + r.modifiedCount + (r.movedCount ?? 0) === 0) {
          <div class="compare-empty">
            <mat-icon>check_circle</mat-icon>
            <span>{{ 'documents.detail.compare.noChanges' | translate }}</span>
          </div>
        } @else {
          <mat-slide-toggle [ngModel]="changesOnly()" (ngModelChange)="changesOnly.set($event)" class="compare-toggle">
            {{ 'documents.detail.compare.changesOnly' | translate }}
          </mat-slide-toggle>
        }

        <div class="clause-list">
          @for (clause of visibleClauses(); track $index) {
            <div class="clause" [class.added]="clause.change === 'Added'"
                 [class.removed]="clause.change === 'Removed'"
                 [class.modified]="clause.change === 'Modified'"
                 [class.moved]="clause.change === 'Moved'">

              @if (clause.change === 'Moved') {
                <span class="moved-tag"><mat-icon>swap_vert</mat-icon> {{ 'documents.detail.compare.movedHere' | translate }}</span>
              }

              @if (clause.change === 'Unchanged') {
                <p class="clause-text unchanged-text">{{ clause.currentText }}</p>
              }

              @if (clause.change === 'Removed' || clause.change === 'Modified' || clause.change === 'Moved') {
                <p class="clause-text side-previous">
                  <span class="side-tag">v{{ result()!.fromVersion }}</span>
                  @for (w of clause.previousWords; track $index) {
                    <span class="word" [class.marked]="w.isChanged">{{ w.text }}</span>
                  }
                </p>
              }

              @if (clause.change === 'Added' || clause.change === 'Modified' || clause.change === 'Moved') {
                <p class="clause-text side-current">
                  <span class="side-tag">v{{ result()!.toVersion }}</span>
                  @for (w of clause.currentWords; track $index) {
                    <span class="word" [class.marked]="w.isChanged">{{ w.text }}</span>
                  }
                </p>
              }
            </div>
          }
        </div>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .compare-title { display: flex; align-items: center; gap: 8px; }
    .compare-content { min-width: min(760px, 80vw); max-height: 70vh; }

    .compare-picker { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .picker-arrow { color: #5f6368; }

    .compare-hint { color: #5f6368; font-size: 13px; margin: 4px 0; }
    .compare-error {
      display: flex; align-items: center; gap: 8px; margin: 8px 0;
      padding: 10px 12px; border-radius: 8px; background: #fce8e6; color: #c5221f; font-size: 13px;
    }

    .compare-summary { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0 4px; }
    .stat { padding: 4px 10px; border-radius: 12px; font-size: 12px; }
    .stat.added { background: #e6f4ea; color: #137333; }
    .stat.removed { background: #fce8e6; color: #c5221f; }
    .stat.modified { background: #fef7e0; color: #b06000; }
    .stat.unchanged { background: #f1f3f4; color: #5f6368; }
    .stat.moved { background: #e8f0fe; color: #1a56c4; }

    .compare-empty {
      display: flex; align-items: center; gap: 8px; margin: 16px 0;
      padding: 12px; border-radius: 8px; background: #e6f4ea; color: #137333; font-size: 14px;
    }

    .compare-toggle { margin: 8px 0 4px; }

    .clause-list { margin-top: 8px; }
    .clause { padding: 8px 12px; border-left: 3px solid transparent; margin-bottom: 6px; border-radius: 4px; }
    .clause.added { border-left-color: #34a853; background: #f2fbf4; }
    .clause.removed { border-left-color: #ea4335; background: #fdf4f3; }
    .clause.modified { border-left-color: #f9ab00; background: #fffbf0; }
    .clause.moved { border-left-color: #4a7fe0; background: #f3f7ff; }
    .moved-tag { display: inline-flex; align-items: center; gap: 4px; font-size: 12px; font-weight: 600; color: #1a56c4; }
    .moved-tag mat-icon { font-size: 16px; width: 16px; height: 16px; }

    .clause-text { margin: 2px 0; font-size: 14px; line-height: 1.6; }
    .unchanged-text { color: #80868b; }
    .side-tag {
      display: inline-block; margin-right: 8px; padding: 0 6px; border-radius: 8px;
      background: #f1f3f4; color: #5f6368; font-size: 11px; font-weight: 600; vertical-align: middle;
    }
    .word { margin-right: 4px; }
    .side-previous .word.marked { background: #fadad7; text-decoration: line-through; }
    .side-current .word.marked { background: #ceead6; font-weight: 600; }
  `]
})
export class VersionCompareDialogComponent {
  private readonly documentService = inject(DocumentService);
  private readonly translate = inject(TranslateService);
  readonly data = inject<VersionCompareDialogData>(MAT_DIALOG_DATA);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly result = signal<VersionComparison | null>(null);

  // Signals rather than plain fields: `sameVersion` and `visibleClauses` below are computed, and a
  // computed with no signal dependencies caches on first render and never recomputes.
  readonly fromVersion = signal(0);
  readonly toVersion = signal(0);
  readonly changesOnly = signal(true);

  /** Newest first: the version a reviewer is looking at is the one they compare *to*. */
  readonly availableVersions: number[];

  readonly sameVersion = computed(() => this.fromVersion() === this.toVersion());

  readonly visibleClauses = computed<ClauseDiff[]>(() => {
    const clauses = this.result()?.clauses ?? [];
    return this.changesOnly() ? clauses.filter(c => c.change !== 'Unchanged') : clauses;
  });

  constructor() {
    this.availableVersions = [this.data.currentVersion, ...this.data.versions.map(v => v.versionNumber)]
      .filter((v, i, all) => all.indexOf(v) === i)
      .sort((a, b) => b - a);

    this.toVersion.set(this.availableVersions[0]);
    // Default to the immediately preceding version: "what did they change in the round that just
    // finished" is the question being asked far more often than any older comparison.
    this.fromVersion.set(this.availableVersions[1] ?? this.availableVersions[0]);

    if (!this.sameVersion()) this.run();
  }

  label(version: number): string {
    return version === this.data.currentVersion
      ? this.translate.instant('documents.detail.compare.versionCurrent', { version })
      : this.translate.instant('documents.detail.compare.version', { version });
  }

  /** A stale redline beside freshly chosen versions is worse than none — clear it on change. */
  onSelectionChange(): void {
    this.result.set(null);
    this.error.set(null);
  }

  run(): void {
    if (this.sameVersion()) return;

    this.loading.set(true);
    this.error.set(null);

    this.documentService.compareVersions(this.data.documentId, this.fromVersion(), this.toVersion()).subscribe({
      next: comparison => {
        this.result.set(comparison);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.detail ?? this.translate.instant('documents.detail.compare.failed'));
        this.loading.set(false);
      }
    });
  }
}
