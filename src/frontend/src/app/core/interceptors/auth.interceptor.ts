import { HttpErrorResponse, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';

/**
 * Adds the in-memory access token; on a 401 renews it once from the session cookie and retries. A 403 with code
 * mfa.setupRequired sends an Administrator or Manager to two-factor setup (R-M1-3).
 */
export const authInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const isAuthCall = req.url.includes('/v1/auth/');

  const withToken = (token: string | null) =>
    token && !isAuthCall ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(withToken(auth.token())).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && !isAuthCall && auth.user()) {
        return auth.refresh().pipe(
          switchMap(token => next(withToken(token))),
          catchError(refreshErr => {
            auth.clear();
            router.navigate(['/auth/login']);
            return throwError(() => refreshErr);
          })
        );
      }
      if (err.status === 403 && err.error?.code === 'mfa.setupRequired') {
        router.navigate(['/auth/mfa-setup']);
      }
      return throwError(() => err);
    })
  );
};
