import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  UserDto, UpdateUserRequest, Invitation, UserSessionInfo,
  ProfileDto, UpdateProfileRequest, ChangePasswordRequest, PagedResult
} from '../models';

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly api = inject(ApiService);

  // ---- Admin user management ----
  getUsers(params?: { pageNumber?: number; pageSize?: number; search?: string }): Observable<PagedResult<UserDto>> {
    return this.api.get<PagedResult<UserDto>>('/v1/admin/users', params as Record<string, unknown>);
  }

  getRoles(): Observable<string[]> {
    return this.api.get<string[]>('/v1/admin/roles');
  }

  /** Accounts are created by invitation only (R-M1-1). */
  inviteUser(request: { email: string; firstName: string; lastName: string; role: string }): Observable<Invitation> {
    return this.api.post<Invitation>('/v1/admin/invitations', request);
  }

  getInvitations(): Observable<Invitation[]> {
    return this.api.get<Invitation[]>('/v1/admin/invitations');
  }

  revokeInvitation(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/admin/invitations/${id}`);
  }

  updateUser(id: string, request: UpdateUserRequest): Observable<void> {
    return this.api.put<void>(`/v1/admin/users/${id}`, request);
  }

  /** Signs the user out everywhere and e-mails them a reset link; the administrator never sets the password. */
  resetUserPassword(id: string): Observable<void> {
    return this.api.post<void>(`/v1/admin/users/${id}/reset-password`);
  }

  // ---- Self-service profile ----
  getProfile(): Observable<ProfileDto> {
    return this.api.get<ProfileDto>('/v1/profile');
  }

  updateProfile(request: UpdateProfileRequest): Observable<ProfileDto> {
    return this.api.put<ProfileDto>('/v1/profile', request);
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.api.post<void>('/v1/profile/change-password', request);
  }

  getSessions(): Observable<UserSessionInfo[]> {
    return this.api.get<UserSessionInfo[]>('/v1/profile/sessions');
  }

  endSession(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/profile/sessions/${id}`);
  }

  uploadAvatar(file: File): Observable<{ avatarUrl: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.api.upload<{ avatarUrl: string }>('/v1/profile/avatar', formData);
  }
}
