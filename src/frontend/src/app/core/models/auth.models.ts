export interface LoginRequest {
  email: string;
  password: string;
}

/**
 * Answer of login, two-factor, refresh and invitation acceptance. Either a session (access token kept in memory
 * only; the refresh token is an HttpOnly cookie the page never sees) or a two-factor challenge to answer.
 */
export interface SignInResponse {
  accessToken?: string;
  expiresAt?: string;
  user?: UserInfo;
  mfaRequired: boolean;
  mfaChallenge?: string;
}

export interface UserInfo {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  roles: string[];
  avatarUrl?: string;
  mfaEnabled: boolean;
  department?: string;
  company?: string;
  /** Administrator or Manager who must set up two-factor sign-in before using the app. */
  mfaSetupRequired: boolean;
}

export interface MfaSetup {
  sharedKey: string;
  authenticatorUri: string;
  qrCodeDataUri: string;
}

export interface UserSessionInfo {
  id: string;
  createdAt: string;
  lastUsedAt: string;
  expiresAt: string;
  ipAddress?: string;
  userAgent?: string;
}

export interface Invitation {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  role: string;
  createdAt: string;
  expiresAt: string;
  acceptedAt?: string;
  revokedAt?: string;
  status: 'Pending' | 'Accepted' | 'Revoked' | 'Expired';
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}
