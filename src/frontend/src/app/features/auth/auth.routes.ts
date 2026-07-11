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
        title: 'Sign In — SignFlow'
      },
      {
        path: 'register',
        loadComponent: () => import('./register/register.component').then(m => m.RegisterComponent),
        title: 'Create Account — SignFlow'
      },
      {
        path: 'forgot-password',
        loadComponent: () => import('./forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent),
        title: 'Forgot Password — SignFlow'
      },
      {
        path: 'reset-password',
        loadComponent: () => import('./reset-password/reset-password.component').then(m => m.ResetPasswordComponent),
        title: 'Reset Password — SignFlow'
      }
    ]
  }
];
