import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Levantamentos | Rastro AI',
    loadComponent: () => import('./pages/surveys/surveys.page').then((m) => m.SurveysPage),
  },
  {
    path: 'surveys/:id',
    title: 'Fluxo | Rastro AI',
    loadComponent: () => import('./pages/survey/survey.page').then((m) => m.SurveyPage),
  },
  {
    path: 'configuracoes',
    title: 'Configurações | Rastro AI',
    loadComponent: () => import('./pages/settings/settings.page').then((m) => m.SettingsPage),
  },
  { path: '**', redirectTo: '' },
];
