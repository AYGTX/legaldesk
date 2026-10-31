import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { AddCommentRequest, CommentDto, Mentionable } from '../models';

@Injectable({ providedIn: 'root' })
export class CommentService {
  private readonly api = inject(ApiService);

  /** Authenticated document view (owner, admin, or recipient with an account). */
  getForDocument(documentId: string): Observable<CommentDto[]> {
    return this.api.get<CommentDto[]>(`/v1/documents/${documentId}/comments`);
  }

  addForDocument(documentId: string, request: AddCommentRequest): Observable<CommentDto> {
    return this.api.post<CommentDto>(`/v1/documents/${documentId}/comments`, request);
  }

  /** Who a comment on this contract can mention. */
  getMentionables(documentId: string): Observable<Mentionable[]> {
    return this.api.get<Mentionable[]>(`/v1/documents/${documentId}/mentionables`);
  }

  resolve(commentId: string, resolved: boolean): Observable<void> {
    return this.api.put<void>(`/v1/comments/${commentId}/resolve`, { resolved });
  }

  delete(commentId: string): Observable<void> {
    return this.api.delete<void>(`/v1/comments/${commentId}`);
  }

  /** Signing session (token-authenticated, works for external signers with no account). */
  getForToken(token: string): Observable<CommentDto[]> {
    return this.api.get<CommentDto[]>(`/v1/sign/${token}/comments`);
  }

  addForToken(token: string, request: AddCommentRequest): Observable<CommentDto> {
    return this.api.post<CommentDto>(`/v1/sign/${token}/comments`, request);
  }
}
