import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { RequestType, CreateRequestTypeRequest, UpdateRequestTypeRequest, SlaMetrics } from '../models';

@Injectable({ providedIn: 'root' })
export class RequestTypeService {
  private readonly api = inject(ApiService);

  // ---- Admin management ----
  getAll(): Observable<RequestType[]> {
    return this.api.get<RequestType[]>('/v1/request-types');
  }

  create(request: CreateRequestTypeRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/request-types', request);
  }

  update(id: string, request: UpdateRequestTypeRequest): Observable<void> {
    return this.api.put<void>(`/v1/request-types/${id}`, request);
  }

  setStatus(id: string, isActive: boolean): Observable<void> {
    return this.api.patch<void>(`/v1/request-types/${id}/status`, { isActive });
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/request-types/${id}`);
  }

  // ---- Available to all authenticated users ----
  getActive(): Observable<RequestType[]> {
    return this.api.get<RequestType[]>('/v1/request-types/active');
  }

  getSlaMetrics(): Observable<SlaMetrics> {
    return this.api.get<SlaMetrics>('/v1/documents/sla-metrics');
  }
}
