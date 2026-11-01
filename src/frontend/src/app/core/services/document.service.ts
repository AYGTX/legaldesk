import { AskContractRequest, AskContractReply } from '../models/contract-chat.model';
import { ContractScheduleRow, KeyTerms, RenewalNote } from '../models/contract-lifecycle-ai.model';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  DocumentDetail, DocumentSummary, DashboardData,
  FieldDto, RecipientInputDto, PagedResult, MyRequests, WorkflowType, AiSummaryDto,
  Review, ReviewRun, ReviewItemStatus, ReviewRuleSetting, RiskSeverity, VersionComparison, TemplateRevisionForm, ContractRound
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
    statuses?: string;
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

  /** `reason` is required to send with open high-risk findings (kept in the audit trail); a blocked contract is never sent. */
  send(id: string, reason?: string): Observable<void> {
    return this.api.post<void>(`/v1/documents/${id}/send`, { reason: reason ?? null });
  }

  /** Owner response to a change request: swaps in a revised PDF and returns the document to Draft. */
  uploadRevision(id: string, file: File, changeNote?: string): Observable<{ documentId: string; version: number; pageCount: number }> {
    const formData = new FormData();
    formData.append('file', file);
    if (changeNote?.trim()) formData.append('changeNote', changeNote.trim());
    return this.api.upload(`/v1/documents/${id}/revision`, formData);
  }

  /** The answers an owner may change after changes were requested on a template contract, with what was asked. */
  getRevisionForm(id: string): Observable<TemplateRevisionForm> {
    return this.api.get<TemplateRevisionForm>(`/v1/documents/${id}/revision-form`);
  }

  /** Revises a template contract by its answers: keeps the old version, creates the next, restarts the circuit. */
  reviseAnswers(id: string, filledValues: Record<string, string>, changeNote?: string): Observable<{ documentId: string; version: number; pageCount: number }> {
    return this.api.post(`/v1/documents/${id}/revise-answers`, { filledValues, changeNote: changeNote?.trim() || null });
  }

  /** The current version as a Word file: edited in Word, it is uploaded back as the next version (R-M7-3). */
  downloadDocx(id: string): Observable<Blob> {
    return this.api.download(`/v1/documents/${id}/docx`);
  }

  /** Every round of the circuit, newest first (R-M7-2). */
  getRounds(id: string): Observable<ContractRound[]> {
    return this.api.get<ContractRound[]>(`/v1/documents/${id}/rounds`);
  }

  /** Admin: reopen an expired request, restarting its SLA clock. */
  reopen(id: string, slaHours?: number): Observable<void> {
    return this.api.post<void>(`/v1/documents/${id}/reopen`, { slaHours: slaHours ?? null });
  }



  /** Every contract with its end date and notice deadline, most urgent first. */
  getSchedule(): Observable<ContractScheduleRow[]> {
    return this.api.get<ContractScheduleRow[]>('/v1/documents/schedule');
  }

  /** Harvey reads the contract's key data, each value with the sentence it comes from. Nothing is saved. */
  extractKeyTerms(id: string, language: string): Observable<KeyTerms> {
    return this.api.post<KeyTerms>(`/v1/documents/${id}/key-terms?language=${language}`, {});
  }

  /** Harvey's decision note for the contract's next deadline: renew, renegotiate or terminate. */
  writeRenewalNote(id: string, language: string): Observable<RenewalNote> {
    return this.api.post<RenewalNote>(`/v1/documents/${id}/renewal-note?language=${language}`, {});
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/documents/${id}`);
  }

  download(id: string): Observable<Blob> {
    return this.api.download(`/v1/documents/${id}/download`);
  }

  /** Downloads one superseded version's PDF. The filename carries the version — see DocumentVersionAccess. */
  downloadVersion(id: string, versionNumber: number): Observable<Blob> {
    return this.api.download(`/v1/documents/${id}/versions/${versionNumber}/download`);
  }

  /** Redlines two versions against each other. Both PDFs are re-read server-side, so this is not instant. */
  compareVersions(id: string, from: number, to: number): Observable<VersionComparison> {
    return this.api.get<VersionComparison>(`/v1/documents/${id}/versions/compare`, { from, to });
  }

  /** Streams the original PDF inline as a Blob through the authenticated HTTP client (JWT attached). */
  stream(id: string): Observable<Blob> {
    return this.api.download(`/v1/documents/${id}/stream`);
  }

  /** Replaces the field layout; coordinates in PDF points. A layout saved since `layoutVersion` answers 409 layout.stale. */
  saveFields(documentId: string, fields: FieldDto[], layoutVersion?: number): Observable<{ layoutVersion: number }> {
    return this.api.put<{ layoutVersion: number }>(`/v1/documents/${documentId}/fields`, { layoutVersion: layoutVersion ?? null, fields });
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

  /** One turn of "ask the contract": answered from the contract's text only, with verified excerpts. */
  askContract(id: string, request: AskContractRequest): Observable<AskContractReply> {
    return this.api.post<AskContractReply>(`/v1/documents/${id}/ask`, request);
  }

  /** Sends a participant a new signing link by e-mail; their previous link stops working. */
  resendLink(id: string, recipientId: string): Observable<void> {
    return this.api.post<void>(`/v1/documents/${id}/recipients/${recipientId}/resend-link`, {});
  }

  /** Proposes the contract's fields from its PDF (owner); nothing is saved. Coordinates in PDF points. */
  detectContractFields(id: string): Observable<import('../models').DetectedFormFields> {
    return this.api.post<import('../models').DetectedFormFields>(`/v1/documents/${id}/detect-fields`, {});
  }

  /** Starts Harvey's review of the current version in the background; the result arrives over SignalR. */
  startReview(id: string): Observable<ReviewRun> {
    return this.api.post<ReviewRun>(`/v1/documents/${id}/reviews`, {});
  }

  /** The contract's review: latest run, verdict, scores and findings. Runs nothing. */
  getReview(id: string): Observable<Review> {
    return this.api.get<Review>(`/v1/documents/${id}/reviews/latest`);
  }

  /** Accept a finding or mark it not applicable (both with a reason), or reopen it. The API re-grades the contract. */
  decideReviewItem(id: string, itemId: string, status: ReviewItemStatus, reason?: string): Observable<Review> {
    return this.api.patch<Review>(`/v1/documents/${id}/review-items/${itemId}`, { status, reason: reason ?? null });
  }

  /** Administrators: Harvey's rules in service and the organisation's setting for each. */
  getReviewSettings(): Observable<ReviewRuleSetting[]> {
    return this.api.get<ReviewRuleSetting[]>('/v1/review-settings');
  }

  setReviewSetting(ruleKey: string, enabled: boolean, severityOverride: RiskSeverity | null): Observable<void> {
    return this.api.put<void>(`/v1/review-settings/${encodeURIComponent(ruleKey)}`, { enabled, severityOverride });
  }
}
