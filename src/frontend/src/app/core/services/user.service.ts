import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  UserDto, CreateUserRequest, UpdateUserRequest,
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

  createUser(request: CreateUserRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/admin/users', request);
  }

  updateUser(id: string, request: UpdateUserRequest): Observable<void> {
    return this.api.put<void>(`/v1/admin/users/${id}`, request);
  }

  deleteUser(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/admin/users/${id}`);
  }

  resetUserPassword(id: string, newPassword: string): Observable<void> {
    return this.api.post<void>(`/v1/admin/users/${id}/reset-password`, { newPassword });
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

  uploadAvatar(file: File): Observable<{ avatarUrl: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.api.upload<{ avatarUrl: string }>('/v1/profile/avatar', formData);
  }
}
