import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from './api.service';
import { SigningSession, SignFieldRequest, SignFieldResult, SigningIntro, OpenedSigningSession } from '../models';

/** Route token of a session opened from the participant's LegalDesk account (no e-mailed link). */
export const ACCOUNT_SESSION = 'session';

/**
 * The participant's side of a contract (M5). The e-mailed link shows what is asked and sends a 6-digit code; the code
 * opens a 30-minute session whose secret goes in the X-Signing-Session header (signingSessionInterceptor) — the link
 * alone never acts. Secrets are kept per link, for this tab only, so a reload does not ask for a new code.
 */
@Injectable({ providedIn: 'root' })
export class SigningService {
  private readonly api = inject(ApiService);

  private static readonly StoreKey = 'ld_signing_sessions';
  private secrets: Record<string, string> = {};

  /** Bumped when the server says a session has ended: the signing page asks for a new code. */
  readonly sessionLost = signal<{ token: string; at: number } | null>(null);

  constructor() {
    try { this.secrets = JSON.parse(sessionStorage.getItem(SigningService.StoreKey) ?? '{}'); } catch { this.secrets = {}; }
  }

  /** The signing session's secret for this link (or for the account route), for the interceptor. */
  sessionSecret(token: string): string | null {
    return this.secrets[token] ?? null;
  }

  private remember(token: string, session: OpenedSigningSession): void {
    this.secrets = { ...this.secrets, [token]: session.secret };
    this.persist();
  }

  sessionEnded(token: string): void {
    const { [token]: _, ...rest } = this.secrets;
    this.secrets = rest;
    this.persist();
    this.sessionLost.set({ token, at: Date.now() });
  }

  private persist(): void {
    try { sessionStorage.setItem(SigningService.StoreKey, JSON.stringify(this.secrets)); } catch { /* private window: memory only */ }
  }

  /** What the link is for: contract, sender, participant, masked e-mail. Needs no session. */
  getIntro(token: string): Observable<SigningIntro> {
    return this.api.get<SigningIntro>(`/v1/sign/${token}/intro`);
  }

  sendCode(token: string): Observable<void> {
    return this.api.post<void>(`/v1/sign/${token}/otp/send`, {});
  }

  verifyCode(token: string, code: string): Observable<OpenedSigningSession> {
    return this.api.post<OpenedSigningSession>(`/v1/sign/${token}/otp/verify`, { code }).pipe(tap(s => this.remember(token, s)));
  }

  /** Signed in to LegalDesk as the participant: the account proves the address, no code. */
  openAccountSession(recipientId: string): Observable<OpenedSigningSession> {
    return this.api.post<OpenedSigningSession>(`/v1/requests/${recipientId}/signing-session`, {}).pipe(tap(s => this.remember(ACCOUNT_SESSION, s)));
  }

  getSession(token: string): Observable<SigningSession> {
    return this.api.get<SigningSession>(`/v1/sign/${token}`);
  }

  /** The PDF being signed. */
  streamDocument(token: string): Observable<Blob> {
    return this.api.download(`/v1/sign/${token}/document`);
  }

  signField(token: string, fieldId: string, request: SignFieldRequest): Observable<SignFieldResult> {
    return this.api.post<SignFieldResult>(`/v1/sign/${token}/fields/${fieldId}`, request);
  }

  /** Confirms the contract (and its review, where shown) was read — required before signing or approving. */
  acknowledgeReview(token: string): Observable<void> {
    return this.api.post<void>(`/v1/sign/${token}/acknowledge-review`, {});
  }

  approveSigning(token: string): Observable<{ documentCompleted: boolean }> {
    return this.api.post<{ documentCompleted: boolean }>(`/v1/sign/${token}/approve`, {});
  }

  rejectDocument(token: string, reason: string): Observable<void> {
    return this.api.post<void>(`/v1/sign/${token}/decline`, { reason });
  }

  /** Hands this participant's step to someone else (R-M4-5); this link stops working, theirs is e-mailed. */
  delegate(token: string, request: { name: string; email: string; note: string }): Observable<{ recipientId: string; name: string; email: string }> {
    return this.api.post(`/v1/sign/${token}/delegate`, request);
  }

  /** Reviewers/approvers only — pauses the workflow for a revision instead of rejecting it. */
  requestChanges(token: string, reason: string): Observable<void> {
    return this.api.post<void>(`/v1/sign/${token}/request-changes`, { reason });
  }


  /** Check or uncheck one of the recipient's own checklist items. */
  toggleChecklistItem(token: string, itemId: string, checked: boolean): Observable<void> {
    return this.api.post<void>(`/v1/sign/${token}/checklist/${itemId}`, { checked });
  }
}
