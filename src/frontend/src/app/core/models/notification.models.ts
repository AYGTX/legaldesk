/** Must stay in step with the backend NotificationType enum — a missing member falls back to a
 *  generic bell icon, which is how ObligationDue once went unstyled. */
export type NotificationType =
  | 'DocumentReceived' | 'Reminder' | 'Completed' | 'Rejected'
  | 'Expired' | 'WorkflowFinished' | 'DelegationRequest' | 'CommentMention'
  | 'ChangesRequested' | 'ObligationDue' | 'ContractTermAlert';

export interface AppNotification {
  id: string;
  type: NotificationType;
  subject: string;
  body: string;
  isRead: boolean;
  documentId?: string;
  createdAt: string;
}

export interface NotificationsResult {
  unreadCount: number;
  items: AppNotification[];
}
