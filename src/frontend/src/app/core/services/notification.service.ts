import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AppNotification, NotificationsResult } from '../models';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly api = inject(ApiService);

  readonly notifications = signal<AppNotification[]>([]);
  readonly unreadCount = signal(0);

  load(): void {
    this.api.get<NotificationsResult>('/v1/notifications', { take: 30 }).subscribe({
      next: r => {
        this.notifications.set(r.items);
        this.unreadCount.set(r.unreadCount);
      },
      error: () => { /* silently ignore — bell just stays empty */ }
    });
  }

  /** Called by the bell when a real-time notification arrives. */
  prepend(n: AppNotification): void {
    this.notifications.update(list => [n, ...list.filter(x => x.id !== n.id)].slice(0, 30));
    if (!n.isRead) this.unreadCount.update(c => c + 1);
  }

  markRead(id: string): void {
    const target = this.notifications().find(n => n.id === id);
    if (!target || target.isRead) return;
    this.notifications.update(list => list.map(n => n.id === id ? { ...n, isRead: true } : n));
    this.unreadCount.update(c => Math.max(0, c - 1));
    this.api.post(`/v1/notifications/${id}/read`).subscribe({ error: () => {} });
  }

  markAllRead(): void {
    if (this.unreadCount() === 0) return;
    this.notifications.update(list => list.map(n => ({ ...n, isRead: true })));
    this.unreadCount.set(0);
    this.api.post('/v1/notifications/read-all').subscribe({ error: () => {} });
  }
}
