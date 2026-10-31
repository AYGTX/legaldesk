import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Observable } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TeamService } from '../../core/services/team.service';
import { UserService } from '../../core/services/user.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { UserDto } from '../../core/models';
import { HolidayDto, TeamDto } from '../../core/models/team.models';

/**
 * Teams that share a step of a circuit (any member acts for the team), and the organisation's days off, which due
 * dates skip (M4). Fixed public holidays come seeded; holidays that move every year are added here each year.
 */
@Component({
  selector: 'sf-teams',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatSlideToggleModule, MatProgressSpinnerModule, MatSnackBarModule, TranslatePipe
  ],
  template: `
    <div class="tm-page">
      <section class="tm-card">
        <div class="tm-head">
          <div>
            <h2>{{ 'admin.teams.title' | translate }}</h2>
            <p class="hint">{{ 'admin.teams.hint' | translate }}</p>
          </div>
          @if (!editing()) {
            <button mat-flat-button color="primary" (click)="startCreate()"><mat-icon>group_add</mat-icon> {{ 'admin.teams.new' | translate }}</button>
          }
        </div>

        @if (editing()) {
          <div class="tm-editor">
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <mat-label>{{ 'admin.teams.name' | translate }}</mat-label>
              <input matInput maxlength="120" [(ngModel)]="name" />
            </mat-form-field>
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <mat-label>{{ 'admin.teams.description' | translate }}</mat-label>
              <input matInput maxlength="500" [(ngModel)]="description" />
            </mat-form-field>
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <mat-label>{{ 'admin.teams.members' | translate }}</mat-label>
              <mat-select multiple [(ngModel)]="memberIds">
                @for (u of users(); track u.id) { <mat-option [value]="u.id">{{ u.fullName }} ({{ u.email }})</mat-option> }
              </mat-select>
            </mat-form-field>
            @if (error()) { <div class="tm-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div> }
            <div class="tm-actions">
              <button mat-button (click)="editing.set(false)">{{ 'common.cancel' | translate }}</button>
              <button mat-flat-button color="primary" (click)="save()" [disabled]="saving()">{{ 'common.save' | translate }}</button>
            </div>
          </div>
        }

        @if (loading()) {
          <div class="tm-loading"><mat-spinner diameter="28"></mat-spinner></div>
        } @else if (teams().length === 0) {
          <p class="tm-empty">{{ 'admin.teams.empty' | translate }}</p>
        } @else {
          <ul class="tm-list">
            @for (t of teams(); track t.id) {
              <li>
                <div class="tm-main">
                  <strong>{{ t.name }}</strong>
                  @if (t.description) { <span class="muted">{{ t.description }}</span> }
                  <span class="tm-members">
                    @if (activeMembers(t) === 0) {
                      <span class="tm-warn"><mat-icon>warning_amber</mat-icon> {{ 'admin.teams.noActiveMember' | translate }}</span>
                    } @else {
                      {{ 'admin.teams.memberCount' | translate: { count: activeMembers(t) } }} · {{ memberNames(t) }}
                    }
                  </span>
                </div>
                <button mat-icon-button (click)="startEdit(t)" [attr.aria-label]="'common.edit' | translate"><mat-icon>edit</mat-icon></button>
                <button mat-icon-button (click)="removeTeam(t)" [attr.aria-label]="'common.delete' | translate"><mat-icon>delete_outline</mat-icon></button>
              </li>
            }
          </ul>
        }
      </section>

      <section class="tm-card">
        <h2>{{ 'admin.holidays.title' | translate }}</h2>
        <p class="hint">{{ 'admin.holidays.hint' | translate }}</p>

        <div class="tm-holiday-form">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ 'admin.holidays.date' | translate }}</mat-label>
            <input matInput type="date" [(ngModel)]="holidayDate" />
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
            <mat-label>{{ 'admin.holidays.name' | translate }}</mat-label>
            <input matInput maxlength="120" [(ngModel)]="holidayName" />
          </mat-form-field>
          <mat-slide-toggle [(ngModel)]="holidayEveryYear">{{ 'admin.holidays.everyYear' | translate }}</mat-slide-toggle>
          <button mat-stroked-button (click)="addHoliday()" [disabled]="!holidayDate || !holidayName.trim()">
            <mat-icon>add</mat-icon> {{ 'admin.holidays.add' | translate }}
          </button>
        </div>

        <ul class="tm-list">
          @for (h of sortedHolidays(); track h.id) {
            <li>
              <div class="tm-main">
                <strong>{{ h.everyYear ? (h.date | date:'d MMMM') : (h.date | date:'d MMMM y') }}</strong>
                <span>{{ h.name }}</span>
                <span class="muted">{{ (h.everyYear ? 'admin.holidays.yearly' : 'admin.holidays.once') | translate }}</span>
              </div>
              <button mat-icon-button (click)="removeHoliday(h)" [attr.aria-label]="'common.delete' | translate"><mat-icon>delete_outline</mat-icon></button>
            </li>
          }
        </ul>
      </section>
    </div>
  `,
  styles: [`
    .tm-page { display: flex; flex-direction: column; gap: 20px; max-width: 920px; }
    .tm-card { background: var(--sf-surface, #fff); border: 1px solid #e3e8ef; border-radius: 12px; padding: 20px; }
    .tm-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    h2 { margin: 0 0 4px; font-size: 18px; }
    .hint { margin: 0 0 12px; color: #5f6b7a; font-size: 13px; }
    .muted { color: #6b7686; font-size: 13px; }
    .tm-editor { display: flex; flex-direction: column; gap: 12px; padding: 16px; margin: 12px 0; border-radius: 10px; background: #f6f8fb; }
    .tm-actions { display: flex; justify-content: flex-end; gap: 8px; }
    .tm-error { display: flex; align-items: center; gap: 8px; padding: 10px 12px; border-radius: 8px; background: #fce8e6; color: #c5221f; font-size: 13px; }
    .tm-loading { display: flex; justify-content: center; padding: 16px; }
    .tm-empty { color: #6b7686; }
    .tm-list { list-style: none; margin: 0; padding: 0; }
    .tm-list li { display: flex; align-items: center; gap: 8px; padding: 10px 0; border-top: 1px solid #eef1f5; }
    .tm-main { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
    .tm-members { font-size: 13px; color: #4a5566; }
    .tm-warn { display: inline-flex; align-items: center; gap: 4px; color: #b06000; }
    .tm-warn mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .tm-holiday-form { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 8px; }
    .tm-holiday-form .grow { flex: 1; min-width: 180px; }
    @media (max-width: 600px) { .tm-card { padding: 14px; } }
  `]
})
export class TeamsComponent implements OnInit {
  private readonly svc = inject(TeamService);
  private readonly userSvc = inject(UserService);
  private readonly confirm = inject(ConfirmService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly teams = signal<TeamDto[]>([]);
  readonly holidays = signal<HolidayDto[]>([]);
  readonly users = signal<UserDto[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly editing = signal(false);
  readonly error = signal('');
  readonly sortedHolidays = computed(() => [...this.holidays()].sort((a, b) =>
    (a.everyYear ? a.date.slice(5) : a.date).localeCompare(b.everyYear ? b.date.slice(5) : b.date)));

  private editId: string | null = null;
  name = '';
  description = '';
  memberIds: string[] = [];
  holidayDate = '';
  holidayName = '';
  holidayEveryYear = false;

  ngOnInit(): void {
    this.load();
    this.userSvc.getUsers({ pageSize: 200 }).subscribe(r => this.users.set(r.items.filter(u => u.isActive)));
    this.svc.getHolidays().subscribe(h => this.holidays.set(h));
  }

  private load(): void {
    this.loading.set(true);
    this.svc.getTeams().subscribe({
      next: t => { this.teams.set(t); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  activeMembers(t: TeamDto): number {
    return t.members.filter(m => m.isActive).length;
  }

  memberNames(t: TeamDto): string {
    return t.members.filter(m => m.isActive).map(m => m.name).join(', ');
  }

  startCreate(): void {
    this.editId = null;
    this.name = ''; this.description = ''; this.memberIds = [];
    this.error.set('');
    this.editing.set(true);
  }

  startEdit(t: TeamDto): void {
    this.editId = t.id;
    this.name = t.name; this.description = t.description ?? ''; this.memberIds = t.members.map(m => m.userId);
    this.error.set('');
    this.editing.set(true);
  }

  save(): void {
    if (!this.name.trim()) { this.error.set(this.translate.instant('admin.teams.nameRequired')); return; }
    const request = { name: this.name.trim(), description: this.description.trim() || null, memberIds: this.memberIds };
    this.saving.set(true);
    const call = this.editId ? this.svc.updateTeam(this.editId, request) : this.svc.createTeam(request);
    (call as Observable<unknown>).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.load();
        this.snack.open(this.translate.instant('admin.teams.saved'), 'OK', { duration: 3000 });
      },
      error: (e: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.saving.set(false);
        const messages = e?.error?.errors ? Object.values(e.error.errors).flat().join(' ') : '';
        this.error.set(messages || e?.error?.detail || this.translate.instant('admin.teams.saveFailed'));
      }
    });
  }

  removeTeam(t: TeamDto): void {
    this.confirm.confirm({
      title: this.translate.instant('admin.teams.deleteTitle'),
      message: this.translate.instant('admin.teams.deleteMessage', { name: t.name }),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true
    }).subscribe(ok => {
      if (!ok) return;
      this.svc.deleteTeam(t.id).subscribe({
        next: () => this.load(),
        error: (e: { error?: { code?: string; detail?: string } }) => this.snack.open(
          e?.error?.code === 'team.inUse'
            ? this.translate.instant('admin.teams.inUse', { name: t.name })
            : e?.error?.detail || this.translate.instant('admin.teams.deleteFailed'),
          'OK', { duration: 6000 })
      });
    });
  }

  addHoliday(): void {
    this.svc.addHoliday({ date: this.holidayDate, name: this.holidayName.trim(), everyYear: this.holidayEveryYear }).subscribe({
      next: () => {
        this.holidayDate = ''; this.holidayName = ''; this.holidayEveryYear = false;
        this.svc.getHolidays().subscribe(h => this.holidays.set(h));
      },
      error: (e: { error?: { detail?: string } }) =>
        this.snack.open(e?.error?.detail || this.translate.instant('admin.holidays.addFailed'), 'OK', { duration: 5000 })
    });
  }

  removeHoliday(h: HolidayDto): void {
    this.svc.deleteHoliday(h.id).subscribe({
      next: () => this.holidays.update(list => list.filter(x => x.id !== h.id)),
      error: () => this.snack.open(this.translate.instant('admin.holidays.deleteFailed'), 'OK', { duration: 5000 })
    });
  }
}
