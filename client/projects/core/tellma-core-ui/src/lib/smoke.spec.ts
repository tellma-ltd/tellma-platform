import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TM_CORE_UI_VERSION } from './version';

@Component({
  template: `<p>smoke</p>`,
})
class SmokeHost {}

describe('core-ui workspace smoke', () => {
  it('exposes the version marker', () => {
    expect(TM_CORE_UI_VERSION).toBe('0.1.0');
  });

  it('renders a component through the zoneless TestBed', async () => {
    const fixture = TestBed.createComponent(SmokeHost);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('smoke');
  });
});
