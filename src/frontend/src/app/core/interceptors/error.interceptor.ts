import { HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

export const errorInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  return next(req).pipe(
    catchError(err => {
      // Global error handling — toast notifications handled in components
      if (err.status === 0) {
        console.error('Network error — backend may be unreachable');
      }
      return throwError(() => err);
    })
  );
};
