import { Component, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatAccordion, MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { AuthService } from '../../core/auth/auth.service';
import { ClauseDialogComponent, ClauseDialogData } from '../../features/clauses/clause-dialog.component';
import {
  ClauseCategory, Review, ReviewItem, ReviewItemStatus, RISK_SEVERITY_ORDER
} from '../../core/models';

/** Best-effort mapping from a finding title to the clause library's category. */
const CATEGORY_KEYWORDS: [RegExp, ClauseCategory][] = [
  [/confiden/i, 'Confidentiality'],
  [/liab|responsabilit/i, 'Liability'],
  [/terminat|résiliation/i, 'Termination'],
  [/data protect|privacy|gdpr|donn[ée]es/i, 'DataProtection'],
  [/intellectual property|\bip\b|copyright|trademark|propri[ée]t[ée]/i, 'IntellectualProperty'],
  [/payment|invoic|pricing|fee|paiement/i, 'Payment'],
  [/force majeure/i, 'ForceMajeure'],
  [/governing law|jurisdiction|juridiction/i, 'GoverningLaw'],
  [/complian|conformit/i, 'Compliance'],
  [/warrant|garantie/i, 'Warranty'],
  [/non-?compete|exclusiv|concurrence/i, 'NonCompete']
];

type Decision = 'Accepted' | 'NotApplicable';

/**
 * Harvey's findings on the contract (rework spec M8), each with where it comes from: the rule and its version, the
 * lawyer who approved it, how it was found, the contract's own words and the law. The owner fixes the text, accepts a
 * risk or marks a false positive, each with a reason; the API re-grades and the new review comes straight back.
 */
@Component({
  selector: 'sf-readiness-checklist',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatExpansionModule,
    MatTooltipModule, MatSnackBarModule, TranslatePipe
  ],
  template: `
    <div class="checklist">
      @if (items().length === 0) {
        <div class="empty">
          <mat-icon>task_alt</mat-icon>
          <p>{{ 'shared.review.empty' | translate }}</p>
        </div>
      } @else {
        <div class="checklist-header">
          <h3 class="checklist-title">
            {{ 'shared.review.title' | translate }}
            <span class="count-chip">{{ openItems().length }}</span>
          </h3>
          <span class="header-actions">
            <button mat-button class="toggle-handled" (click)="accordion()?.openAll()">
              <mat-icon>unfold_more</mat-icon> {{ 'shared.readiness.checklist.expandAll' | translate }}
            </button>
            <button mat-button class="toggle-handled" (click)="accordion()?.closeAll()">
              <mat-icon>unfold_less</mat-icon> {{ 'shared.readiness.checklist.collapseAll' | translate }}
            </button>
            @if (handledItems().length > 0) {
              <button mat-button class="toggle-handled" (click)="showHandled.set(!showHandled())">
                <mat-icon>{{ showHandled() ? 'visibility_off' : 'visibility' }}</mat-icon>
                {{ (showHandled() ? 'shared.readiness.checklist.hideHandled' : 'shared.readiness.checklist.showHandled')
                    | translate: { count: handledItems().length } }}
              </button>
            }
          </span>
        </div>

        <mat-accordion multi>
          @for (item of visibleItems(); track item.id) {
            <mat-expansion-panel [expanded]="item.status === 'Open' && isSevere(item)" class="item-panel"
                                 [class.handled]="item.status !== 'Open'">
              <mat-expansion-panel-header>
                <mat-panel-title class="item-title">
                  <mat-icon class="status-icon" [ngClass]="'st-' + item.status.toLowerCase()">
                    {{ statusIcon(item.status) }}
                  </mat-icon>
                  <span class="item-name">{{ item.title }}</span>
                  <span class="severity-chip" [ngClass]="'sev-' + item.severity.toLowerCase()">
                    {{ ('documents.detail.severity' + item.severity) | translate }}
                  </span>
                  @if (item.clauseReference) {
                    <span class="category-chip">{{ item.clauseReference }}</span>
                  }
                  @if (item.status !== 'Open') {
                    <span class="state-chip">{{ ('shared.review.status.' + item.status) | translate }}</span>
                  }
                </mat-panel-title>
              </mat-expansion-panel-header>

              <p class="item-detail">{{ item.detail }}</p>

              @if (item.quotes.length > 0) {
                <div class="item-block">
                  <span class="item-block-label">{{ 'shared.review.quotes' | translate }}
                    @if (fromOcr()) { <span class="ocr-chip">{{ 'shared.review.fromOcr' | translate }}</span> }
                  </span>
                  @for (q of item.quotes; track $index) {
                    <blockquote class="quote">« {{ q }} »</blockquote>
                  }
                </div>
              }

              @if (item.articles.length > 0) {
                <div class="item-block">
                  <span class="item-block-label">{{ 'shared.review.law' | translate }}</span>
                  @for (a of item.articles; track a.articleId) {
                    <div class="article">
                      <strong>{{ a.citation }}</strong>
                      <p class="article-quote">« {{ a.quote }} »</p>
                      @if (a.why) { <p class="article-why">→ {{ a.why }}</p> }
                      @if (a.explanation) {
                        <p class="article-plain"><em>{{ 'shared.review.inPlainWords' | translate }}</em> {{ a.explanation }}</p>
                      }
                    </div>
                  }
                </div>
              } @else if (item.whyItMatters) {
                <div class="item-block">
                  <span class="item-block-label">{{ 'documents.detail.analysis.whyItMatters' | translate }}</span>
                  <p class="pre-line">{{ item.whyItMatters }}</p>
                </div>
              }

              @if (item.recommendedAction) {
                <div class="item-block">
                  <span class="item-block-label">{{ 'documents.detail.analysis.recommendedAction' | translate }}</span>
                  <p>{{ item.recommendedAction }}</p>
                </div>
              }
              @if (item.proposedLanguage) {
                <div class="item-block">
                  <span class="item-block-label">{{ 'shared.review.modelClause' | translate }}</span>
                  <pre class="proposed-language">{{ item.proposedLanguage }}</pre>
                  <div class="inline-actions">
                    <button mat-stroked-button (click)="copyLanguage(item.proposedLanguage!)">
                      <mat-icon>content_copy</mat-icon> {{ 'documents.detail.analysis.copy' | translate }}
                    </button>
                    <button mat-stroked-button (click)="addToLibrary(item)">
                      <mat-icon>library_add</mat-icon> {{ 'documents.detail.analysis.addToLibrary' | translate }}
                    </button>
                  </div>
                </div>
              }

              <!-- Where the finding comes from (R-M8-11). -->
              <p class="origin">
                <mat-icon class="origin-icon">gavel</mat-icon>
                @if (item.ruleVersion != null) {
                  {{ 'shared.review.origin.rule' | translate: { version: item.ruleVersion } }}
                }
                @if (item.ruleApprovedBy) {
                  · {{ 'shared.review.origin.approved' | translate: { by: item.ruleApprovedBy, date: (item.ruleApprovedAt | date: 'mediumDate') } }}
                }
                @if (item.detectionKind) {
                  · {{ ('shared.review.detection.' + item.detectionKind) | translate }}
                }
                @if (item.confidence != null) {
                  · {{ 'shared.review.origin.confidence' | translate: { value: (item.confidence * 100 | number: '1.0-0') } }}
                }
              </p>

              @if (item.status === 'Fixed') {
                <p class="decision">{{ 'shared.review.fixedIn' | translate: { version: item.fixedInVersion } }}</p>
              } @else if (item.status !== 'Open') {
                <p class="decision">
                  {{ ('shared.review.status.' + item.status) | translate }}
                  @if (item.decidedBy) { — {{ item.decidedBy }} }
                  @if (item.decidedAt) { , {{ item.decidedAt | date: 'medium' }} }
                  @if (item.reason) { : « {{ item.reason }} » }
                </p>
              }

              @if (!readonly() && item.status !== 'Fixed') {
                <div class="item-actions">
                  @if (item.status === 'Open') {
                    <button mat-stroked-button [disabled]="busyId() !== null || !canDecide(item)"
                            (click)="startDecision(item, 'Accepted')"
                            [matTooltip]="(canDecide(item) ? 'shared.review.acceptHint' : 'shared.review.criticalRole') | translate">
                      <mat-icon>handshake</mat-icon> {{ 'shared.review.accept' | translate }}
                    </button>
                    <button mat-stroked-button [disabled]="busyId() !== null || !canDecide(item)"
                            (click)="startDecision(item, 'NotApplicable')"
                            [matTooltip]="(canDecide(item) ? 'shared.review.notApplicableHint' : 'shared.review.criticalRole') | translate">
                      <mat-icon>block</mat-icon> {{ 'shared.review.notApplicable' | translate }}
                    </button>
                  } @else {
                    <button mat-stroked-button [disabled]="busyId() !== null" (click)="decide(item, 'Open')">
                      <mat-icon>undo</mat-icon> {{ 'shared.readiness.checklist.reopen' | translate }}
                    </button>
                  }
                </div>
                @if (deciding()?.id === item.id) {
                  <div class="accept-box">
                    <label>{{ (deciding()!.decision === 'Accepted' ? 'shared.review.acceptReason' : 'shared.review.notApplicableReason') | translate }}</label>
                    <textarea rows="2" maxlength="1000" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
                    <div class="inline-actions">
                      <button mat-flat-button color="primary"
                              [disabled]="busyId() !== null || reason().trim().length === 0"
                              (click)="decide(item, deciding()!.decision, reason().trim())">
                        {{ 'shared.review.confirm' | translate }}
                      </button>
                      <button mat-button (click)="deciding.set(null)">{{ 'common.cancel' | translate }}</button>
                    </div>
                  </div>
                }
              }
            </mat-expansion-panel>
          }
        </mat-accordion>
      }

      @if (fixedItems().length > 0) {
        <details class="fixed">
          <summary>{{ 'shared.review.fixedTitle' | translate: { count: fixedItems().length } }}</summary>
          <ul>
            @for (f of fixedItems(); track f.id) {
              <li>{{ f.title }} @if (f.clauseReference) { · {{ f.clauseReference }} }
                — {{ 'shared.review.fixedIn' | translate: { version: f.fixedInVersion } }}</li>
            }
          </ul>
        </details>
      }
    </div>
  `,
  styles: [`
    .checklist-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 10px; flex-wrap: wrap; }
    .checklist-title { display: flex; align-items: center; gap: 8px; margin: 0; font-size: 14px; font-weight: 600; color: var(--g-text); }
    .count-chip { font-size: 11.5px; font-weight: 700; background: var(--g-blue-container); color: var(--g-blue); border-radius: 20px; padding: 1px 9px; }
    .toggle-handled { font-size: 12.5px; }
    .header-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 4px; }

    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 22px 0; color: var(--g-text-muted); text-align: center; }
    .empty mat-icon { font-size: 30px; width: 30px; height: 30px; color: #1e6b2e; }
    .empty p { margin: 0; font-size: 13px; }

    .item-panel.handled { opacity: 0.7; }
    .item-title { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .item-panel ::ng-deep .mat-expansion-panel-header { height: auto; min-height: 48px; padding-top: 10px; padding-bottom: 10px; }
    .item-panel ::ng-deep .mat-content { overflow: visible; }
    .item-name { flex: 1 1 220px; min-width: 0; white-space: normal; overflow-wrap: anywhere; font-size: 13.5px; font-weight: 600; color: var(--g-text); }
    .status-icon { font-size: 19px; width: 19px; height: 19px; flex-shrink: 0; }
    .st-open { color: var(--g-text-muted); }
    .st-accepted { color: #b06000; }
    .st-notapplicable { color: var(--g-text-muted); }
    .st-fixed { color: #1e6b2e; }

    .severity-chip, .category-chip, .state-chip { font-size: 10.5px; font-weight: 700; border-radius: 6px; padding: 1px 7px; }
    .severity-chip { text-transform: uppercase; letter-spacing: 0.03em; }
    .category-chip, .state-chip { font-weight: 500; background: var(--g-surface-variant); color: var(--g-text-3); }
    .sev-critical { background: #7f1d1d; color: #fff; }
    .sev-high { background: #fee2e2; color: #991b1b; }
    .sev-medium { background: #fef3c7; color: #92400e; }
    .sev-low { background: #e6f4ea; color: #1e6b2e; }

    .item-detail { margin: 0 0 10px; font-size: 13px; color: var(--g-text-3); line-height: 1.55; }
    .item-block { margin-bottom: 10px; }
    .item-block-label { display: block; font-size: 11px; font-weight: 700; color: var(--g-text-muted); text-transform: uppercase; letter-spacing: 0.04em; margin-bottom: 4px; }
    .item-block p { margin: 0; font-size: 13px; color: var(--g-text-2); line-height: 1.55; }
    .quote { margin: 0 0 6px; padding: 6px 10px; border-left: 3px solid var(--g-border); background: var(--g-surface-variant); font-size: 13px; color: var(--g-text-2); }
    .article { margin-bottom: 8px; font-size: 13px; }
    .article .article-quote { font-style: italic; }
    .article .article-why, .article .article-plain { color: var(--g-text-3); }
    .proposed-language { white-space: pre-wrap; font-family: 'Consolas', 'Courier New', monospace; font-size: 12.5px; background: var(--g-surface-variant); border: 1px solid var(--g-border-light); border-radius: 6px; padding: 10px; margin: 0 0 8px; line-height: 1.5; }
    .origin { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; font-size: 11.5px; color: var(--g-text-muted); margin: 0 0 8px; }
    .origin-icon { font-size: 14px; width: 14px; height: 14px; }
    .decision { font-size: 12px; font-style: italic; color: var(--g-text-muted); margin: 0 0 8px; }
    .inline-actions, .item-actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .item-actions { margin-top: 12px; padding-top: 12px; border-top: 1px solid var(--g-border-light); }
    .accept-box { margin-top: 10px; padding: 10px 12px; border-radius: 8px; background: #fef7e0; display: flex; flex-direction: column; gap: 6px; }
    .accept-box label { font-size: 12px; font-weight: 500; color: #b06000; }
    .accept-box textarea { width: 100%; box-sizing: border-box; border: 1px solid #e8c36a; border-radius: 6px; padding: 8px; font: inherit; font-size: 13px; }
    .fixed { margin-top: 14px; font-size: 12.5px; color: var(--g-text-3); }
    .fixed summary { cursor: pointer; font-weight: 600; }
    .fixed ul { margin: 6px 0 0; padding-left: 18px; }
    .pre-line { white-space: pre-line; }
    .ocr-chip { margin-left: 6px; font-size: 10px; font-weight: 600; text-transform: none; letter-spacing: 0; background: #fef3c7; color: #92400e; border-radius: 6px; padding: 1px 6px; }
    :host-context(html.dark) .accept-box { background: rgba(255, 196, 0, 0.08); }
  `]
})
export class ReadinessChecklistComponent {
  readonly documentId = input.required<string>();
  readonly items = input<ReviewItem[]>([]);
  readonly fixedItems = input<ReviewItem[]>([]);
  /** The words reviewed came from text recognition on a scan: quotes may carry reading errors (R-M2-4). */
  readonly fromOcr = input(false);
  /** Viewers who do not own the contract, and contracts already sent, show the list without actions. */
  readonly readonly = input(false);

