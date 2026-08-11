import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';
import { environment } from '../../../environments/environment';
import { AppNotification } from '../models';

export interface DocumentProgressEvent {
  documentId: string;
  message: string;
  completedSteps: number;
  totalSteps: number;
  isCompleted: boolean;
}

export interface PresenceViewer {
  userId: string;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class HubService {
  private readonly auth = inject(AuthService);
  private connection: signalR.HubConnection | null = null;

  readonly isConnected = signal(false);
  readonly lastProgress = signal<DocumentProgressEvent | null>(null);
  /** Bumped whenever a real-time notification arrives (the notification bell reacts to this). */
  readonly lastNotification = signal<AppNotification | null>(null);
  /**
   * Bumped when a document's comments change (content-free ping by design — the payload
   * carries no comment data, so viewers refetch and per-viewer anonymization is preserved).
   */
  readonly lastCommentsChange = signal<{ documentId: string; at: number } | null>(null);
  /**
   * Everyone currently viewing a document (authenticated hub connections only — external
   * token signers never connect, so they don't appear).
   */
  readonly presence = signal<{ documentId: string; viewers: PresenceViewer[] } | null>(null);

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.signalrUrl, {
        accessTokenFactory: () => this.auth.token() ?? ''
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.connection.on('DocumentProgress', (event: DocumentProgressEvent) => {
      this.lastProgress.set(event);
    });

    this.connection.on('notification', (event: AppNotification) => {
      this.lastNotification.set(event);
    });

    this.connection.on('comments-changed', (event: { documentId: string }) => {
      this.lastCommentsChange.set({ documentId: event.documentId, at: Date.now() });
    });

    this.connection.on('presence-changed', (event: { documentId: string; viewers: PresenceViewer[] }) => {
      this.presence.set(event);
    });

    this.connection.onreconnected(() => this.isConnected.set(true));
    this.connection.onclose(() => this.isConnected.set(false));

    try {
      await this.connection.start();
      this.isConnected.set(true);
    } catch {
      this.isConnected.set(false);
    }
  }

  async joinDocument(documentId: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('JoinDocumentGroup', documentId);
    }
  }

  async leaveDocument(documentId: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('LeaveDocumentGroup', documentId);
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.isConnected.set(false);
    }
  }
}
