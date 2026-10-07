import { inject } from '@angular/core';
import { CanActivateFn, CanDeactivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { SessionClient } from '../session/session-client';

export const posGuard: CanActivateFn = () => {
  const sessions = inject(SessionClient);
  const router = inject(Router);

  return sessions.current().pipe(
    map((session) => session.context ? true : router.createUrlTree(['/'])),
    catchError(() => of(router.createUrlTree(['/']))),
  );
};

export const posCanDeactivateGuard: CanDeactivateFn<{ canDeactivate: () => boolean }> = (page) =>
  page.canDeactivate();
