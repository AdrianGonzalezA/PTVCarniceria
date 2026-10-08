import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { SessionClient } from '../session/session-client';

export const catalogAdminGuard: CanActivateFn = () => {
  const sessions = inject(SessionClient);
  const router = inject(Router);

  return sessions.current().pipe(
    map((session) =>
      session.context?.permissions.includes('catalog.manage')
        ? true
        : router.createUrlTree(['/']),
    ),
    catchError(() => of(router.createUrlTree(['/']))),
  );
};
