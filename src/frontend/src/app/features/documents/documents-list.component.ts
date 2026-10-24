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
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { DocumentSummary, DocumentStatus, RequestType, ReceivedRequest } from '../../core/models';
import { remainingTime, RemainingTime } from '../../core/utils/sla';
import { CreateDocumentDialogComponent } from '../../shared/create-document-dialog/create-document-dialog.component';

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

const RECEIVED_STATUS_COLORS: Record<string, string> = {
  Pending: '#f59e0b', InProgress: '#1a73e8', Completed: '#34a853', Rejected: '#ea4335',
  Expired: '#f29900', Cancelled: '#80868b', Skipped: '#80868b', Delegated: '#8430ce',
  Escalated: '#f29900', ChangesRequested: '#d97706'
};

@Component({
  selector: 'sf-documents-list',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule,
    MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule,
    MatMenuModule, MatProgressSpinnerModule, MatTooltipModule, MatDialogModule,
    MatFormFieldModule, MatSelectModule, TranslatePipe
  ],
  template: `
    <div class="documents-page">

      <!-- ── Header ── -->
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'documents.list.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'documents.list.subtitle' | translate }}</p>
        </div>
        <button mat-flat-button color="primary" (click)="openCreateDialog()">
          <mat-icon>add</mat-icon> {{ 'createDocument.newDocument' | translate }}
        </button>
      </div>

      <!-- ── Action required (top) ── -->
      @if (awaitingMe().length > 0) {
        <section class="action-section">
                <div class="section-header">
                  <span class="section-label">{{ 'requests.needsAction' | translate }}</span>
                  <span class="count-chip urgent">{{ awaitingMe().length }}</span>
                </div>
                <div class="list-surface">
                  @for (r of awaitingMe(); track r.documentId; let last = $last) {
                    <div class="list-row action-row" [class.no-border]="last">
                      <div class="row-icon-wrap action-icon">
                        <mat-icon>edit_document</mat-icon>
                      </div>
                      <div class="row-body">
                        <span class="row-title">{{ r.title }}</span>
                        <span class="row-meta">
                          {{ 'requests.from' | translate }} <strong class="meta-strong">{{ r.ownerName }}</strong>
                          @if (r.receivedAt) { &nbsp;·&nbsp; {{ r.receivedAt | date:'MMM d, y' }} }
                        </span>
                      </div>
                      <div class="row-end">
                        @if (remaining(r.expiresAt); as rem) {
                          <span class="sla-chip sla-{{ rem.level }}"
                                [matTooltip]="r.expiresAt ? ('requests.deadline' | translate: { date: (r.expiresAt | date:'medium') }) : ''"
                                matTooltipPosition="left">
                            <mat-icon>{{ rem.expired ? 'error_outline' : 'schedule' }}</mat-icon>
                            {{ rem.text }}
                          </span>
                        }
                        <button mat-stroked-button (click)="openContract(r)">
                          <mat-icon>visibility</mat-icon> {{ 'requests.openDocument' | translate }}
                        </button>
                        <button mat-flat-button class="sign-btn" (click)="openSigning(r)">
                          <mat-icon>{{ actionIcon(r) }}</mat-icon> {{ actionLabel(r) | translate }}
                        </button>
                      </div>
                    </div>
                  }
                </div>
        </section>
      }

      <!-- ── All contracts ── -->
      <section class="contracts-section">
        <div class="section-header">
          <span class="section-label">{{ 'documents.list.allDocumentsTab' | translate }}</span>
        </div>
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

        <!-- Status (multi-select) -->
        <mat-form-field appearance="outline" class="status-field" subscriptSizing="dynamic">
          <mat-select [formControl]="statusControl" multiple [placeholder]="'documents.list.statusFilter' | translate" [attr.aria-label]="'documents.list.statusFilter' | translate">
            <mat-select-trigger>
              @if ((statusControl.value ?? []).length === 0) {
                {{ 'documents.list.allStatuses' | translate }}
              } @else if ((statusControl.value ?? []).length === 1) {
                {{ statusLabel(statusControl.value![0]) }}
              } @else {
                {{ 'documents.list.statusesSelected' | translate: { count: statusControl.value!.length } }}
              }
            </mat-select-trigger>
            @for (s of statusOptions; track s.value) {
              <mat-option [value]="s.value">
                <span class="status-dot" [style.background]="s.color"></span>{{ s.label }}
              </mat-option>
            }
          </mat-select>
        </mat-form-field>

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
                  <button mat-stroked-button (click)="openCreateDialog()">{{ 'createDocument.newDocument' | translate }}</button>
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
      </section>

      <!-- ── Other contracts received (history) ── -->
      @if (otherReceived().length > 0) {
        <section class="history-section">
          <button class="history-toggle" (click)="showHistory.set(!showHistory())">
            <mat-icon>{{ showHistory() ? 'expand_less' : 'expand_more' }}</mat-icon>
            {{ (showHistory() ? 'documents.list.hideHistory' : 'documents.list.showHistory') | translate }}
            <span class="count-chip">{{ otherReceived().length }}</span>
          </button>
          @if (showHistory()) {
              @if (otherReceived().length > 0) {
                <div class="section-header" [style.margin-top]="awaitingMe().length ? '24px' : '0'">
                  <span class="section-label">{{ 'requests.history' | translate }}</span>
                  <span class="count-chip">{{ otherReceived().length }}</span>
                </div>
                <div class="list-surface">
                  @for (r of otherReceived(); track r.documentId; let last = $last) {
                    <div class="list-row" [class.no-border]="last">
                      <div class="row-icon-wrap" [style.background]="receivedStatusBg(effectiveStatus(r))" [style.color]="receivedStatusColor(effectiveStatus(r))">
                        <mat-icon>{{ receivedStatusIcon(effectiveStatus(r)) }}</mat-icon>
                      </div>
                      <div class="row-body">
                        <span class="row-title faded">{{ r.title }}</span>
                        <span class="row-meta">
                          {{ 'requests.from' | translate }} {{ r.ownerName }}
                          @if (r.signedAt) { &nbsp;·&nbsp; {{ 'requests.signed' | translate }} {{ r.signedAt | date:'MMM d, y' }} }
                        </span>
                        <span class="row-task">
                          {{ 'requests.yourTask' | translate }} : <strong>{{ taskLabel(r) }}</strong>
                        </span>
                        <span class="row-note" [class.row-note-over]="isDeclined(r)">{{ taskNote(r) }}</span>
                      </div>
                      <div class="row-end">
                        <span class="status-chip" [style.background]="receivedStatusBg(effectiveStatus(r))" [style.color]="receivedStatusColor(effectiveStatus(r))">
                          {{ receivedStatusLabel(effectiveStatus(r)) }}
                        </span>
                        <button mat-stroked-button (click)="openContract(r)">
                          <mat-icon>visibility</mat-icon> {{ 'requests.view' | translate }}
                        </button>
                      </div>
                    </div>
                  }
                </div>
              }
          }
        </section>
      }
    </div>
  `,
  styleUrl: './documents-list.component.scss'
})
export class DocumentsListComponent implements OnInit {
  private readonly documentService = inject(DocumentService);
  private readonly confirmService = inject(ConfirmService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly translate = inject(TranslateService);
  private readonly dialog = inject(MatDialog);

  openCreateDialog(): void {
    this.dialog.open(CreateDocumentDialogComponent, { width: '720px', maxWidth: '92vw' });
  }

  readonly displayedColumns = ['title', 'status', 'owner', 'recipients', 'createdAt', 'actions'];
  readonly statusOptions = Object.entries(STATUS_COLORS).map(([value, color]) => ({
    value,
    color,
    label: this.translate.instant(`documents.list.status.${value}`)
  }));

  readonly searchControl = new FormControl('');
  readonly statusControl = new FormControl<string[]>([]);
  readonly showHistory = signal(false);
  readonly requestTypeControl = new FormControl('');

  readonly loading = signal(true);
  readonly documents = signal<DocumentSummary[]>([]);
  readonly totalCount = signal(0);
  readonly requestTypes = signal<RequestType[]>([]);

  readonly requestsLoading = signal(true);
  readonly received = signal<ReceivedRequest[]>([]);
  readonly awaitingMe = computed(() => this.received().filter(r => r.isMyTurn));
  readonly otherReceived = computed(() => this.received().filter(r => !r.isMyTurn));

  private static readonly RECEIVED_STATUS_LABEL_KEYS: Record<string, string> = {
    Pending: 'pending', InProgress: 'inProgress', Completed: 'completed', Rejected: 'rejected',
    Expired: 'expired', Cancelled: 'cancelled', WaitingForOthers: 'waitingForOthers', Skipped: 'skipped',
    Delegated: 'delegated', Escalated: 'escalated', ChangesRequested: 'changesRequested'
  };
  private static readonly ACTION_LABEL_KEYS: Record<string, string> = {
    Signature: 'requests.signNow', Approval: 'requests.approveNow',
    Review: 'requests.reviewNow', ViewOnly: 'requests.viewNow', Stamp: 'requests.signNow'
  };
  private static readonly ACTION_ICONS: Record<string, string> = {
    Signature: 'draw', Approval: 'fact_check', Review: 'rate_review', ViewOnly: 'visibility', Stamp: 'draw'
  };

  /** A participant who never got to act on a contract that ended without them shows the contract's outcome, not "pending". */
  effectiveStatus(r: ReceivedRequest): string {
    const open = r.myStatus === 'Pending' || r.myStatus === 'InProgress';
    return open && ['Rejected', 'Cancelled', 'Expired'].includes(r.documentStatus) ? r.documentStatus : r.myStatus;
  }

  receivedStatusLabel(status: string): string {
    const key = DocumentsListComponent.RECEIVED_STATUS_LABEL_KEYS[status];
    return key ? this.translate.instant(`requests.status.${key}`) : status;
  }
  receivedStatusColor(status: string): string { return RECEIVED_STATUS_COLORS[status] ?? '#64748b'; }
  receivedStatusBg(status: string): string { return `${this.receivedStatusColor(status)}20`; }
  receivedStatusIcon(status: string): string {
    switch (status) {
      case 'Completed': return 'check_circle';
      case 'Rejected': return 'cancel';
      case 'InProgress': return 'hourglass_top';
      case 'Expired': return 'schedule';
      case 'Skipped': return 'skip_next';
      case 'Delegated': return 'forward';
      case 'ChangesRequested': return 'published_with_changes';
      default: return 'radio_button_unchecked';
    }
  }

  /** The job this participant was given on the contract: sign, approve, review… */
  taskLabel(r: ReceivedRequest): string {
    return r.roleLabel || this.translate.instant(`documents.detail.roles.${r.approvalType}`);
  }

  /** The contract was declined, so nothing is expected from anyone any more. */
  isDeclined(r: ReceivedRequest): boolean {
    return r.documentStatus === 'Rejected';
  }

  /** One line saying where the participant stands: done, nothing to do, or waiting for others. */
  taskNote(r: ReceivedRequest): string {
    let key: string;
    if (r.documentStatus === 'Rejected') key = r.myStatus === 'Rejected' ? 'youDeclined' : 'declinedNothingToDo';
    else if (r.documentStatus === 'Cancelled') key = 'cancelledNothingToDo';
    else if (r.documentStatus === 'Expired') key = 'expiredNothingToDo';
    else if (r.myStatus === 'Completed') key = 'youDone';
    else if (r.documentStatus === 'Completed') key = 'contractDone';
    else if (r.documentStatus === 'ChangesRequested') key = 'paused';
    else key = 'waitingTurn';
    return this.translate.instant(`requests.taskNote.${key}`);
  }

  actionLabel(r: ReceivedRequest): string {
    return DocumentsListComponent.ACTION_LABEL_KEYS[r.approvalType] ?? 'requests.signNow';
  }
  actionIcon(r: ReceivedRequest): string {
    return DocumentsListComponent.ACTION_ICONS[r.approvalType] ?? 'draw';
  }

  remaining(expiresAt?: string): RemainingTime | null {
    return remainingTime(expiresAt);
  }

  /** Goes straight to the screen that shows the document itself, with the approve / sign / decline controls. */
  openSigning(r: ReceivedRequest): void {
    if (r.secureToken && r.isMyTurn) this.router.navigate(['/sign', r.secureToken]);
    else this.openContract(r);
  }

  /** The contract page: details, history and analysis. */
  openContract(r: ReceivedRequest): void {
    this.router.navigate(['/documents', r.documentId]);
  }

  private loadReceived(): void {
    this.requestsLoading.set(true);
    this.documentService.getMyRequests().subscribe({
      next: data => {
        this.received.set(data.received);
        this.requestsLoading.set(false);
      },
      error: () => this.requestsLoading.set(false)
    });
  }

  pageSize = 25;
  private pageNumber = 1;

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
    if (queryStatus) this.statusControl.setValue(queryStatus.split(',').filter(Boolean));

    this.requestTypeService.getActive().subscribe({
      next: types => this.requestTypes.set(types),
      error: () => {}
    });

    this.load();
    this.loadReceived();

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
      statuses: (this.statusControl.value ?? []).join(',') || undefined,
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
    this.confirmService.confirm({
      message: this.translate.instant('documents.list.confirmDelete', { title: doc.title }),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true
    }).subscribe(ok => {
      if (ok) this.documentService.delete(doc.id).subscribe(() => this.load());
    });
  }
}
