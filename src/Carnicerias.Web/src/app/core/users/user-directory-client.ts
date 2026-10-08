import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface UserListItem {
  readonly userId: string;
  readonly username: string;
  readonly email: string;
  readonly isActive: boolean;
  readonly createdAtUtc: string;
}

export interface UserListPage {
  readonly items: readonly UserListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface UserAssignments {
  readonly role: 'administrator' | 'cashier';
  readonly branchIds: readonly string[];
}

export interface UserPasswordPolicy {
  readonly minimumLength: number;
}

@Injectable({ providedIn: 'root' })
export class UserDirectoryClient {
  private readonly http = inject(HttpClient);

  list(page: number, pageSize: number, search: string) {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);

    return this.http.get<UserListPage>('/api/users', { params, withCredentials: true });
  }

  passwordPolicy() {
    return this.http.get<UserPasswordPolicy>('/api/users/password-policy', { withCredentials: true });
  }

  createCashier(username: string, email: string, password: string, branchIds: readonly string[]) {
    return this.http.post<UserListItem>('/api/users', { username, email, password, branchIds }, {
      withCredentials: true,
    });
  }

  assignments(userId: string) {
    return this.http.get<UserAssignments>(`/api/users/${encodeURIComponent(userId)}/assignments`, {
      withCredentials: true,
    });
  }

  replaceAssignments(userId: string, branchIds: readonly string[]) {
    return this.http.put<UserAssignments>(`/api/users/${encodeURIComponent(userId)}/assignments`,
      { branchIds }, { withCredentials: true });
  }

  resetUserPassword(userId: string, password: string) {
    return this.http.put<void>(`/api/users/${encodeURIComponent(userId)}/password`,
      { password }, { withCredentials: true });
  }

  update(userId: string, changes: { readonly username?: string; readonly email?: string; readonly isActive?: boolean }) {
    return this.http.patch<UserListItem>(`/api/users/${encodeURIComponent(userId)}`, changes, {
      withCredentials: true,
    });
  }

  revokeSessions(userId: string) {
    return this.http.delete<{ readonly revokedSessions: number }>(
      `/api/users/${encodeURIComponent(userId)}/sessions`,
      { withCredentials: true },
    );
  }
}
