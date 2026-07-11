import { WorkflowType } from './document.models';

export type HtmlFieldType = 'Text' | 'Textarea' | 'Date' | 'Number' | 'Checkbox' | 'Signature';
export type HtmlFieldRole = 'Initiator' | 'Approver';

export interface HtmlTemplateFieldDto {
  id: string;
  fieldKey: string;
  label: string;
  type: HtmlFieldType;
  role: HtmlFieldRole;
  isRequired: boolean;
  order: number;
  autoFillFrom?: string;
  optionsJson?: string;
  preAssignedApproverId?: string;
  preAssignedApproverName?: string;
  workflowStage: number;
}

export interface HtmlTemplateDto {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  createdById: string;
  createdByName: string;
  defaultApproverId?: string;
  defaultApproverName?: string;
  workflowType: WorkflowType;
  fieldCount: number;
  createdAt: string;
  updatedAt?: string;
}

export interface HtmlTemplateDetailDto {
  id: string;
  name: string;
  description?: string;
  htmlContent: string;
  isActive: boolean;
  createdById: string;
  createdByName: string;
  defaultApproverId?: string;
  defaultApproverName?: string;
  workflowType: WorkflowType;
  hideRecipientIdentities: boolean;
  createdAt: string;
  updatedAt?: string;
  fields: HtmlTemplateFieldDto[];
}

export interface HtmlTemplateFieldInput {
  fieldKey: string;
  label: string;
  type: HtmlFieldType;
  role: HtmlFieldRole;
  isRequired: boolean;
  order: number;
  autoFillFrom?: string;
  optionsJson?: string;
  preAssignedApproverId?: string;
  workflowStage: number;
}

export interface CreateHtmlTemplateRequest {
  name: string;
  htmlContent: string;
  description?: string;
  defaultApproverId?: string;
  fields: HtmlTemplateFieldInput[];
  workflowType: WorkflowType;
  hideRecipientIdentities: boolean;
}

export interface UpdateHtmlTemplateRequest {
  name: string;
  htmlContent: string;
  description?: string;
  defaultApproverId?: string;
  isActive: boolean;
  fields: HtmlTemplateFieldInput[];
  workflowType: WorkflowType;
  hideRecipientIdentities: boolean;
}

/** One approver choice: either an existing system user, or an external signer with no account (name + email only). */
export interface ApproverSelection {
  userId?: string;
  externalName?: string;
  externalEmail?: string;
}

export interface SubmitFromTemplateRequest {
  filledValues: Record<string, string>;
  approvers?: ApproverSelection[];    // one per Approver Signature field needing a choice
  title?: string;
  message?: string;
  expirationDays?: number;
}

export interface GeneratedHtmlTemplateField {
  fieldKey: string;
  label: string;
  type: HtmlFieldType;
  role: HtmlFieldRole;
  isRequired: boolean;
  order: number;
  autoFillFrom?: string;
}

export interface GeneratedHtmlTemplate {
  htmlContent: string;
  suggestedName: string;
  fields: GeneratedHtmlTemplateField[];
}
