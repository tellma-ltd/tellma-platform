import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      // The shell's language toggle injects TranslocoService.
      providers: [provideRouter(routes), provideTellmaUi()],
    }).compileComponents();
  });

  it('should create the app', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(fixture.componentInstance).toBeTruthy();
  });
});
