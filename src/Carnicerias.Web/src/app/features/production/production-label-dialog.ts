import { Component, ElementRef, EventEmitter, Input, OnInit, Output,
  ViewChild, AfterViewInit } from '@angular/core';
import JsBarcode from 'jsbarcode';
import { ProductionLine } from '../../core/production/production-client';
import { formatLocalizedDecimal } from '../../shared/localized-decimal';
import { AdminDetailDialog } from '../admin/admin-detail-dialog';

@Component({
  selector: 'app-production-label-dialog',
  imports: [AdminDetailDialog],
  template: `
    <app-admin-detail-dialog labelledBy="production-label-title" [compact]="true"
      (closed)="closed.emit()">
      <section class="production-label-preview">
        <h2 id="production-label-title">Etiqueta de pieza</h2>
        <p><strong>{{ companyName }}</strong> · {{ branchName }}</p>
        <p>{{ line.productCode }} · {{ line.productName }}</p>
        <p>Identificador: <strong>{{ line.outputExternalIdentifier }}</strong></p>
        <p>Peso real: <strong>{{ weight }} kg</strong></p>
        <svg #barcodeSvg role="img" [attr.aria-label]="'Código de barras ' + line.outputRawBarcode"></svg>
        <p class="label-code">{{ line.outputRawBarcode }}</p>
        <p class="label-source">Origen: {{ line.outputSourceSystem }} · Orden {{ orderId.slice(0, 8) }}</p>
        @if (error) { <p class="error-message" role="alert">{{ error }}</p> }
        <div class="editor-actions">
          <button type="button" (click)="print()" [disabled]="!!error">Imprimir etiqueta</button>
          <button type="button" class="secondary-button" (click)="closed.emit()">Cerrar</button>
        </div>
      </section>
    </app-admin-detail-dialog>
  `,
  styles: [`
    .production-label-preview { width: min(28rem, 80vw); padding: 1.25rem; color: #222; background: #fff; }
    .production-label-preview h2 { margin: 0 0 .75rem; }
    .production-label-preview p { margin: .35rem 0; }
    .production-label-preview svg { display: block; max-width: 100%; margin: 1rem auto; }
    .label-source { font-size: .85rem; }
    .editor-actions { display: flex; gap: .5rem; margin-top: 1rem; }
  `],
})
export class ProductionLabelDialog implements OnInit, AfterViewInit {
  @Input({ required: true }) line!: ProductionLine;
  @Input({ required: true }) companyName = '';
  @Input({ required: true }) branchName = '';
  @Input({ required: true }) orderId = '';
  @Output() readonly closed = new EventEmitter<void>();
  @ViewChild('barcodeSvg') private barcodeSvg!: ElementRef<SVGSVGElement>;
  protected error: string | null = null;
  protected weight = '';

  ngOnInit(): void { this.weight = formatLocalizedDecimal(this.line.massKg, 3); }

  ngAfterViewInit(): void {
    try {
      JsBarcode(this.barcodeSvg.nativeElement, this.line.outputRawBarcode ?? '', {
        format: 'CODE128', width: 2, height: 64, margin: 8, displayValue: false,
      });
    } catch {
      this.error = 'El código no puede imprimirse como Code 128.';
    }
  }

  protected print(): void {
    if (this.error) return;
    const target = window.open('', '_blank', 'width=480,height=420');
    if (!target) {
      this.error = 'El navegador bloqueó la ventana de impresión.';
      return;
    }
    const document = target.document;
    document.title = `Etiqueta ${this.line.outputExternalIdentifier ?? ''}`;
    const style = document.createElement('style');
    style.textContent = `@page{size:80mm 55mm;margin:3mm}
      body{font:11px Arial,sans-serif;color:#111;margin:0;padding:2mm}
      h1{font-size:14px;margin:0 0 2mm}p{margin:1mm 0}
      svg{display:block;width:100%;height:auto;margin:2mm 0}`;
    document.head.append(style);
    const heading = document.createElement('h1');
    heading.textContent = `${this.line.productCode} · ${this.line.productName}`;
    const location = document.createElement('p');
    location.textContent = `${this.companyName} · ${this.branchName}`;
    const details = document.createElement('p');
    details.textContent = `ID ${this.line.outputExternalIdentifier} · ${this.weight} kg`;
    const source = document.createElement('p');
    source.textContent = `${this.line.outputSourceSystem} · Orden ${this.orderId.slice(0, 8)}`;
    const code = document.createElement('p');
    code.textContent = this.line.outputRawBarcode;
    document.body.append(heading, location, details,
      document.importNode(this.barcodeSvg.nativeElement, true), code, source);
    target.focus();
    target.print();
  }
}
