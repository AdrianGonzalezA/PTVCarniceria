import '@angular/compiler';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { CatalogClient } from './catalog-client';

describe('CatalogClient', () => {
  it('looks up one scale PLU without changing existing product search parameters', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(CatalogClient).products({ priceListId: 'list-id', scalePlu: '00123' }).subscribe();
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne(value => value.url === '/api/catalog/products');
    expect(request.request.params.get('scalePlu')).toBe('00123');
    expect(request.request.params.get('priceListId')).toBe('list-id');
    expect(request.request.params.has('code')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 50, totalItems: 0 });
    http.verify();
  });

  it('reads price lists using the server-managed session and no client branch filter', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    let lists: readonly { id: string; name: string }[] = [];
    TestBed.inject(CatalogClient).priceLists().subscribe((value) => { lists = value; });

    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/catalog/price-lists');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.params.keys()).toEqual([]);
    request.flush([{ id: 'list-id', name: 'Mayorista' }]);
    expect(lists).toEqual([{ id: 'list-id', name: 'Mayorista' }]);
    http.verify();
  });
});
