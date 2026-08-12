import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ClauseService } from '../../core/services/clause.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { AuthService } from '../../core/auth/auth.service';
import { Clause, ClauseCategory, ClauseStatus, CLAUSE_CATEGORIES } from '../../core/models';
import { ClauseDialogComponent } from './clause-dialog.component';

@Component({
  selector: 'sf-clause-library',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatMenuModule, MatTooltipModule, MatProgressSpinnerModule,
    MatDialogModule, MatSnackBarModule, TranslatePipe
  ],
  template: `
    <div class="library-page">
      <div class="page-header">
        <div class="header-text">
          <a class="back-link" routerLink="/admin"><mat-icon>arrow_back</mat-icon> {{ 'clauses.backToAdmin' | translate }}</a>
          <h1>{{ 'clauses.title' | translate }}</h1>
          <p>{{ 'clauses.subtitle' | translate }}</p>
        </div>
        <button mat-flat-button color="primary" (click)="openDialog(null)">
          <mat-icon>add</mat-icon> {{ 'clauses.addClause' | translate }}
        </button>
      </div>

      <div class="stats-row">
        <div class="stat"><span class="stat-value">{{ clauses().length }}</span><span class="stat-label">{{ 'clauses.stats.total' | translate }}</span></div>
        <div class="stat"><span class="stat-value approved">{{ countBy('Approved') }}</span><span class="stat-label">{{ 'clauses.status.Approved' | translate }}</span></div>
        <div class="stat"><span class="stat-value draft">{{ countBy('Draft') }}</span><span class="stat-label">{{ 'clauses.status.Draft' | translate }}</span></div>
        <div class="stat"><span class="stat-value deprecated">{{ countBy('Deprecated') }}</span><span class="stat-label">{{ 'clauses.status.Deprecated' | translate }}</span></div>
      </div>

      <div class="filters">
        <mat-form-field appearance="outline" class="search-field">
          <mat-label>{{ 'clauses.searchLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="load()" [placeholder]="'clauses.searchPlaceholder' | translate" />
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

        <mat-form-field appearance="outline">
          <mat-label>{{ 'clauses.filterStatus' | translate }}</mat-label>
          <mat-select [(ngModel)]="statusFilter" (selectionChange)="load()">
            <mat-option [value]="null">{{ 'clauses.allStatuses' | translate }}</mat-option>
            <mat-option value="Draft">{{ 'clauses.status.Draft' | translate }}</mat-option>
            <mat-option value="Approved">{{ 'clauses.status.Approved' | translate }}</mat-option>
            <mat-option value="Deprecated">{{ 'clauses.status.Deprecated' | translate }}</mat-option>
          </mat-select>
        </mat-form-field>
      </div>

      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="44"></mat-spinner></div>
      } @else if (clauses().length === 0) {
        <div class="empty-state">
          <mat-icon>menu_book</mat-icon>
          <h3>{{ 'clauses.emptyTitle' | translate }}</h3>
          <p>{{ 'clauses.emptyHint' | translate }}</p>
        </div>
      } @else {
        <div class="clause-grid">
          @for (c of clauses(); track c.id) {
            <mat-card class="clause-card" [class.deprecated]="c.status === 'Deprecated'">
              <div class="clause-card-head">
                <div class="clause-heading">
                  <span class="clause-title">{{ c.title }}</span>
                  <div class="clause-chips">
                    <span class="chip category">{{ ('clauses.category.' + c.category) | translate }}</span>
                    <span class="chip lang">{{ c.language | uppercase }}</span>
                    <span class="chip status" [ngClass]="'status-' + c.status.toLowerCase()">{{ ('clauses.status.' + c.status) | translate }}</span>
                    @if (c.riskLevel) {
                      <span class="chip risk" [ngClass]="'risk-' + c.riskLevel.toLowerCase()">{{ ('clauses.risk.' + c.riskLevel) | translate }}</span>
                    }
                  </div>
                </div>
                <button mat-icon-button [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
                <mat-menu #menu="matMenu">
                  <button mat-menu-item (click)="openDialog(c)"><mat-icon>edit</mat-icon> {{ 'common.edit' | translate }}</button>
                  @if (isAdmin()) {
                    @if (c.status !== 'Approved') {
                      <button mat-menu-item (click)="setStatus(c, 'Approved')"><mat-icon>verified</mat-icon> {{ 'clauses.approve' | translate }}</button>
                    }
                    @if (c.status !== 'Deprecated') {
                      <button mat-menu-item (click)="setStatus(c, 'Deprecated')"><mat-icon>archive</mat-icon> {{ 'clauses.deprecate' | translate }}</button>
                    }
                    @if (c.status !== 'Draft') {
                      <button mat-menu-item (click)="setStatus(c, 'Draft')"><mat-icon>undo</mat-icon> {{ 'clauses.revertToDraft' | translate }}</button>
                    }
                    <button mat-menu-item class="danger" (click)="remove(c)"><mat-icon>delete_outline</mat-icon> {{ 'common.delete' | translate }}</button>
                  }
                </mat-menu>
              </div>

              <div class="clause-body" [innerHTML]="c.body"></div>

              @if (c.guidance) {
                <div class="clause-guidance">
                  <mat-icon>tips_and_updates</mat-icon>
                  <span>{{ c.guidance }}</span>
                </div>
              }

              <div class="clause-footer">
                <span [matTooltip]="'clauses.versionTooltip' | translate">v{{ c.version }}</span>
                <span [matTooltip]="'clauses.usageTooltip' | translate">{{ 'clauses.usedTimes' | translate: { count: c.usageCount } }}</span>
                @if (c.requestTypeName) {
                  <span class="scope-tag">{{ c.requestTypeName }}</span>
                }
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './clause-library.component.scss'
})
export class ClauseLibraryComponent implements OnInit {
  private readonly service = inject(ClauseService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly confirmService = inject(ConfirmService);
  private readonly auth = inject(AuthService);

  readonly categories = CLAUSE_CATEGORIES;
  readonly loading = signal(true);
  readonly clauses = signal<Clause[]>([]);
  readonly isAdmin = computed(() => this.auth.isAdmin());

  search = '';
  categoryFilter: ClauseCategory | null = null;
  languageFilter: string | null = null;
  statusFilter: ClauseStatus | null = null;

  ngOnInit(): void {
    this.load();
  }

  countBy(status: ClauseStatus): number {
    return this.clauses().filter(c => c.status === status).length;
  }

  load(): void {
    this.loading.set(true);
    this.service.getAll({
      search: this.search || undefined,
      category: this.categoryFilter ?? undefined,
      language: this.languageFilter ?? undefined,
      status: this.statusFilter ?? undefined
    }).subscribe({
      next: list => { this.clauses.set(list); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.snack.open(this.translate.instant('clauses.loadFailed'), 'OK', { duration: 4000 });
      }
    });
  }

  openDialog(clause: Clause | null): void {
    this.dialog.open(ClauseDialogComponent, { data: { clause }, width: '720px', maxWidth: '95vw' })
      .afterClosed().subscribe(saved => { if (saved) this.load(); });
  }

  setStatus(clause: Clause, status: ClauseStatus): void {
    this.service.setStatus(clause.id, status).subscribe({
      next: () => this.load(),
      error: err => this.snack.open(err.error?.detail ?? this.translate.instant('clauses.statusFailed'), 'OK', { duration: 4000 })
    });
  }

  remove(clause: Clause): void {
    this.confirmService.confirm({
      message: this.translate.instant('clauses.confirmDelete', { title: clause.title })
    }).subscribe(ok => {
      if (!ok) return;
      this.service.delete(clause.id).subscribe({
        next: () => this.load(),
        error: err => this.snack.open(err.error?.detail ?? this.translate.instant('clauses.deleteFailed'), 'OK', { duration: 5000 })
      });
    });
  }
}
