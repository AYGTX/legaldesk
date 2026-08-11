import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  DocumentDetail, DocumentSummary, DashboardData,
  FieldDto, RecipientInputDto, PagedResult, MyRequests, WorkflowType, AiSummaryDto
} from '../models';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly api = inject(ApiService);

  getDashboard(): Observable<DashboardData> {
    return this.api.get<DashboardData>('/v1/documents/dashboard');
  }

  getMyRequests(): Observable<MyRequests> {
    return this.api.get<MyRequests>('/v1/requests/mine');
  }

  getDocuments(params?: {
    pageNumber?: number;
    pageSize?: number;
    search?: string;
    status?: string;
    sortBy?: string;
    descending?: boolean;
    requestTypeId?: string;
  }): Observable<PagedResult<DocumentSummary>> {
    return this.api.get<PagedResult<DocumentSummary>>('/v1/documents', params as Record<string, unknown>);
  }

  getDocument(id: string): Observable<DocumentDetail> {
    return this.api.get<DocumentDetail>(`/v1/documents/${id}`);
  }

  upload(formData: FormData): Observable<{ documentId: string; title: string; pageCount: number }> {
    return this.api.upload('/v1/documents', formData);
  }

  send(id: string): Observable<void> {
    return this.api.post<void>(`/v1/documents/${id}/send`);
  }

  /** Owner response to a change request: swaps in a revised PDF and returns the document to Draft. */
  uploadRevision(id: string, file: File, changeNote?: string): Observable<{ documentId: string; version: number; pageCount: number }> {
    const formData = new FormData();
    formData.append('file', file);
    if (changeNote?.trim()) formData.append('changeNote', changeNote.trim());
    return this.api.upload(`/v1/documents/${id}/revision`, formData);
  }

  /** Admin: reopen an expired request, restarting its SLA clock. */
  reopen(id: string, slaHours?: number): Observable<void> {
    return this.api.post<void>(`/v1/documents/${id}/reopen`, { slaHours: slaHours ?? null });
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/documents/${id}`);
  }

  download(id: string): Observable<Blob> {
    return this.api.download(`/v1/documents/${id}/download`);
  }

  /** Streams the original PDF inline as a Blob through the authenticated HTTP client (JWT attached). */
  stream(id: string): Observable<Blob> {
    return this.api.download(`/v1/documents/${id}/stream`);
  }

  saveFields(documentId: string, fields: FieldDto[]): Observable<void> {
    return this.api.put<void>(`/v1/documents/${documentId}/fields`, fields);
  }

  saveRecipients(documentId: string, workflowType: WorkflowType, recipients: RecipientInputDto[], hideRecipientIdentities = false): Observable<void> {
    return this.api.put<void>(`/v1/documents/${documentId}/recipients`, { workflowType, recipients, hideRecipientIdentities });
  }

  /** Owner: define the review/approval checklist for one recipient (draft documents only). */
  setChecklist(documentId: string, recipientId: string, items: string[]): Observable<void> {
    return this.api.put<void>(`/v1/documents/${documentId}/recipients/${recipientId}/checklist`, { items });
  }

  getAuditTrail(documentId: string, pageNumber = 1, pageSize = 50): Observable<PagedResult<unknown>> {
    return this.api.get(`/v1/documents/${documentId}/audit`, { pageNumber, pageSize });
  }

  exportAuditTrail(documentId: string): Observable<Blob> {
    return this.api.download(`/v1/documents/${documentId}/audit/export`);
  }

  getDocumentSummary(id: string): Observable<AiSummaryDto> {
    return this.api.get<AiSummaryDto>(`/v1/documents/${id}/summary`);
  }
}
