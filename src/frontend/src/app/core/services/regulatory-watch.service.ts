import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  RegulatoryCategory,
  RegulatoryJurisdiction,
  RegulatoryText,
  RegulatoryTextInput,
  RegulatoryTextStatus
} from '../models';

@Injectable({ providedIn: 'root' })
export class RegulatoryWatchService {
  private readonly api = inject(ApiService);

  /** Full registry including drafts — administrators and managers only. */
  getAll(filters?: {
    search?: string;
    category?: RegulatoryCategory;
    jurisdiction?: RegulatoryJurisdiction;
    language?: string;
    status?: RegulatoryTextStatus;
  }): Observable<RegulatoryText[]> {
    return this.api.get<RegulatoryText[]>('/v1/regulatory-texts', filters as Record<string, unknown>);
  }

  /** Published regulatory texts. */
  getPublished(filters?: {
    search?: string;
    category?: RegulatoryCategory;
    jurisdiction?: RegulatoryJurisdiction;
    language?: string;
    requestTypeId?: string;
  }): Observable<RegulatoryText[]> {
    return this.api.get<RegulatoryText[]>('/v1/regulatory-texts/published', filters as Record<string, unknown>);
  }

  create(input: RegulatoryTextInput): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/regulatory-texts', input);
  }

  update(id: string, input: RegulatoryTextInput): Observable<void> {
    return this.api.put<void>(`/v1/regulatory-texts/${id}`, input);
  }

  setStatus(id: string, status: RegulatoryTextStatus): Observable<void> {
    return this.api.patch<void>(`/v1/regulatory-texts/${id}/status`, { status });
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/regulatory-texts/${id}`);
  }
}
