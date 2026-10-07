import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface PriceListOption {
  readonly id: string;
  readonly name: string;
}

export interface CatalogCategory {
  readonly id: string;
  readonly name: string;
  readonly productCount: number;
}

export interface CatalogProduct {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly categoryId: string;
  readonly unit: string;
  readonly saleMode: 'weight' | 'unit';
  readonly price: number;
  readonly currency: 'ARS';
  readonly priceEffectiveFromUtc: string;
  readonly availableStock: number;
}

export interface CatalogProductPage {
  readonly items: readonly CatalogProduct[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
}

@Injectable({ providedIn: 'root' })
export class CatalogClient {
  private readonly http = inject(HttpClient);

  priceLists() {
    return this.http.get<readonly PriceListOption[]>('/api/catalog/price-lists', {
      withCredentials: true,
    });
  }

  categories(priceListId: string) {
    return this.http.get<readonly CatalogCategory[]>('/api/catalog/categories', {
      params: new HttpParams().set('priceListId', priceListId),
      withCredentials: true,
    });
  }

  products(filters: {
    readonly priceListId: string;
    readonly categoryId?: string;
    readonly q?: string;
    readonly code?: string;
    readonly page?: number;
    readonly pageSize?: number;
  }) {
    let params = new HttpParams()
      .set('priceListId', filters.priceListId)
      .set('page', filters.page ?? 1)
      .set('pageSize', filters.pageSize ?? 50);
    if (filters.categoryId) params = params.set('categoryId', filters.categoryId);
    if (filters.q) params = params.set('q', filters.q);
    if (filters.code) params = params.set('code', filters.code);
    return this.http.get<CatalogProductPage>('/api/catalog/products', {
      params,
      withCredentials: true,
    });
  }
}
