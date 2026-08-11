import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { AdminReport, OrgAuditLogEntry, PagedResult } from '../models';

@Injectable({ providedIn: 'root' })
export class ReportService {
  private readonly api = inject(ApiService);

  /** Organization-wide overview — server enforces Administrator/Manager roles. */
  getOverview(): Observable<AdminReport> {
    return this.api.get<AdminReport>('/v1/reports/overview');
  }

  /** Organization-wide audit log across all documents — server enforces Administrator/Manager roles. */
  getAuditLog(params?: { pageNumber?: number; pageSize?: number; search?: string; action?: string }): Observable<PagedResult<OrgAuditLogEntry>> {
    return this.api.get<PagedResult<OrgAuditLogEntry>>('/v1/reports/audit-log', params as Record<string, unknown>);
  }
}
