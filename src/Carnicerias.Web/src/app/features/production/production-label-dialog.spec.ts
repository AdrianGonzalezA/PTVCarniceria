import { TestBed } from '@angular/core/testing';
import { ProductionLabelDialog } from './production-label-dialog';

describe('ProductionLabelDialog', () => {
  it('renders the exact stored barcode and measured weight for printing', () => {
    TestBed.configureTestingModule({ imports: [ProductionLabelDialog] });
    const fixture = TestBed.createComponent(ProductionLabelDialog);
    fixture.componentRef.setInput('companyName', 'Carnicería de prueba');
    fixture.componentRef.setInput('branchName', 'Sucursal A');
    fixture.componentRef.setInput('orderId', '12345678-order');
    fixture.componentRef.setInput('line', {
      id: 'line', productId: 'product', productCode: 'A1', productName: 'Asado',
      unit: 'kg', saleMode: 'Weight', direction: 'Output', quantity: 2.5,
      massKg: 2.5, inputPieceId: null, outputBarcodeProfileId: 'profile',
      outputSourceSystem: 'Producción', outputExternalIdentifier: '250661',
      outputRawBarcode: '25066102500', outputIdentifierField: 'pro_identif',
      inventoryMovementId: null, outputPieceId: null,
    });
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const barcode = host.querySelector('svg[role=img]')!;
    expect(barcode.getAttribute('aria-label')).toContain('25066102500');
    expect(barcode.querySelectorAll('rect').length).toBeGreaterThan(10);
    expect(host.textContent).toContain('2,500 kg');
    expect(host.textContent).toContain('250661');
    expect(host.querySelector<HTMLButtonElement>('button')!.disabled).toBe(false);
  });
});
