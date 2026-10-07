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

@Injectable({ providedIn: 'root' })
export class UserDirectoryClient {
  private readonly http = inject(HttpClient);

  list(page: number, pageSize: number, search: string) {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);

    return this.http.get<UserListPage>('/api/users', { params, withCredentials: true });
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
