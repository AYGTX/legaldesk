import { TemplatePreset } from './workflow-preset.models';
import { WorkflowType } from './document.models';
import { ReviewArticle, RiskSeverity } from './contract-intelligence.model';

export type HtmlFieldType = 'Text' | 'Textarea' | 'Date' | 'Number' | 'Checkbox' | 'Signature' | 'Dropdown';
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
  /** What the request form starts this field as when nothing else fills it. */
  defaultValue?: string;
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
  /** Request type this template carries — requests created from it inherit this type (and its SLA) at creation time. */
  requestTypeId?: string;
  requestTypeName?: string;
  requestTypeSlaHours?: number;
  /** The reusable approval workflow this template follows, with its steps. */
  workflowPreset?: TemplatePreset;
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
  defaultValue?: string;
}

export interface CreateHtmlTemplateRequest {
  name: string;
  htmlContent: string;
  description?: string;
  defaultApproverId?: string;
  fields: HtmlTemplateFieldInput[];
  workflowType: WorkflowType;
  hideRecipientIdentities: boolean;
  requestTypeId?: string;
  workflowPresetId?: string;
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
  requestTypeId?: string;
  workflowPresetId?: string;
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

/** The legal review of a template as written: what a reviewer would flag in the contract it can produce. */
export interface TemplateReviewResult {
  riskScore: number;
  findings: TemplateFinding[];
  limitations: string[];
  gridVersion?: string | null;
}

/** One risk Harvey found in the template, with the rule and the clause. */
export interface TemplateFinding {
  ruleKey: string;
  ruleVersion?: number | null;
  clauseType: string;
  severity: RiskSeverity;
  explanation: string;
  recommendedAction?: string | null;
  proposedLanguage?: string | null;
  contractReference?: string | null;
  quotes: string[];
  articles: ReviewArticle[];
}
