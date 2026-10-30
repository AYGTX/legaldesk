import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/** Signed in, and two-factor set up where the role requires it (R-M1-3). */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) return router.createUrlTree(['/auth/login']);
  if (auth.mfaSetupRequired()) return router.createUrlTree(['/auth/mfa-setup']);
  return true;
};

/** Signed in, whatever the two-factor state: the two-factor setup page itself. */
export const sessionGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated() ? true : router.createUrlTree(['/auth/login']);
};
