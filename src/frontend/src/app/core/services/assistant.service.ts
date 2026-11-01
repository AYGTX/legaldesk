import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from './api.service';
import { AssistantStatus, ContractSummary, ExplainChanges, OrganisationSettings } from '../models/assistant.models';

/**
 * The Harvey assistant (M12): off unless the organisation turned it on, in which case its screens are hidden.
 * The status is read once per session and refreshed when an administrator changes it.
 */
@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly api = inject(ApiService);
  private loaded = false;

  /** False until the server says otherwise: nothing model-related is shown on a guess. */
  readonly enabled = signal(false);

  loadStatus(): void {
    if (this.loaded) return;
    this.loaded = true;
    this.api.get<AssistantStatus>('/v1/assistant/status').subscribe({
      next: s => this.enabled.set(s.enabled),
      error: () => { this.loaded = false; this.enabled.set(false); }
    });
  }

  summary(documentId: string): Observable<ContractSummary> {
    return this.api.get<ContractSummary>(`/v1/documents/${documentId}/summary`);
  }

  signingSummary(token: string): Observable<ContractSummary> {
    return this.api.get<ContractSummary>(`/v1/sign/${token}/summary`);
  }

  explain(documentId: string, from: number, to: number): Observable<ExplainChanges> {
    return this.api.post<ExplainChanges>(`/v1/documents/${documentId}/versions/explain?from=${from}&to=${to}`, {});
  }

  settings(): Observable<OrganisationSettings> {
    return this.api.get<OrganisationSettings>('/v1/organisation-settings');
  }

  saveSettings(modelEnabled: boolean, alertOffsets: string): Observable<OrganisationSettings> {
    return this.api.put<OrganisationSettings>('/v1/organisation-settings', { modelEnabled, alertOffsets }).pipe(
      tap(s => { this.loaded = true; this.enabled.set(s.modelEnabled && s.modelAvailable); })
    );
  }
}

/** The message for an assistant refusal: off, or the limit reached. */
export function assistantErrorKey(err: { status?: number; error?: { code?: string } }): string {
  const code = err?.error?.code;
  if (code === 'model.limit' || err?.status === 429) return 'assistant.limit';
  if (code === 'model.off') return 'assistant.off';
  return 'assistant.failed';
}
