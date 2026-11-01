import { WorkflowType } from './document.models';
import { HtmlTemplateDto } from './html-template.models';
import { PdfTemplateDto } from './pdf-template.models';

export type TemplateKind = 'html' | 'pdf';

export type TemplateStatus = 'Draft' | 'InLegalReview' | 'Approved' | 'Retired';

/** Where a template version stands after a lifecycle action. */
export interface TemplateVersionResult {
  id: string;
  kind: 'Html' | 'Pdf';
  version: number;
  status: TemplateStatus;
  parentTemplateId?: string | null;
}

/** Normalized view of an HtmlTemplateDto or PdfTemplateDto for the unified /templates gallery. */
export interface UnifiedTemplate {
  id: string;
  kind: TemplateKind;
  name: string;
  description?: string;
  isActive: boolean;
  fieldCount: number;
  pageCount?: number;
  workflowType: WorkflowType;
  createdByName: string;
  createdAt: string;
  status: TemplateStatus;
  version: number;
}

export function fromHtmlTemplate(t: HtmlTemplateDto): UnifiedTemplate {
  return {
    id: t.id,
    kind: 'html',
    name: t.name,
    description: t.description,
    isActive: t.isActive,
    fieldCount: t.fieldCount,
    workflowType: t.workflowType,
    createdByName: t.createdByName,
    createdAt: t.createdAt,
    status: t.status ?? 'Approved',
    version: t.version ?? 1
  };
}

export function fromPdfTemplate(t: PdfTemplateDto): UnifiedTemplate {
  return {
    id: t.id,
    kind: 'pdf',
    name: t.name,
    description: t.description,
    isActive: t.isActive,
    fieldCount: t.fieldCount,
    pageCount: t.pageCount,
    workflowType: t.workflowType,
    createdByName: t.createdByName,
    createdAt: t.createdAt,
    status: t.status ?? 'Approved',
    version: t.version ?? 1
  };
}
