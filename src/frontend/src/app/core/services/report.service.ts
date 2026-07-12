import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { AdminReport } from '../models';

@Injectable({ providedIn: 'root' })
export class ReportService {
  private readonly api = inject(ApiService);

  /** Organization-wide overview — server enforces Administrator/Manager roles. */
  getOverview(): Observable<AdminReport> {
    return this.api.get<AdminReport>('/v1/reports/overview');
  }
}
