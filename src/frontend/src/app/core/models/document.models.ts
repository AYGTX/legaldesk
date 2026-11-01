import { ReadinessVerdict } from './contract-intelligence.model';

export type DocumentStatus = 'Draft' | 'Pending' | 'InProgress' | 'Sent' | 'Completed' | 'Rejected' | 'Expired' | 'Cancelled' | 'WaitingForOthers' | 'ChangesRequested';
export type FieldType = 'Signature' | 'Initials' | 'Date' | 'Name' | 'Email' | 'Company' | 'Title' | 'Text' | 'MultilineText' | 'Number' | 'Checkbox' | 'Radio' | 'Dropdown' | 'Image' | 'Stamp' | 'Attachment' | 'QrCode' | 'Barcode' | 'Custom';
export type ApprovalType = 'Signature' | 'Approval' | 'Review' | 'ViewOnly' | 'Stamp' | 'Reject';
export type WorkflowType = 'Sequential' | 'Parallel' | 'Mixed';
export type WorkflowStepStatus = 'Pending' | 'InProgress' | 'Completed' | 'Rejected' | 'Skipped' | 'Delegated' | 'Escalated' | 'Expired' | 'ChangesRequested';

export interface AiSummaryDto {
  summary: string;
  generatedAt: string;
}

export interface DocumentSummary {
  id: string;
  title: string;
  status: DocumentStatus;
  fileName: string;
  fileSizeBytes: number;
  pageCount: number;
  recipientCount: number;
  signedCount: number;
  createdAt: string;
  sentAt?: string;
  completedAt?: string;
  expiresAt?: string;
  ownerName?: string;
  ownerEmail?: string;
  tags?: string;
  requestTypeName?: string;
}

export interface DocumentDetail {
  id: string;
  title: string;
  description?: string;
  status: DocumentStatus;
  workflowType: WorkflowType;
  fileName: string;
  storagePath: string;
  finalStoragePath?: string;
  fileSizeBytes: number;
  pageCount: number;
  subject?: string;
  message?: string;
  hash?: string;
  createdAt: string;
  sentAt?: string;
  completedAt?: string;
  expiresAt?: string;
  verificationCode?: string;
  slaHours?: number;
  requestTypeName?: string;
  requestTypeIcon?: string;
  requestTypeColor?: string;
  aiSummary?: string;
  aiSummaryGeneratedAt?: string;
  /** Review verdict and scores of the current version, carried on the detail payload so the band renders at once. */
  readinessVerdict: ReadinessVerdict;
  readinessScore?: number;
  riskScore?: number;
  /** Field layout version, sent back with a save (R-M3-4). */
  layoutVersion?: number;
  readinessEvaluatedAt?: string;
  hideRecipientIdentities: boolean;
  owner: OwnerInfo;
  recipients: RecipientInfo[];
  fields: SignatureFieldInfo[];
  workflow?: WorkflowInfo;
  version: number;
  versions?: DocumentVersionInfo[];
  /** Set for contracts built from a template ('Html' | 'Pdf'); their owner revises answers, never the file. */
  templateKind?: 'Html' | 'Pdf' | null;
  /** The template and version the contract came from (R-M6-5). */
  templateName?: string | null;
  templateVersion?: number | null;
  /** Library clauses the contract carries, each at its version (R-M6-7). */
  libraryClauses?: { clauseId: string; title: string; version: number }[] | null;
  templateRevisions?: TemplateRevision[];
}

export interface DocumentVersionInfo {
  id: string;
  versionNumber: number;
  fileSizeBytes: number;
  changeNote?: string;
  changedByName?: string;
  createdAt: string;
}

export interface OwnerInfo {
  id: string;
  fullName: string;
  email: string;
  avatarUrl?: string;
}

export interface RecipientInfo {
  id: string;
  name: string;
  email: string;
  order: number;
  approvalType: ApprovalType;
  status: string;
  signedAt?: string;
  viewedAt?: string;
  /** Free-text participant label (e.g. "Legal team") — display only, capabilities come from approvalType. */
  roleLabel?: string;
  /** Reason given on decline or request-changes. */
  declineReason?: string;
  /** Review/Approval prerequisites the recipient must check off before approving. */
  checklistItems?: ChecklistItemInfo[];
  /** All: the stage waits for everyone on it. Any: the first answer closes the stage (R-M4-2). */
  stageRule?: StageRule;
  /** A team step: any active member of the team acts for it (R-M4-3). */
  poolTeamId?: string | null;
  poolRole?: string | null;
  /** Due this many working days after the step opens (weekends and holidays skipped). */
  dueInWorkingDays?: number | null;
  /** Reminder interval while the step is open; 0 turns reminders off. */
  reminderEveryDays?: number;
  /** After this many days the escalation contact (else the owner) is told. */
  escalateAfterDays?: number | null;
  escalationEmail?: string | null;
  /** A stand-in: the participant this person acts for. */
  delegatedFromRecipientId?: string | null;
  company?: string | null;
}

