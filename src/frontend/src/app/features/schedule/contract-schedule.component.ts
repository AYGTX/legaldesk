import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ContractScheduleRow } from '../../core/models/contract-lifecycle-ai.model';

type Filter = 'all' | 'urgent' | 'soon' | 'missing' | 'proposed';

/**
 * « Échéancier »: every contract with its end date and notice deadline, most urgent first. Dates read by Harvey at
 * upload are shown as proposed until someone confirms them on the contract; contracts with no dates are listed too.
 */
@Component({
  selector: 'sf-contract-schedule',
  standalone: true,
  imports: [CommonModule, RouterLink, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="sch-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'schedule.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'schedule.intro' | translate }}</p>
        </div>
      </div>

      <div class="sch-kpis">
        <div class="kpi"><span class="kpi-v c-red">{{ count('NoticeDeadlinePassed') }}</span><span>{{ 'schedule.kpi.noticePassed' | translate }}</span></div>
        <div class="kpi"><span class="kpi-v c-amber">{{ soonCount() }}</span><span>{{ 'schedule.kpi.soon' | translate }}</span></div>
        <div class="kpi"><span class="kpi-v">{{ count('InForce') }}</span><span>{{ 'schedule.kpi.inForce' | translate }}</span></div>
        <div class="kpi"><span class="kpi-v c-grey">{{ count('MissingDates') }}</span><span>{{ 'schedule.kpi.missing' | translate }}</span></div>
      </div>

      <div class="filter-pills">
        @for (f of filters; track f.value) {
          <button class="pill" [class.active]="filter() === f.value" (click)="filter.set(f.value)">
            {{ ('schedule.filter.' + f.value) | translate }}@if (f.value === 'all') { ({{ rows().length }}) }
          </button>
        }
      </div>

      @if (loading()) {
        <div class="sch-loading"><mat-spinner diameter="32"></mat-spinner></div>
      } @else if (shown().length === 0) {
        <div class="table-surface sch-empty"><mat-icon>event_available</mat-icon><p>{{ 'schedule.empty' | translate }}</p></div>
      } @else {
        <div class="table-surface">
          <table class="sch-table">
            <thead>
              <tr>
                <th>{{ 'schedule.col.contract' | translate }}</th>
                <th>{{ 'schedule.col.status' | translate }}</th>
                <th>{{ 'schedule.col.end' | translate }}</th>
                <th>{{ 'schedule.col.daysLeft' | translate }}</th>
                <th>{{ 'schedule.col.notice' | translate }}</th>
                <th>{{ 'schedule.col.renewal' | translate }}</th>
              </tr>
            </thead>
            <tbody>
              @for (r of shown(); track r.documentId) {
                <tr class="sch-row" [routerLink]="['/contracts', r.documentId]">
                  <td class="sch-title">
                    {{ r.title }}
                    @if (r.requestTypeName) { <span class="sch-type">{{ r.requestTypeName }}</span> }
                  </td>
                  <td>
                    <span class="tag" [ngClass]="'tag-' + r.stage">{{ ('schedule.stage.' + r.stage) | translate }}</span>
                    @if (r.endDate && !r.termConfirmed) {
                      <span class="tag tag-proposed" [matTooltip]="'schedule.proposedHint' | translate">✦ {{ 'schedule.proposed' | translate }}</span>
                    }
                  </td>
                  <td>{{ r.endDate ? (r.endDate | date: 'dd/MM/yyyy') : '—' }}</td>
                  <td [class.c-red]="(r.daysToEnd ?? 999) < 0" [class.c-amber]="(r.daysToEnd ?? 999) >= 0 && (r.daysToEnd ?? 999) <= 30">
                    {{ r.daysToEnd == null ? '—' : r.daysToEnd }}
                  </td>
                  <td [class.c-red]="(r.daysToNoticeDeadline ?? 999) < 0">{{ r.noticeDeadline ? (r.noticeDeadline | date: 'dd/MM/yyyy') : '—' }}</td>
                  <td>
                    @if (r.autoRenews) { <mat-icon class="sch-renew" [matTooltip]="'schedule.autoRenews' | translate">autorenew</mat-icon> }
                    @else if (r.endDate) { <span class="sch-muted">{{ 'schedule.noRenewal' | translate }}</span> }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
  styles: [`
    .sch-page { }
    .sch-kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 16px; }
    .kpi { background: var(--g-surface); border: 1px solid var(--g-border); border-radius: 12px; box-shadow: 0 1px 2px rgba(0,0,0,0.06); padding: 14px 16px; display: flex; flex-direction: column; gap: 2px; color: var(--g-text-3); font-size: 13px; }
    .kpi-v { font-size: 26px; font-weight: 700; color: var(--g-text); }
    .c-red { color: var(--g-red) !important; } .c-amber { color: #b45309 !important; } .c-grey { color: var(--g-text-3) !important; }

    .filter-pills { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px; }
    .pill {
      height: 36px; padding: 0 16px; border-radius: 18px;
      border: 1px solid var(--g-border); background: var(--g-surface);
      font-size: 13px; font-weight: 500; color: var(--g-text-2);
      cursor: pointer; white-space: nowrap; font-family: inherit;
      transition: background 0.12s, border-color 0.12s, color 0.12s;
    }
    .pill:hover { background: var(--g-hover); border-color: var(--g-text-muted); }
    .pill.active { background: var(--g-nav-active-bg); border-color: var(--g-blue); color: var(--g-nav-active-text); font-weight: 600; }

    .sch-loading { display: flex; justify-content: center; padding: 40px; }
    .table-surface { background: var(--g-surface); border: 1px solid var(--g-border); border-radius: 12px; overflow-x: auto; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .sch-empty { display: flex; flex-direction: column; align-items: center; padding: 56px 24px; color: var(--g-text-muted); }
    .sch-table { width: 100%; border-collapse: collapse; font-size: 13px; color: var(--g-text); }
    .sch-table th { text-align: left; font-size: 11px; font-weight: 600; color: var(--g-text-3); text-transform: uppercase; letter-spacing: 0.06em; padding: 14px 16px; background: var(--g-surface-variant); border-bottom: 1px solid var(--g-border); white-space: nowrap; }
    .sch-table td { padding: 12px 16px; border-bottom: 1px solid var(--g-border-light); vertical-align: middle; }
    .sch-table tbody tr:last-child td { border-bottom: none; }
    .sch-row { cursor: pointer; transition: background 0.1s; }
    .sch-row:hover { background: var(--g-hover); }
    .sch-title { font-weight: 500; color: var(--g-text); }
    .sch-type { display: block; font-size: 11px; color: var(--g-blue); font-weight: 600; }
    .tag { display: inline-block; font-size: 11px; font-weight: 600; border-radius: 99px; padding: 3px 10px; margin: 2px 4px 2px 0; white-space: nowrap; }
    .tag-NoticeDeadlinePassed, .tag-Expired { background: #fee2e2; color: #991b1b; }
    .tag-ExpiringSoon { background: #fef3c7; color: #92400e; }
    .tag-InForce { background: #e6f4ea; color: #1e6b2e; }
    .tag-NotStarted, .tag-Unknown { background: #e0e7ff; color: #3730a3; }
    .tag-MissingDates { background: var(--g-hover); color: var(--g-text-3); }
    .tag-proposed { background: #ede9fe; color: #6750a4; }
    .sch-renew { color: #6750a4; font-size: 20px; width: 20px; height: 20px; }
    .sch-muted { color: var(--g-text-muted); font-size: 12px; }
    @media (max-width: 720px) { .sch-kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `]
})
export class ContractScheduleComponent implements OnInit {
  private readonly documents = inject(DocumentService);

  readonly rows = signal<ContractScheduleRow[]>([]);
  readonly loading = signal(true);
  readonly filter = signal<Filter>('all');
  readonly filters: { value: Filter }[] = [{ value: 'all' }, { value: 'urgent' }, { value: 'soon' }, { value: 'proposed' }, { value: 'missing' }];

  readonly soonCount = computed(() => this.rows().filter(r => this.isSoon(r)).length);

  readonly shown = computed(() => {
    const rows = this.rows();
    switch (this.filter()) {
      case 'urgent': return rows.filter(r => r.stage === 'NoticeDeadlinePassed' || (r.daysToNoticeDeadline != null && r.daysToNoticeDeadline >= 0 && r.daysToNoticeDeadline <= 30));
      case 'soon': return rows.filter(r => this.isSoon(r));
      case 'missing': return rows.filter(r => r.stage === 'MissingDates');
      case 'proposed': return rows.filter(r => r.endDate && !r.termConfirmed);
      default: return rows;
    }
  });

  ngOnInit(): void {
    this.documents.getSchedule().subscribe({
      next: rows => { this.rows.set(rows); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  count(stage: string): number {
    return this.rows().filter(r => r.stage === stage).length;
  }

  /** Ends within 90 days and is not over yet. */
  private isSoon(r: ContractScheduleRow): boolean {
    return r.daysToEnd != null && r.daysToEnd >= 0 && r.daysToEnd <= 90;
  }
}
