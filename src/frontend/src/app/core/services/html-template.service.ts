import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PagedResult } from '../models';
import {
  HtmlTemplateDto, HtmlTemplateDetailDto,
  CreateHtmlTemplateRequest, UpdateHtmlTemplateRequest,
  SubmitFromTemplateRequest, GeneratedHtmlTemplate
} from '../models/html-template.models';

@Injectable({ providedIn: 'root' })
export class HtmlTemplateService {
  private readonly api = inject(ApiService);

  getAll(params?: { pageNumber?: number; pageSize?: number; search?: string; activeOnly?: boolean }): Observable<PagedResult<HtmlTemplateDto>> {
    return this.api.get<PagedResult<HtmlTemplateDto>>('/v1/html-templates', params as Record<string, unknown>);
  }

  getById(id: string): Observable<HtmlTemplateDetailDto> {
    return this.api.get<HtmlTemplateDetailDto>(`/v1/html-templates/${id}`);
  }

  create(request: CreateHtmlTemplateRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/html-templates', request);
  }

  update(id: string, request: UpdateHtmlTemplateRequest): Observable<void> {
    return this.api.put<void>(`/v1/html-templates/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/html-templates/${id}`);
  }

  submit(templateId: string, request: SubmitFromTemplateRequest): Observable<{ documentId: string }> {
    return this.api.post<{ documentId: string }>(`/v1/html-templates/${templateId}/submit`, request);
  }

  generateFromText(userText: string): Observable<GeneratedHtmlTemplate> {
    return this.api.post<GeneratedHtmlTemplate>('/v1/html-templates/generate', { userText });
  }
}
