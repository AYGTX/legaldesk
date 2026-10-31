import { HttpErrorResponse, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { SigningService } from '../services/signing.service';

const SIGN_URL = /\/v1\/sign\/([^/?]+)/;

/**
 * Sends the signing session's secret with every call to the signing endpoints (R-M5-4), and tells the signing page when
 * the session has ended (401 session.*) so it asks for a new code instead of failing.
 */
export const signingSessionInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  const token = SIGN_URL.exec(req.url)?.[1];
  if (!token) return next(req);

  const signing = inject(SigningService);
  const secret = signing.sessionSecret(token);
  return next(secret ? req.clone({ setHeaders: { 'X-Signing-Session': secret } }) : req).pipe(
    catchError((err: HttpErrorResponse) => {
      const code = err.error?.code as string | undefined;
      if (err.status === 401 && code?.startsWith('session.') && secret) signing.sessionEnded(token);
      return throwError(() => err);
    })
  );
};
