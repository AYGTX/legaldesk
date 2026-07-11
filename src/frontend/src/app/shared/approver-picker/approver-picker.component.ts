import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { UserDto } from '../../core/models';
import { ApproverEntry } from '../../core/models/approver-entry.model';

/**
 * Single approver slot: existing-user vs external-signer toggle, plus the matching picker.
 * Shared by every request-origination flow (HTML templates, PDF templates, and — once wired up —
 * the one-off upload/designer flow) so the mode-toggle UI and validation only exist once.
 */
@Component({
  selector: 'sf-approver-picker',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <div class="ap-toggle">
      <button type="button" [class.active]="entry().mode === 'user'" (click)="entry().mode = 'user'">
        {{ 'shared.approverPicker.existingUser' | translate }}
      </button>
      <button type="button" [class.active]="entry().mode === 'external'" (click)="entry().mode = 'external'">
        <mat-icon>alternate_email</mat-icon> {{ 'shared.approverPicker.externalSigner' | translate }}
      </button>
    </div>

    @if (entry().mode === 'user') {
      @if (loadingUsers()) {
        <mat-spinner diameter="20"></mat-spinner>
      } @else {
        <select [(ngModel)]="entry().userId" class="ap-select">
          <option value="">{{ 'shared.approverPicker.selectApprover' | translate }}</option>
          @for (u of users(); track u.id) {
            <option [value]="u.id">{{ u.fullName }} ({{ u.email }})</option>
          }
        </select>
      }
    } @else {
      <div class="ap-external">
        <input type="text" [(ngModel)]="entry().externalName" class="ap-input" [placeholder]="'shared.approverPicker.fullName' | translate" />
        <input type="email" [(ngModel)]="entry().externalEmail" class="ap-input" [placeholder]="'shared.approverPicker.emailAddress' | translate" />
      </div>
      @if (showHint()) {
        <p class="ap-hint">{{ 'shared.approverPicker.externalSignerHint' | translate }}</p>
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .ap-toggle { display: flex; gap: 6px; margin-bottom: 8px; }
    .ap-toggle button { display: flex; align-items: center; gap: 4px; height: 30px; padding: 0 12px; border-radius: 15px; border: 1px solid #c7d0dc; background: #fff; color: #5f6368; font-size: 12px; font-family: inherit; cursor: pointer; }
    .ap-toggle button mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .ap-toggle button.active { background: #0b57d0; border-color: #0b57d0; color: #fff; }
    .ap-select { width: 100%; height: 44px; border-radius: 8px; border: 1px solid #c7d0dc; padding: 0 14px; font-size: 14px; font-family: inherit; outline: none; background: #fff; box-sizing: border-box; }
    .ap-select:focus { border-color: #0b57d0; box-shadow: 0 0 0 2px rgba(11,87,208,0.15); }
    .ap-external { display: flex; gap: 10px; }
    .ap-input { flex: 1; height: 44px; border-radius: 8px; border: 1px solid #c7d0dc; padding: 0 14px; font-size: 14px; font-family: inherit; outline: none; background: #fff; box-sizing: border-box; }
    .ap-input:focus { border-color: #0b57d0; box-shadow: 0 0 0 2px rgba(11,87,208,0.15); }
    .ap-hint { font-size: 11px; color: #9aa0a6; margin: 6px 0 0; }
  `]
})
export class ApproverPickerComponent {
  readonly entry = input.required<ApproverEntry>();
  readonly users = input<UserDto[]>([]);
  readonly loadingUsers = input<boolean>(false);
  readonly showHint = input<boolean>(true);
}
