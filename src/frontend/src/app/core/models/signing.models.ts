import { ApprovalType, ChecklistItemInfo, DocumentStatus, FieldType, WorkflowStepStatus } from './document.models';
import { WorkflowParticipant } from './workflow.models';

export type SignatureType = 'Drawn' | 'Typed' | 'Uploaded' | 'Stamp';

export interface SigningSession {
  documentId: string;
  documentTitle: string;
  storagePath: string;
  pageCount: number;
  message?: string;
  recipient: RecipientSession;
  fields: AssignedField[];
  isCompleted: boolean;
  isRejected: boolean;
  isExpired: boolean;
  isMyTurn: boolean;
  waitingForName?: string;
  expiresAt?: string;
  slaHours?: number;
  participants: WorkflowParticipant[];
  isHtmlTemplate?: boolean;
  renderedHtmlContent?: string;
  /** Expiry of this specific signing link — distinct from the document's overall SLA deadline (expiresAt above). */
  linkExpiresAt?: string;
  /** Lets the signing UI show the paused state ('ChangesRequested') to every participant. */
  documentStatus: DocumentStatus;
}

export interface RecipientSession {
  id: string;
  name: string;
  email: string;
  approvalType: ApprovalType;
  status: WorkflowStepStatus;
  /** Free-text participant label (e.g. "Legal team") — display only, capabilities come from approvalType. */
  roleLabel?: string;
  /** Prerequisites to check off before approving (Review/Approval roles only). */
  checklistItems?: ChecklistItemInfo[];
}

export interface AssignedField {
  id: string;
  fieldType: FieldType;
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
  rotation?: number;
  label?: string;
  placeholder?: string;
  isRequired: boolean;
  isSigned: boolean;
  defaultValue?: string;
  options?: string;
  tooltip?: string;
  fontFamily?: string;
  fontSize?: number;
  fontColor?: string;
  /** Radio fields sharing a group name form a single-choice group. */
  groupName?: string;
  /** Client-side only: the value/image rendered after signing in this session. */
  signedValue?: string;
}

export interface SignFieldRequest {
  signatureType: SignatureType;
  imageBase64?: string;
  typedText?: string;
  fontFamily?: string;
  location?: string;
}

export interface SignFieldResult {
  signatureId: string;
  allFieldsSigned: boolean;
  documentCompleted: boolean;
}
