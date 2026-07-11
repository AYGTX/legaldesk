import { Component, OnInit, computed, effect, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CommentService } from '../../core/services/comment.service';
import { HubService } from '../../core/services/hub.service';
import { AddCommentRequest, CommentDto } from '../../core/models';
import { resolveFileUrl } from '../../core/utils/file-url';

/**
 * Threaded document discussion, shared by the authenticated document-detail view and the
 * token-based signing experience. Exactly one of [documentId] (JWT endpoints) or [token]
 * (public signing endpoints) must be set. Resolve/delete moderation is only available in
 * authenticated mode — the backend has no token-based moderation endpoints.
 * Live refresh: authenticated hosts join the document's SignalR group, so the panel refetches
 * on the content-free "comments-changed" ping; token mode refreshes after each own action.
 */
@Component({
  selector: 'sf-comments-panel',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatSnackBarModule, MatTooltipModule,
    TranslatePipe
  ],
  template: `
    <div class="cp-root">
      <div class="cp-header">
        <mat-icon>forum</mat-icon>
        <span class="cp-title">{{ 'comments.title' | translate }}</span>
        @if (!loading()) { <span class="cp-count">{{ comments().length }}</span> }
      </div>

      @if (loading()) {
        <div class="cp-loading"><mat-spinner diameter="28"></mat-spinner></div>
      } @else {
        @if (topLevel().length === 0) {
          <div class="cp-empty">
            <mat-icon>chat_bubble_outline</mat-icon>
            <p>{{ 'comments.empty' | translate }}</p>
          </div>
        }

        @for (c of topLevel(); track c.id) {
          <div class="cp-thread" [class.cp-resolved]="c.isResolved">
            <div class="cp-comment">
              @if (avatarOf(c); as url) {
                <img class="cp-avatar-img" [src]="url" alt="" />
              } @else {
                <div class="cp-avatar" [class.cp-avatar-anon]="c.isAnonymized">{{ initialsOf(c) }}</div>
              }
              <div class="cp-body">
                <div class="cp-meta">
                  <span class="cp-author">{{ displayNameOf(c) }}</span>
                  @if (c.page) { <span class="cp-page-chip">{{ 'comments.pageTag' | translate: { page: c.page } }}</span> }
                  @if (c.isResolved) {
                    <span class="cp-resolved-chip"><mat-icon>check</mat-icon>{{ 'comments.resolved' | translate }}</span>
                  }
                  <span class="cp-time">{{ c.createdAt | date:'MMM d, HH:mm' }}</span>
                </div>
                <p class="cp-content">{{ c.content }}</p>
                <div class="cp-actions">
                  <button class="cp-link-btn" (click)="startReply(c.id)">{{ 'comments.reply' | translate }}</button>
                  @if (canResolve(c)) {
                    <button class="cp-link-btn" (click)="toggleResolve(c)">
                      {{ (c.isResolved ? 'comments.reopen' : 'comments.resolve') | translate }}
                    </button>
                  }
                  @if (canDelete(c)) {
                    <button class="cp-link-btn cp-danger" (click)="remove(c)">{{ 'comments.delete' | translate }}</button>
                  }
                </div>
              </div>
            </div>

            @for (r of repliesFor(c.id); track r.id) {
              <div class="cp-comment cp-reply">
                @if (avatarOf(r); as url) {
                  <img class="cp-avatar-img" [src]="url" alt="" />
                } @else {
                  <div class="cp-avatar" [class.cp-avatar-anon]="r.isAnonymized">{{ initialsOf(r) }}</div>
                }
                <div class="cp-body">
                  <div class="cp-meta">
                    <span class="cp-author">{{ displayNameOf(r) }}</span>
                    <span class="cp-time">{{ r.createdAt | date:'MMM d, HH:mm' }}</span>
                  </div>
                  <p class="cp-content">{{ r.content }}</p>
                  @if (canDelete(r)) {
                    <div class="cp-actions">
                      <button class="cp-link-btn cp-danger" (click)="remove(r)">{{ 'comments.delete' | translate }}</button>
                    </div>
                  }
                </div>
              </div>
            }

            @if (replyTo() === c.id) {
              <div class="cp-composer cp-reply-composer">
                <textarea [(ngModel)]="replyDraft" rows="2" maxlength="4000"
                          [placeholder]="'comments.replyPlaceholder' | translate"></textarea>
                <div class="cp-composer-actions">
                  <button mat-button (click)="cancelReply()">{{ 'common.cancel' | translate }}</button>
                  <button mat-flat-button color="primary" [disabled]="!replyDraft.trim() || posting()" (click)="postReply(c.id)">
                    {{ 'comments.send' | translate }}
                  </button>
                </div>
              </div>
            }
          </div>
        }

        <div class="cp-composer">
          <textarea [(ngModel)]="draft" rows="3" maxlength="4000"
                    [placeholder]="'comments.placeholder' | translate"></textarea>
          <div class="cp-composer-actions">
            <button mat-flat-button color="primary" [disabled]="!draft.trim() || posting()" (click)="post()">
              <mat-icon>send</mat-icon> {{ 'comments.send' | translate }}
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .cp-root { display: flex; flex-direction: column; gap: 12px; }
    .cp-header { display: flex; align-items: center; gap: 8px; font-weight: 500; }
    .cp-header mat-icon { color: #5f6368; }
    .cp-count { background: #e8edf2; color: #5f6368; border-radius: 10px; padding: 1px 8px; font-size: 12px; }
    .cp-loading { display: flex; justify-content: center; padding: 16px 0; }
    .cp-empty { text-align: center; color: #9aa0a6; padding: 12px 0; }
    .cp-empty mat-icon { font-size: 28px; width: 28px; height: 28px; }
    .cp-empty p { font-size: 13px; margin: 4px 0 0; }
    .cp-thread { border: 1px solid #e8edf2; border-radius: 10px; padding: 10px 12px; }
    .cp-thread.cp-resolved { background: #f4f9f4; border-color: #cde8d2; }
    .cp-comment { display: flex; gap: 10px; }
    .cp-reply { margin: 10px 0 0 34px; }
    .cp-avatar, .cp-avatar-img { width: 28px; height: 28px; border-radius: 50%; flex-shrink: 0; }
    .cp-avatar { background: #1a73e8; color: #fff; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 600; }
    .cp-avatar-anon { background: #9aa0a6; }
    .cp-avatar-img { object-fit: cover; }
    .cp-body { min-width: 0; flex: 1; }
    .cp-meta { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .cp-author { font-size: 13px; font-weight: 600; }
    .cp-time { font-size: 11px; color: #9aa0a6; }
    .cp-page-chip { font-size: 11px; background: #e8f0fe; color: #1a73e8; border-radius: 8px; padding: 0 6px; }
    .cp-resolved-chip { display: inline-flex; align-items: center; gap: 2px; font-size: 11px; color: #1e6b2e; }
    .cp-resolved-chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .cp-content { font-size: 13px; margin: 4px 0 6px; white-space: pre-wrap; word-break: break-word; }
    .cp-actions { display: flex; gap: 12px; }
    .cp-link-btn { background: none; border: none; padding: 0; font-size: 12px; color: #1a73e8; cursor: pointer; }
    .cp-link-btn.cp-danger { color: #c5221f; }
    .cp-composer textarea { width: 100%; box-sizing: border-box; border: 1px solid #c7d0dc; border-radius: 8px; padding: 8px 10px; font: inherit; font-size: 13px; resize: vertical; }
    .cp-composer textarea:focus { outline: 2px solid #1a73e8; outline-offset: -1px; }
    .cp-composer-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 6px; }
    .cp-reply-composer { margin: 10px 0 0 34px; }
  `]
})
export class CommentsPanelComponent implements OnInit {
  /** Authenticated mode: loads via /documents/{id}/comments. */
  readonly documentId = input<string | null>(null);
  /** Signing-session mode: loads via /sign/{token}/comments. */
  readonly token = input<string | null>(null);
  /** Owner/admin viewers may resolve/delete any thread (authenticated mode only). */
  readonly canModerate = input(false);

