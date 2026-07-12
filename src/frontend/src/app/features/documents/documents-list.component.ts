import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { DocumentSummary, DocumentStatus, RequestType } from '../../core/models';
import { remainingTime, RemainingTime } from '../../core/utils/sla';

const STATUS_COLORS: Record<string, string> = {
  Draft: '#5f6368',
  Pending: '#f59e0b',
  Sent: '#1a73e8',
  Completed: '#22c55e',
  Rejected: '#ef4444',
  Expired: '#f97316',
  Cancelled: '#80868b',
  WaitingForOthers: '#8b5cf6',
  ChangesRequested: '#d97706'
};

@Component({
  selector: 'sf-documents-list',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
    MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule,
    MatMenuModule, MatProgressSpinnerModule, MatTooltipModule,
    TranslatePipe
  ],
  template: `
    <div class="documents-page">

      <!-- ── Header ── -->
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'documents.list.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'documents.list.subtitle' | translate }}</p>
        </div>
        <button mat-flat-button color="primary" routerLink="/documents/upload">
          <mat-icon>upload_file</mat-icon> {{ 'documents.list.uploadDocument' | translate }}
        </button>
      </div>

      <!-- ── Toolbar ── -->
      <div class="toolbar">

        <!-- Search -->
        <div class="search-wrap">
          <mat-icon class="search-icon">search</mat-icon>
          <input
            class="search-input"
            [formControl]="searchControl"
            [placeholder]="'documents.list.searchPlaceholder' | translate"
            autocomplete="off" />
          @if (searchControl.value) {
            <button class="clear-btn" (click)="searchControl.setValue('')" [matTooltip]="'documents.list.clearSearch' | translate">
              <mat-icon>close</mat-icon>
            </button>
          }
        </div>

        <!-- Status pills -->
        <div class="filter-pills">
          <button class="pill" [class.active]="statusControl.value === ''" (click)="setStatus('')">{{ 'documents.list.allStatuses' | translate }}</button>
          @for (s of statusOptions; track s.value) {
            <button class="pill"
                    [class.active]="statusControl.value === s.value"
                    (click)="setStatus(s.value)"
                    [style.--pill-color]="s.color">
              {{ s.label }}
            </button>
          }
        </div>

        <!-- Request type (native select) -->
        @if (requestTypes().length > 0) {
          <select class="type-select" [formControl]="requestTypeControl">
            <option value="">{{ 'documents.list.allTypes' | translate }}</option>
            @for (rt of requestTypes(); track rt.id) {
              <option [value]="rt.id">{{ rt.name }}</option>
            }
          </select>
        }
      </div>

      <!-- ── Result count ── -->
      @if (!loading()) {
        <p class="result-count">
          @if (totalCount() === 0) {
            {{ 'documents.list.noDocumentsFound' | translate }}
          } @else {
            {{ (totalCount() !== 1 ? 'documents.list.documentCountPlural' : 'documents.list.documentCountSingular') | translate: { count: totalCount() } }}
          }
        </p>
      }

      <!-- ── Table ── -->
      <div class="table-surface">
        @if (loading()) {
          <div class="table-overlay"><mat-spinner diameter="36"></mat-spinner></div>
        }

        <table mat-table [dataSource]="documents()" class="doc-table">

          <!-- Document -->
          <ng-container matColumnDef="title">
            <th mat-header-cell *matHeaderCellDef>{{ 'documents.list.columns.document' | translate }}</th>
            <td mat-cell *matCellDef="let doc">
              <div class="doc-cell">
                <div class="pdf-badge">
                  <mat-icon>picture_as_pdf</mat-icon>
                </div>
                <div class="doc-text">
                  <a [routerLink]="['/documents', doc.id]" class="doc-title" (click)="$event.stopPropagation()">
                    {{ doc.title }}
                  </a>
                  <span class="doc-meta">
                    {{ (doc.pageCount > 1 ? 'documents.list.pageCountPlural' : 'documents.list.pageCountSingular') | translate: { count: doc.pageCount } }}
                    @if (doc.requestTypeName) {
                      &nbsp;·&nbsp;<span class="type-tag">{{ doc.requestTypeName }}</span>
                    }
                  </span>
                </div>
              </div>
            </td>
          </ng-container>

          <!-- Status -->
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>{{ 'common.status' | translate }}</th>
            <td mat-cell *matCellDef="let doc">
              <span class="status-chip"
                    [style.background]="statusBg(doc.status)"
                    [style.color]="statusColor(doc.status)">
                {{ statusLabel(doc.status) }}
              </span>
              @if (remainingFor(doc); as r) {
                <div class="sla-chip sla-{{ r.level }}">
                  <mat-icon>{{ r.expired ? 'error_outline' : 'schedule' }}</mat-icon>
                  {{ r.text }}
                </div>
              }
            </td>
          </ng-container>

          <!-- Owner -->
          <ng-container matColumnDef="owner">
            <th mat-header-cell *matHeaderCellDef>{{ 'documents.list.columns.owner' | translate }}</th>
            <td mat-cell *matCellDef="let doc">
              <span class="owner-cell">{{ doc.ownerName }}</span>
            </td>
          </ng-container>

          <!-- Recipients -->
          <ng-container matColumnDef="recipients">
            <th mat-header-cell *matHeaderCellDef>{{ 'documents.list.columns.recipients' | translate }}</th>
            <td mat-cell *matCellDef="let doc">
              <span class="recip-cell">
                <mat-icon>group</mat-icon>
                {{ doc.recipientCount }}
              </span>
            </td>
          </ng-container>

          <!-- Created -->
          <ng-container matColumnDef="createdAt">
            <th mat-header-cell *matHeaderCellDef>{{ 'documents.list.columns.created' | translate }}</th>
            <td mat-cell *matCellDef="let doc">
              <span class="date-cell">{{ doc.createdAt | date:'MMM d, y' }}</span>
            </td>
          </ng-container>

          <!-- Actions -->
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let doc" class="actions-cell">
              <button mat-icon-button class="more-btn"
                      [matMenuTriggerFor]="docMenu"
                      (click)="$event.stopPropagation()"
                      [matTooltip]="'documents.list.moreOptions' | translate">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #docMenu="matMenu">
                <button mat-menu-item [routerLink]="['/documents', doc.id]">
                  <mat-icon>open_in_new</mat-icon><span>{{ 'documents.list.menu.open' | translate }}</span>
                </button>
                @if (doc.status === 'Draft') {
                  <button mat-menu-item [routerLink]="['/documents', doc.id, 'designer']">
                    <mat-icon>edit</mat-icon><span>{{ 'documents.list.menu.designFields' | translate }}</span>
                  </button>
                }
                <button mat-menu-item (click)="download(doc)">
                  <mat-icon>download</mat-icon><span>{{ 'common.download' | translate }}</span>
                </button>
                <div class="menu-divider"></div>
                <button mat-menu-item class="danger-item" (click)="confirmDelete(doc)">
                  <mat-icon>delete_outline</mat-icon><span>{{ 'common.delete' | translate }}</span>
                </button>
              </mat-menu>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
          <tr mat-row *matRowDef="let row; columns: displayedColumns;"
              class="doc-row"
              [routerLink]="['/documents', row.id]"></tr>

          <tr class="mat-row" *matNoDataRow>
            <td class="mat-cell" [colSpan]="displayedColumns.length">
              @if (!loading()) {
                <div class="empty-state">
                  <div class="empty-icon-wrap"><mat-icon>description</mat-icon></div>
                  <p class="empty-title">{{ 'documents.list.noDocumentsFound' | translate }}</p>
                  <p class="empty-sub">{{ 'documents.list.emptySub' | translate }}</p>
                  <a mat-stroked-button routerLink="/documents/upload">{{ 'documents.list.uploadDocument' | translate }}</a>
                </div>
              }
            </td>
          </tr>
        </table>

        <mat-paginator
          [length]="totalCount()"
          [pageSize]="pageSize"
          [pageSizeOptions]="[10, 25, 50]"
          (page)="onPage($event)"
          showFirstLastButtons>
        </mat-paginator>
      </div>
    </div>
  `,
  styleUrl: './documents-list.component.scss'
})
export class DocumentsListComponent implements OnInit {
  private readonly documentService = inject(DocumentService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly translate = inject(TranslateService);

  readonly displayedColumns = ['title', 'status', 'owner', 'recipients', 'createdAt', 'actions'];
  readonly statusOptions = Object.entries(STATUS_COLORS).map(([value, color]) => ({
    value,
    color,
    label: this.translate.instant(`documents.list.status.${value}`)
  }));

  readonly searchControl = new FormControl('');
  readonly statusControl = new FormControl('');
  readonly requestTypeControl = new FormControl('');

  readonly loading = signal(true);
  readonly documents = signal<DocumentSummary[]>([]);
  readonly totalCount = signal(0);
  readonly requestTypes = signal<RequestType[]>([]);

  pageSize = 25;
  private pageNumber = 1;

  setStatus(value: string): void {
    this.statusControl.setValue(value);
  }

  statusLabel(status: string): string { return this.translate.instant(`documents.list.status.${status}`); }
  statusColor(status: string): string { return STATUS_COLORS[status] ?? '#5f6368'; }
  statusBg(status: string): string {
    const c = STATUS_COLORS[status] ?? '#5f6368';
    return `${c}20`;
  }

  /** Remaining SLA time for active requests (hidden for terminal/draft/paused states). */
  remainingFor(doc: DocumentSummary): RemainingTime | null {
    if (['Draft', 'Completed', 'Rejected', 'Cancelled', 'ChangesRequested'].includes(doc.status)) return null;
    return remainingTime(doc.expiresAt);
  }

  ngOnInit(): void {
    const queryStatus = this.route.snapshot.queryParamMap.get('status');
    if (queryStatus) this.statusControl.setValue(queryStatus);

    this.requestTypeService.getActive().subscribe({
      next: types => this.requestTypes.set(types),
      error: () => {}
    });

    this.load();

    this.searchControl.valueChanges.pipe(debounceTime(300), distinctUntilChanged()).subscribe(() => {
      this.pageNumber = 1;
      this.load();
    });
    this.statusControl.valueChanges.subscribe(() => {
      this.pageNumber = 1;
      this.load();
    });
    this.requestTypeControl.valueChanges.subscribe(() => {
      this.pageNumber = 1;
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.documentService.getDocuments({
      pageNumber: this.pageNumber,
      pageSize: this.pageSize,
      search: this.searchControl.value ?? '',
      status: this.statusControl.value ?? '',
      requestTypeId: this.requestTypeControl.value || undefined
    }).subscribe({
      next: result => {
        this.documents.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  onPage(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.load();
  }

  download(doc: DocumentSummary): void {
    this.documentService.download(doc.id).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${doc.title}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    });
  }

  confirmDelete(doc: DocumentSummary): void {
    if (!confirm(this.translate.instant('documents.list.confirmDelete', { title: doc.title }))) return;
    this.documentService.delete(doc.id).subscribe(() => this.load());
  }
}
