import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { Clause, ClauseCategory, ClauseDescription, ClauseInput, ClauseLanguage, ClauseStatus, LibraryClause } from '../models';

/** The clause library (M9): clauses keep every wording; an approved wording never changes. */
@Injectable({ providedIn: 'root' })
export class ClauseService {
  private readonly api = inject(ApiService);

  /** Full library with every wording, drafts included — administrators and managers only. */
  getAll(filters?: {
    search?: string;
    category?: ClauseCategory;
    language?: string;
    status?: ClauseStatus;
  }): Observable<LibraryClause[]> {
    return this.api.get<LibraryClause[]>('/v1/clauses', filters as Record<string, unknown>);
  }

  /** Approved wordings offered when drafting a contract. */
  getApproved(filters?: {
    search?: string;
    category?: ClauseCategory;
    language?: string;
    requestTypeId?: string;
  }): Observable<Clause[]> {
    return this.api.get<Clause[]>('/v1/clauses/approved', filters as Record<string, unknown>);
  }

  /** The organisation's approved wordings for a Harvey rule, the contract's language first (R-M9-3). */
  forRule(ruleKey: string, language?: string): Observable<Clause[]> {
    return this.api.get<Clause[]>(`/v1/clauses/for-rule/${encodeURIComponent(ruleKey)}`, language ? { language } : undefined);
  }

  create(input: ClauseInput): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/clauses', input);
  }

  describe(id: string, description: ClauseDescription): Observable<void> {
    return this.api.put<void>(`/v1/clauses/${id}`, description);
  }

  /** New wording: rewrites the open draft, or drafts the next version. */
  redraft(id: string, body: string, language: ClauseLanguage): Observable<{ number: number }> {
    return this.api.post<{ number: number }>(`/v1/clauses/${id}/versions`, { body, language });
  }

  approve(id: string, number: number): Observable<void> {
    return this.api.post<void>(`/v1/clauses/${id}/versions/${number}/approve`, {});
  }

  deprecate(id: string, number: number): Observable<void> {
    return this.api.post<void>(`/v1/clauses/${id}/versions/${number}/deprecate`, {});
  }

  linkRules(id: string, ruleKeys: string[]): Observable<void> {
    return this.api.put<void>(`/v1/clauses/${id}/rules`, { ruleKeys });
  }

  recordUsage(id: string): Observable<void> {
    return this.api.post<void>(`/v1/clauses/${id}/usage`, {});
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/clauses/${id}`);
  }
}
