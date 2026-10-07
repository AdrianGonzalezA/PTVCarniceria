import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CurrentSession, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-admin-page',
  imports: [RouterLink],
  templateUrl: './admin-page.html',
  styleUrl: './admin-page.scss',
})
export class AdminPage implements OnInit {
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly signingOut = signal(false);

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('platform.users.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401 || error.status === 403) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.loadError.set('No se pudo consultar la sesión. Intentá nuevamente.');
      },
    });
  }

  protected signOut(): void {
    if (this.signingOut()) return;
    this.signingOut.set(true);
    this.sessions.logout().subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: () => {
        this.signingOut.set(false);
        this.loadError.set('No se pudo cerrar la sesión. Intentá nuevamente.');
      },
    });
  }
}
