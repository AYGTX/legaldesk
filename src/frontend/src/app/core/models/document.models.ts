export type DocumentStatus = 'Draft' | 'Pending' | 'Sent' | 'Completed' | 'Rejected' | 'Expired' | 'Cancelled' | 'WaitingForOthers';
export type FieldType = 'Signature' | 'Initials' | 'Date' | 'Name' | 'Email' | 'Company' | 'Title' | 'Text' | 'MultilineText' | 'Number' | 'Checkbox' | 'Radio' | 'Dropdown' | 'Image' | 'Stamp' | 'Attachment' | 'QrCode' | 'Barcode' | 'Custom';
export type ApprovalType = 'Signature' | 'Approval' | 'Review' | 'ViewOnly' | 'Stamp' | 'Reject';
export type WorkflowType = 'Sequential' | 'Parallel' | 'Mixed';
export type WorkflowStepStatus = 'Pending' | 'InProgress' | 'Completed' | 'Rejected' | 'Skipped' | 'Delegated' | 'Escalated' | 'Expired';

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
  hideRecipientIdentities: boolean;
  owner: OwnerInfo;
  recipients: RecipientInfo[];
  fields: SignatureFieldInfo[];
  workflow?: WorkflowInfo;
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
  secureToken: string;
  signedAt?: string;
  viewedAt?: string;
  /** Free-text participant label (e.g. "Legal team") — display only, capabilities come from approvalType. */
  roleLabel?: string;
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
  zIndex: number;
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
  zIndex: number;
  visibilityRules?: string;
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
}

