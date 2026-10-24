import { TemplatePreset } from './workflow-preset.models';
import { WorkflowType } from './document.models';

/** Field types supported by the PDF-template flow (mirrors backend PdfFieldType). */
export type PdfFieldType = 'Text' | 'Date' | 'Number' | 'Checkbox' | 'Signature' | 'Dropdown' | 'Textarea';

/** Who fills a field at runtime (mirrors backend PdfFieldFillRole). */
export type PdfFieldFillRole = 'Requester' | 'Approver' | 'System';

/** Auto-fill sources understood by the backend resolver. */
export const PDF_AUTO_FILL_OPTIONS: { value: string; labelKey: string }[] = [
  { value: 'user.fullName', labelKey: 'pdfTemplates.autofill.fullName' },
  { value: 'user.firstName', labelKey: 'pdfTemplates.autofill.firstName' },
  { value: 'user.lastName', labelKey: 'pdfTemplates.autofill.lastName' },
  { value: 'user.email', labelKey: 'pdfTemplates.autofill.email' },
  { value: 'user.company', labelKey: 'pdfTemplates.autofill.company' },
  { value: 'user.department', labelKey: 'pdfTemplates.autofill.department' },
  { value: 'system.today', labelKey: 'pdfTemplates.autofill.today' },
  { value: 'system.now', labelKey: 'pdfTemplates.autofill.now' },
  { value: 'request.title', labelKey: 'pdfTemplates.autofill.requestTitle' }
];

export interface PdfTemplateDto {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  pageCount: number;
  createdById: string;
  createdByName: string;
  defaultApproverId?: string;
  defaultApproverName?: string;
  workflowType: WorkflowType;
  fieldCount: number;
  createdAt: string;
  updatedAt?: string;
}

export interface PdfTemplateFieldDto {
  id?: string;
  fieldKey: string;
  label: string;
  type: PdfFieldType;
  fillRole: PdfFieldFillRole;
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
  isRequired: boolean;
  order: number;
  autoFillFrom?: string;
  defaultValue?: string;
  optionsJson?: string;
  preAssignedApproverId?: string;
  preAssignedApproverName?: string;
  workflowStage: number;
}

export interface PdfTemplateDetailDto {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  pageCount: number;
  storagePath: string;
  createdById: string;
  createdByName: string;
  defaultApproverId?: string;
  defaultApproverName?: string;
  workflowType: WorkflowType;
  hideRecipientIdentities: boolean;
  createdAt: string;
  updatedAt?: string;
  fields: PdfTemplateFieldDto[];
  /** Request type this template carries — requests created from it inherit this type (and its SLA) at creation time. */
  requestTypeId?: string;
  requestTypeName?: string;
  requestTypeSlaHours?: number;
  /** The reusable approval workflow this template follows, with its steps. */
  workflowPreset?: TemplatePreset;
  /** Library clauses appended, as an annex, to every request made from this template. */
  annexClauses?: AnnexClause[];
}

/** A library clause carried onto requests as an annex. */
export interface AnnexClause {
  id: string;
  title: string;
  version: number;
}

export interface CreatePdfTemplateResult {
  id: string;
  name: string;
  pageCount: number;
}

export interface UpdatePdfTemplateRequest {
  name: string;
  description?: string;
  defaultApproverId?: string;
  isActive: boolean;
  fields: PdfTemplateFieldDto[];
  workflowType: WorkflowType;
  hideRecipientIdentities: boolean;
  requestTypeId?: string;
  workflowPresetId?: string;
  annexClauseIds?: string[];
}

export interface PdfApproverSelection {
  userId?: string;
  externalName?: string;
  externalEmail?: string;
}

export interface SubmitPdfTemplateRequest {
  filledValues: Record<string, string>;
  approvers?: PdfApproverSelection[];
  title?: string;
  message?: string;
  /** Library clauses the requester adds; they travel as an annex after the template's own. */
  clauseIds?: string[];
  /** False keeps the request a draft until it has been reviewed and the owner sends it. */
  startImmediately?: boolean;
}

/** A field proposed by the form detector; coordinates use the designer's model (top-left, 1.5x the PDF's points). */
export interface DetectedFormField {
  fieldKey: string;
  label: string;
  type: PdfFieldType;
  fillRole: PdfFieldFillRole;
  page: number;
  x: number; y: number; width: number; height: number;
  isRequired: boolean;
  autoFillFrom?: string;
  options: string[];
  /** "pdf": already fillable in the PDF; "model": found by the vision model. */
  source: 'pdf' | 'model';
  /** "ai": label tidied by a local language model; "rule": from the built-in rules. */
  confidence: 'rule' | 'ai';
}

export interface DetectedFormFields {
  pages: number;
  /** Null when the vision model is not installed on the server. */
  model: string | null;
  aiUsed: boolean;
  fields: DetectedFormField[];
}
