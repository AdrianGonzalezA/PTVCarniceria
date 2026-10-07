import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { UserDirectoryClient, UserListPage } from '../../core/users/user-directory-client';
import { SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-users-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './users-page.html',
  styleUrl: './users-page.scss',
})
export class UsersPage implements OnInit {
  private readonly usersClient = inject(UserDirectoryClient);
  private readonly sessionClient = inject(SessionClient);
  private readonly router = inject(Router);

  protected readonly currentUsername = signal('');
  protected readonly currentUserId = signal('');
  protected readonly pageData = signal<UserListPage | null>(null);
  protected readonly editingUserId = signal<string | null>(null);
  protected readonly draftUsername = signal('');
  protected readonly draftEmail = signal('');
  protected readonly isLoading = signal(true);
  protected readonly isSigningOut = signal(false);
  protected readonly actionUserId = signal<string | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly searchDraft = signal('');
  protected readonly appliedSearch = signal('');
  protected readonly firstItem = computed(() => {
    const page = this.pageData();
    return !page || page.totalItems === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
  });
  protected readonly lastItem = computed(() => {
    const page = this.pageData();
    return page ? Math.min(page.page * page.pageSize, page.totalItems) : 0;
  });

  ngOnInit(): void {
    this.sessionClient.current().subscribe({
      next: (session) => {
        this.currentUsername.set(session.username);
        this.currentUserId.set(session.userId);
      },
      error: () => void this.router.navigateByUrl('/'),
    });
    this.loadUsers(1);
  }

  protected updateSearch(event: Event): void {
    this.searchDraft.set((event.target as HTMLInputElement).value.slice(0, 100));
  }

  protected search(): void {
    this.appliedSearch.set(this.searchDraft().trim());
    this.loadUsers(1);
  }

  protected goToPage(page: number): void {
    const data = this.pageData();
    if (!data || page < 1 || page > data.totalPages || this.isLoading()) return;
    this.loadUsers(page);
  }

  protected startEditing(userId: string): void {
    const user = this.pageData()?.items.find((item) => item.userId === userId);
    if (!user || this.actionUserId()) return;
    this.editingUserId.set(userId);
    this.draftUsername.set(user.username);
    this.draftEmail.set(user.email);
    this.actionMessage.set(null);
    this.actionError.set(null);
  }

  protected updateDraftUsername(event: Event): void {
    this.draftUsername.set((event.target as HTMLInputElement).value.slice(0, 100));
  }

  protected updateDraftEmail(event: Event): void {
    this.draftEmail.set((event.target as HTMLInputElement).value.slice(0, 320));
  }

  protected cancelEditing(): void {
    this.editingUserId.set(null);
    this.draftUsername.set('');
    this.draftEmail.set('');
  }

  protected saveUser(userId: string): void {
    const username = this.draftUsername().trim();
    const email = this.draftEmail().trim();
    if (!username || !email || this.actionUserId()) return;

    this.actionUserId.set(userId);
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.usersClient.update(userId, { username, email }).subscribe({
      next: (updatedUser) => {
        this.replaceUser(updatedUser);
        this.cancelEditing();
        this.actionMessage.set('Datos del usuario actualizados.');
        this.actionUserId.set(null);
      },
      error: (error: HttpErrorResponse) => {
        this.actionError.set(
          error.status === 409
            ? 'Ya existe un usuario o correo con esos datos.'
            : error.status === 400
              ? 'Revisá el nombre de usuario y el correo electrónico.'
              : 'No se pudieron guardar los cambios. Revisá la conexión e intentá de nuevo.',
        );
        this.actionUserId.set(null);
      },
    });
  }

  protected toggleUser(userId: string, isActive: boolean): void {
    if (this.actionUserId() || this.currentUserId() === userId) return;
    if (isActive && !window.confirm('Al inactivar este usuario se cerrarán todas sus sesiones. ¿Querés continuar?')) {
      return;
    }

    this.actionUserId.set(userId);
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.usersClient.update(userId, { isActive: !isActive }).subscribe({
      next: (updatedUser) => {
        this.replaceUser(updatedUser);
        this.actionMessage.set(isActive ? 'Usuario inactivado y sesiones cerradas.' : 'Usuario reactivado. Las sesiones anteriores no se restauraron.');
        this.actionUserId.set(null);
      },
      error: (error: HttpErrorResponse) => {
        this.actionError.set(
          error.status === 409
            ? 'No podés inactivar tu propia cuenta desde esta pantalla.'
            : error.status === 403
              ? 'No tenés permiso para modificar usuarios.'
            : 'No se pudo actualizar el usuario. Revisá la conexión e intentá de nuevo.',
        );
        this.actionUserId.set(null);
      },
    });
  }

  protected revokeSessions(userId: string): void {
    if (this.actionUserId() || this.currentUserId() === userId) return;
    if (!window.confirm('Se cerrarán todas las sesiones activas de este usuario. ¿Querés continuar?')) return;

    this.actionUserId.set(userId);
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.usersClient.revokeSessions(userId).subscribe({
      next: (result) => {
        this.actionMessage.set(`Se cerraron ${result.revokedSessions} sesiones activas.`);
        this.actionUserId.set(null);
      },
      error: (error: HttpErrorResponse) => {
        this.actionError.set(
          error.status === 409
            ? 'No podés cerrar tus propias sesiones desde esta pantalla.'
            : error.status === 403
              ? 'No tenés permiso para cerrar sesiones.'
            : 'No se pudieron cerrar las sesiones. Revisá la conexión e intentá de nuevo.',
        );
        this.actionUserId.set(null);
      },
    });
  }

  protected signOut(): void {
    if (this.isSigningOut()) return;

    this.isSigningOut.set(true);
    this.sessionClient.logout().subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: () => {
        this.isSigningOut.set(false);
        this.loadError.set('No se pudo cerrar la sesión. Intentá de nuevo.');
      },
    });
  }

  private loadUsers(page: number): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.usersClient.list(page, 20, this.appliedSearch()).subscribe({
      next: (result) => {
        this.pageData.set(result);
        this.isLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.isLoading.set(false);
        if (error.status === 401) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.loadError.set(
          error.status === 403
            ? 'No tenés permiso para administrar usuarios.'
            : 'No se pudo cargar el listado. Revisá la conexión e intentá de nuevo.',
        );
      },
    });
  }

  private replaceUser(updatedUser: NonNullable<UserListPage['items'][number]>): void {
    this.pageData.update((page) => page
      ? { ...page, items: page.items.map((user) => user.userId === updatedUser.userId ? updatedUser : user) }
      : page);
  }
}
