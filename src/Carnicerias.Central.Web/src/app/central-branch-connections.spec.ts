import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CentralBranchConnections } from './central-branch-connections';

describe('CentralBranchConnections', () => {
  it('registers a branch and clears the one-time credential after success', async () => {
    TestBed.configureTestingModule({
      imports: [CentralBranchConnections],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(CentralBranchConnections);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const empty = { data: [], pagination: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0 } };
    http.expectOne('/api/v1/branch-connections?page=1&pageSize=20').flush(empty);
    await fixture.whenStable();
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    element.querySelector<HTMLButtonElement>('#add-branch-connection')!.click();
    fixture.detectChanges();
    expect(element.querySelector<HTMLDialogElement>('#branch-connection-dialog')?.open).toBe(true);

    for (const [id, value] of [
      ['connection-installation-id', '11111111-1111-1111-1111-111111111111'],
      ['connection-company-id', '22222222-2222-2222-2222-222222222222'],
      ['connection-branch-id', '33333333-3333-3333-3333-333333333333'],
      ['connection-label', 'Sucursal Norte'],
      ['connection-url', 'https://branch.example/'],
      ['connection-token', 'A'.repeat(64)],
    ]) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    element.querySelector<HTMLFormElement>('#branch-connection-form')!.dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }));
    const create = http.expectOne('/api/v1/branch-connections');
    expect(create.request.body.token).toBe('A'.repeat(64));
    create.flush({ installationId: '11111111-1111-1111-1111-111111111111',
      remoteCompanyId: '22222222-2222-2222-2222-222222222222',
      remoteBranchId: '33333333-3333-3333-3333-333333333333',
      label: 'Sucursal Norte', destinationUrl: 'https://branch.example/',
      isActive: true, hasCredential: true });
    http.expectOne('/api/v1/branch-connections?page=1&pageSize=20').flush({
      data: [{ installationId: '11111111-1111-1111-1111-111111111111',
        remoteCompanyId: '22222222-2222-2222-2222-222222222222',
        remoteBranchId: '33333333-3333-3333-3333-333333333333',
        label: 'Sucursal Norte', destinationUrl: 'https://branch.example/',
        isActive: true, hasCredential: true }],
      pagination: { page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector<HTMLDialogElement>('#branch-connection-dialog')?.open).toBe(false);
    expect(element.querySelector<HTMLInputElement>('#connection-token')?.value).toBe('');
    expect(element.textContent).toContain('Sucursal Norte');
    http.verify();
  });
});
