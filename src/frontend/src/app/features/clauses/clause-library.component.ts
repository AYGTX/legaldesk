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
import { ClauseCategory, ClauseStatus, ClauseVersion, CLAUSE_CATEGORIES, LibraryClause } from '../../core/models';
import { problemText } from '../../core/utils/problem-text';
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
          <a class="back-link" routerLink="/admin/users"><mat-icon>arrow_back</mat-icon> {{ 'clauses.backToAdmin' | translate }}</a>
          <h1>{{ 'clauses.title' | translate }}</h1>
          <p>{{ 'clauses.subtitle' | translate }}</p>
        </div>
        <button mat-flat-button color="primary" (click)="openDialog(null)">
          <mat-icon>add</mat-icon> {{ 'clauses.addClause' | translate }}
        </button>
      </div>

      <div class="stats-row">
        <div class="stat"><span class="stat-value">{{ clauses().length }}</span><span class="stat-label">{{ 'clauses.stats.total' | translate }}</span></div>
        <div class="stat"><span class="stat-value approved">{{ inUseCount() }}</span><span class="stat-label">{{ 'clauses.stats.inUse' | translate }}</span></div>
        <div class="stat"><span class="stat-value draft">{{ waitingCount() }}</span><span class="stat-label">{{ 'clauses.stats.waiting' | translate }}</span></div>
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
            <mat-card class="clause-card" [class.deprecated]="!c.approved && c.latest?.status === 'Deprecated'">
              <div class="clause-card-head">
                <div class="clause-heading">
                  <span class="clause-title">{{ c.title }}</span>
                  <div class="clause-chips">
                    <span class="chip category">{{ ('clauses.category.' + c.category) | translate }}</span>
                    @if (shown(c); as v) { <span class="chip lang">{{ v.language | uppercase }}</span> }
                    @if (c.approved) {
                      <span class="chip status-approved">{{ 'clauses.inUse' | translate: { version: c.approved.number } }}</span>
                    }
                    @if (pending(c); as d) {
                      <span class="chip status-draft">{{ 'clauses.draftWaiting' | translate: { version: d.number } }}</span>
                    } @else if (!c.approved) {
                      <span class="chip status-deprecated">{{ 'clauses.notInUse' | translate }}</span>
                    }
                    @if (c.riskLevel) {
                      <span class="chip risk" [ngClass]="'risk-' + c.riskLevel.toLowerCase()">{{ ('clauses.risk.' + c.riskLevel) | translate }}</span>
                    }
                  </div>
                </div>
                <button mat-icon-button [matMenuTriggerFor]="menu" [attr.aria-label]="'clauses.actions' | translate"><mat-icon>more_vert</mat-icon></button>
                <mat-menu #menu="matMenu">
                  <button mat-menu-item (click)="openDialog(c)"><mat-icon>edit</mat-icon> {{ 'common.edit' | translate }}</button>
                  @if (canApprove()) {
                    @if (pending(c); as d) {
                      <button mat-menu-item (click)="approve(c, d.number)" [disabled]="d.authorId === myId()"
                              [matTooltip]="d.authorId === myId() ? ('clauses.ownWording' | translate) : ''">
                        <mat-icon>verified</mat-icon> {{ 'clauses.approveVersion' | translate: { version: d.number } }}
                      </button>
                    }
                    @if (c.approved) {
                      <button mat-menu-item (click)="deprecate(c, c.approved.number)"><mat-icon>archive</mat-icon> {{ 'clauses.withdrawVersion' | translate: { version: c.approved.number } }}</button>
                    }
                    <button mat-menu-item class="danger" (click)="remove(c)"><mat-icon>delete_outline</mat-icon> {{ 'common.delete' | translate }}</button>
                  }
                </mat-menu>
              </div>

              @if (shown(c); as v) {
                <div class="clause-body" [innerHTML]="v.body"></div>
              }

              @if (pending(c); as d) {
                @if (c.approved) {
                  <details class="clause-draft">
                    <summary>{{ 'clauses.draftWording' | translate: { version: d.number, author: d.authorName ?? '' } }}</summary>
                    <div class="clause-body" [innerHTML]="d.body"></div>
                  </details>
                } @else {
                  <p class="clause-draft-note">{{ 'clauses.draftBy' | translate: { author: d.authorName ?? '' } }}</p>
                }
              }

              @if (c.guidance) {
                <div class="clause-guidance">
                  <mat-icon>tips_and_updates</mat-icon>
                  <span>{{ c.guidance }}</span>
                </div>
              }

              @if (c.ruleKeys.length) {
                <div class="clause-rules" [matTooltip]="'clauses.rulesHint' | translate">
                  <mat-icon>rule</mat-icon>
                  @for (k of c.ruleKeys; track k) { <code>{{ k }}</code> }
                </div>
              }

              @if (c.versions.length > 1) {
                <details class="clause-history">
                  <summary>{{ 'clauses.history' | translate: { count: c.versions.length } }}</summary>
                  <ul>
                    @for (v of c.versions; track v.number) {
                      <li>
                        <strong>v{{ v.number }}</strong>
                        <span class="chip" [ngClass]="'status-' + v.status.toLowerCase()">{{ ('clauses.status.' + v.status) | translate }}</span>
                        <span>{{ v.authorName }}</span>
                        @if (v.approvedByName) { <span>· {{ 'clauses.approvedBy' | translate: { name: v.approvedByName } }}</span> }
                        <span class="muted">{{ (v.approvedAt ?? v.createdAt) | date:'d MMM y' }}</span>
                      </li>
                    }
                  </ul>
                </details>
              }

              <div class="clause-footer">
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
  readonly clauses = signal<LibraryClause[]>([]);
  /** Approving wording is for managers and administrators, never the wording's author (R-M9-2). */
  readonly canApprove = computed(() => this.auth.isManager());
  readonly myId = computed(() => this.auth.user()?.id ?? '');
  readonly inUseCount = computed(() => this.clauses().filter(c => !!c.approved).length);
  readonly waitingCount = computed(() => this.clauses().filter(c => !!this.pending(c)).length);

  search = '';
  categoryFilter: ClauseCategory | null = null;
  languageFilter: string | null = null;
  statusFilter: ClauseStatus | null = null;

  ngOnInit(): void {
    this.load();
  }

  /** The wording a reader sees first: the one in use, else the newest. */
  shown(c: LibraryClause): ClauseVersion | null {
    return c.approved ?? c.latest ?? c.versions[0] ?? null;
  }

  /** A draft waiting for approval. */
  pending(c: LibraryClause): ClauseVersion | null {
    const latest = c.latest ?? c.versions[0];
    return latest?.status === 'Draft' ? latest : null;
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

  openDialog(clause: LibraryClause | null): void {
    this.dialog.open(ClauseDialogComponent, { data: { clause }, width: '720px', maxWidth: '95vw' })
      .afterClosed().subscribe(saved => { if (saved) this.load(); });
  }

  approve(clause: LibraryClause, number: number): void {
    this.service.approve(clause.id, number).subscribe({
      next: () => { this.snack.open(this.translate.instant('clauses.approved', { version: number }), 'OK', { duration: 3000 }); this.load(); },
      error: err => this.snack.open(problemText(this.translate, err, 'clauses.statusFailed'), 'OK', { duration: 5000 })
    });
  }

  deprecate(clause: LibraryClause, number: number): void {
    this.confirmService.confirm({
      message: this.translate.instant('clauses.confirmWithdraw', { title: clause.title, version: number })
    }).subscribe(ok => {
      if (!ok) return;
      this.service.deprecate(clause.id, number).subscribe({
        next: () => this.load(),
        error: err => this.snack.open(problemText(this.translate, err, 'clauses.statusFailed'), 'OK', { duration: 5000 })
      });
    });
  }

  remove(clause: LibraryClause): void {
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
