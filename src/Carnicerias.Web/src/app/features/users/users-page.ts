import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminAreaTabs } from '../admin/admin-area-tabs';
import { AdminDetailDialog } from '../admin/admin-detail-dialog';
import { UserAssignments, UserDirectoryClient, UserListPage } from '../../core/users/user-directory-client';
import { OperationalBranchOption, SessionClient } from '../../core/session/session-client';

@Component({
  selector: 'app-users-page',
  imports: [DatePipe, RouterLink, AdminAreaTabs, AdminDetailDialog],
  templateUrl: './users-page.html',
  styleUrl: './users-page.scss',
})
export class UsersPage implements OnInit {
  private readonly usersClient = inject(UserDirectoryClient);
  private readonly sessionClient = inject(SessionClient);
  private readonly router = inject(Router);

  protected readonly currentUsername = signal('');
  protected readonly currentUserId = signal('');
  protected readonly currentCompanyName = signal('');
  protected readonly minimumPasswordLength = signal(12);
  protected readonly branches = signal<readonly OperationalBranchOption[]>([]);
  protected readonly createOpen = signal(false);
  protected readonly createUsername = signal('');
  protected readonly createEmail = signal('');
  protected readonly createPassword = signal('');
  protected readonly createBranchIds = signal<readonly string[]>([]);
  protected readonly creating = signal(false);
  protected readonly assignmentUserId = signal<string | null>(null);
  protected readonly assignmentData = signal<UserAssignments | null>(null);
  protected readonly assignmentBranchIds = signal<readonly string[]>([]);
  protected readonly assignmentLoading = signal(false);
  protected readonly assignmentSaving = signal(false);
  protected readonly assignmentUsername = computed(() => this.pageData()?.items.find((user) =>
    user.userId === this.assignmentUserId())?.username ?? 'usuario');
  protected readonly passwordUserId = signal<string | null>(null);
  protected readonly resetPasswordDraft = signal('');
  protected readonly resettingPassword = signal(false);
  protected readonly passwordUsername = computed(() => this.pageData()?.items.find((user) =>
    user.userId === this.passwordUserId())?.username ?? 'usuario');
  protected readonly pageData = signal<UserListPage | null>(null);
  protected readonly editingUserId = signal<string | null>(null);
  protected readonly draftUsername = signal('');
  protected readonly draftEmail = signal('');
  protected readonly isLoading = signal(true);
  protected readonly isSigningOut = signal(false);
  protected readonly actionUserId = signal<string | null>(null);
  protected readonly pendingAction = signal<{ userId: string; kind: 'status' | 'sessions'; isActive?: boolean } | null>(null);
  protected readonly pendingUsername = computed(() => this.pageData()?.items.find((user) =>
    user.userId === this.pendingAction()?.userId)?.username ?? 'usuario');
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
        if (!session.context?.permissions.includes('platform.users.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.currentUsername.set(session.username);
        this.currentUserId.set(session.userId);
        this.currentCompanyName.set(session.context.companyName);
        this.loadUsers(1);
        this.usersClient.passwordPolicy().subscribe({
          next: (policy) => this.minimumPasswordLength.set(policy.minimumLength),
          error: () => this.actionError.set('No se pudo consultar la política de contraseñas.'),
        });
        this.sessionClient.operationalContexts().subscribe({
          next: (companies) => this.branches.set(companies.find((company) =>
            company.companyId === session.context?.companyId)?.branches ?? []),
          error: () => this.actionError.set('No se pudieron cargar las sucursales para asignaciones.'),
        });
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected openCreate(): void {
    this.createOpen.set(true);
    this.createUsername.set('');
    this.createEmail.set('');
    this.createPassword.set('');
    this.createBranchIds.set([]);
    this.actionError.set(null);
  }

  protected closeCreate(): void {
    if (this.creating()) return;
    this.createOpen.set(false);
    this.createPassword.set('');
  }

  protected updateCreateField(field: 'username' | 'email' | 'password', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    if (field === 'username') this.createUsername.set(value.slice(0, 100));
    else if (field === 'email') this.createEmail.set(value.slice(0, 320));
    else this.createPassword.set(value);
  }

  protected toggleCreateBranch(branchId: string, event: Event): void {
    const selected = (event.target as HTMLInputElement).checked;
    this.createBranchIds.update((ids) => selected
      ? [...ids, branchId] : ids.filter((id) => id !== branchId));
  }

  protected createCashier(): void {
    const username = this.createUsername().trim();
    const email = this.createEmail().trim();
    const password = this.createPassword();
    const branchIds = this.createBranchIds();
    if (!username || !email || password.length < this.minimumPasswordLength() || branchIds.length === 0 || this.creating()) {
      this.actionError.set(`Completá usuario, correo, contraseña de al menos ${this.minimumPasswordLength()} caracteres y una sucursal.`);
      return;
    }
    this.creating.set(true);
    this.actionError.set(null);
    this.usersClient.createCashier(username, email, password, branchIds).subscribe({
      next: () => {
        this.creating.set(false);
        this.closeCreate();
        this.actionMessage.set('Cajero creado y asignado a las sucursales seleccionadas.');
        this.loadUsers(1);
      },
      error: (error: HttpErrorResponse) => {
        this.creating.set(false);
        this.actionError.set(error.status === 409 ? 'Ya existe ese usuario o correo.' :
          error.status === 400 ? 'Revisá los datos y las sucursales.' :
            'No se pudo crear el cajero. Intentá de nuevo.');
      },
    });
  }

  protected openAssignments(userId: string): void {
    if (this.assignmentLoading() || this.assignmentSaving()) return;
    this.assignmentUserId.set(userId);
    this.assignmentData.set(null);
    this.assignmentLoading.set(true);
    this.actionError.set(null);
    this.usersClient.assignments(userId).subscribe({
      next: (assignments) => {
        if (this.assignmentUserId() !== userId) return;
        this.assignmentData.set(assignments);
        this.assignmentBranchIds.set(assignments.branchIds);
        this.assignmentLoading.set(false);
      },
      error: () => {
        this.assignmentLoading.set(false);
        this.actionError.set('No se pudieron consultar las asignaciones.');
      },
    });
  }

  protected closeAssignments(): void {
    if (this.assignmentSaving()) return;
    this.assignmentUserId.set(null);
    this.assignmentData.set(null);
  }

  protected toggleAssignmentBranch(branchId: string, event: Event): void {
    const selected = (event.target as HTMLInputElement).checked;
    this.assignmentBranchIds.update((ids) => selected
      ? [...ids, branchId] : ids.filter((id) => id !== branchId));
  }

  protected saveAssignments(): void {
    const userId = this.assignmentUserId();
    if (!userId || this.assignmentData()?.role !== 'cashier' || this.assignmentSaving()) return;
    if (this.assignmentBranchIds().length === 0) {
      this.actionError.set('El cajero debe conservar al menos una sucursal.');
      return;
    }
    this.assignmentSaving.set(true);
    this.actionError.set(null);
    this.usersClient.replaceAssignments(userId, this.assignmentBranchIds()).subscribe({
      next: () => {
        this.assignmentSaving.set(false);
        this.closeAssignments();
        this.actionMessage.set('Asignaciones actualizadas. Las sesiones anteriores del cajero se cerraron.');
      },
      error: (error: HttpErrorResponse) => {
        this.assignmentSaving.set(false);
        this.actionError.set(error.error?.error?.code === 'USER_HAS_OPEN_OPERATIONS'
          ? 'El cajero tiene turno o ticket abierto en una sucursal que querés quitar.'
          : 'No se pudieron guardar las asignaciones.');
      },
    });
  }

  protected openPasswordReset(userId: string): void {
    this.passwordUserId.set(userId);
    this.resetPasswordDraft.set('');
    this.actionError.set(null);
  }

  protected closePasswordReset(): void {
    if (this.resettingPassword()) return;
    this.passwordUserId.set(null);
    this.resetPasswordDraft.set('');
  }

  protected updateResetPassword(event: Event): void {
    this.resetPasswordDraft.set((event.target as HTMLInputElement).value);
  }

  protected resetUserPassword(): void {
    const userId = this.passwordUserId();
    const password = this.resetPasswordDraft();
    if (!userId || password.length < this.minimumPasswordLength() || this.resettingPassword()) {
      this.actionError.set(`La contraseña nueva debe tener al menos ${this.minimumPasswordLength()} caracteres.`);
      return;
    }
    this.resettingPassword.set(true);
    this.actionError.set(null);
    this.usersClient.resetUserPassword(userId, password).subscribe({
      next: () => {
        this.resettingPassword.set(false);
        this.closePasswordReset();
        this.actionMessage.set('Contraseña actualizada. Las sesiones anteriores se cerraron.');
        if (userId === this.currentUserId()) void this.router.navigateByUrl('/');
      },
      error: () => {
        this.resettingPassword.set(false);
        this.actionError.set('No se pudo restablecer la contraseña del usuario.');
      },
    });
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
    if (this.actionUserId()) return;
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
    this.pendingAction.set({ userId, kind: 'status', isActive });
  }

  private executeToggleUser(userId: string, isActive: boolean): void {

    this.actionUserId.set(userId);
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.usersClient.update(userId, { isActive: !isActive }).subscribe({
      next: (updatedUser) => {
        this.replaceUser(updatedUser);
        this.actionMessage.set(isActive ? 'Usuario inactivado y sesiones cerradas.' : 'Usuario reactivado. Las sesiones anteriores no se restauraron.');
        this.actionUserId.set(null);
        this.pendingAction.set(null);
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
        this.pendingAction.set(null);
      },
    });
  }

  protected revokeSessions(userId: string): void {
    if (this.actionUserId() || this.currentUserId() === userId) return;
    this.pendingAction.set({ userId, kind: 'sessions' });
  }

  private executeRevokeSessions(userId: string): void {

    this.actionUserId.set(userId);
    this.actionMessage.set(null);
    this.actionError.set(null);
    this.usersClient.revokeSessions(userId).subscribe({
      next: (result) => {
        this.actionMessage.set(`Se cerraron ${result.revokedSessions} sesiones activas.`);
        this.actionUserId.set(null);
        this.pendingAction.set(null);
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
        this.pendingAction.set(null);
      },
    });
  }

  protected confirmAction(): void {
    const action = this.pendingAction();
    if (!action || this.actionUserId()) return;
    if (action.kind === 'status') this.executeToggleUser(action.userId, !!action.isActive);
    else this.executeRevokeSessions(action.userId);
  }

  protected closeAction(): void {
    if (!this.actionUserId()) this.pendingAction.set(null);
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
