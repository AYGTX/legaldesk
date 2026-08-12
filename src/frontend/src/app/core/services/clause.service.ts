import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { Clause, ClauseCategory, ClauseInput, ClauseStatus } from '../models';

@Injectable({ providedIn: 'root' })
export class ClauseService {
  private readonly api = inject(ApiService);

  /** Full library including drafts — administrators and managers only. */
  getAll(filters?: {
    search?: string;
    category?: ClauseCategory;
    language?: string;
    status?: ClauseStatus;
  }): Observable<Clause[]> {
    return this.api.get<Clause[]>('/v1/clauses', filters as Record<string, unknown>);
  }

  /** Approved clauses offered when drafting a contract. */
  getApproved(filters?: {
    search?: string;
    category?: ClauseCategory;
    language?: string;
    requestTypeId?: string;
  }): Observable<Clause[]> {
    return this.api.get<Clause[]>('/v1/clauses/approved', filters as Record<string, unknown>);
  }

  create(input: ClauseInput): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/clauses', input);
  }

  update(id: string, input: ClauseInput): Observable<void> {
    return this.api.put<void>(`/v1/clauses/${id}`, input);
  }

  setStatus(id: string, status: ClauseStatus): Observable<void> {
    return this.api.patch<void>(`/v1/clauses/${id}/status`, { status });
  }

  recordUsage(id: string): Observable<void> {
    return this.api.post<void>(`/v1/clauses/${id}/usage`, {});
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/clauses/${id}`);
  }
}
