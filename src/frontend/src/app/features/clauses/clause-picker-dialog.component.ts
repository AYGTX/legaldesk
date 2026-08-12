import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { ClauseService } from '../../core/services/clause.service';
import { Clause, ClauseCategory, CLAUSE_CATEGORIES } from '../../core/models';

/**
 * Browses approved clauses and returns the one the drafter picked, so the caller can insert its
 * wording. Only ever shows Approved clauses — draft wording must not reach a contract.
 */
@Component({
  selector: 'sf-clause-picker-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>
      <mat-icon class="title-icon">menu_book</mat-icon> {{ 'clauses.picker.title' | translate }}
    </h2>
    <mat-dialog-content>
      <p class="picker-hint">{{ 'clauses.picker.hint' | translate }}</p>

      <div class="picker-filters">
        <mat-form-field appearance="outline" class="search-field">
          <mat-label>{{ 'clauses.searchLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="load()" />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'clauses.filterCategory' | translate }}</mat-label>
          <mat-select [(ngModel)]="categoryFilter" (selectionChange)="load()">
            <mat-option [value]="null">{{ 'clauses.allCategories' | translate }}</mat-option>
            @for (c of categories; track c) {
              <mat-option [value]="c">{{ ('clauses.category.' + c) | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>{{ 'clauses.filterLanguage' | translate }}</mat-label>
          <mat-select [(ngModel)]="languageFilter" (selectionChange)="load()">
            <mat-option [value]="null">{{ 'clauses.allLanguages' | translate }}</mat-option>
            <mat-option value="en">{{ 'clauses.language.en' | translate }}</mat-option>
            <mat-option value="fr">{{ 'clauses.language.fr' | translate }}</mat-option>
          </mat-select>
        </mat-form-field>
      </div>

      @if (loading()) {
        <div class="picker-loading"><mat-spinner diameter="36"></mat-spinner></div>
      } @else if (clauses().length === 0) {
        <div class="picker-empty">
          <mat-icon>search_off</mat-icon>
          <p>{{ 'clauses.picker.empty' | translate }}</p>
        </div>
      } @else {
        <div class="picker-list">
          @for (c of clauses(); track c.id) {
            <div class="picker-item" [class.selected]="selected()?.id === c.id" (click)="selected.set(c)">
              <div class="picker-item-head">
                <span class="picker-item-title">{{ c.title }}</span>
                <div class="picker-chips">
                  <span class="chip category">{{ ('clauses.category.' + c.category) | translate }}</span>
                  <span class="chip lang">{{ c.language | uppercase }}</span>
                  @if (c.riskLevel) {
                    <span class="chip" [ngClass]="'risk-' + c.riskLevel.toLowerCase()">{{ ('clauses.risk.' + c.riskLevel) | translate }}</span>
                  }
                </div>
              </div>
              <div class="picker-item-body" [innerHTML]="c.body"></div>
              @if (c.guidance) {
                <div class="picker-item-guidance">
                  <mat-icon>tips_and_updates</mat-icon><span>{{ c.guidance }}</span>
                </div>
              }
            </div>
          }
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!selected()" (click)="insert()">
        <mat-icon>add_circle_outline</mat-icon> {{ 'clauses.picker.insert' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .title-icon { vertical-align: middle; margin-right: 6px; color: #7c3aed; }
    .picker-hint { font-size: 13px; color: #5f6368; margin: 0 0 12px; }
    .picker-filters { display: flex; gap: 10px; flex-wrap: wrap; }
    .picker-filters mat-form-field { min-width: 160px; }
    .search-field { flex: 1; min-width: 220px; }
    .picker-loading { display: flex; justify-content: center; padding: 40px; }
    .picker-empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 40px; color: #9ca3af; }
    .picker-empty mat-icon { font-size: 32px; width: 32px; height: 32px; }
    .picker-empty p { margin: 0; font-size: 13px; }
    .picker-list { display: flex; flex-direction: column; gap: 10px; max-height: 46vh; overflow-y: auto; padding: 2px; }
    .picker-item { border: 1px solid #e8eaed; border-radius: 10px; padding: 12px; cursor: pointer; transition: border-color .15s, background .15s; }
    .picker-item:hover { border-color: #c7d0dc; background: #fbfbfd; }
    .picker-item.selected { border-color: #7c3aed; background: #faf5ff; box-shadow: 0 0 0 1px #7c3aed inset; }
    .picker-item-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; margin-bottom: 8px; }
    .picker-item-title { font-size: 14px; font-weight: 600; color: #1f1f1f; }
    .picker-chips { display: flex; gap: 5px; flex-wrap: wrap; }
    .chip { font-size: 11px; font-weight: 600; border-radius: 6px; padding: 2px 8px; white-space: nowrap; }
    .chip.category { background: #eef1f5; color: #5f6368; }
    .chip.lang { background: #e8f0fe; color: #1a73e8; }
    .risk-low { background: #e6f4ea; color: #1e6b2e; }
    .risk-medium { background: #fef3c7; color: #92400e; }
    .risk-high { background: #fee2e2; color: #991b1b; }
    .picker-item-body { font-size: 12.5px; line-height: 1.6; color: #374151; max-height: 120px; overflow-y: auto; }
    .picker-item-body h3 { font-size: 12.5px; font-weight: 600; margin: 0 0 4px; }
    .picker-item-body p { margin: 0; }
    .picker-item-guidance { display: flex; gap: 6px; font-size: 11.5px; color: #6b7280; margin-top: 8px; line-height: 1.5; }
    .picker-item-guidance mat-icon { font-size: 14px; width: 14px; height: 14px; color: #f59e0b; flex-shrink: 0; margin-top: 1px; }
    mat-dialog-content { min-width: 620px; }
    @media (max-width: 700px) { mat-dialog-content { min-width: 0; } }
  `]
})
export class ClausePickerDialogComponent implements OnInit {
  private readonly service = inject(ClauseService);
  readonly dialogRef = inject(MatDialogRef<ClausePickerDialogComponent, Clause>);

  readonly categories = CLAUSE_CATEGORIES;
  readonly loading = signal(true);
  readonly clauses = signal<Clause[]>([]);
  readonly selected = signal<Clause | null>(null);

  search = '';
  categoryFilter: ClauseCategory | null = null;
  languageFilter: string | null = null;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.selected.set(null);
    this.service.getApproved({
      search: this.search || undefined,
      category: this.categoryFilter ?? undefined,
      language: this.languageFilter ?? undefined
    }).subscribe({
      next: list => { this.clauses.set(list); this.loading.set(false); },
      error: () => { this.clauses.set([]); this.loading.set(false); }
    });
  }

  insert(): void {
    const clause = this.selected();
    if (!clause) return;
    // Usage is recorded optimistically — a failed counter must never block the insertion.
    this.service.recordUsage(clause.id).subscribe({ next: () => {}, error: () => {} });
    this.dialogRef.close(clause);
  }
}
