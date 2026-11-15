import { Routes } from '@angular/router';
import { leaveGuard } from './core/leave-guard';

// O título é a chave de tradução (route.*); o PageTitleStrategy traduz e acrescenta o nome do produto.

export const routes: Routes = [
  {
    path: '',
    title: 'route.surveys',
    loadComponent: () => import('./pages/surveys/surveys.page').then((m) => m.SurveysPage),
  },
  {
    path: 'surveys/:id',
    title: 'route.survey',
    loadComponent: () => import('./pages/survey/survey.page').then((m) => m.SurveyPage),
  },
  {
    path: 'surveys/:id/visao',
    title: 'route.consolidated',
    canDeactivate: [leaveGuard],
    loadComponent: () => import('./pages/diagram/diagram.page').then((m) => m.DiagramPage),
  },
  {
    path: 'repositorios/:nome/banco',
    title: 'route.database',
    loadComponent: () => import('./pages/database/database.page').then((m) => m.DatabasePage),
  },
  {
    path: 'configuracoes',
    title: 'route.settings',
    loadComponent: () => import('./pages/settings/settings.page').then((m) => m.SettingsPage),
  },
  { path: '**', redirectTo: '' },
];
