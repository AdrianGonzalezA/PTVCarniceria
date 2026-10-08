import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AdminCategory {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly productCount: number;
}

export interface AdminCategoryPage {
  readonly items: readonly AdminCategory[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface AdminCategoryUpdate {
  readonly name?: string;
  readonly isActive?: boolean;
}

@Injectable({ providedIn: 'root' })
export class AdminCategoryClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/categories';

  list(page = 1, search = '') {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search) params = params.set('search', search);
    return this.http.get<AdminCategoryPage>(this.endpoint, { params, withCredentials: true });
  }

  create(name: string) {
    return this.http.post<AdminCategory>(this.endpoint, { name }, { withCredentials: true });
  }

  update(id: string, changes: AdminCategoryUpdate) {
    return this.http.patch<AdminCategory>(`${this.endpoint}/${id}`, changes, { withCredentials: true });
  }
}
