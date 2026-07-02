import { Routes } from '@angular/router';

import { StoryIndex } from './story-index';
import { StoryPage } from './story-page';

export const routes: Routes = [
  { path: '', component: StoryIndex },
  { path: 'story/:id', component: StoryPage },
];
