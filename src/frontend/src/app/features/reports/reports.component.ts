import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ReportService } from '../../core/services/report.service';
import { AdminReport, MonthlyVolume } from '../../core/models';

/** Organization-wide analytics for administrators and managers (server enforces the roles). */
@Component({
  selector: 'sf-reports',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="reports-page">
      <div class="page-header">
        <div>
          <h1>{{ 'reports.title' | translate }}</h1>
          <p class="subtitle">{{ 'reports.subtitle' | translate }}</p>
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

      } @else if (error()) {
        <div class="error-state">
          <mat-icon>lock</mat-icon>
          <p>{{ error() }}</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .reports-page { padding: 24px; max-width: 1100px; margin: 0 auto; }
    .page-header h1 { font-size: 24px; font-weight: 600; margin: 0; }
    .subtitle { color: #5f6368; margin: 4px 0 20px; font-size: 14px; }
    .loading-state { display: flex; justify-content: center; padding: 80px; }
    .error-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 80px; color: #5f6368; }

    .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 12px; margin-bottom: 20px; }
    .kpi { background: #fff; border: 1px solid #e8edf2; border-radius: 12px; padding: 14px; display: flex; flex-direction: column; gap: 2px; }
    .kpi-value { font-size: 22px; font-weight: 700; color: #202124; }
    .kpi-label { font-size: 12px; color: #5f6368; }
    .c-blue { color: #1a73e8; } .c-green { color: #188038; } .c-red { color: #d93025; }
    .c-orange { color: #f97316; } .c-amber { color: #d97706; }

    .report-grid { display: grid; grid-template-columns: 2fr 1fr; gap: 16px; margin-bottom: 16px; }
    @media (max-width: 800px) { .report-grid { grid-template-columns: 1fr; } }

    .volume-chart { display: flex; align-items: flex-end; gap: 12px; height: 160px; padding-top: 8px; }
    .volume-month { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 6px; justify-content: flex-end; height: 100%; }
    .volume-bars { display: flex; gap: 4px; align-items: flex-end; flex: 1; }
    .vbar { width: 18px; border-radius: 4px 4px 0 0; min-height: 2px; }
    .vbar.created { background: #aecbfa; }
    .vbar.completed { background: #34a853; }
    .volume-label { font-size: 11px; color: #5f6368; }
    .volume-legend { display: flex; gap: 16px; margin-top: 10px; }
    .legend-item { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: #5f6368; }
    .legend-item .dot { width: 10px; height: 10px; border-radius: 3px; display: inline-block; }
    .dot.created { background: #aecbfa; } .dot.completed { background: #34a853; }

    .rev-row { display: flex; align-items: center; gap: 10px; padding: 8px 0; }
    .rev-row mat-icon { color: #d97706; }
    .rev-value { font-size: 20px; font-weight: 700; min-width: 36px; text-align: right; }
    .rev-label { font-size: 13px; color: #5f6368; }
    .rev-hint { font-size: 12px; color: #80868b; margin: 10px 0 0; }

    .table-card { margin-bottom: 16px; }
    .report-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .report-table th { text-align: left; color: #5f6368; font-weight: 500; padding: 8px; border-bottom: 1px solid #e8edf2; }
    .report-table td { padding: 8px; border-bottom: 1px solid #f1f3f4; }
    .report-table .num { text-align: right; }
    .type-cell { display: inline-flex; align-items: center; gap: 8px; }
    .type-cell mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .empty-note { color: #80868b; font-size: 13px; margin: 8px 0; }
  `]
})
export class ReportsComponent implements OnInit {
  private readonly reportService = inject(ReportService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly report = signal<AdminReport | null>(null);
  readonly error = signal<string | null>(null);

  private readonly maxMonthly = computed(() =>
    Math.max(1, ...(this.report()?.monthlyVolume.flatMap(m => [m.created, m.completed]) ?? [1])));

  barHeight(value: number): number {
    return Math.round((value / this.maxMonthly()) * 120);
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
  }
}
