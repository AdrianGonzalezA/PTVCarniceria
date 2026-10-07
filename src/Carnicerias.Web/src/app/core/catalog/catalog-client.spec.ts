import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CatalogClient } from './catalog-client';

describe('CatalogClient', () => {
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
