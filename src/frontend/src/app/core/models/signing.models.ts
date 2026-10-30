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
  /** Values other participants already filled in, shown read-only over the PDF. */
  filledFields?: FilledField[];
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
  /** What the participant must read before acting, and whether they already confirmed it. */
  reviewGate?: ReviewGate;
}

export type ReviewVerdictName = 'NotEvaluated' | 'Ready' | 'NeedsWork' | 'Blocked';

export interface ReviewGate {
  /** The participant has a task (sign, approve, review), so they confirm they read the contract first. */
  required: boolean;
  acknowledged: boolean;
  /** The AI review is for the organisation's own members; outside signers read the contract without it. */
  reviewRestricted: boolean;
  review?: AiReview;
}

export interface AiReview {
  verdict: ReviewVerdictName;
  score?: number | null;
  riskScore?: number | null;
  openCount: number;
  criticalCount: number;
  handledCount: number;
  items: AiReviewItem[];
}

export interface AiReviewItem {
  severity: string;
  title: string;
  detail: string;
  whyItMatters?: string;
  recommendedAction?: string;
  status: 'Open' | 'Fixed' | 'Accepted' | 'NotApplicable';
  reason?: string;
  clauseReference?: string;
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

export interface FilledField {
  id: string;
  fieldType: string;
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
  rotation?: number;
  value?: string;
  signerName?: string;
  fontFamily?: string;
  fontSize?: number;
  fontColor?: string;
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
