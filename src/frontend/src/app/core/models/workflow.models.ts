import { WorkflowStepStatus } from './document.models';

export interface WorkflowParticipant {
  order: number;
  name: string;
  status: WorkflowStepStatus;
  isCurrent: boolean;
  isMe?: boolean;
  signedAt?: string;
}
