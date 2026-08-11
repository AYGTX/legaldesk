import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap, throwError } from 'rxjs';
import { ApiService } from '../services/api.service';
import {
  LoginRequest, LoginResult, RegisterRequest,
  ForgotPasswordRequest, ResetPasswordRequest,
  RefreshTokenRequest, UserInfo
} from '../models';

const TOKEN_KEY = 'sf_access_token';
const REFRESH_KEY = 'sf_refresh_token';
const USER_KEY = 'sf_user';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  private readonly _user = signal<UserInfo | null>(this.loadUser());
  private readonly _token = signal<string | null>(localStorage.getItem(TOKEN_KEY));

  readonly user = this._user.asReadonly();
  readonly token = this._token.asReadonly();
  readonly isAuthenticated = computed(() => !!this._user() && !!this._token());
  readonly isAdmin = computed(() => this._user()?.roles.includes('Administrator') ?? false);
  readonly isManager = computed(() => this._user()?.roles.some(r => ['Administrator', 'Manager'].includes(r)) ?? false);

  login(request: LoginRequest): Observable<LoginResult> {
    return this.api.post<LoginResult>('/v1/auth/login', request).pipe(
      tap(result => this.storeSession(result))
    );
  }

  register(request: RegisterRequest): Observable<LoginResult> {
    return this.api.post<LoginResult>('/v1/auth/register', request).pipe(
      tap(result => this.storeSession(result))
    );
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<void> {
    return this.api.post<void>('/v1/auth/forgot-password', request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.api.post<void>('/v1/auth/reset-password', request);
  }

  refreshToken(): Observable<LoginResult> {
    const accessToken = this._token();
    const storedRefreshToken = localStorage.getItem(REFRESH_KEY);
    // Guards against the empty-token refresh call that used to reach the backend whenever a
    // logout raced an in-flight request's 401 — an empty access token there threw an unhandled
    // ArgumentNullException instead of failing cleanly.
    if (!accessToken || !storedRefreshToken) {
      return throwError(() => new Error('No session to refresh.'));
    }

    const request: RefreshTokenRequest = { accessToken, refreshToken: storedRefreshToken };
    return this.api.post<LoginResult>('/v1/auth/refresh', request).pipe(
      tap(result => this.storeSession(result))
    );
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
    this._user.set(null);
    this._token.set(null);
    this.router.navigate(['/auth/login']);
  }

  /** Updates the cached user's name after a profile edit (keeps the header in sync). */
  patchUserName(firstName: string, lastName: string, fullName: string): void {
    this.updateUser(u => ({ ...u, firstName, lastName, fullName }));
  }

  /** Updates the cached user's avatar after an upload. */
  patchAvatar(avatarUrl: string): void {
    this.updateUser(u => ({ ...u, avatarUrl }));
  }

  private updateUser(mutate: (user: UserInfo) => UserInfo): void {
    const current = this._user();
    if (!current) return;
    const updated = mutate(current);
    localStorage.setItem(USER_KEY, JSON.stringify(updated));
    this._user.set(updated);
  }

  private storeSession(result: LoginResult): void {
    localStorage.setItem(TOKEN_KEY, result.accessToken);
    localStorage.setItem(REFRESH_KEY, result.refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(result.user));
    this._token.set(result.accessToken);
    this._user.set(result.user);
  }

  private loadUser(): UserInfo | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
