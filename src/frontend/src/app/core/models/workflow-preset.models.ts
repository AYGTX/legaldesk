import { WorkflowType } from './document.models';

export type PresetApproverKind = 'User' | 'Role' | 'Department';

/** A step as the admin screen edits and sends it. Which of userId / roleName / department counts depends on `kind`. */
export interface WorkflowPresetStepInput {
  stage: number;
  label?: string;
  kind: PresetApproverKind;
  userId?: string;
  roleName?: string;
  department?: string;
  conditionFieldKey?: string;
  /** One of ==, !=, >, >=, <, <=; absent means "the field is filled in / ticked". */
  conditionOperator?: string;
  conditionValue?: string;
}

export interface WorkflowPresetStepDto extends WorkflowPresetStepInput {
  order: number;
  userName?: string;
}

export interface WorkflowPresetDto {
  id: string;
  name: string;
  description?: string;
  workflowType: WorkflowType;
  isActive: boolean;
  steps: WorkflowPresetStepDto[];
  /** How many templates (HTML and PDF) follow this workflow. */
  templateCount: number;
}

export interface SaveWorkflowPresetRequest {
  name: string;
  description?: string;
  workflowType: WorkflowType;
  isActive: boolean;
  steps: WorkflowPresetStepInput[];
}

/** One step as the person creating a request sees it. */
export interface PresetStepSummary {
  order: number;
  label?: string;
  who: string;
  condition?: string;
}

/** The workflow a template follows, as returned with the template. */
export interface TemplatePreset {
  id: string;
  name: string;
  workflowType: WorkflowType;
  isActive: boolean;
  steps: PresetStepSummary[];
}
