import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  DocumentDetail, DocumentSummary, DashboardData,
  FieldDto, RecipientInputDto, PagedResult, MyRequests, WorkflowType, AiSummaryDto,
  ContractAnalysisResponse, ComplianceAnalysisResponse,
  Readiness, ReadinessItemStatus, ContractTermUpdate
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

  /** `force` acknowledges a "needs work" pre-flight verdict; a blocked contract is never forceable. */
  send(id: string, force = false): Observable<void> {
    return this.api.post<void>(`/v1/documents/${id}/send`, { force });
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

  updateObligation(id: string, dueDate: string, obligationType: string): Observable<void> {
    return this.api.patch<void>(`/v1/documents/${id}/obligation`, { dueDate, obligationType });
  }

  /** Records the negotiated term; re-arms the lifecycle alert ladder against the new dates. */
  updateContractTerm(id: string, term: ContractTermUpdate): Observable<void> {
    return this.api.patch<void>(`/v1/documents/${id}/term`, term);
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

  /** Runs the full model-backed legal review — takes tens of seconds, so this is user-triggered. */
  analyzeContract(id: string): Observable<ContractAnalysisResponse> {
    return this.api.post<ContractAnalysisResponse>(`/v1/documents/${id}/analyze`, {});
  }

  /** Checks the contract against the published regulatory watch registry — user-triggered. */
  analyzeCompliance(id: string): Observable<ComplianceAnalysisResponse> {
    return this.api.post<ComplianceAnalysisResponse>(`/v1/documents/${id}/compliance`, {});
  }

  /**
   * Pre-flight review: runs the legal review and the regulatory check, then grades whether the
   * contract is fit to send. Slow — it is the full model review plus the compliance scan.
   */
  evaluateReadiness(id: string): Observable<Readiness> {
    return this.api.post<Readiness>(`/v1/documents/${id}/readiness`, {});
  }

  /** Reads the stored verdict and to-do list without re-running any analysis. */
  getReadiness(id: string): Observable<Readiness> {
    return this.api.get<Readiness>(`/v1/documents/${id}/readiness`);
  }

  /** Ticks a to-do off or dismisses it; the API re-grades and returns the new verdict. */
  setReadinessItemStatus(id: string, itemId: string, status: ReadinessItemStatus, note?: string): Observable<Readiness> {
    return this.api.patch<Readiness>(`/v1/documents/${id}/readiness/items/${itemId}`, { status, note });
  }
}
