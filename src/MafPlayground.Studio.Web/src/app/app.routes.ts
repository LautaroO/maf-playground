import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: 'catalog',
    loadComponent: () => import('./catalog/catalog').then((module) => module.Catalog),
  },
  {
    path: 'input',
    loadComponent: () => import('./input/input-workspace').then((module) => module.InputWorkspace),
  },
  { path: 'runs', loadComponent: () => import('./runs/run-list').then((module) => module.RunList) },
  {
    path: 'runs/:id',
    loadComponent: () => import('./runs/run-workspace').then((module) => module.RunWorkspace),
  },
  { path: '', pathMatch: 'full', redirectTo: 'catalog' },
  { path: '**', redirectTo: 'catalog' },
];
