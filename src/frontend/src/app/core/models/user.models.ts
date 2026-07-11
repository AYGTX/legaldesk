export interface UserDto {
  id: string;
  fullName: string;
  email: string;
  company?: string;
  department?: string;
  title?: string;
  isActive: boolean;
  createdAt: string;
  lastLoginAt?: string;
  roles: string[];
}

export interface CreateUserRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  company?: string;
  department?: string;
  title?: string;
  roles: string[];
}

export interface UpdateUserRequest {
  firstName: string;
  lastName: string;
  company?: string;
  department?: string;
  title?: string;
  isActive: boolean;
  roles: string[];
}

export interface ProfileDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  company?: string;
  department?: string;
  title?: string;
  avatarUrl?: string;
  signatureUrl?: string;
  mfaEnabled: boolean;
  createdAt: string;
  lastLoginAt?: string;
  roles: string[];
}

export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
  company?: string;
  department?: string;
  title?: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
