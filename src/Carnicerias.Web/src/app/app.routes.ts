import { Routes } from '@angular/router';
import { WelcomePage } from './features/welcome/welcome-page';
import { UsersPage } from './features/users/users-page';
import { userAdminGuard } from './core/users/user-admin.guard';
import { AdminPage } from './features/admin/admin-page';
import { CategoriesPage } from './features/admin/categories-page';
import { ProductsPage } from './features/admin/products-page';
import { PriceListsPage } from './features/admin/price-lists-page';
import { catalogAdminGuard } from './core/admin/catalog-admin.guard';
import { PosPage } from './features/pos/pos-page';
import { posCanDeactivateGuard, posGuard } from './core/pos/pos.guard';

export const routes: Routes = [
  { path: '', component: WelcomePage },
  { path: 'users', component: UsersPage, canActivate: [userAdminGuard] },
  { path: 'admin', component: AdminPage, canActivate: [userAdminGuard] },
  { path: 'admin/categories', component: CategoriesPage, canActivate: [catalogAdminGuard] },
  { path: 'admin/products', component: ProductsPage, canActivate: [catalogAdminGuard] },
  { path: 'admin/price-lists', component: PriceListsPage, canActivate: [catalogAdminGuard] },
  { path: 'pos', component: PosPage, canActivate: [posGuard], canDeactivate: [posCanDeactivateGuard] },
  { path: '**', redirectTo: '' },
];