  /** Emits the re-graded review returned by the API, so the parent can refresh its verdict. */
  readonly changed = output<Review>();

  private readonly documentService = inject(DocumentService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  /** Accepting a critical finding, or calling it not applicable, is a manager's call; the API enforces the same rule. */
  private readonly isManager = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    return roles.includes('Administrator') || roles.includes('Manager');
  });

  readonly deciding = signal<{ id: string; decision: Decision } | null>(null);
  readonly reason = signal('');
  readonly busyId = signal<string | null>(null);
  readonly accordion = viewChild(MatAccordion);
  readonly showHandled = signal(false);

  readonly openItems = computed(() => this.items().filter(i => i.status === 'Open'));
  readonly handledItems = computed(() => this.items().filter(i => i.status !== 'Open'));
  readonly visibleItems = computed(() =>
    this.showHandled() ? this.sorted(this.items()) : this.sorted(this.openItems()));

  private sorted(items: ReviewItem[]): ReviewItem[] {
    return [...items].sort((a, b) => {
      if (a.status !== b.status) return a.status === 'Open' ? -1 : 1;
      return RISK_SEVERITY_ORDER.indexOf(a.severity) - RISK_SEVERITY_ORDER.indexOf(b.severity);
    });
  }

  /** Critical and high findings open by default: the reader should not have to hunt for them. */
  isSevere(item: ReviewItem): boolean {
    return item.severity === 'Critical' || item.severity === 'High';
  }

  canDecide(item: ReviewItem): boolean {
    return item.severity !== 'Critical' || this.isManager();
  }

  statusIcon(status: ReviewItemStatus): string {
    switch (status) {
      case 'Fixed': return 'check_circle';
      case 'Accepted': return 'handshake';
      case 'NotApplicable': return 'remove_circle_outline';
      default: return 'radio_button_unchecked';
    }
  }

  startDecision(item: ReviewItem, decision: Decision): void {
    this.reason.set('');
    this.deciding.set({ id: item.id, decision });
  }

  decide(item: ReviewItem, status: ReviewItemStatus, reason?: string): void {
    this.busyId.set(item.id);
    this.documentService.decideReviewItem(this.documentId(), item.id, status, reason).subscribe({
      next: review => {
        this.busyId.set(null);
        this.deciding.set(null);
        this.changed.emit(review);
      },
      error: () => {
        this.busyId.set(null);
        this.snack.open(this.translate.instant('shared.readiness.checklist.updateFailed'), 'OK', { duration: 3000 });
      }
    });
  }

  copyLanguage(text: string): void {
    navigator.clipboard.writeText(text).then(
      () => this.snack.open(this.translate.instant('documents.detail.analysis.copied'), 'OK', { duration: 2500 }),
      () => this.snack.open(this.translate.instant('documents.detail.linkCopyFailed'), 'OK', { duration: 3000 })
    );
  }

  addToLibrary(item: ReviewItem): void {
    const dialogRef = this.dialog.open(ClauseDialogComponent, {
      width: '640px',
      data: {
        clause: null,
        prefill: {
          title: item.title,
          body: item.proposedLanguage ?? '',
          category: CATEGORY_KEYWORDS.find(([pattern]) => pattern.test(item.title))?.[1] ?? 'Other',
          riskLevel: item.severity === 'Critical' ? 'High' : item.severity
        }
      } as ClauseDialogData
    });

    dialogRef.afterClosed().subscribe(created => {
      if (created) this.snack.open(this.translate.instant('documents.detail.analysis.addedToLibrary'), 'OK', { duration: 3000 });
    });
  }
}
