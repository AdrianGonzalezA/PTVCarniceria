import { Routes } from '@angular/router';
import { WelcomePage } from './features/welcome/welcome-page';
import { userAdminGuard } from './core/users/user-admin.guard';
import { organizationAdminGuard } from './core/admin/organization-admin.guard';
import { cashWithdrawalAdminGuard } from './core/admin/cash-withdrawal-admin.guard';
import { fiscalSalesBookGuard } from './core/admin/fiscal-sales-book.guard';
import { inventoryAdminGuard } from './core/inventory/inventory-admin.guard';
import { inventoryReceiptGuard } from './core/inventory/inventory-receipt.guard';
import { inventoryTransferGuard } from './core/inventory/inventory-transfer.guard';
import { catalogAdminGuard } from './core/admin/catalog-admin.guard';
import { posCanDeactivateGuard, posGuard } from './core/pos/pos.guard';

export const routes: Routes = [
  { path: '', component: WelcomePage },
  { path: 'superadmin', loadComponent: () => import('./features/manufacturer/manufacturer-page').then((page) => page.ManufacturerPage) },
  { path: 'users', loadComponent: () => import('./features/users/users-page').then((page) => page.UsersPage), canActivate: [userAdminGuard] },
  { path: 'admin/configuracion', loadComponent: () => import('./features/admin/admin-page').then((page) => page.AdminPage), canActivate: [userAdminGuard], data: { area: 'configuration' } },
  { path: 'admin', loadComponent: () => import('./features/admin/admin-page').then((page) => page.AdminPage), canActivate: [userAdminGuard], data: { area: 'business' } },
  { path: 'admin/categories', loadComponent: () => import('./features/admin/categories-page').then((page) => page.CategoriesPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/suppliers', loadComponent: () => import('./features/admin/suppliers-page').then((page) => page.SuppliersPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/products', loadComponent: () => import('./features/admin/products-page').then((page) => page.ProductsPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/taxes', loadComponent: () => import('./features/admin/product-tax-page').then((page) => page.ProductTaxPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/price-lists', loadComponent: () => import('./features/admin/price-lists-page').then((page) => page.PriceListsPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/organization', loadComponent: () => import('./features/admin/organization-page').then((page) => page.OrganizationPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/central-connection', loadComponent: () => import('./features/admin/central-connection-page').then((page) => page.CentralConnectionPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/arca', loadComponent: () => import('./features/admin/arca-settings-page').then((page) => page.ArcaSettingsPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/payment-gateways', loadComponent: () => import('./features/admin/payment-gateways-page').then((page) => page.PaymentGatewaysPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/customers', loadComponent: () => import('./features/admin/customers-page').then((page) => page.CustomersPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/stock', loadComponent: () => import('./features/admin/stock-page').then((page) => page.StockPage), canActivate: [inventoryAdminGuard] },
  { path: 'admin/barcode-layouts', loadComponent: () => import('./features/admin/barcode-layout-page').then((page) => page.BarcodeLayoutPage), canActivate: [inventoryAdminGuard] },
  { path: 'admin/pieces', loadComponent: () => import('./features/admin/piece-receipt-page').then((page) => page.PieceReceiptPage), canActivate: [inventoryAdminGuard] },
  { path: 'admin/receipts', loadComponent: () => import('./features/admin/inventory-receipts-page').then((page) => page.InventoryReceiptsPage), canActivate: [inventoryReceiptGuard] },
  { path: 'admin/transfers', loadComponent: () => import('./features/admin/inventory-transfers-page').then((page) => page.InventoryTransfersPage), canActivate: [inventoryTransferGuard] },
  { path: 'admin/history', loadComponent: () => import('./features/admin/history-page').then((page) => page.HistoryPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/fiscal-sales-book', loadComponent: () => import('./features/admin/fiscal-sales-book-page').then((page) => page.FiscalSalesBookPage), canActivate: [fiscalSalesBookGuard] },
  { path: 'admin/cash-withdrawals', loadComponent: () => import('./features/admin/cash-withdrawals-page').then((page) => page.CashWithdrawalsPage), canActivate: [cashWithdrawalAdminGuard] },
  { path: 'admin/accounts', loadComponent: () => import('./features/admin/accounts-page').then((page) => page.AccountsPage), canActivate: [organizationAdminGuard] },
  { path: 'pos', loadComponent: () => import('./features/pos/pos-page').then((page) => page.PosPage), canActivate: [posGuard], canDeactivate: [posCanDeactivateGuard] },
  { path: '**', redirectTo: '' },
];
