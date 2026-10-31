import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PagedResult } from '../models/shared.models';
import {
  PdfTemplateDto, PdfTemplateDetailDto, CreatePdfTemplateResult,
  UpdatePdfTemplateRequest, SubmitPdfTemplateRequest, DetectedFormFields
} from '../models/pdf-template.models';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PdfTemplateService {
  private readonly api = inject(ApiService);

  getAll(params: { pageNumber?: number; pageSize?: number; search?: string; activeOnly?: boolean } = {})
    : Observable<PagedResult<PdfTemplateDto>> {
    return this.api.get<PagedResult<PdfTemplateDto>>('/v1/pdf-templates', params as Record<string, unknown>);
  }

  getById(id: string): Observable<PdfTemplateDetailDto> {
    return this.api.get<PdfTemplateDetailDto>(`/v1/pdf-templates/${id}`);
  }

  /** Upload the base PDF for a new template (admin). Fields are configured afterwards via update(). */
  create(file: File, name: string, requestTypeId: string, description?: string): Observable<CreatePdfTemplateResult> {
    const form = new FormData();
    form.append('file', file);
    form.append('name', name);
    form.append('requestTypeId', requestTypeId);
    if (description) form.append('description', description);
    return this.api.upload<CreatePdfTemplateResult>('/v1/pdf-templates', form);
  }

  update(id: string, request: UpdatePdfTemplateRequest): Observable<void> {
    return this.api.put<void>(`/v1/pdf-templates/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/pdf-templates/${id}`);
  }

  submit(id: string, request: SubmitPdfTemplateRequest): Observable<{ documentId: string }> {
    return this.api.post<{ documentId: string }>(`/v1/pdf-templates/${id}/submit`, request);
  }

  /** Fetch the base PDF as a Blob (PDF.js can't attach the JWT, so we stream then pass { data }). */
  stream(id: string): Observable<Blob> {
    return this.api.download(`/v1/pdf-templates/${id}/stream`);
  }

  /** Proposes fields (type, label, fill role, auto-fill, coordinates) for a contract PDF. Nothing is saved. */
  detectFields(file: Blob): Observable<DetectedFormFields> {
    const form = new FormData();
    form.append('file', file, 'contract.pdf');
    return this.api.upload<DetectedFormFields>('/v1/pdf-templates/detect-fields', form);
  }

  /** Absolute stream URL (rarely needed; prefer stream() blob for authenticated rendering). */
  streamUrl(id: string): string {
    return `${environment.apiUrl}/v1/pdf-templates/${id}/stream`;
  }
}
