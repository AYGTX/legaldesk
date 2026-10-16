import { Component, computed, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { AuthService } from '../../core/auth/auth.service';
import { ClauseDialogComponent, ClauseDialogData } from '../../features/clauses/clause-dialog.component';
import {
  ClauseCategory, Readiness, ReadinessItem, ReadinessItemStatus, RISK_SEVERITY_ORDER
} from '../../core/models';

/** Best-effort mapping from a free-text finding title to the clause library's category. */
const CATEGORY_KEYWORDS: [RegExp, ClauseCategory][] = [
  [/confiden/i, 'Confidentiality'],
  [/liab/i, 'Liability'],
  [/terminat/i, 'Termination'],
  [/data protect|privacy|gdpr|donn[ée]es/i, 'DataProtection'],
  [/intellectual property|\bip\b|copyright|trademark|propri[ée]t[ée]/i, 'IntellectualProperty'],
  [/payment|invoic|pricing|fee|paiement/i, 'Payment'],
  [/force majeure/i, 'ForceMajeure'],
  [/governing law|jurisdiction|juridiction/i, 'GoverningLaw'],
  [/complian|conformit/i, 'Compliance'],
  [/warrant|garantie/i, 'Warranty'],
  [/non-?compete|exclusiv|concurrence/i, 'NonCompete']
];

/**
 * The actionable half of the pre-flight review: every adjustment the contract needs, each one
 * resolvable or dismissable in place. Ticking an item off re-grades the contract server-side, and
 * the new verdict comes straight back — which is why this emits the whole Readiness, not just an id.
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
          <p>{{ 'shared.readiness.checklist.empty' | translate }}</p>
        </div>
      } @else {
        <div class="checklist-header">
          <h3 class="checklist-title">
            {{ 'shared.readiness.checklist.title' | translate }}
            <span class="count-chip">{{ openItems().length }}</span>
          </h3>
          @if (handledItems().length > 0) {
            <button mat-button class="toggle-handled" (click)="showHandled.set(!showHandled())">
              <mat-icon>{{ showHandled() ? 'visibility_off' : 'visibility' }}</mat-icon>
              {{ (showHandled() ? 'shared.readiness.checklist.hideHandled' : 'shared.readiness.checklist.showHandled')
                  | translate: { count: handledItems().length } }}
            </button>
          }
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
                  <span class="item-name" [class.struck]="item.status !== 'Open'">{{ item.title }}</span>
                  <span class="severity-chip" [ngClass]="'sev-' + item.severity.toLowerCase()">
                    {{ ('documents.detail.severity' + item.severity) | translate }}
                  </span>
                  <span class="category-chip">
                    {{ ('shared.readiness.category.' + item.category) | translate }}
                  </span>
                </mat-panel-title>
              </mat-expansion-panel-header>

              <p class="item-detail">{{ item.detail }}</p>

              @if (item.whyItMatters) {
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
                  <span class="item-block-label">{{ 'documents.detail.analysis.proposedLanguage' | translate }}</span>
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

              @if (item.status !== 'Open' && item.resolutionNote) {
                <p class="resolution-note">{{ item.resolutionNote }}</p>
              }

              @if (!readonly()) {
                <div class="item-actions">
                  @if (item.status === 'Open') {
                    <button mat-flat-button color="primary" [disabled]="busyId() !== null"
                            (click)="setStatus(item, 'Resolved')">
                      <mat-icon>check</mat-icon> {{ 'shared.readiness.checklist.markResolved' | translate }}
                    </button>
                    @if (item.severity === 'Critical') {
                      <!-- Accepting a critical finding is a legal call: a manager, with a reason that goes on record. -->
                      <button mat-stroked-button [disabled]="busyId() !== null || !canAcceptCritical()"
                              (click)="startAccept(item)"
                              [matTooltip]="(canAcceptCritical() ? 'shared.readiness.checklist.acceptHint' : 'shared.readiness.checklist.acceptRole') | translate">
                        <mat-icon>gavel</mat-icon> {{ 'shared.readiness.checklist.accept' | translate }}
                      </button>
                    } @else {
                      <button mat-stroked-button [disabled]="busyId() !== null"
                              (click)="setStatus(item, 'Dismissed')"
                              [matTooltip]="'shared.readiness.checklist.dismissHint' | translate">
                        <mat-icon>block</mat-icon> {{ 'shared.readiness.checklist.dismiss' | translate }}
                      </button>
                    }
                  } @else {
                    <button mat-stroked-button [disabled]="busyId() !== null"
                            (click)="setStatus(item, 'Open')">
                      <mat-icon>undo</mat-icon> {{ 'shared.readiness.checklist.reopen' | translate }}
                    </button>
                  }
                </div>
                @if (acceptingId() === item.id) {
                  <div class="accept-box">
                    <label>{{ 'shared.readiness.checklist.acceptReason' | translate }}</label>
                    <textarea rows="2" [value]="acceptReason()" (input)="acceptReason.set($any($event.target).value)"></textarea>
                    <div class="inline-actions">
                      <button mat-flat-button color="primary"
                              [disabled]="busyId() !== null || acceptReason().trim().length === 0"
                              (click)="confirmAccept(item)">
                        {{ 'shared.readiness.checklist.acceptConfirm' | translate }}
                      </button>
                      <button mat-button (click)="acceptingId.set(null)">{{ 'common.cancel' | translate }}</button>
                    </div>
                  </div>
                }
              }
            </mat-expansion-panel>
          }
        </mat-accordion>
      }
    </div>
  `,
  styles: [`
    .checklist-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 10px; }
    .checklist-title { display: flex; align-items: center; gap: 8px; margin: 0; font-size: 14px; font-weight: 600; color: var(--g-text); }
    .count-chip { font-size: 11.5px; font-weight: 700; background: var(--g-blue-container); color: var(--g-blue); border-radius: 20px; padding: 1px 9px; }
    .toggle-handled { font-size: 12.5px; }

    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 22px 0; color: var(--g-text-muted); text-align: center; }
    .empty mat-icon { font-size: 30px; width: 30px; height: 30px; color: #1e6b2e; }
    .empty p { margin: 0; font-size: 13px; }

    .item-panel.handled { opacity: 0.62; }
    .item-title { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .status-icon { font-size: 19px; width: 19px; height: 19px; flex-shrink: 0; }
    .st-open { color: var(--g-text-muted); }
    .st-resolved { color: #1e6b2e; }
    .st-dismissed { color: var(--g-text-muted); }
    .item-name { font-size: 13.5px; font-weight: 600; color: var(--g-text); }
    .item-name.struck { text-decoration: line-through; }

    .severity-chip, .category-chip { font-size: 10.5px; font-weight: 700; border-radius: 6px; padding: 1px 7px; text-transform: uppercase; letter-spacing: 0.03em; }
    .category-chip { font-weight: 500; background: var(--g-surface-variant); color: var(--g-text-3); text-transform: none; letter-spacing: 0; }
    .sev-critical { background: #7f1d1d; color: #fff; }
    .sev-high { background: #fee2e2; color: #991b1b; }
    .sev-medium { background: #fef3c7; color: #92400e; }
    .sev-low { background: #e6f4ea; color: #1e6b2e; }

    .item-detail { margin: 0 0 10px; font-size: 13px; color: var(--g-text-3); line-height: 1.55; }
    .item-block { margin-bottom: 10px; }
    .item-block-label { display: block; font-size: 11px; font-weight: 700; color: var(--g-text-muted); text-transform: uppercase; letter-spacing: 0.04em; margin-bottom: 2px; }
    .item-block p { margin: 0; font-size: 13px; color: var(--g-text-2); line-height: 1.55; }
    .proposed-language { white-space: pre-wrap; font-family: 'Consolas', 'Courier New', monospace; font-size: 12.5px; background: var(--g-surface-variant); border: 1px solid var(--g-border-light); border-radius: 6px; padding: 10px; margin: 0 0 8px; line-height: 1.5; }
    .inline-actions, .item-actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .item-actions { margin-top: 12px; padding-top: 12px; border-top: 1px solid var(--g-border-light); }
    .accept-box { margin-top: 10px; padding: 10px 12px; border-radius: 8px; background: #fef7e0; display: flex; flex-direction: column; gap: 6px; }
    .accept-box label { font-size: 12px; font-weight: 500; color: #b06000; }
    .accept-box textarea { width: 100%; box-sizing: border-box; border: 1px solid #e8c36a; border-radius: 6px; padding: 8px; font: inherit; font-size: 13px; }
    .resolution-note { font-size: 12px; font-style: italic; color: var(--g-text-muted); margin: 0 0 8px; }
    .pre-line { white-space: pre-line; }
  `]
})
export class ReadinessChecklistComponent {
  readonly documentId = input.required<string>();
  readonly items = input<ReadinessItem[]>([]);
  /** Viewers who do not own the contract see the list but cannot change it. */
  readonly readonly = input(false);

  /** Emits the re-graded readiness returned by the API, so the parent can refresh its verdict. */
  readonly changed = output<Readiness>();

  private readonly documentService = inject(DocumentService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  /** Only a manager or administrator may accept a critical finding; the API enforces the same rule. */
  readonly canAcceptCritical = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    return roles.includes('Administrator') || roles.includes('Manager');
  });
  readonly acceptingId = signal<string | null>(null);
  readonly acceptReason = signal('');

  readonly busyId = signal<string | null>(null);
  readonly showHandled = signal(false);

  readonly openItems = computed(() => this.items().filter(i => i.status === 'Open'));
  readonly handledItems = computed(() => this.items().filter(i => i.status !== 'Open'));
  readonly visibleItems = computed(() =>
    this.showHandled() ? this.sorted(this.items()) : this.sorted(this.openItems()));

  private sorted(items: ReadinessItem[]): ReadinessItem[] {
    return [...items].sort((a, b) => {
      if (a.status !== b.status) return a.status === 'Open' ? -1 : 1;
      return RISK_SEVERITY_ORDER.indexOf(a.severity) - RISK_SEVERITY_ORDER.indexOf(b.severity);
    });
  }

  /** Critical and high findings open by default — the reader should not have to hunt for them. */
  isSevere(item: ReadinessItem): boolean {
    return item.severity === 'Critical' || item.severity === 'High';
  }

  statusIcon(status: ReadinessItemStatus): string {
    switch (status) {
      case 'Resolved': return 'check_circle';
      case 'Dismissed': return 'remove_circle_outline';
      default: return 'radio_button_unchecked';
    }
  }

  startAccept(item: ReadinessItem): void {
    this.acceptReason.set('');
    this.acceptingId.set(item.id);
  }

  confirmAccept(item: ReadinessItem): void {
    this.setStatus(item, 'Dismissed', this.acceptReason().trim());
  }

  setStatus(item: ReadinessItem, status: ReadinessItemStatus, note?: string): void {
    this.busyId.set(item.id);
    this.documentService.setReadinessItemStatus(this.documentId(), item.id, status, note).subscribe({
      next: readiness => {
        this.busyId.set(null);
        this.acceptingId.set(null);
        this.changed.emit(readiness);
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

  addToLibrary(item: ReadinessItem): void {
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
