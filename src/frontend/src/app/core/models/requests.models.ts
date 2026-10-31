import { DocumentStatus, WorkflowType, WorkflowStepStatus, ApprovalType } from './document.models';

export interface RecipientProgress {
  name: string;
  email: string;
  order: number;
  status: WorkflowStepStatus;
  isCurrent: boolean;
  signedAt?: string;
  viewedAt?: string;
}

export interface SentRequest {
  documentId: string;
  title: string;
  status: DocumentStatus;
  workflowType?: WorkflowType;
  createdAt: string;
  sentAt?: string;
  completedAt?: string;
  expiresAt?: string;
  totalRecipients: number;
  completedRecipients: number;
  currentRecipientName?: string;
  recipients: RecipientProgress[];
}

export interface ReceivedRequest {
  documentId: string;
  title: string;
  ownerName: string;
  documentStatus: DocumentStatus;
  myStatus: WorkflowStepStatus;
  myOrder: number;
  isMyTurn: boolean;
  /** Opens the signing page through the signed-in account (no e-mailed link needed). */
  recipientId: string;
  receivedAt?: string;
  expiresAt?: string;
  signedAt?: string;
  approvalType: ApprovalType;
  roleLabel?: string;
}

export interface MyRequests {
  sent: SentRequest[];
  received: ReceivedRequest[];
}
