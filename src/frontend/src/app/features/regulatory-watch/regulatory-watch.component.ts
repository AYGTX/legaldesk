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
import { RegulatoryWatchService } from '../../core/services/regulatory-watch.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { AuthService } from '../../core/auth/auth.service';
import {
  RegulatoryText,
  RegulatoryCategory,
  RegulatoryJurisdiction,
  RegulatoryTextStatus,
  REGULATORY_CATEGORIES,
  REGULATORY_JURISDICTIONS
} from '../../core/models';
import { RegulatoryTextDialogComponent } from './regulatory-text-dialog.component';
import { RegulatoryArticlesDialogComponent } from './regulatory-articles-dialog.component';

@Component({
  selector: 'sf-regulatory-watch',
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
          <a class="back-link" routerLink="/admin"><mat-icon>arrow_back</mat-icon> {{ 'regulatoryWatch.backToAdmin' | translate }}</a>
          <h1>{{ 'regulatoryWatch.title' | translate }}</h1>
          <p>{{ 'regulatoryWatch.subtitle' | translate }}</p>
        </div>
        <button mat-flat-button color="primary" (click)="openDialog(null)">
          <mat-icon>add</mat-icon> {{ 'regulatoryWatch.addText' | translate }}
        </button>
      </div>

      <div class="stats-row">
        <div class="stat"><span class="stat-value">{{ texts().length }}</span><span class="stat-label">{{ 'regulatoryWatch.stats.total' | translate }}</span></div>
        <div class="stat"><span class="stat-value published">{{ countBy('Published') }}</span><span class="stat-label">{{ 'regulatoryWatch.status.Published' | translate }}</span></div>
        <div class="stat"><span class="stat-value draft">{{ countBy('Draft') }}</span><span class="stat-label">{{ 'regulatoryWatch.status.Draft' | translate }}</span></div>
        <div class="stat"><span class="stat-value archived">{{ countBy('Archived') }}</span><span class="stat-label">{{ 'regulatoryWatch.status.Archived' | translate }}</span></div>
      </div>

      <div class="filters">
        <mat-form-field appearance="outline" class="search-field">
          <mat-label>{{ 'regulatoryWatch.searchLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="load()" [placeholder]="'regulatoryWatch.searchPlaceholder' | translate" />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'regulatoryWatch.filterCategory' | translate }}</mat-label>
          <mat-select [(ngModel)]="categoryFilter" (selectionChange)="load()">
            <mat-option [value]="null">{{ 'regulatoryWatch.allCategories' | translate }}</mat-option>
            @for (c of categories; track c) {
              <mat-option [value]="c">{{ ('regulatoryWatch.category.' + c) | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'regulatoryWatch.filterJurisdiction' | translate }}</mat-label>
          <mat-select [(ngModel)]="jurisdictionFilter" (selectionChange)="load()">
            <mat-option [value]="null">{{ 'regulatoryWatch.allJurisdictions' | translate }}</mat-option>
            @for (j of jurisdictions; track j) {
              <mat-option [value]="j">{{ ('regulatoryWatch.jurisdiction.' + j) | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'regulatoryWatch.filterStatus' | translate }}</mat-label>
          <mat-select [(ngModel)]="statusFilter" (selectionChange)="load()">
            <mat-option [value]="null">{{ 'regulatoryWatch.allStatuses' | translate }}</mat-option>
            <mat-option value="Draft">{{ 'regulatoryWatch.status.Draft' | translate }}</mat-option>
            <mat-option value="Published">{{ 'regulatoryWatch.status.Published' | translate }}</mat-option>
            <mat-option value="Archived">{{ 'regulatoryWatch.status.Archived' | translate }}</mat-option>
          </mat-select>
        </mat-form-field>
      </div>

      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="44"></mat-spinner></div>
      } @else if (texts().length === 0) {
        <div class="empty-state">
          <mat-icon>travel_explore</mat-icon>
          <h3>{{ 'regulatoryWatch.emptyTitle' | translate }}</h3>
          <p>{{ 'regulatoryWatch.emptyHint' | translate }}</p>
        </div>
      } @else {
        <div class="text-grid">
          @for (t of texts(); track t.id) {
            <mat-card class="text-card" [class.archived]="t.status === 'Archived'">
              <div class="text-card-head">
                <div class="text-heading">
                  <span class="text-title">{{ t.title }}</span>
                  @if (t.reference) { <span class="text-reference">{{ t.reference }}</span> }
                  <div class="text-chips">
                    <span class="chip category">{{ ('regulatoryWatch.category.' + t.category) | translate }}</span>
                    <span class="chip jurisdiction">{{ ('regulatoryWatch.jurisdiction.' + t.jurisdiction) | translate }}</span>
                    <span class="chip lang">{{ t.language | uppercase }}</span>
                    <span class="chip status" [ngClass]="'status-' + t.status.toLowerCase()">{{ ('regulatoryWatch.status.' + t.status) | translate }}</span>
                    @if (t.verifiedAt) {
                      <span class="chip verified" [matTooltip]="('regulatoryWatch.verifiedTooltip' | translate: { name: t.verifiedByName || '—', date: (t.verifiedAt | date:'mediumDate') })">
                        <mat-icon class="chip-icon">verified</mat-icon> {{ 'regulatoryWatch.verified' | translate }}
                      </span>
                    } @else {
                      <span class="chip unverified" [matTooltip]="'regulatoryWatch.unverifiedTooltip' | translate">
                        <mat-icon class="chip-icon">report</mat-icon> {{ 'regulatoryWatch.unverified' | translate }}
                      </span>
                    }
                    <span class="chip articles">{{ 'regulatoryWatch.articleCount' | translate: { count: t.articleCount ?? 0 } }}</span>
                  </div>
                </div>
                <button mat-icon-button [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
                <mat-menu #menu="matMenu">
                  <button mat-menu-item (click)="openDialog(t)"><mat-icon>edit</mat-icon> {{ 'common.edit' | translate }}</button>
                  <button mat-menu-item (click)="openArticles(t)"><mat-icon>gavel</mat-icon> {{ 'regulatoryWatch.manageArticles' | translate }}</button>
                  @if (isAdmin()) {
                    @if (t.status !== 'Published') {
                      <button mat-menu-item (click)="setStatus(t, 'Published')"><mat-icon>publish</mat-icon> {{ 'regulatoryWatch.publish' | translate }}</button>
                    }
                    @if (t.status !== 'Archived') {
                      <button mat-menu-item (click)="setStatus(t, 'Archived')"><mat-icon>archive</mat-icon> {{ 'regulatoryWatch.archive' | translate }}</button>
                    }
                    @if (t.status !== 'Draft') {
                      <button mat-menu-item (click)="setStatus(t, 'Draft')"><mat-icon>undo</mat-icon> {{ 'regulatoryWatch.revertToDraft' | translate }}</button>
                    }
                    <button mat-menu-item class="danger" (click)="remove(t)"><mat-icon>delete_outline</mat-icon> {{ 'common.delete' | translate }}</button>
                  }
                </mat-menu>
              </div>

              <div class="text-summary" [innerHTML]="t.summary"></div>

              <div class="text-footer">
                @if (t.effectiveDate) {
                  <span [matTooltip]="'regulatoryWatch.effectiveDateTooltip' | translate">{{ t.effectiveDate | date }}</span>
                }
                @if (t.requestTypeName) {
                  <span class="scope-tag">{{ t.requestTypeName }}</span>
                }
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './regulatory-watch.component.scss'
})
export class RegulatoryWatchComponent implements OnInit {
  private readonly service = inject(RegulatoryWatchService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly confirmService = inject(ConfirmService);
  private readonly auth = inject(AuthService);

  readonly categories = REGULATORY_CATEGORIES;
  readonly jurisdictions = REGULATORY_JURISDICTIONS;
  readonly loading = signal(true);
  readonly texts = signal<RegulatoryText[]>([]);
  readonly isAdmin = computed(() => this.auth.isAdmin());

  search = '';
  categoryFilter: RegulatoryCategory | null = null;
  jurisdictionFilter: RegulatoryJurisdiction | null = null;
  statusFilter: RegulatoryTextStatus | null = null;

  ngOnInit(): void {
    this.load();
  }

  countBy(status: RegulatoryTextStatus): number {
    return this.texts().filter(t => t.status === status).length;
  }

  load(): void {
    this.loading.set(true);
    this.service.getAll({
      search: this.search || undefined,
      category: this.categoryFilter ?? undefined,
      jurisdiction: this.jurisdictionFilter ?? undefined,
      status: this.statusFilter ?? undefined
    }).subscribe({
      next: list => { this.texts.set(list); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.snack.open(this.translate.instant('regulatoryWatch.loadFailed'), 'OK', { duration: 4000 });
      }
    });
  }

  openDialog(text: RegulatoryText | null): void {
    this.dialog.open(RegulatoryTextDialogComponent, { data: { text }, width: '720px', maxWidth: '95vw' })
      .afterClosed().subscribe(saved => { if (saved) this.load(); });
  }

  /** The text of the law, article by article, and the sign-off against the official source. */
  openArticles(text: RegulatoryText): void {
    this.dialog.open(RegulatoryArticlesDialogComponent, { data: { text }, width: '920px', maxWidth: '96vw', disableClose: true })
      .afterClosed().subscribe(changed => { if (changed) this.load(); });
  }

  setStatus(text: RegulatoryText, status: RegulatoryTextStatus): void {
    this.service.setStatus(text.id, status).subscribe({
      next: () => this.load(),
      error: err => this.snack.open(err.error?.detail ?? this.translate.instant('regulatoryWatch.statusFailed'), 'OK', { duration: 4000 })
    });
  }

  remove(text: RegulatoryText): void {
    this.confirmService.confirm({
      message: this.translate.instant('regulatoryWatch.confirmDelete', { title: text.title })
    }).subscribe(ok => {
      if (!ok) return;
      this.service.delete(text.id).subscribe({
        next: () => this.load(),
        error: err => this.snack.open(err.error?.detail ?? this.translate.instant('regulatoryWatch.deleteFailed'), 'OK', { duration: 5000 })
      });
    });
  }
}
