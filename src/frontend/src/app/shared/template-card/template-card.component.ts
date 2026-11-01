import { Component, input, output } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { UnifiedTemplate } from '../../core/models/template-gallery.model';

/** One card in the unified /templates gallery — normalizes the HTML-template and PDF-template row layouts. */
@Component({
  selector: 'sf-template-card',
  standalone: true,
  imports: [CommonModule, DatePipe, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="tc-row">
      <div class="tc-icon" [class.inactive]="!template().isActive" [class.pdf]="template().kind === 'pdf'">
        <mat-icon>{{ template().kind === 'pdf' ? 'picture_as_pdf' : 'article' }}</mat-icon>
      </div>
      <div class="tc-body">
        <div class="tc-top">
          <span class="tc-name">{{ template().name }}</span>
          <span class="tc-kind-badge" [class.pdf]="template().kind === 'pdf'">
            {{ (template().kind === 'pdf' ? 'templates.kindPdf' : 'templates.kindHtml') | translate }}
          </span>
          @if (!template().isActive) {
            <span class="tc-inactive-badge">{{ 'templates.inactive' | translate }}</span>
          }
          @if (canManage()) {
            <span class="tc-version">v{{ template().version }}</span>
            <span class="tc-status" [attr.data-status]="template().status">{{ ('templates.status.' + template().status) | translate }}</span>
          }
        </div>
        @if (template().description) {
          <p class="tc-desc">{{ template().description }}</p>
        }
        <div class="tc-meta">
          @if (template().pageCount) {
            <span>{{ 'templates.pages' | translate: { n: template().pageCount } }}</span>
            <span class="sep">·</span>
          }
          <span>{{ 'templates.fields' | translate: { n: template().fieldCount } }}</span>
          <span class="sep">·</span>
          <span>{{ 'templates.createdBy' | translate: { name: template().createdByName } }}</span>
          <span class="sep">·</span>
          <span>{{ template().createdAt | date:'mediumDate' }}</span>
        </div>
      </div>
      <div class="tc-actions">
        @if (template().isActive && template().fieldCount > 0 && template().status === 'Approved') {
          <button type="button" class="tc-use-btn" (click)="use.emit(template())">
            <mat-icon>send</mat-icon> {{ 'templates.useTemplate' | translate }}
          </button>
        }
        @if (canManage()) {
          @switch (template().status) {
            @case ('Draft') {
              <button type="button" class="tc-text-btn" (click)="lifecycle.emit({ template: template(), action: 'submit' })">
                <mat-icon>gavel</mat-icon> {{ 'templates.lifecycle.submit' | translate }}
              </button>
            }
            @case ('InLegalReview') {
              <button type="button" class="tc-text-btn" (click)="lifecycle.emit({ template: template(), action: 'approve' })"
                      [matTooltip]="'templates.lifecycle.approveHint' | translate">
                <mat-icon>verified</mat-icon> {{ 'templates.lifecycle.approve' | translate }}
              </button>
            }
            @case ('Approved') {
              <button type="button" class="tc-icon-btn" (click)="lifecycle.emit({ template: template(), action: 'next' })"
                      [matTooltip]="'templates.lifecycle.next' | translate">
                <mat-icon>edit_note</mat-icon>
              </button>
              <button type="button" class="tc-icon-btn" (click)="lifecycle.emit({ template: template(), action: 'retire' })"
                      [matTooltip]="'templates.lifecycle.retire' | translate">
                <mat-icon>archive</mat-icon>
              </button>
            }
          }
          @if (template().status === 'Draft' || template().status === 'InLegalReview') {
            <button type="button" class="tc-icon-btn" (click)="manage.emit(template())" [matTooltip]="'templates.manage' | translate">
              <mat-icon>tune</mat-icon>
            </button>
          }
          @if (template().kind === 'pdf') {
            <button type="button" class="tc-icon-btn tc-delete" (click)="remove.emit(template())" [matTooltip]="'common.delete' | translate">
              <mat-icon>delete_outline</mat-icon>
            </button>
          }
        }
      </div>
    </div>
  `,
  styles: [`
    .tc-row { display: flex; align-items: center; gap: 16px; padding: 16px 20px; border-bottom: 1px solid #e8edf2; }
    .tc-row:last-child { border-bottom: none; }
    .tc-row:hover { background: #f8faff; }

    .tc-icon { width: 40px; height: 40px; border-radius: 10px; background: #e8f0fe; color: #0b57d0; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .tc-icon.pdf { background: #fce8e6; color: #c5221f; }
    .tc-icon.inactive { background: #f1f3f4; color: #9aa0a6; }

    .tc-body { flex: 1; min-width: 0; }
    .tc-top { display: flex; align-items: center; gap: 8px; margin-bottom: 2px; flex-wrap: wrap; }
    .tc-name { font-size: 14px; font-weight: 500; color: #1f1f1f; }
    .tc-kind-badge { font-size: 10px; font-weight: 600; letter-spacing: 0.3px; text-transform: uppercase; background: #e8f0fe; color: #0b57d0; padding: 2px 8px; border-radius: 10px; }
    .tc-kind-badge.pdf { background: #fce8e6; color: #c5221f; }
    .tc-inactive-badge { font-size: 11px; font-weight: 500; background: #f1f3f4; color: #5f6368; padding: 2px 8px; border-radius: 10px; }
    .tc-desc { font-size: 13px; color: #5f6368; margin: 0 0 4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .tc-meta { font-size: 12px; color: #80868b; display: flex; align-items: center; gap: 4px; flex-wrap: wrap; }
    .sep { color: #c7d0dc; }

    .tc-actions { display: flex; align-items: center; gap: 4px; flex-shrink: 0; }
    .tc-use-btn { display: flex; align-items: center; gap: 6px; height: 34px; padding: 0 16px; border-radius: 17px; border: none; background: #0b57d0; color: #fff; font-size: 13px; font-weight: 500; font-family: inherit; cursor: pointer; }
    .tc-use-btn mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .tc-icon-btn { width: 36px; height: 36px; border-radius: 50%; border: none; background: transparent; color: #5f6368; cursor: pointer; display: flex; align-items: center; justify-content: center; }
    .tc-icon-btn:hover { background: #f1f3f4; }
    .tc-delete:hover { background: #fce8e6; color: #c5221f; }
    .tc-version { font-size: 11px; font-weight: 600; color: #4a5566; background: #eef1f5; padding: 2px 7px; border-radius: 10px; }
    .tc-status { font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: 10px; background: #fef3c7; color: #92400e; }
    .tc-status[data-status='InLegalReview'] { background: #e8f0fe; color: #1a56c4; }
    .tc-status[data-status='Approved'] { background: #e6f4ea; color: #1e6b2e; }
    .tc-status[data-status='Retired'] { background: #f1f3f4; color: #6b7280; }
    .tc-text-btn { display: flex; align-items: center; gap: 4px; height: 32px; padding: 0 12px; border-radius: 16px; border: 1px solid #c7d0dc; background: #fff; color: #1f1f1f; font-size: 12.5px; font-family: inherit; cursor: pointer; }
    .tc-text-btn mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .tc-text-btn:hover { background: #f1f3f4; }
  `]
})
export class TemplateCardComponent {
  readonly template = input.required<UnifiedTemplate>();
  readonly canManage = input(false);
  readonly use = output<UnifiedTemplate>();
  readonly manage = output<UnifiedTemplate>();
  readonly remove = output<UnifiedTemplate>();
  readonly lifecycle = output<{ template: UnifiedTemplate; action: 'submit' | 'approve' | 'retire' | 'next' }>();
}
