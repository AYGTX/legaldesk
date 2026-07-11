import { WorkflowType } from './document.models';
import { HtmlTemplateDto } from './html-template.models';
import { PdfTemplateDto } from './pdf-template.models';

export type TemplateKind = 'html' | 'pdf';

/** Normalized view of an HtmlTemplateDto or PdfTemplateDto for the unified /templates gallery. */
export interface UnifiedTemplate {
  id: string;
  kind: TemplateKind;
  name: string;
  description?: string;
  isActive: boolean;
  fieldCount: number;
  pageCount?: number;
  defaultApproverName?: string;
  workflowType: WorkflowType;
  createdByName: string;
  createdAt: string;
}

export function fromHtmlTemplate(t: HtmlTemplateDto): UnifiedTemplate {
  return {
    id: t.id,
    kind: 'html',
    name: t.name,
    description: t.description,
    isActive: t.isActive,
    fieldCount: t.fieldCount,
    defaultApproverName: t.defaultApproverName,
    workflowType: t.workflowType,
    createdByName: t.createdByName,
    createdAt: t.createdAt
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
    defaultApproverName: t.defaultApproverName,
    workflowType: t.workflowType,
    createdByName: t.createdByName,
    createdAt: t.createdAt
  };
}
