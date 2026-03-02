import { Routes } from '@angular/router';

export const routes: Routes = [
	{
		path: '',
		pathMatch: 'full',
		loadComponent: () =>
			import('./pages/demo-page/demo-page.component').then(
				(m) => m.DemoPageComponent,
			),
	},
];
