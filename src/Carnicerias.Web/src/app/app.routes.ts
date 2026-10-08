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
  { path: 'admin', loadComponent: () => import('./features/admin/admin-page').then((page) => page.AdminPage), canActivate: [userAdminGuard] },
  { path: 'admin/categories', loadComponent: () => import('./features/admin/categories-page').then((page) => page.CategoriesPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/products', loadComponent: () => import('./features/admin/products-page').then((page) => page.ProductsPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/price-lists', loadComponent: () => import('./features/admin/price-lists-page').then((page) => page.PriceListsPage), canActivate: [catalogAdminGuard] },
  { path: 'admin/organization', loadComponent: () => import('./features/admin/organization-page').then((page) => page.OrganizationPage), canActivate: [organizationAdminGuard] },
  { path: 'admin/stock', loadComponent: () => import('./features/admin/stock-page').then((page) => page.StockPage), canActivate: [inventoryAdminGuard] },
  { path: 'admin/history', loadComponent: () => import('./features/admin/history-page').then((page) => page.HistoryPage), canActivate: [organizationAdminGuard] },
  { path: 'pos', loadComponent: () => import('./features/pos/pos-page').then((page) => page.PosPage), canActivate: [posGuard], canDeactivate: [posCanDeactivateGuard] },
  { path: '**', redirectTo: '' },
];
