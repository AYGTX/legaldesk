export type NotificationType =
  | 'DocumentReceived' | 'Reminder' | 'Completed' | 'Rejected'
  | 'Expired' | 'WorkflowFinished' | 'DelegationRequest' | 'CommentMention'
  | 'ChangesRequested';

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
