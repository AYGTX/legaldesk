import { Routes } from '@angular/router';
import { AuthShellComponent } from './auth-shell.component';

export const authRoutes: Routes = [
  {
    path: '',
    component: AuthShellComponent,
    children: [
      { path: '', redirectTo: 'login', pathMatch: 'full' },
      {
        path: 'login',
        loadComponent: () => import('./login/login.component').then(m => m.LoginComponent),
        title: 'Sign In — LegalDesk'
      },
      {
        // Accounts exist only by invitation (R-M1-1); old links to the sign-up page land on sign-in.
        path: 'register',
        redirectTo: 'login'
      },
      {
        path: 'accept-invitation',
        loadComponent: () => import('./accept-invitation/accept-invitation.component').then(m => m.AcceptInvitationComponent),
        title: 'Join LegalDesk'
      },
      {
        path: 'forgot-password',
        loadComponent: () => import('./forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent),
        title: 'Forgot Password — LegalDesk'
      },
      {
        path: 'reset-password',
        loadComponent: () => import('./reset-password/reset-password.component').then(m => m.ResetPasswordComponent),
        title: 'Reset Password — LegalDesk'
      }
    ]
  }
];
