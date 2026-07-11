import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { VacationService } from '../../core/services/vacation.service';
import { Vacation } from '../../core/models';

function dateRangeValid(control: AbstractControl): ValidationErrors | null {
  const start = control.get('startDate')?.value;
  const end = control.get('endDate')?.value;
  if (start && end && new Date(end) < new Date(start)) return { rangeInvalid: true };
  return null;
}

@Component({
  selector: 'sf-vacation-panel',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule,
    MatIconModule, MatDatepickerModule, MatProgressSpinnerModule, MatSnackBarModule,
    MatTooltipModule, TranslatePipe
  ],
  template: `
    <div class="vacation-panel">
      <p class="intro">
        <mat-icon>info</mat-icon>
        {{ 'vacation.intro' | translate }}
      </p>

      <!-- Schedule form -->
      <div class="schedule-form">
        <h3 class="section-title">{{ 'vacation.scheduleTitle' | translate }}</h3>
        @if (error()) {
          <div class="error-banner"><mat-icon>error_outline</mat-icon><span>{{ error() }}</span></div>
        }
        <form [formGroup]="form" (ngSubmit)="schedule()">
          <div class="row">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'vacation.startDate' | translate }}</mat-label>
              <input matInput [matDatepicker]="startPicker" formControlName="startDate" [min]="today" />
              <mat-datepicker-toggle matSuffix [for]="startPicker"></mat-datepicker-toggle>
              <mat-datepicker #startPicker></mat-datepicker>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'vacation.endDate' | translate }}</mat-label>
              <input matInput [matDatepicker]="endPicker" formControlName="endDate" [min]="form.value.startDate || today" />
              <mat-datepicker-toggle matSuffix [for]="endPicker"></mat-datepicker-toggle>
              <mat-datepicker #endPicker></mat-datepicker>
            </mat-form-field>
          </div>
          <mat-form-field appearance="outline" class="full">
            <mat-label>{{ 'vacation.note' | translate }}</mat-label>
            <input matInput formControlName="note" [placeholder]="'vacation.notePlaceholder' | translate" maxlength="500" />
          </mat-form-field>
          @if (form.hasError('rangeInvalid')) {
            <p class="field-hint error">{{ 'vacation.rangeInvalid' | translate }}</p>
          }
          <div class="actions">
            <button mat-flat-button color="primary" type="submit" [disabled]="saving() || form.invalid">
              @if (saving()) {
                <mat-spinner diameter="18"></mat-spinner>
              } @else {
                <span class="btn-content"><mat-icon>event_available</mat-icon> {{ 'vacation.schedule' | translate }}</span>
              }
            </button>
          </div>
        </form>
      </div>

      <!-- Existing vacations -->
      <div class="vacation-list">
        <h3 class="section-title">{{ 'vacation.scheduledVacationsTitle' | translate }}</h3>
        @if (loading()) {
          <div class="loading"><mat-spinner diameter="32"></mat-spinner></div>
        } @else if (vacations().length === 0) {
          <div class="empty"><mat-icon>beach_access</mat-icon><p>{{ 'vacation.noVacations' | translate }}</p></div>
        } @else {
          @for (v of vacations(); track v.id) {
            <div class="vacation-item" [class.active]="v.isActive">
              <div class="vacation-icon"><mat-icon>{{ v.isActive ? 'flight_takeoff' : 'event' }}</mat-icon></div>
              <div class="vacation-info">
                <span class="vacation-dates">
                  {{ v.startDate | date:'MMM d, y' }} – {{ v.endDate | date:'MMM d, y' }}
                  @if (v.isActive) { <span class="active-badge">{{ 'vacation.onVacationNow' | translate }}</span> }
                </span>
                @if (v.note) { <span class="vacation-note">{{ v.note }}</span> }
              </div>
              <button mat-icon-button (click)="cancel(v)" [matTooltip]="'common.cancel' | translate" [disabled]="cancelling() === v.id">
                <mat-icon>delete</mat-icon>
              </button>
            </div>
          }
        }
      </div>
    </div>
  `,
  styleUrl: './vacation-panel.component.scss'
})
export class VacationPanelComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly vacationService = inject(VacationService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly today = new Date();
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly cancelling = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly vacations = signal<Vacation[]>([]);

  readonly form = this.fb.group({
    startDate: [null as Date | null, Validators.required],
    endDate: [null as Date | null, Validators.required],
    note: ['']
  }, { validators: dateRangeValid });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.vacationService.getMyVacations().subscribe({
      next: v => { this.vacations.set(v); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  schedule(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    this.error.set(null);
    const v = this.form.value;
    this.vacationService.schedule({
      startDate: this.toIso(v.startDate!),
      endDate: this.toIso(v.endDate!),
      note: v.note || undefined
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.reset();
        this.snackBar.open(this.translate.instant('vacation.messages.scheduled'), this.translate.instant('common.close'), { duration: 3000 });
        this.load();
      },
      error: err => {
        this.saving.set(false);
        this.error.set(err.error?.detail ?? err.error?.title ?? this.translate.instant('vacation.messages.scheduleFailed'));
      }
    });
  }

  cancel(v: Vacation): void {
    if (!confirm(this.translate.instant('vacation.confirmCancel'))) return;
    this.cancelling.set(v.id);
    this.vacationService.cancel(v.id).subscribe({
      next: () => { this.cancelling.set(null); this.snackBar.open(this.translate.instant('vacation.messages.cancelled'), this.translate.instant('common.close'), { duration: 3000 }); this.load(); },
      error: () => { this.cancelling.set(null); this.snackBar.open(this.translate.instant('vacation.messages.cancelFailed'), this.translate.instant('common.close'), { duration: 4000 }); }
    });
  }

  private toIso(d: Date): string {
    // Send a date-only value (local calendar day) without timezone shifting.
    const year = d.getFullYear();
    const month = `${d.getMonth() + 1}`.padStart(2, '0');
    const day = `${d.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
}
