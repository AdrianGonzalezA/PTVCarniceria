import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminAreaTabs } from './admin-area-tabs';

describe('AdminAreaTabs', () => {
  it('keeps Negocio active while offering Configuración', () => {
    TestBed.configureTestingModule({ imports: [AdminAreaTabs], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(AdminAreaTabs);
    fixture.componentRef.setInput('area', 'business');
    fixture.detectChanges();

    const tabs = fixture.nativeElement.querySelectorAll('nav[aria-label="Áreas de administración"] a') as NodeListOf<HTMLAnchorElement>;
    expect(tabs.length).toBe(2);
    expect(tabs[0].getAttribute('href')).toBe('/admin');
    expect(tabs[0].getAttribute('aria-current')).toBe('page');
    expect(tabs[1].getAttribute('href')).toBe('/admin/configuracion');
    expect(tabs[1].getAttribute('aria-current')).toBeNull();
  });

  it('keeps Configuración active while offering Negocio', () => {
    TestBed.configureTestingModule({ imports: [AdminAreaTabs], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(AdminAreaTabs);
    fixture.componentRef.setInput('area', 'configuration');
    fixture.detectChanges();

    const tabs = fixture.nativeElement.querySelectorAll('nav a') as NodeListOf<HTMLAnchorElement>;
    expect(tabs[0].getAttribute('aria-current')).toBeNull();
    expect(tabs[1].getAttribute('aria-current')).toBe('page');
  });
});
