import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, firstValueFrom, map, of, shareReplay, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiService } from '../services/api.service';
import {
  LoginRequest, SignInResponse, ForgotPasswordRequest, ResetPasswordRequest, UserInfo, MfaSetup
} from '../models';

// Only the display data of the signed-in user is kept across reloads; no token ever touches storage (R-M1-4).
const USER_KEY = 'sf_user';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly authUrl = `${environment.apiUrl}/v1/auth`;

  private readonly _user = signal<UserInfo | null>(this.loadUser());
  private readonly _token = signal<string | null>(null);
  private refreshInFlight: Observable<string> | null = null;

  readonly user = this._user.asReadonly();
  readonly token = this._token.asReadonly();
  readonly isAuthenticated = computed(() => !!this._user() && !!this._token());
  readonly isAdmin = computed(() => this._user()?.roles.includes('Administrator') ?? false);
  readonly isManager = computed(() => this._user()?.roles.some(r => ['Administrator', 'Manager'].includes(r)) ?? false);
  readonly mfaSetupRequired = computed(() => this._user()?.mfaSetupRequired ?? false);

  /** Called once at start-up: the session cookie, if any, yields a fresh access token. */
  restoreSession(): Promise<void> {
    if (!this._user()) return Promise.resolve();
    return firstValueFrom(this.refresh().pipe(map(() => undefined), catchError(() => of(undefined))));
  }

  /** Password step; resolves to a challenge when two-factor sign-in is on. */
  login(request: LoginRequest): Observable<SignInResponse> {
    return this.http.post<SignInResponse>(`${this.authUrl}/login`, request, { withCredentials: true })
      .pipe(tap(r => this.accept(r)));
  }

  /** Second step: a code from the authenticator app, or a recovery code. */
  verifyMfa(challenge: string, code: string): Observable<SignInResponse> {
    return this.http.post<SignInResponse>(`${this.authUrl}/mfa`, { challenge, code }, { withCredentials: true })
      .pipe(tap(r => this.accept(r)));
  }

  acceptInvitation(token: string, password: string): Observable<SignInResponse> {
    return this.http.post<SignInResponse>(`${this.authUrl}/invitations/accept`, { token, password }, { withCredentials: true })
      .pipe(tap(r => this.accept(r)));
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<void> {
    return this.api.post<void>('/v1/auth/forgot-password', request);
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.api.post<void>('/v1/auth/reset-password', request);
  }

  /**
   * A new access token from the session cookie. Concurrent callers share one request, so a burst of 401s
   * triggers a single rotation (two rotations with the same cookie would look like a replay and end the session).
   */
  refresh(): Observable<string> {
    if (!this.refreshInFlight) {
      this.refreshInFlight = this.http.post<SignInResponse>(`${this.authUrl}/refresh`, {}, { withCredentials: true }).pipe(
        tap(r => this.accept(r)),
        map(r => r.accessToken!),
        catchError(err => {
          this.clear();
          return throwError(() => err);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay(1)
      );
    }
    return this.refreshInFlight;
  }

  logout(): void {
    this.http.post(`${this.authUrl}/logout`, {}, { withCredentials: true })
      .pipe(catchError(() => of(null)))
      .subscribe();
    this.clear();
    this.router.navigate(['/auth/login']);
  }

  // Two-factor setup (profile)
  beginMfaSetup(): Observable<MfaSetup> {
    return this.api.post<MfaSetup>('/v1/profile/mfa/setup');
  }

  confirmMfaSetup(code: string): Observable<{ codes: string[] }> {
    return this.api.post<{ codes: string[] }>('/v1/profile/mfa/confirm', { code });
  }

  disableMfa(code: string): Observable<void> {
    return this.api.post<void>('/v1/profile/mfa/disable', { code });
  }

  /** Updates the cached user's name after a profile edit (keeps the header in sync). */
  patchUserName(firstName: string, lastName: string, fullName: string): void {
    this.updateUser(u => ({ ...u, firstName, lastName, fullName }));
  }

  /** Updates the cached user's avatar after an upload. */
  patchAvatar(avatarUrl: string): void {
    this.updateUser(u => ({ ...u, avatarUrl }));
  }

  /** Forgets the session locally (the cookie is ended by logout, or already invalid). */
  clear(): void {
    localStorage.removeItem(USER_KEY);
    // Tokens of the previous version of the app, stored before sessions moved to a cookie.
    localStorage.removeItem('sf_access_token');
    localStorage.removeItem('sf_refresh_token');
    this._user.set(null);
    this._token.set(null);
  }

  private accept(response: SignInResponse): void {
    if (response.mfaRequired || !response.accessToken || !response.user) return;
    localStorage.setItem(USER_KEY, JSON.stringify(response.user));
    this._token.set(response.accessToken);
    this._user.set(response.user);
  }

  private updateUser(mutate: (user: UserInfo) => UserInfo): void {
    const current = this._user();
    if (!current) return;
    const updated = mutate(current);
    localStorage.setItem(USER_KEY, JSON.stringify(updated));
    this._user.set(updated);
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
