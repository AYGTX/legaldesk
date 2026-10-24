import { Component, ElementRef, ViewChild, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ChatCitation, ChatTurn } from '../../core/models/contract-chat.model';

interface Message extends ChatTurn {
  citations?: ChatCitation[];
  /** False when the model gave no excerpt that checks out against the contract. */
  grounded?: boolean;
  error?: boolean;
}

/** Suggested first questions: shown until the first message, translated under documents.detail.chat.suggestions. */
const SUGGESTIONS = ['term', 'termination', 'liability', 'payment'] as const;

/**
 * "Ask the contract": a conversation about one contract. The server answers only from the contract's text and returns
 * the excerpts that back the answer, each already checked word for word; an answer with none is flagged as unverified.
 * The conversation lives here and is sent back with every question, so nothing about it is stored on the server.
 */
@Component({
  selector: 'sf-contract-chat-panel',
  standalone: true,
  imports: [FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe],
  template: `
    <mat-card class="chat-card">
      @if (messages().length) {
        <mat-card-header>
          <mat-card-title class="chat-title">
            <mat-icon>forum</mat-icon> {{ 'documents.detail.chat.title' | translate }}
          </mat-card-title>
          <button mat-icon-button class="clear-btn" (click)="clear()" [matTooltip]="'documents.detail.chat.clear' | translate">
            <mat-icon>delete_sweep</mat-icon>
          </button>
        </mat-card-header>
      }
      <mat-card-content>
        <div class="thread" #thread aria-live="polite">
          @if (!messages().length) {
            <div class="empty-state">
              <span class="empty-icon"><mat-icon>auto_awesome</mat-icon></span>
              <h2 class="empty-title">{{ 'documents.detail.chat.title' | translate }}</h2>
              <p class="empty">{{ 'documents.detail.chat.empty' | translate }}</p>
              <div class="suggestions">
                @for (s of suggestions; track s) {
                  <button class="pill" type="button" (click)="askSuggestion(s)">
                    {{ ('documents.detail.chat.suggestions.' + s) | translate }}
                  </button>
                }
              </div>
            </div>
          }
          @for (m of messages(); track $index) {
            <div class="msg" [class.user]="m.role === 'user'" [class.error]="m.error">
              <div class="bubble">{{ m.content }}</div>
              @if (m.role === 'assistant' && !m.error) {
                @for (c of m.citations ?? []; track c.quote) {
                  <blockquote class="quote">
                    <mat-icon class="ok" [matTooltip]="'documents.detail.chat.verifiedHint' | translate">verified</mat-icon>
                    <span>« {{ c.quote }} »</span>
                  </blockquote>
                }
                @if (m.grounded === false) {
                  <div class="unverified">
                    <mat-icon>warning_amber</mat-icon> {{ 'documents.detail.chat.unverified' | translate }}
                  </div>
                }
              }
            </div>
          }
          @if (busy()) {
            <div class="msg"><div class="bubble thinking"><mat-spinner diameter="16"></mat-spinner> {{ 'documents.detail.chat.thinking' | translate }}</div></div>
          }
        </div>

        <form class="composer" (ngSubmit)="send()">
          <textarea rows="2" name="draft" [(ngModel)]="draft" [disabled]="busy()" maxlength="1000"
                    [placeholder]="'documents.detail.chat.placeholder' | translate"
                    (keydown.enter)="onEnter($event)"></textarea>
          <button class="send-btn" type="submit" [disabled]="busy() || !draft.trim()"
                  [attr.aria-label]="'documents.detail.chat.send' | translate" [matTooltip]="'documents.detail.chat.send' | translate">
            <mat-icon>send</mat-icon>
          </button>
        </form>
        <p class="disclaimer">{{ 'documents.detail.chat.disclaimer' | translate }}</p>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .chat-card { min-height: 460px; display: flex; flex-direction: column; }
    .chat-card mat-card-content { flex: 1; display: flex; flex-direction: column; }
    .chat-title { display: flex; align-items: center; gap: 8px; }
    .clear-btn { margin-left: auto; }
    .chat-note { display: flex; gap: 6px; align-items: flex-start; font-size: 12px; color: #5f6368; margin: 0 0 8px; }
    .chat-note mat-icon { font-size: 16px; width: 16px; height: 16px; flex: 0 0 auto; }
    .thread { flex: 1; max-height: 420px; overflow-y: auto; display: flex; flex-direction: column; gap: 10px; padding: 4px 2px 8px; }
    .thread:has(.empty-state) { max-height: none; justify-content: center; }
    .empty-state { display: flex; flex-direction: column; align-items: center; text-align: center; padding: 32px 16px; }
    .empty-icon { display: inline-flex; padding: 12px; border-radius: 16px; background: #e8f0fe; color: #1a73e8; }
    .empty-icon mat-icon { font-size: 26px; width: 26px; height: 26px; }
    .empty-title { margin: 16px 0 4px; font-size: 26px; font-weight: 600; }
    .empty { color: #5f6368; font-size: 14px; margin: 0; max-width: 420px; }
    .suggestions { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; margin-top: 24px; }
    .pill { border: 1px solid #dadce0; background: transparent; border-radius: 99px; padding: 6px 14px; font: inherit; font-size: 13.5px; cursor: pointer; color: inherit; }
    .pill:hover { border-color: #1a73e8; color: #1a73e8; }
    .msg { display: flex; flex-direction: column; gap: 6px; max-width: 92%; align-self: flex-start; }
    .msg.user { align-self: flex-end; align-items: flex-end; }
    .bubble { background: #f1f3f4; border-radius: 12px; padding: 8px 12px; white-space: pre-wrap; font-size: 14px; line-height: 1.45; }
    .msg.user .bubble { background: #e8f0fe; }
    .msg.error .bubble { background: #fce8e6; color: #a50e0e; }
    .thinking { display: flex; align-items: center; gap: 8px; color: #5f6368; }
    .quote { margin: 0; padding: 6px 10px; border-left: 3px solid #188038; background: #f6fbf7; font-size: 13px; font-style: italic;
             display: flex; gap: 6px; align-items: flex-start; border-radius: 0 6px 6px 0; }
    .quote .ok { color: #188038; font-size: 16px; width: 16px; height: 16px; flex: 0 0 auto; }
    .unverified { display: flex; gap: 6px; align-items: center; font-size: 12px; color: #b06000; background: #fef7e0; border-radius: 6px; padding: 4px 8px; }
    .unverified mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .composer { display: flex; gap: 8px; align-items: flex-end; margin-top: 12px; border: 1px solid #dadce0; border-radius: 12px; padding: 8px; }
    .composer:focus-within { border-color: #1a73e8; box-shadow: 0 0 0 2px rgba(26, 115, 232, 0.18); }
    .composer textarea { flex: 1; resize: none; border: 0; outline: none; background: transparent; padding: 6px 8px; font: inherit; }
    .send-btn { display: inline-flex; align-items: center; justify-content: center; border: 0; cursor: pointer; border-radius: 8px; width: 36px; height: 36px; background: #1a73e8; color: #fff; }
    .send-btn:disabled { opacity: 0.4; cursor: default; }
    .send-btn mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .disclaimer { font-size: 11px; color: #80868b; margin: 6px 0 0; }
  `]
})
export class ContractChatPanelComponent {
  readonly documentId = input.required<string>();

