import { Component, OnInit, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatBadgeModule } from '@angular/material/badge';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ViewChild } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NotificationService } from '../../core/services/notification.service';
import { HubService } from '../../core/services/hub.service';
import { AppNotification, NotificationType } from '../../core/models';

const ICONS: Record<NotificationType, string> = {
  DocumentReceived: 'edit_document',
  Reminder: 'alarm',
  Completed: 'task_alt',
  Rejected: 'cancel',
  Expired: 'event_busy',
  WorkflowFinished: 'verified',
  DelegationRequest: 'forward',
  CommentMention: 'comment',
  ChangesRequested: 'published_with_changes',
  ObligationDue: 'event_repeat',
  RegulatoryAlert: 'gavel',
  ContractTermAlert: 'update'
};

const COLORS: Record<NotificationType, string> = {
  DocumentReceived: '#0b57d0',
  Reminder:         '#7a5900',
  Completed:        '#1e6b2e',
  Rejected:         '#b3261e',
  Expired:          '#b3261e',
  WorkflowFinished: '#1e6b2e',
  DelegationRequest:'#6750a4',
  CommentMention:   '#0b57d0',
  ChangesRequested: '#b26a00',
  ObligationDue:    '#7a5900',
  RegulatoryAlert:  '#6750a4',
  ContractTermAlert:'#b26a00'
};

@Component({
  selector: 'sf-notification-bell',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatIconModule, MatBadgeModule,
    MatMenuModule, MatTooltipModule, TranslatePipe
  ],
  template: `
    <button mat-icon-button [matMenuTriggerFor]="menu" (menuOpened)="onOpen()"
            [matTooltip]="'shared.notificationBell.tooltip' | translate" class="bell-btn">
      <mat-icon [matBadge]="unread() > 9 ? '9+' : unread()" [matBadgeHidden]="unread() === 0"
                matBadgeColor="warn" matBadgeSize="small">notifications</mat-icon>
    </button>

    <mat-menu #menu="matMenu" class="notif-menu" xPosition="before" panelClass="notif-panel">
      <div class="notif-header" (click)="$event.stopPropagation()">
        <span class="notif-title">{{ 'shared.notificationBell.title' | translate }}</span>
        @if (unread() > 0) {
          <button mat-button class="mark-all" (click)="markAllRead()">{{ 'shared.notificationBell.markAllRead' | translate }}</button>
        }
      </div>

      <div class="notif-list">
        @if (items().length === 0) {
          <div class="notif-empty">
            <mat-icon>notifications_off</mat-icon>
            <p>{{ 'shared.notificationBell.empty' | translate }}</p>
          </div>
        } @else {
          @for (n of items(); track n.id) {
            <button mat-menu-item class="notif-item" [class.unread]="!n.isRead" (click)="open(n)">
              <div class="notif-icon" [style.background]="color(n.type) + '20'" [style.color]="color(n.type)">
                <mat-icon>{{ icon(n.type) }}</mat-icon>
              </div>
              <div class="notif-body">
                <span class="notif-subject">{{ n.subject }}</span>
                <span class="notif-text">{{ n.body }}</span>
                <span class="notif-time">{{ timeAgo(n.createdAt) }}</span>
              </div>
              @if (!n.isRead) { <span class="unread-dot"></span> }
            </button>
          }
        }
      </div>
    </mat-menu>
  `,
  styleUrl: './notification-bell.component.scss'
})
export class NotificationBellComponent implements OnInit {
  @ViewChild(MatMenuTrigger) trigger?: MatMenuTrigger;

  private readonly service = inject(NotificationService);
  private readonly hub = inject(HubService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly items = this.service.notifications;
  readonly unread = this.service.unreadCount;

  constructor() {
    // React to real-time pushes from SignalR.
    effect(() => {
      const pushed = this.hub.lastNotification();
      if (pushed) this.service.prepend(pushed);
    });
  }

  ngOnInit(): void {
    this.service.load();
  }

  onOpen(): void {
    // Refresh on open in case anything was missed while disconnected.
    this.service.load();
  }

  icon(type: NotificationType): string { return ICONS[type] ?? 'notifications'; }
  color(type: NotificationType): string { return COLORS[type] ?? '#6366f1'; }

  open(n: AppNotification): void {
    this.service.markRead(n.id);
    if (n.documentId) this.router.navigate(['/contracts', n.documentId]);
  }

  markAllRead(): void {
    this.service.markAllRead();
  }

  timeAgo(iso: string): string {
    const diff = Date.now() - new Date(iso).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1) return this.translate.instant('shared.notificationBell.justNow');
    if (mins < 60) return this.translate.instant('shared.notificationBell.minutesAgo', { count: mins });
    const hours = Math.floor(mins / 60);
    if (hours < 24) return this.translate.instant('shared.notificationBell.hoursAgo', { count: hours });
    const days = Math.floor(hours / 24);
    if (days < 7) return this.translate.instant('shared.notificationBell.daysAgo', { count: days });
    return new Date(iso).toLocaleDateString();
  }
}