export type StageRule = 'All' | 'Any';

export interface ChecklistItemInfo {
  id: string;
  text: string;
  order: number;
  isChecked: boolean;
}

export interface SignatureFieldInfo {
  id: string;
  fieldType: FieldType;
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
  rotation?: number;
  recipientId?: string;
  label?: string;
  placeholder?: string;
  isRequired: boolean;
  isSigned: boolean;
  signedAt?: string;
  fontFamily?: string;
  fontSize?: number;
  fontColor?: string;
  backgroundColor?: string;
  borderColor?: string;
  options?: string;
  defaultValue?: string;
  validationRule?: string;
  isReadOnly: boolean;
  tooltip?: string;
  /** Radio fields sharing a group name form a single-choice group. */
  groupName?: string;
}

export interface WorkflowInfo {
  id: string;
  type: WorkflowType;
  status: WorkflowStepStatus;
  currentStepOrder: number;
  steps: WorkflowStepInfo[];
}

export interface WorkflowStepInfo {
  id: string;
  order: number;
  name: string;
  approvalType: ApprovalType;
  status: WorkflowStepStatus;
  recipientId: string;
  isParallel: boolean;
  dueAt?: string;
  completedAt?: string;
}

export interface DashboardData {
  pendingSignatures: number;
  waitingForOthers: number;
  completed: number;
  rejected: number;
  expired: number;
  drafts: number;
  totalDocuments: number;
  recentActivity: RecentActivity[];
  pendingDocuments: DocumentSummary[];
  changesRequested: number;
  /** The user's own executed contracts whose term ends within 30 days. */
  expiringSoon: number;
}

export interface RecentActivity {
  documentId: string;
  documentTitle: string;
  action: string;
  actorName?: string;
  timestamp: string;
  description?: string;
}

export interface FieldDto {
  id?: string;
  fieldType: FieldType;
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
  rotation?: number;
  recipientId?: string;
  label?: string;
  placeholder?: string;
  defaultValue?: string;
  fontFamily?: string;
  fontSize?: number;
  fontColor?: string;
  borderColor?: string;
  backgroundColor?: string;
  isRequired: boolean;
  isReadOnly?: boolean;
  validationRule?: string;
  options?: string;
  groupName?: string;
  tooltip?: string;
  maxLength?: number;
}

export interface RecipientInputDto {
  id?: string;
  name: string;
  email: string;
  order: number;
  approvalType: ApprovalType;
  privateMessage?: string;
  company?: string;
  /** Free-text participant label (e.g. "Legal team") — display only, capabilities come from approvalType. */
  roleLabel?: string;
  /** All: the stage waits for everyone on it. Any: the first answer closes the stage (R-M4-2). */
  stageRule?: StageRule;
  /** A team step: any active member of the team acts for it (R-M4-3). */
  poolTeamId?: string | null;
  poolRole?: string | null;
  /** Due this many working days after the step opens (weekends and holidays skipped). */
  dueInWorkingDays?: number | null;
  /** Reminder interval while the step is open; 0 turns reminders off. */
  reminderEveryDays?: number;
  /** After this many days the escalation contact (else the owner) is told. */
  escalateAfterDays?: number | null;
  escalationEmail?: string | null;
}


export type RoundOutcome = 'Open' | 'Completed' | 'Declined' | 'ChangesRequested' | 'Superseded';

/** One round of the circuit (R-M7-2): the version it asked people to act on, and what each step did. */
export interface ContractRound {
  id: string;
  number: number;
  versionNumber: number;
  openedAt: string;
  closedAt?: string | null;
  outcome: RoundOutcome;
  steps: RoundStep[];
}

export interface RoundStep {
  order: number;
  name: string;
  approvalType: string;
  stageRule: 'All' | 'Any';
  status: string;
  openedAt?: string | null;
  dueAt?: string | null;
  completedAt?: string | null;
  declineReason?: string | null;
  /** For a team or role step, the member who acted. */
  actedByName?: string | null;
  handedOver: boolean;
  standsIn: boolean;
}

/** One answer that differs between two versions of a template contract. */
export interface TemplateFieldChange {
  fieldKey: string;
  label: string;
  oldValue?: string;
  newValue?: string;
}

/** What changed between two versions of a template contract, and who changed it. */
export interface TemplateRevision {
  fromVersion: number;
  toVersion: number;
  changeNote?: string;
  changedByName?: string;
  createdAt: string;
  changes: TemplateFieldChange[];
}

export interface TemplateRevisionFormField {
  fieldKey: string;
  label: string;
  type: string;
  isRequired: boolean;
  optionsJson?: string;
}

/** The form an owner fills again after a reviewer asked for changes on a template contract. */
export interface TemplateRevisionForm {
  templateKind: string;
  currentVersion: number;
  fields: TemplateRevisionFormField[];
  values: Record<string, string>;
  changeRequests: { requestedBy: string; reason?: string }[];
}
