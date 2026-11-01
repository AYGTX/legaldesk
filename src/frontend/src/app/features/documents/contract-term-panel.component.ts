import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LifecycleService } from '../../core/services/lifecycle.service';
import {
  ContractLifecycle, NoticeForm, NoticeUnit, Obligation, ObligationAction, ObligationKind, ObligationRecurrence, RenewalMode, TermDecision
} from '../../core/models/lifecycle.models';
import { lifecycleStageTone } from '../../core/utils/contract-term';
import { problemText } from '../../core/utils/problem-text';

/**
 * A signed contract's life (M10): its term as a person confirmed it, the deadlines computed from it, the decision that
 * stops the alerts, its obligations, and what happened to it. Nothing here is filled in by the model (R-M12-9).
 */
@Component({
  selector: 'sf-contract-term-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <mat-card class="term-card" [class.wide]="wide()">
      <mat-card-header>
        <mat-card-title class="term-title">
          <mat-icon class="term-icon">update</mat-icon> {{ 'lifecycle.title' | translate }}
        </mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @if (error()) { <div class="term-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }

        <!-- Term -->
        @if (editing()) {
          <div class="term-form">
            <div class="term-form-row">
              <label class="f"><span>{{ 'lifecycle.term.start' | translate }}</span><input type="date" [(ngModel)]="form.startDate" /></label>
              <label class="f"><span>{{ 'lifecycle.term.end' | translate }}</span><input type="date" [(ngModel)]="form.endDate" /></label>
              <label class="f"><span>{{ 'lifecycle.term.renewal' | translate }}</span>
                <select [(ngModel)]="form.renewalMode">
                  @for (m of renewalModes; track m) { <option [value]="m">{{ ('lifecycle.renewalMode.' + m) | translate }}</option> }
                </select>
              </label>
              @if (form.renewalMode !== 'None') {
                <label class="f"><span>{{ 'lifecycle.term.renewalMonths' | translate }}</span><input type="number" min="1" max="120" [(ngModel)]="form.renewalMonths" /></label>
              }
            </div>
            <div class="term-form-row">
              <label class="f"><span>{{ 'lifecycle.term.notice' | translate }}</span>
                <span class="pair">
                  <input type="number" min="1" max="730" [(ngModel)]="form.noticeAmount" [placeholder]="'lifecycle.term.noNotice' | translate" />
                  <select [(ngModel)]="form.noticeUnit">
                    <option value="Days">{{ 'lifecycle.noticeUnit.Days' | translate }}</option>
                    <option value="Months">{{ 'lifecycle.noticeUnit.Months' | translate }}</option>
                  </select>
                </span>
              </label>
              <label class="f"><span>{{ 'lifecycle.term.noticeForm' | translate }}</span>
                <select [(ngModel)]="form.noticeForm">
                  <option [ngValue]="null">—</option>
                  @for (n of noticeForms; track n) { <option [ngValue]="n">{{ ('lifecycle.noticeForm.' + n) | translate }}</option> }
                </select>
              </label>
            </div>
            <p class="hint">{{ 'lifecycle.term.confirmHint' | translate }}</p>
            <div class="actions">
              <button mat-button (click)="editing.set(false)">{{ 'common.cancel' | translate }}</button>
              <button mat-flat-button color="primary" (click)="saveTerm()" [disabled]="busy() || !form.endDate">{{ 'lifecycle.term.save' | translate }}</button>
            </div>
          </div>
        } @else if (data()?.term; as t) {
          <div class="term-stage-row">
            <span class="term-stage-chip" [ngClass]="'tone-' + tone(t.stage)">{{ ('documents.detail.contractTerm.stage.' + t.stage) | translate }}</span>
            @if (t.renewalMode === 'Automatic') { <span class="chip renew"><mat-icon>autorenew</mat-icon> {{ 'lifecycle.renewalMode.Automatic' | translate }}</span> }
            @if (!t.confirmed) { <span class="chip unset">{{ 'lifecycle.term.unconfirmed' | translate }}</span> }
            @if (t.decision !== 'None') { <span class="chip decided"><mat-icon>task_alt</mat-icon> {{ ('lifecycle.decision.' + t.decision) | translate }}</span> }
          </div>
          <div class="term-grid">
            <div class="cell"><span class="lbl">{{ 'lifecycle.term.start' | translate }}</span><span class="val">{{ (t.startDate | date:'d MMM y') || '—' }}</span></div>
            <div class="cell"><span class="lbl">{{ 'lifecycle.term.currentEnd' | translate }}</span><span class="val">{{ (t.currentEndDate | date:'d MMM y') || '—' }}</span></div>
            <div class="cell"><span class="lbl">{{ 'lifecycle.term.notice' | translate }}</span>
              <span class="val">{{ t.noticeAmount ? t.noticeAmount + ' ' + (('lifecycle.noticeUnit.' + t.noticeUnit) | translate) : '—' }}</span></div>
            <div class="cell"><span class="lbl">{{ 'lifecycle.term.noticeDeadline' | translate }}</span>
              <span class="val" [class.danger]="(t.daysToNoticeDeadline ?? 1) <= 0">{{ (t.noticeDeadline | date:'d MMM y') || '—' }}</span></div>
          </div>
          @if (t.confirmedByName) { <p class="hint">{{ 'lifecycle.term.confirmedBy' | translate: { name: t.confirmedByName, date: (t.confirmedAt | date:'d MMM y') } }}</p> }
          @if (canEdit()) {
            <div class="actions left">
              <button mat-stroked-button (click)="startEdit()">{{ 'lifecycle.term.edit' | translate }}</button>
              @if (t.confirmed && t.decision === 'None') {
                @for (d of decisions; track d) {
                  <button mat-stroked-button (click)="startDecision(d)">{{ ('lifecycle.decision.' + d) | translate }}</button>
                }
              }
            </div>
          }
          @if (deciding(); as d) {
            <div class="term-form decision">
              <p>{{ ('lifecycle.decisionHint.' + d) | translate }}</p>
              @if (d === 'Terminate') {
                <div class="term-form-row">
                  <label class="f"><span>{{ 'lifecycle.decisionForm.noticeSentAt' | translate }}</span><input type="date" [(ngModel)]="noticeSentAt" /></label>
                  <label class="f"><span>{{ 'lifecycle.decisionForm.proof' | translate }}</span>
                    <input type="file" accept="application/pdf,image/*,.eml,.msg" (change)="onProof($event)" /></label>
                </div>
              }
              <div class="actions">
                <button mat-button (click)="deciding.set(null)">{{ 'common.cancel' | translate }}</button>
                <button mat-flat-button color="primary" (click)="decide(d)" [disabled]="busy() || (d === 'Terminate' && (!noticeSentAt || !proof))">
                  {{ 'lifecycle.decisionForm.record' | translate }}
                </button>
              </div>
            </div>
          }
        } @else {
          <div class="term-empty">
            <mat-icon>update</mat-icon>
            <p>{{ 'lifecycle.term.none' | translate }}</p>
            @if (canEdit()) { <button mat-flat-button color="primary" (click)="startEdit()">{{ 'lifecycle.term.record' | translate }}</button> }
          </div>
        }

        <!-- Obligations (R-M10-5) -->
        <div class="section">
          <h4>{{ 'lifecycle.obligations.title' | translate }}</h4>
          @for (o of data()?.obligations ?? []; track o.id) {
            <div class="obligation" [class.muted]="o.status !== 'Upcoming'">
              <div class="o-main">
                <strong>{{ o.title }}</strong>
                <span class="o-meta">
                  {{ ('lifecycle.kind.' + o.kind) | translate }}
                  · {{ (o.dueDate | date:'d MMM y') || ('lifecycle.obligations.noDate' | translate) }}
                  @if (o.recurrence !== 'None') { · {{ ('lifecycle.recurrence.' + o.recurrence) | translate }} }
                  @if (o.ownerName) { · {{ o.ownerName }} }
                  · {{ ('lifecycle.status.' + o.status) | translate }}
                </span>
                @if (o.quote) { <span class="o-quote">« {{ o.quote }} »</span> }
                @if (!o.confirmed) { <span class="chip unset">{{ 'lifecycle.obligations.toConfirm' | translate }}</span> }
              </div>
              @if (canEdit() && o.status === 'Upcoming') {
                <div class="o-actions">
                  @if (!o.confirmed) {
                    <button mat-icon-button (click)="editObligation(o)" [matTooltip]="'lifecycle.obligations.confirmHint' | translate"><mat-icon>edit_calendar</mat-icon></button>
                  } @else {
                    <button mat-icon-button (click)="act(o, 'Done')" [matTooltip]="'lifecycle.obligations.done' | translate"><mat-icon>check_circle</mat-icon></button>
                    <button mat-icon-button (click)="editObligation(o)" [matTooltip]="'common.edit' | translate"><mat-icon>edit</mat-icon></button>
                  }
                  <button mat-icon-button (click)="act(o, 'Waive')" [matTooltip]="'lifecycle.obligations.waive' | translate"><mat-icon>do_not_disturb_on</mat-icon></button>
                  <button mat-icon-button (click)="act(o, 'Delete')" [matTooltip]="'common.delete' | translate"><mat-icon>delete_outline</mat-icon></button>
                </div>
              }
            </div>
          } @empty {
            <p class="hint">{{ 'lifecycle.obligations.none' | translate }}</p>
          }
          @if (canEdit()) {
            @if (oForm) {
              <div class="term-form">
                <div class="term-form-row">
                  <label class="f wide"><span>{{ 'lifecycle.obligations.name' | translate }}</span><input [(ngModel)]="oForm.title" maxlength="300" /></label>
                  <label class="f"><span>{{ 'lifecycle.obligations.kind' | translate }}</span>
                    <select [(ngModel)]="oForm.kind">@for (k of kinds; track k) { <option [value]="k">{{ ('lifecycle.kind.' + k) | translate }}</option> }</select>
                  </label>
                  <label class="f"><span>{{ 'lifecycle.obligations.due' | translate }}</span><input type="date" [(ngModel)]="oForm.dueDate" /></label>
                  <label class="f"><span>{{ 'lifecycle.obligations.recurrence' | translate }}</span>
                    <select [(ngModel)]="oForm.recurrence">@for (r of recurrences; track r) { <option [value]="r">{{ ('lifecycle.recurrence.' + r) | translate }}</option> }</select>
                  </label>
                </div>
                <div class="actions">
                  <button mat-button (click)="oForm = null; oEditing = null">{{ 'common.cancel' | translate }}</button>
                  <button mat-flat-button color="primary" (click)="saveObligation()" [disabled]="busy() || !oForm.title.trim() || !oForm.dueDate">
                    {{ (oEditing && !oEditing.confirmed ? 'lifecycle.obligations.confirm' : 'common.save') | translate }}
                  </button>
                </div>
              </div>
            } @else {
              <button mat-stroked-button (click)="newObligation()"><mat-icon>add</mat-icon> {{ 'lifecycle.obligations.add' | translate }}</button>
            }
          }
        </div>

        @if (data()?.events?.length) {
          <details class="section">
            <summary>{{ 'lifecycle.history' | translate }}</summary>
            <ul class="events">
              @for (e of data()!.events; track $index) {
                <li>{{ e.at | date:'d MMM y' }} · {{ ('lifecycle.event.' + e.kind) | translate }} @if (e.details) { — {{ e.details }} } @if (e.byName) { · {{ e.byName }} }</li>
              }
            </ul>
          </details>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .term-card { border: 1px solid #dcd5f2; background: #f8f6ff; }
    .term-title { display: flex; align-items: center; gap: 6px; font-size: 15px; }
    .term-icon { color: #6750a4; font-size: 18px; width: 18px; height: 18px; }
    .term-stage-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 10px; }
    .term-stage-chip { font-size: 11px; font-weight: 700; border-radius: 8px; padding: 4px 10px; }
    .tone-ok { background: #e6f4ea; color: #1e6b2e; } .tone-warn { background: #fef3c7; color: #92400e; }
    .tone-danger { background: #fee2e2; color: #991b1b; } .tone-neutral { background: #f3f4f6; color: #6b7280; }
    .chip { display: inline-flex; align-items: center; gap: 4px; font-size: 11px; font-weight: 600; border-radius: 8px; padding: 3px 8px; }
    .chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .chip.renew { color: #6750a4; background: #ede9fe; } .chip.unset { color: #92400e; background: #fef3c7; } .chip.decided { color: #1e6b2e; background: #e6f4ea; }
    .term-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 24px; }
    .wide .term-grid { grid-template-columns: repeat(4, minmax(0, 1fr)); }
    .cell { display: flex; flex-direction: column; gap: 2px; } .lbl { font-size: 12px; color: #9ca3af; } .val { font-size: 14px; color: #374151; font-weight: 500; }
    .val.danger { color: #991b1b; }
    .hint { font-size: 12px; color: #6b7280; margin: 6px 0; }
    .actions { display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap; margin-top: 8px; } .actions.left { justify-content: flex-start; }
    .term-form { display: flex; flex-direction: column; gap: 10px; padding: 12px; border-radius: 10px; background: #fff; border: 1px solid #e5e1f5; margin: 8px 0; }
    .term-form-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 10px; }
    .f { display: flex; flex-direction: column; gap: 3px; font-size: 12px; color: #4a5566; } .f.wide { grid-column: span 2; }
    .f input, .f select { padding: 7px 8px; border: 1px solid #c7d0dc; border-radius: 6px; font: inherit; font-size: 13px; background: #fff; }
    .pair { display: flex; gap: 6px; } .pair input { width: 90px; }
    .term-empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 12px 0; color: #9ca3af; text-align: center; }
    .term-error { display: flex; align-items: center; gap: 8px; background: #fee2e2; color: #991b1b; border-radius: 8px; padding: 10px 12px; font-size: 13px; margin-bottom: 8px; }
    .section { margin-top: 16px; border-top: 1px solid #e5e1f5; padding-top: 12px; }
    .section h4 { margin: 0 0 8px; font-size: 14px; } .section summary { cursor: pointer; font-size: 13px; color: #4a5566; }
    .obligation { display: flex; align-items: flex-start; gap: 8px; padding: 6px 0; border-bottom: 1px solid #f0edf9; }
    .obligation.muted { opacity: .6; }
    .o-main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; font-size: 13px; }
    .o-meta { color: #6b7280; font-size: 12px; } .o-quote { font-style: italic; color: #4a5566; font-size: 12px; }
    .o-actions { display: flex; }
    .events { margin: 6px 0 0; padding-left: 18px; font-size: 12px; color: #4a5566; }
    @media (max-width: 600px) { .wide .term-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .f.wide { grid-column: auto; } }
  `]
})
export class ContractTermPanelComponent implements OnInit {
  readonly documentId = input.required<string>();
  readonly canEdit = input(false);
  readonly wide = input(false);
  readonly saved = output<void>();

  private readonly svc = inject(LifecycleService);
  private readonly translate = inject(TranslateService);

  readonly data = signal<ContractLifecycle | null>(null);
  readonly editing = signal(false);
  readonly deciding = signal<TermDecision | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly renewalModes: RenewalMode[] = ['None', 'Automatic', 'Option'];
  readonly noticeForms: NoticeForm[] = ['Letter', 'Email', 'RegisteredMail'];
  readonly decisions: TermDecision[] = ['Renew', 'Renegotiate', 'Terminate', 'LetExpire'];
  readonly kinds: ObligationKind[] = ['Payment', 'Report', 'InsuranceCertificate', 'Audit', 'Renewal', 'Other'];
  readonly recurrences: ObligationRecurrence[] = ['None', 'Monthly', 'Quarterly', 'Yearly'];

  form = { startDate: '', endDate: '', renewalMode: 'None' as RenewalMode, renewalMonths: 12 as number | null,
    noticeAmount: null as number | null, noticeUnit: 'Days' as NoticeUnit, noticeForm: null as NoticeForm | null };
  noticeSentAt = '';
  proof: File | null = null;
  oForm: { title: string; kind: ObligationKind; dueDate: string; recurrence: ObligationRecurrence; quote: string | null } | null = null;
  oEditing: Obligation | null = null;

  ngOnInit(): void { this.load(); }

  load(): void {
    this.svc.get(this.documentId()).subscribe({ next: d => this.data.set(d), error: () => this.data.set({ obligations: [], events: [] }) });
  }

  tone(stage: string): string { return lifecycleStageTone(stage as never); }

  startEdit(): void {
    const t = this.data()?.term;
    this.form = {
      startDate: t?.startDate?.substring(0, 10) ?? '', endDate: t?.currentEndDate?.substring(0, 10) ?? '',
      renewalMode: t?.renewalMode ?? 'None', renewalMonths: t?.renewalMonths ?? 12, noticeAmount: t?.noticeAmount ?? null,
      noticeUnit: t?.noticeUnit ?? 'Days', noticeForm: t?.noticeForm ?? null
    };
    this.error.set(null);
    this.editing.set(true);
  }

  saveTerm(): void {
    this.run(this.svc.setTerm(this.documentId(), {
      startDate: this.form.startDate || null, endDate: this.form.endDate || null, renewalMode: this.form.renewalMode,
      renewalMonths: this.form.renewalMode === 'None' ? null : this.form.renewalMonths, noticeAmount: this.form.noticeAmount || null,
      noticeUnit: this.form.noticeUnit, noticeForm: this.form.noticeForm
    }), () => this.editing.set(false));
  }

  startDecision(d: TermDecision): void { this.noticeSentAt = ''; this.proof = null; this.error.set(null); this.deciding.set(d); }

  onProof(event: Event): void { this.proof = (event.target as HTMLInputElement).files?.[0] ?? null; }

  decide(d: TermDecision): void {
    this.run(this.svc.decide(this.documentId(), d, this.noticeSentAt || null, this.proof), () => this.deciding.set(null));
  }

  newObligation(): void {
    this.oEditing = null;
    this.oForm = { title: '', kind: 'Payment', dueDate: '', recurrence: 'None', quote: null };
  }

  editObligation(o: Obligation): void {
    this.oEditing = o;
    this.oForm = { title: o.title, kind: o.kind, dueDate: o.dueDate?.substring(0, 10) ?? '', recurrence: o.recurrence, quote: o.quote ?? null };
  }

  saveObligation(): void {
    const f = this.oForm!;
    const request = { title: f.title.trim(), kind: f.kind, dueDate: f.dueDate || null, recurrence: f.recurrence, quote: f.quote };
    const editing = this.oEditing;
    if (!editing) {
      this.run(this.svc.addObligation(this.documentId(), request), () => this.oForm = null);
      return;
    }
    // A suggestion is confirmed once a person gave its date (R-M10-6).
    this.run(this.svc.updateObligation(this.documentId(), editing.id, request), () => {
      this.oForm = null;
      this.oEditing = null;
      if (!editing.confirmed) this.run(this.svc.act(this.documentId(), editing.id, 'Confirm'));
    });
  }

  act(o: Obligation, action: ObligationAction): void { this.run(this.svc.act(this.documentId(), o.id, action)); }

  private run(call: import('rxjs').Observable<unknown>, done?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: () => { this.busy.set(false); done?.(); this.load(); this.saved.emit(); },
      error: (err: unknown) => { this.busy.set(false); this.error.set(problemText(this.translate, err, 'lifecycle.failed')); }
    });
  }
}