  private readonly docs = inject(DocumentService);
  private readonly translate = inject(TranslateService);

  @ViewChild('thread') private thread?: ElementRef<HTMLDivElement>;

  readonly suggestions = SUGGESTIONS;
  readonly messages = signal<Message[]>([]);
  readonly busy = signal(false);
  draft = '';

  onEnter(event: Event): void {
    const e = event as KeyboardEvent;
    if (e.shiftKey) return; // Shift+Enter adds a line
    e.preventDefault();
    this.send();
  }

  send(): void {
    const text = this.draft.trim();
    if (text) this.ask(text);
  }

  /** Pipes are not allowed in event bindings, so the suggestion is translated here. */
  askSuggestion(key: string): void {
    this.ask(this.translate.instant('documents.detail.chat.suggestions.' + key));
  }

  ask(question: string): void {
    if (this.busy()) return;
    // The history sent back is what was said so far, without the error bubbles.
    const history: ChatTurn[] = this.messages()
      .filter(m => !m.error)
      .map(m => ({ role: m.role, content: m.content }));
    this.messages.update(ms => [...ms, { role: 'user', content: question }]);
    this.draft = '';
    this.busy.set(true);
    this.scrollDown();

    const language = (this.translate.currentLang() ?? 'fr').startsWith('en') ? 'en' : 'fr';
    this.docs.askContract(this.documentId(), { question, history, language }).subscribe({
      next: reply => {
        this.messages.update(ms => [...ms, {
          role: 'assistant', content: reply.answer, citations: reply.citations, grounded: reply.grounded
        }]);
        this.busy.set(false);
        this.scrollDown();
      },
      error: (err: { status?: number }) => {
        const key = err?.status === 503 ? 'notConfigured' : err?.status === 403 ? 'forbidden'
          : err?.status === 422 ? 'unreadable' : 'failed';
        this.messages.update(ms => [...ms, {
          role: 'assistant', content: this.translate.instant('documents.detail.chat.errors.' + key), error: true
        }]);
        this.busy.set(false);
        this.scrollDown();
      }
    });
  }

  clear(): void {
    this.messages.set([]);
  }

  private scrollDown(): void {
    setTimeout(() => {
      const el = this.thread?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }
}
