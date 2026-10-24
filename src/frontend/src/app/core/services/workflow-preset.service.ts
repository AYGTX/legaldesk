import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { SaveWorkflowPresetRequest, WorkflowPresetDto } from '../models/workflow-preset.models';

@Injectable({ providedIn: 'root' })
export class WorkflowPresetService {
  private readonly api = inject(ApiService);

  /** Administrators and managers. `activeOnly` is what a template's picker wants. */
  getAll(activeOnly = false): Observable<WorkflowPresetDto[]> {
    return this.api.get<WorkflowPresetDto[]>('/v1/workflow-presets', activeOnly ? { activeOnly: true } : undefined);
  }

  create(request: SaveWorkflowPresetRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/workflow-presets', request);
  }

  update(id: string, request: SaveWorkflowPresetRequest): Observable<void> {
    return this.api.put<void>(`/v1/workflow-presets/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/workflow-presets/${id}`);
  }
}
