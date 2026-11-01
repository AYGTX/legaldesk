import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { TemplateKind, TemplateVersionResult } from '../models/template-gallery.model';

/** The life of a template version (R-M6-5): send to legal review, approve, retire, draft the next version. */
@Injectable({ providedIn: 'root' })
export class TemplateVersionService {
  private readonly api = inject(ApiService);

  submit(kind: TemplateKind, id: string): Observable<TemplateVersionResult> {
    return this.api.post(`/v1/template-versions/${kind}/${id}/submit`, {});
  }

  /** Harvey reviews the template first; an open critical finding refuses the approval. */
  approve(kind: TemplateKind, id: string): Observable<TemplateVersionResult> {
    return this.api.post(`/v1/template-versions/${kind}/${id}/approve`, {});
  }

  retire(kind: TemplateKind, id: string): Observable<TemplateVersionResult> {
    return this.api.post(`/v1/template-versions/${kind}/${id}/retire`, {});
  }

  /** The draft of the next version (or the one already started). */
  next(kind: TemplateKind, id: string): Observable<TemplateVersionResult> {
    return this.api.post(`/v1/template-versions/${kind}/${id}/next`, {});
  }
}
