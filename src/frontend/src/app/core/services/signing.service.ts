import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { SigningSession, SignFieldRequest, SignFieldResult, AiSummaryDto } from '../models';

@Injectable({ providedIn: 'root' })
export class SigningService {
  private readonly api = inject(ApiService);

  getSession(token: string): Observable<SigningSession> {
    return this.api.get<SigningSession>(`/v1/sign/${token}`);
  }

  /** Streams the document PDF for the signing session (token-authenticated, public). */
  streamDocument(token: string): Observable<Blob> {
    return this.api.download(`/v1/sign/${token}/document`);
  }

  signField(token: string, fieldId: string, request: SignFieldRequest): Observable<SignFieldResult> {
    return this.api.post<SignFieldResult>(`/v1/sign/${token}/fields/${fieldId}/sign`, request);
  }

  approveSigning(token: string): Observable<{ documentCompleted: boolean }> {
    return this.api.post<{ documentCompleted: boolean }>(`/v1/sign/${token}/approve`, {});
  }

  rejectDocument(token: string, reason: string): Observable<void> {
    return this.api.post<void>(`/v1/sign/${token}/reject`, { reason });
  }

  /** Reviewers/approvers only — pauses the workflow for a revision instead of rejecting it. */
  requestChanges(token: string, reason: string): Observable<void> {
    return this.api.post<void>(`/v1/sign/${token}/request-changes`, { reason });
  }

  getDocumentSummary(token: string): Observable<AiSummaryDto> {
    return this.api.get<AiSummaryDto>(`/v1/sign/${token}/summary`);
  }
}
