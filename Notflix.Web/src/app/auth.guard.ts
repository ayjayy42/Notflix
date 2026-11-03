import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './services/auth.service';
export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(AuthService).refreshSession().pipe(
    map(user => user ? true : router.createUrlTree(['/'], { queryParams: { returnUrl: state.url } })),
    catchError(() => of(router.createUrlTree(['/'], { queryParams: { returnUrl: state.url, connectionError: '1' } })))
  );
};