  private readonly commentService = inject(CommentService);
  private readonly hub = inject(HubService);
  private readonly translate = inject(TranslateService);
  private readonly snack = inject(MatSnackBar);

  readonly comments = signal<CommentDto[]>([]);
  readonly loading = signal(true);
  readonly posting = signal(false);
  readonly replyTo = signal<string | null>(null);

  draft = '';
  replyDraft = '';

  readonly topLevel = computed(() => this.comments().filter(c => !c.parentCommentId));

  constructor() {
    effect(() => {
      const change = this.hub.lastCommentsChange();
      const docId = this.documentId();
      if (change && docId && change.documentId === docId) this.load(false);
    });
  }

  ngOnInit(): void {
    this.load();
  }

  repliesFor(parentId: string): CommentDto[] {
    return this.comments().filter(c => c.parentCommentId === parentId);
  }

  displayNameOf(c: CommentDto): string {
    return c.isAnonymized ? this.translate.instant('comments.participant') : c.authorName;
  }

  initialsOf(c: CommentDto): string {
    if (c.isAnonymized) return '?';
    return c.authorName.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);
  }

  avatarOf(c: CommentDto): string | null {
    return resolveFileUrl(c.authorAvatarUrl);
  }

  canResolve(c: CommentDto): boolean {
    return !this.token() && (this.canModerate() || c.isMine);
  }

  canDelete(c: CommentDto): boolean {
    return !this.token() && (this.canModerate() || c.isMine);
  }

  load(showSpinner = true): void {
    if (showSpinner) this.loading.set(true);
    this.request().subscribe({
      next: list => { this.comments.set(list); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.snack.open(this.translate.instant('comments.loadFailed'), 'OK', { duration: 3000 });
      }
    });
  }

  post(): void {
    this.submit({ content: this.draft.trim() }, () => { this.draft = ''; });
  }

  startReply(parentId: string): void {
    this.replyTo.set(parentId);
    this.replyDraft = '';
  }

  cancelReply(): void {
    this.replyTo.set(null);
    this.replyDraft = '';
  }

  postReply(parentId: string): void {
    this.submit({ content: this.replyDraft.trim(), parentCommentId: parentId }, () => this.cancelReply());
  }

  toggleResolve(c: CommentDto): void {
    this.commentService.resolve(c.id, !c.isResolved).subscribe({
      next: () => this.load(false),
      error: () => this.snack.open(this.translate.instant('comments.actionFailed'), 'OK', { duration: 3000 })
    });
  }

  remove(c: CommentDto): void {
    if (!confirm(this.translate.instant('comments.deleteConfirm'))) return;
    this.commentService.delete(c.id).subscribe({
      next: () => this.load(false),
      error: () => this.snack.open(this.translate.instant('comments.actionFailed'), 'OK', { duration: 3000 })
    });
  }

  private submit(request: AddCommentRequest, onSuccess: () => void): void {
    if (!request.content) return;
    this.posting.set(true);
    const call = this.token()
      ? this.commentService.addForToken(this.token()!, request)
      : this.commentService.addForDocument(this.documentId()!, request);
    call.subscribe({
      next: () => { this.posting.set(false); onSuccess(); this.load(false); },
      error: () => {
        this.posting.set(false);
        this.snack.open(this.translate.instant('comments.postFailed'), 'OK', { duration: 3000 });
      }
    });
  }

  private request(): Observable<CommentDto[]> {
    return this.token()
      ? this.commentService.getForToken(this.token()!)
      : this.commentService.getForDocument(this.documentId()!);
  }
}
