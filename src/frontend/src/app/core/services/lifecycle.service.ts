import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { ContractLifecycle, ObligationAction, ObligationRequest, SetTermRequest, TermDecision } from '../models/lifecycle.models';

/** A signed contract's life (M10): term, decision, obligations, and the user's calendar feed. */
@Injectable({ providedIn: 'root' })
export class LifecycleService {
  private readonly api = inject(ApiService);

  get(documentId: string): Observable<ContractLifecycle> {
    return this.api.get<ContractLifecycle>(`/v1/documents/${documentId}/lifecycle`);
  }

  setTerm(documentId: string, request: SetTermRequest): Observable<void> {
    return this.api.put<void>(`/v1/documents/${documentId}/term`, request);
  }

  /** Terminate needs the date the notice was sent and its proof. */
  decide(documentId: string, decision: TermDecision, noticeSentAt?: string | null, proof?: File | null): Observable<void> {
    const form = new FormData();
    form.append('decision', decision);
    if (noticeSentAt) form.append('noticeSentAt', noticeSentAt);
    if (proof) form.append('proof', proof, proof.name);
    return this.api.upload<void>(`/v1/documents/${documentId}/term/decision`, form);
  }

  addObligation(documentId: string, request: ObligationRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>(`/v1/documents/${documentId}/obligations`, request);
  }

  updateObligation(documentId: string, obligationId: string, request: ObligationRequest): Observable<void> {
    return this.api.put<void>(`/v1/documents/${documentId}/obligations/${obligationId}`, request);
  }

  act(documentId: string, obligationId: string, action: ObligationAction): Observable<void> {
    return this.api.post<void>(`/v1/documents/${documentId}/obligations/${obligationId}/${action}`, {});
  }

  /** A new private calendar address (the old one stops working). */
  issueCalendarFeed(): Observable<{ url: string }> {
    return this.api.post<{ url: string }>('/v1/calendar/feed', {});
  }

  revokeCalendarFeed(): Observable<void> {
    return this.api.delete<void>('/v1/calendar/feed');
  }
}
