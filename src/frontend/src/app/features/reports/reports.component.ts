import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ReportService } from '../../core/services/report.service';
import { AdminReport, ContractLifecycle, MonthlyVolume, RiskMap } from '../../core/models';

/** Organization-wide analytics for administrators and managers (server enforces the roles). */
@Component({
  selector: 'sf-reports',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="reports-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'reports.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'reports.subtitle' | translate }}</p>
        </div>
      </div>

      @if (loading()) {
        <div class="loading-state"><mat-spinner diameter="48"></mat-spinner></div>
      } @else if (report(); as r) {

        <!-- KPI tiles -->
        <div class="kpi-grid">
          <div class="kpi"><span class="kpi-value">{{ r.totalRequests }}</span><span class="kpi-label">{{ 'reports.kpi.totalRequests' | translate }}</span></div>
          <div class="kpi"><span class="kpi-value c-blue">{{ r.activeRequests }}</span><span class="kpi-label">{{ 'reports.kpi.active' | translate }}</span></div>
          <div class="kpi"><span class="kpi-value c-green">{{ r.completionRatePercent }}%</span><span class="kpi-label">{{ 'reports.kpi.completionRate' | translate }}</span></div>
          <div class="kpi"><span class="kpi-value">{{ r.avgCompletionHours }}h</span><span class="kpi-label">{{ 'reports.kpi.avgCompletion' | translate }}</span></div>
          <div class="kpi" [matTooltip]="'reports.kpi.slaTooltip' | translate: { count: r.slaTrackedCompleted }">
            <span class="kpi-value" [class.c-green]="r.slaCompliancePercent >= 90" [class.c-amber]="r.slaCompliancePercent >= 70 && r.slaCompliancePercent < 90" [class.c-red]="r.slaCompliancePercent < 70">{{ r.slaCompliancePercent }}%</span>
            <span class="kpi-label">{{ 'reports.kpi.slaCompliance' | translate }}</span>
          </div>
          <div class="kpi"><span class="kpi-value c-red">{{ r.rejectedRequests }}</span><span class="kpi-label">{{ 'reports.kpi.rejected' | translate }}</span></div>
          <div class="kpi"><span class="kpi-value c-orange">{{ r.expiredRequests }}</span><span class="kpi-label">{{ 'reports.kpi.expired' | translate }}</span></div>
          <div class="kpi"><span class="kpi-value c-amber">{{ r.changesRequestedNow }}</span><span class="kpi-label">{{ 'reports.kpi.pausedForChanges' | translate }}</span></div>
        </div>

        <div class="report-grid">
          <!-- Monthly volume -->
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'reports.volume.title' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="volume-chart">
                @for (m of r.monthlyVolume; track m.year + '-' + m.month) {
                  <div class="volume-month">
                    <div class="volume-bars">
                      <div class="vbar created" [style.height.px]="barHeight(m.created)"
                           [matTooltip]="('reports.volume.created' | translate) + ': ' + m.created"></div>
                      <div class="vbar completed" [style.height.px]="barHeight(m.completed)"
                           [matTooltip]="('reports.volume.completed' | translate) + ': ' + m.completed"></div>
                    </div>
                    <span class="volume-label">{{ monthLabel(m) }}</span>
                  </div>
                }
              </div>
              <div class="volume-legend">
                <span class="legend-item"><span class="dot created"></span>{{ 'reports.volume.created' | translate }}</span>
                <span class="legend-item"><span class="dot completed"></span>{{ 'reports.volume.completed' | translate }}</span>
              </div>
            </mat-card-content>
          </mat-card>

          <!-- Revision loop -->
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'reports.revisions.title' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="rev-row"><mat-icon>published_with_changes</mat-icon><span class="rev-value">{{ r.totalChangeRequests }}</span><span class="rev-label">{{ 'reports.revisions.changeRequests' | translate }}</span></div>
              <div class="rev-row"><mat-icon>upload_file</mat-icon><span class="rev-value">{{ r.totalRevisionsUploaded }}</span><span class="rev-label">{{ 'reports.revisions.revisionsUploaded' | translate }}</span></div>
              <div class="rev-row"><mat-icon>history</mat-icon><span class="rev-value">{{ r.documentsWithRevisions }}</span><span class="rev-label">{{ 'reports.revisions.documentsRevised' | translate }}</span></div>
              <p class="rev-hint">{{ 'reports.revisions.hint' | translate }}</p>
            </mat-card-content>
          </mat-card>
        </div>

        <!-- By request type -->
        <mat-card class="table-card">
          <mat-card-header><mat-card-title>{{ 'reports.byType.title' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            @if (r.byRequestType.length === 0) {
              <p class="empty-note">{{ 'reports.byType.empty' | translate }}</p>
            } @else {
              <table class="report-table">
                <thead>
                  <tr>
                    <th>{{ 'reports.byType.type' | translate }}</th>
                    <th class="num">{{ 'reports.table.requests' | translate }}</th>
                    <th class="num">{{ 'reports.table.completed' | translate }}</th>
                    <th class="num">{{ 'reports.table.avgHours' | translate }}</th>
                    <th class="num">{{ 'reports.table.sla' | translate }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (t of r.byRequestType; track t.name) {
                    <tr>
                      <td><span class="type-cell"><mat-icon [style.color]="t.color || '#1a73e8'">{{ t.icon || 'category' }}</mat-icon>{{ t.name }}</span></td>
                      <td class="num">{{ t.count }}</td>
                      <td class="num">{{ t.completed }}</td>
                      <td class="num">{{ t.avgCompletionHours }}h</td>
                      <td class="num">{{ t.slaCompliancePercent }}%</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </mat-card-content>
        </mat-card>

        <!-- By department -->
        <mat-card class="table-card">
          <mat-card-header><mat-card-title>{{ 'reports.byDepartment.title' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            @if (r.byDepartment.length === 0) {
              <p class="empty-note">{{ 'reports.byDepartment.empty' | translate }}</p>
            } @else {
              <table class="report-table">
                <thead>
                  <tr>
                    <th>{{ 'reports.byDepartment.department' | translate }}</th>
                    <th class="num">{{ 'reports.table.requests' | translate }}</th>
                    <th class="num">{{ 'reports.table.completed' | translate }}</th>
                    <th class="num">{{ 'reports.table.avgHours' | translate }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (d of r.byDepartment; track d.department) {
                    <tr>
                      <td>{{ d.department }}</td>
                      <td class="num">{{ d.count }}</td>
                      <td class="num">{{ d.completed }}</td>
                      <td class="num">{{ d.avgCompletionHours }}h</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </mat-card-content>
        </mat-card>

        <!-- Legal risk map -->
        <mat-card class="table-card">
          <mat-card-header>
            <mat-card-title>{{ 'reports.riskMap.title' | translate }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <p class="risk-map-hint">{{ 'reports.riskMap.hint' | translate }}</p>

            @if (loadingRiskMap()) {
              <div class="loading-state"><mat-spinner diameter="32"></mat-spinner></div>
            } @else if (riskMap(); as rm) {
              @if (rm.totalAnalyzed === 0) {
                <p class="empty-note">{{ 'reports.riskMap.empty' | translate }}</p>
              } @else {
                <div class="risk-map-kpis">
                  <div class="kpi"><span class="kpi-value">{{ rm.totalAnalyzed }}</span><span class="kpi-label">{{ 'reports.riskMap.analyzed' | translate }}</span></div>
                  <div class="kpi"><span class="kpi-value" [class.c-green]="rm.avgRiskScore < 25" [class.c-amber]="rm.avgRiskScore >= 25 && rm.avgRiskScore < 50" [class.c-red]="rm.avgRiskScore >= 50">{{ rm.avgRiskScore }}</span><span class="kpi-label">{{ 'reports.riskMap.avgScore' | translate }}</span></div>
                </div>

                <div class="risk-map-grid">
                  <div class="risk-bar-block">
                    <h4>{{ 'reports.riskMap.bySeverity' | translate }}</h4>
                    @for (s of rm.bySeverity; track s.severity) {
                      <div class="risk-bar-row">
                        <span class="risk-bar-label" [ngClass]="'severity-' + s.severity.toLowerCase()">{{ s.severity }}</span>
                        <div class="risk-bar-track"><div class="risk-bar-fill" [ngClass]="'severity-' + s.severity.toLowerCase()" [style.width.%]="barPercent(s.count, maxSeverityCount())"></div></div>
                        <span class="risk-bar-count">{{ s.count }}</span>
                      </div>
                    }
                  </div>

                  <div class="risk-bar-block">
                    <h4>{{ 'reports.riskMap.topClauseTypes' | translate }}</h4>
                    @for (c of rm.topClauseTypes; track c.clauseType) {
                      <div class="risk-bar-row">
                        <span class="risk-bar-label">{{ c.clauseType }}</span>
                        <div class="risk-bar-track"><div class="risk-bar-fill neutral" [style.width.%]="barPercent(c.count, maxClauseCount())"></div></div>
                        <span class="risk-bar-count">{{ c.count }}</span>
                      </div>
                    }
                  </div>
                </div>

                @if (rm.byRequestType.length > 0) {
                  <table class="report-table risk-map-table">
                    <thead>
                      <tr>
                        <th>{{ 'reports.byType.type' | translate }}</th>
                        <th class="num">{{ 'reports.table.requests' | translate }}</th>
                        <th class="num">{{ 'reports.riskMap.avgScore' | translate }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (t of rm.byRequestType; track t.requestTypeName) {
                        <tr>
                          <td>{{ t.requestTypeName }}</td>
                          <td class="num">{{ t.count }}</td>
                          <td class="num">{{ t.avgRiskScore }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                }
              }
            }
          </mat-card-content>
        </mat-card>

        <!-- Contract lifecycle -->
        <mat-card class="table-card">
          <mat-card-header>
            <mat-card-title>{{ 'reports.lifecycle.title' | translate }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <p class="risk-map-hint">{{ 'reports.lifecycle.hint' | translate }}</p>

            @if (loadingLifecycle()) {
              <div class="loading-state"><mat-spinner diameter="32"></mat-spinner></div>
            } @else if (lifecycle(); as lc) {
              @if (lc.totalTracked === 0) {
                <p class="empty-note">{{ 'reports.lifecycle.empty' | translate }}</p>
              } @else {
                <div class="risk-map-kpis">
                  <div class="kpi"><span class="kpi-value">{{ lc.totalTracked }}</span><span class="kpi-label">{{ 'reports.lifecycle.tracked' | translate }}</span></div>
                  <div class="kpi"><span class="kpi-value" [class.c-amber]="lc.expiringIn30Days > 0">{{ lc.expiringIn30Days }}</span><span class="kpi-label">{{ 'reports.lifecycle.in30' | translate }}</span></div>
                  <div class="kpi"><span class="kpi-value">{{ lc.expiringIn90Days }}</span><span class="kpi-label">{{ 'reports.lifecycle.in90' | translate }}</span></div>
                  <div class="kpi"><span class="kpi-value" [class.c-red]="lc.noticeDeadlinePassed > 0">{{ lc.noticeDeadlinePassed }}</span><span class="kpi-label">{{ 'reports.lifecycle.noticePassed' | translate }}</span></div>
                </div>

                <div class="risk-map-grid">
                  <div class="risk-bar-block">
                    <h4>{{ 'reports.lifecycle.byStage' | translate }}</h4>
                    @for (s of lc.byStage; track s.stage) {
                      <div class="risk-bar-row">
                        <span class="risk-bar-label">{{ ('documents.detail.contractTerm.stage.' + s.stage) | translate }}</span>
                        <div class="risk-bar-track"><div class="risk-bar-fill" [ngClass]="'stage-' + s.stage.toLowerCase()" [style.width.%]="barPercent(s.count, maxStageCount())"></div></div>
                        <span class="risk-bar-count">{{ s.count }}</span>
                      </div>
                    }
                  </div>

                  <div class="risk-bar-block">
                    <h4>{{ 'reports.lifecycle.byFamily' | translate }}</h4>
                    @for (f of lc.byRequestType; track f.requestTypeName) {
                      <div class="risk-bar-row">
                        <span class="risk-bar-label">{{ f.requestTypeName }}</span>
                        <div class="risk-bar-track"><div class="risk-bar-fill neutral" [style.width.%]="barPercent(f.expiringIn90Days, maxFamilyExpiring())"></div></div>
                        <span class="risk-bar-count">{{ f.expiringIn90Days }}/{{ f.count }}</span>
                      </div>
                    }
                  </div>
                </div>

                @if (lc.upcoming.length > 0) {
                  <table class="report-table risk-map-table">
                    <thead>
                      <tr>
                        <th>{{ 'reports.lifecycle.contract' | translate }}</th>
                        <th>{{ 'reports.lifecycle.endDate' | translate }}</th>
                        <th>{{ 'reports.lifecycle.noticeDeadline' | translate }}</th>
                        <th class="num">{{ 'reports.lifecycle.daysLeft' | translate }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (c of lc.upcoming; track c.documentId) {
                        <tr>
                          <td>
                            <a [routerLink]="['/documents', c.documentId]">{{ c.title }}</a>
                            @if (c.autoRenews) {
                              <mat-icon class="renew-hint" [matTooltip]="'documents.detail.contractTerm.autoRenewsHint' | translate">autorenew</mat-icon>
                            }
                          </td>
                          <td>{{ c.endDate | date:'MMM d, y' }}</td>
                          <td [class.c-red]="c.stage === 'NoticeDeadlinePassed'">
                            {{ c.noticeDeadline ? (c.noticeDeadline | date:'MMM d, y') : '—' }}
                          </td>
                          <td class="num">{{ c.daysToEnd }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                }
              }
            }
          </mat-card-content>
        </mat-card>

      } @else if (error()) {
        <div class="error-state">
          <mat-icon>lock</mat-icon>
          <p>{{ error() }}</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .reports-page { padding: 0; }
    .page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 24px; }
    .page-title { font-size: 22px; font-weight: 500; color: var(--g-text); margin: 0 0 4px; letter-spacing: -0.2px; }
    .page-subtitle { color: var(--g-text-3); margin: 0; font-size: 14px; }
    .loading-state { display: flex; justify-content: center; padding: 80px; }
    .error-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 80px; color: var(--g-text-3); }

    .kpi-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px; }
    @media (max-width: 860px) { .kpi-grid { grid-template-columns: repeat(2, 1fr); } }
    .kpi { background: var(--g-surface); border: 1px solid var(--g-border); border-radius: 12px; padding: 20px; display: flex; flex-direction: column; gap: 4px; }
    .kpi-value { font-size: 26px; font-weight: 600; color: var(--g-text); }
    .kpi-label { font-size: 13px; color: var(--g-text-3); }
    .c-blue { color: var(--g-blue); } .c-green { color: var(--g-green); } .c-red { color: var(--g-red); }
    .c-orange { color: #f97316; } .c-amber { color: var(--g-yellow); }

    .report-grid { display: grid; grid-template-columns: 2fr 1fr; gap: 16px; margin-bottom: 16px; }
    @media (max-width: 800px) { .report-grid { grid-template-columns: 1fr; } }

    .volume-chart { display: flex; align-items: flex-end; gap: 12px; height: 160px; padding-top: 8px; }
    .volume-month { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 6px; justify-content: flex-end; height: 100%; }
    .volume-bars { display: flex; gap: 4px; align-items: flex-end; flex: 1; }
    .vbar { width: 18px; border-radius: 4px 4px 0 0; min-height: 2px; }
    .vbar.created { background: #aecbfa; }
    .vbar.completed { background: #34a853; }
    .volume-label { font-size: 11px; color: var(--g-text-3); }
    .volume-legend { display: flex; gap: 16px; margin-top: 10px; }
    .legend-item { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--g-text-3); }
    .legend-item .dot { width: 10px; height: 10px; border-radius: 3px; display: inline-block; }
    .dot.created { background: #aecbfa; } .dot.completed { background: #34a853; }

    .rev-row { display: flex; align-items: center; gap: 10px; padding: 8px 0; }
    .rev-row mat-icon { color: var(--g-yellow); }
    .rev-value { font-size: 20px; font-weight: 700; min-width: 36px; text-align: right; color: var(--g-text); }
    .rev-label { font-size: 13px; color: var(--g-text-3); }
    .rev-hint { font-size: 12px; color: var(--g-text-muted); margin: 10px 0 0; }

    .table-card { margin-bottom: 16px; }
    .report-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .report-table th { text-align: left; color: var(--g-text-3); font-weight: 500; padding: 8px; border-bottom: 1px solid var(--g-border-light); }
    .report-table td { padding: 8px; border-bottom: 1px solid var(--g-border-light); color: var(--g-text); }
    .report-table .num { text-align: right; }
    .type-cell { display: inline-flex; align-items: center; gap: 8px; }
    .type-cell mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .empty-note { color: var(--g-text-muted); font-size: 13px; margin: 8px 0; }

    .risk-map-hint { font-size: 12px; color: var(--g-text-muted); margin: 0 0 14px; }
    .risk-map-kpis { display: flex; gap: 16px; margin-bottom: 16px; }
    .risk-map-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; margin-bottom: 16px; }
    @media (max-width: 800px) { .risk-map-grid { grid-template-columns: 1fr; } }
    .risk-bar-block h4 { font-size: 13px; font-weight: 600; color: var(--g-text); margin: 0 0 10px; }
    .risk-bar-row { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .risk-bar-label { font-size: 12px; color: var(--g-text-3); min-width: 110px; flex-shrink: 0; }
    .risk-bar-track { flex: 1; height: 10px; background: var(--g-border-light); border-radius: 5px; overflow: hidden; }
    .risk-bar-fill { height: 100%; border-radius: 5px; }
    .risk-bar-fill.neutral { background: #1a73e8; }
    .risk-bar-fill.severity-critical { background: #7f1d1d; }
    .risk-bar-fill.severity-high { background: #dc2626; }
    .risk-bar-fill.severity-medium { background: #f59e0b; }
    .risk-bar-fill.severity-low { background: #22c55e; }
    .risk-bar-label.severity-critical { color: #7f1d1d; }
    .risk-bar-label.severity-high { color: #dc2626; }
    .risk-bar-label.severity-medium { color: #b45309; }
    .risk-bar-label.severity-low { color: #15803d; }
    .risk-bar-count { font-size: 12px; color: var(--g-text-3); min-width: 24px; text-align: right; }
    .risk-map-table { margin-top: 8px; }
    .risk-bar-fill.stage-expired, .risk-bar-fill.stage-noticedeadlinepassed { background: #dc2626; }
    .risk-bar-fill.stage-expiringsoon { background: #f59e0b; }
    .risk-bar-fill.stage-inforce { background: #16a34a; }
    .risk-bar-fill.stage-notstarted, .risk-bar-fill.stage-unknown { background: #9ca3af; }
    .renew-hint { font-size: 15px; width: 15px; height: 15px; color: #6750a4; vertical-align: middle; margin-left: 4px; cursor: help; }
  `]
})
export class ReportsComponent implements OnInit {
  private readonly reportService = inject(ReportService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly report = signal<AdminReport | null>(null);
  readonly error = signal<string | null>(null);

  readonly loadingRiskMap = signal(true);
  readonly riskMap = signal<RiskMap | null>(null);

  readonly loadingLifecycle = signal(true);
  readonly lifecycle = signal<ContractLifecycle | null>(null);

  private readonly maxMonthly = computed(() =>
    Math.max(1, ...(this.report()?.monthlyVolume.flatMap(m => [m.created, m.completed]) ?? [1])));

  readonly maxSeverityCount = computed(() =>
    Math.max(1, ...(this.riskMap()?.bySeverity.map(s => s.count) ?? [1])));

  readonly maxClauseCount = computed(() =>
    Math.max(1, ...(this.riskMap()?.topClauseTypes.map(c => c.count) ?? [1])));

  readonly maxStageCount = computed(() =>
    Math.max(1, ...(this.lifecycle()?.byStage.map(s => s.count) ?? [1])));

  readonly maxFamilyExpiring = computed(() =>
    Math.max(1, ...(this.lifecycle()?.byRequestType.map(f => f.expiringIn90Days) ?? [1])));

  barHeight(value: number): number {
    return Math.round((value / this.maxMonthly()) * 120);
  }

  barPercent(value: number, max: number): number {
    return Math.max(2, Math.round((value / max) * 100));
  }

  monthLabel(m: MonthlyVolume): string {
    return new Date(m.year, m.month - 1, 1)
      .toLocaleDateString(this.translate.currentLang() === 'fr' ? 'fr-FR' : 'en-US', { month: 'short' });
  }

  ngOnInit(): void {
    this.reportService.getOverview().subscribe({
      next: r => { this.report.set(r); this.loading.set(false); },
      error: err => {
        this.loading.set(false);
        this.error.set(err.status === 403
          ? this.translate.instant('reports.forbidden')
          : this.translate.instant('reports.loadFailed'));
      }
    });

    this.reportService.getRiskMap().subscribe({
      next: rm => { this.riskMap.set(rm); this.loadingRiskMap.set(false); },
      error: () => this.loadingRiskMap.set(false)
    });

    this.reportService.getContractLifecycle().subscribe({
      next: lc => { this.lifecycle.set(lc); this.loadingLifecycle.set(false); },
      error: () => this.loadingLifecycle.set(false)
    });
  }
}
