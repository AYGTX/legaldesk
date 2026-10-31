/** Mirrors backend CommentDto (Features/Comments/CommentDto.cs). */
export interface CommentDto {
  id: string;
  documentId: string;
  userId: string | null;
  authorName: string;
  authorEmail: string | null;
  authorAvatarUrl: string | null;
  content: string;
  parentCommentId: string | null;
  page: number | null;
  x: number | null;
  y: number | null;
  isResolved: boolean;
  isMine: boolean;
  /** True when the viewer must not see the author's identity (HideRecipientIdentities documents). */
  isAnonymized: boolean;
  createdAt: string;
  /** Internal: the organisation only. Participants: the contract's participants see it too (R-M7-1). */
  visibility: CommentVisibility;
  /** The version the comment was written on. */
  versionNumber: number | null;
}

export type CommentVisibility = 'Internal' | 'Participants';

/** Someone a comment can mention: a member of the organisation, or a participant of the contract. */
export interface Mentionable {
  id: string;
  name: string;
  email: string;
  isParticipant: boolean;
}

export interface AddCommentRequest {
  content: string;
  parentCommentId?: string | null;
  page?: number | null;
  x?: number | null;
  y?: number | null;
  visibility?: CommentVisibility;
  mentionIds?: string[];
}
