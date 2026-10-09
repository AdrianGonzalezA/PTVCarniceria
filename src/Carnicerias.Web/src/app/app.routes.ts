import { Routes } from '@angular/router';
import { WelcomePage } from './features/welcome/welcome-page';
import { userAdminGuard } from './core/users/user-admin.guard';
import { organizationAdminGuard } from './core/admin/organization-admin.guard';
import { inventoryAdminGuard } from './core/inventory/inventory-admin.guard';
import { catalogAdminGuard } from './core/admin/catalog-admin.guard';
import { posCanDeactivateGuard, posGuard } from './core/pos/pos.guard';

export const routes: Routes = [
  { path: '', component: WelcomePage },
  { path: 'users', loadComponent: () => import('./features/users/users-page').then((page) => page.UsersPage), canActivate: [userAdminGuard] },
  { path: 'admin/configuracion', loadComponent: () => import('./features/admin/admin-page').then((page) => page.AdminPage), canActivate: [userAdminGuard], data: { area: 'configuration' } },
  { path: 'admin', loadComponent: () => import('./features/admin/admin-page').then((page) => page.AdminPage), canActivate: [userAdminGuard], data: { area: 'business' } },
  { path: 'admin/categories', loadComponent: () => import('./features/admin/categories-page').then((page) => page.CategoriesPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/products', loadComponent: () => import('./features/admin/products-page').then((page) => page.ProductsPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/taxes', loadComponent: () => import('./features/admin/product-tax-page').then((page) => page.ProductTaxPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/price-lists', loadComponent: () => import('./features/admin/price-lists-page').then((page) => page.PriceListsPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/organization', loadComponent: () => import('./features/admin/organization-page').then((page) => page.OrganizationPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/customers', loadComponent: () => import('./features/admin/customers-page').then((page) => page.CustomersPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/stock', loadComponent: () => import('./features/admin/stock-page').then((page) => page.StockPage), canActivate: [inventoryAdminGuard] },
  { path: 'admin/barcode-layouts', loadComponent: () => import('./features/admin/barcode-layout-page').then((page) => page.BarcodeLayoutPage), canActivate: [inventoryAdminGuard] },
  { path: 'admin/pieces', loadComponent: () => import('./features/admin/piece-receipt-page').then((page) => page.PieceReceiptPage), canActivate: [inventoryAdminGuard] },
  { path: 'admin/history', loadComponent: () => import('./features/admin/history-page').then((page) => page.HistoryPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/accounts', loadComponent: () => import('./features/admin/accounts-page').then((page) => page.AccountsPage), canActivate: [organizationAdminGuard] },
  { path: 'pos', loadComponent: () => import('./features/pos/pos-page').then((page) => page.PosPage), canActivate: [posGuard], canDeactivate: [posCanDeactivateGuard] },
  { path: '**', redirectTo: '' },
];
