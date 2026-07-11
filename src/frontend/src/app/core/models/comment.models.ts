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
}

export interface AddCommentRequest {
  content: string;
  parentCommentId?: string | null;
  page?: number | null;
  x?: number | null;
  y?: number | null;
}
