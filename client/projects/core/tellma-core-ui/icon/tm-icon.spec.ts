// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, ErrorHandler, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { ChevronRight, Plus, X } from 'lucide';

import { TmIcon } from './tm-icon';
import { TmIconRegistry, provideTellmaIcons, type TmIconSource } from './tm-icon-registry';

// The emitted token values, set inline because unit tests load no global
// stylesheet: the box sizes and the stroke are what the assertions read.
const TOKENS =
  '--icon-size: 16px; --icon-size-xs: 12px; --icon-size-sm: 14px; --icon-size-md: 16px; ' +
  '--icon-size-lg: 20px; --icon-size-xl: 24px; --icon-stroke-width: 1.75;';

@Component({
  imports: [TmIcon],
  template: `
    <div [attr.style]="tokens" [attr.dir]="dir()">
      <tm-icon data-testid="icon" [name]="name()" [svg]="svg()" [size]="size()" [label]="label()" />
      <span dir="ltr"><tm-icon data-testid="island" [name]="name()" /></span>
    </div>
  `,
})
class Host {
  readonly tokens = TOKENS;
  readonly name = signal<string | undefined>('plus');
  readonly svg = signal<TmIconSource | undefined>(undefined);
  readonly size = signal<'xs' | 'sm' | 'md' | 'lg' | 'xl' | undefined>(undefined);
  readonly label = signal<string | undefined>(undefined);
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
}

@Component({
  imports: [TmIcon],
  template: `
    <div [attr.style]="tokens">
      <tm-icon data-testid="known" name="plus" />
      <tm-icon data-testid="unknown-1" name="no-such-icon" />
      <tm-icon data-testid="unknown-2" name="no-such-icon" />
    </div>
  `,
})
class UnknownHost {
  readonly tokens = TOKENS;
}

function setup(): { fixture: ComponentFixture<Host>; host: Host; icon: HTMLElement } {
  TestBed.configureTestingModule({
    providers: [
      provideTellmaIcons({
        plus: Plus,
        x: X,
        'chevron-right': { node: ChevronRight, mirrorInRtl: true },
      }),
    ],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const icon = fixture.nativeElement.querySelector('[data-testid="icon"]') as HTMLElement;
  return { fixture, host: fixture.componentInstance, icon };
}

function svgOf(icon: Element): SVGSVGElement | null {
  return icon.querySelector(':scope > svg');
}

describe('tm-icon', () => {
  it('draws the glyph in the change-detection pass that creates it', () => {
    const { icon } = setup();
    // One detectChanges, no waiting: the glyph must already be there.
    const svg = svgOf(icon)!;
    expect(svg).not.toBeNull();
    expect(svg.namespaceURI).toBe('http://www.w3.org/2000/svg');
    expect(svg.getAttribute('viewBox')).toBe('0 0 24 24');
    expect(svg.getAttribute('fill')).toBe('none');
    expect(svg.getAttribute('stroke')).toBe('currentColor');
    expect(svg.getAttribute('stroke-linecap')).toBe('round');
    expect(svg.getAttribute('stroke-linejoin')).toBe('round');
    const paths = Array.from(svg.children);
    expect(paths.map((path) => path.getAttribute('d'))).toEqual(
      Plus.map(([, attrs]) => attrs['d']),
    );
  });

  it('is decorative unless labelled, then role="img" named by the label', () => {
    const { fixture, host, icon } = setup();
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.getAttribute('role')).toBeNull();
    expect(icon.getAttribute('aria-label')).toBeNull();

    host.label.set('Add');
    fixture.detectChanges();
    expect(icon.getAttribute('role')).toBe('img');
    expect(icon.getAttribute('aria-label')).toBe('Add');
    expect(icon.getAttribute('aria-hidden')).toBeNull();

    // An empty label is no label.
    host.label.set('');
    fixture.detectChanges();
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.getAttribute('role')).toBeNull();
  });

  it('redraws when the name changes, keeping exactly one glyph', () => {
    const { fixture, host, icon } = setup();
    host.name.set('x');
    fixture.detectChanges();
    expect(icon.querySelectorAll('svg')).toHaveLength(1);
    expect(svgOf(icon)!.children).toHaveLength(X.length);
    expect(svgOf(icon)!.children[0].getAttribute('d')).toBe(X[0][1]['d']);

    host.name.set(undefined);
    fixture.detectChanges();
    expect(svgOf(icon)).toBeNull();
  });

  it('draws a name registered after the icon rendered', () => {
    const { fixture, host, icon } = setup();
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    host.name.set('late');
    fixture.detectChanges();
    expect(svgOf(icon)).toBeNull();

    TestBed.inject(TmIconRegistry).register({ late: X });
    fixture.detectChanges();
    expect(svgOf(icon)!.children).toHaveLength(X.length);
    error.mockRestore();
  });

  it('draws svg data directly, ahead of the name and without the registry', () => {
    const { fixture, host, icon } = setup();
    host.svg.set([['circle', { cx: 12, cy: 12, r: 3, key: 'k1' }]]);
    fixture.detectChanges();
    const circle = svgOf(icon)!.children[0];
    expect(circle.tagName).toBe('circle');
    expect(circle.getAttribute('r')).toBe('3');
    // A framework list key is not an SVG attribute.
    expect(circle.hasAttribute('key')).toBe(false);
  });

  it('draws nested children of grouped data', () => {
    const { fixture, host, icon } = setup();
    host.svg.set([['g', { opacity: 0.5 }, [['path', { d: 'M1 1' }]]]]);
    fixture.detectChanges();
    const group = svgOf(icon)!.children[0];
    expect(group.tagName).toBe('g');
    expect(group.children[0].getAttribute('d')).toBe('M1 1');
  });

  it('refused svg data draws an empty box and reports once through the ErrorHandler', () => {
    const handled: unknown[] = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: ErrorHandler, useValue: { handleError: (e: unknown) => handled.push(e) } },
      ],
    });
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    const refused: TmIconSource = [['path', { onclick: 'alert(1)' }]];
    host.svg.set(refused);
    fixture.detectChanges();
    const icon = fixture.nativeElement.querySelector('[data-testid="icon"]') as HTMLElement;
    expect(svgOf(icon)).toBeNull();
    expect(handled).toHaveLength(1);
    expect(String(handled[0])).toMatch(/refused attribute "onclick"/);

    // The same refused data again reports nothing new.
    host.svg.set(undefined);
    fixture.detectChanges();
    host.svg.set(refused);
    fixture.detectChanges();
    expect(handled).toHaveLength(1);
  });

  it('an unknown name draws an empty box of the same size and logs one development error', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({ providers: [provideTellmaIcons({ plus: Plus })] });
    const fixture = TestBed.createComponent(UnknownHost);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const known = root.querySelector('[data-testid="known"]') as HTMLElement;
    const unknown = root.querySelector('[data-testid="unknown-1"]') as HTMLElement;

    expect(svgOf(known)).not.toBeNull();
    expect(svgOf(unknown)).toBeNull();
    const knownBox = known.getBoundingClientRect();
    const unknownBox = unknown.getBoundingClientRect();
    expect(knownBox.width).toBe(16);
    expect(unknownBox.width).toBe(knownBox.width);
    expect(unknownBox.height).toBe(knownBox.height);

    // Two instances of the same unknown name: one error, naming it.
    expect(error).toHaveBeenCalledTimes(1);
    expect(String(error.mock.calls[0][0])).toContain('"no-such-icon"');
    error.mockRestore();
  });

  it('size picks a step of the ladder; unset, the container sets the box', () => {
    const { fixture, host, icon } = setup();
    const container = icon.parentElement!;
    container.style.setProperty('--icon-size', '18px');
    expect(icon.getBoundingClientRect().width).toBe(18);

    const steps = { xs: 12, sm: 14, md: 16, lg: 20, xl: 24 } as const;
    for (const [size, px] of Object.entries(steps)) {
      host.size.set(size as keyof typeof steps);
      fixture.detectChanges();
      expect(icon.getBoundingClientRect().width, size).toBe(px);
      expect(icon.getBoundingClientRect().height, size).toBe(px);
      expect(svgOf(icon)!.getBoundingClientRect().width, size).toBe(px);
    }
  });

  it('strokes at --icon-stroke-width, which a host may raise', () => {
    const { icon } = setup();
    expect(getComputedStyle(svgOf(icon)!).strokeWidth).toBe('1.75px');
    icon.style.setProperty('--icon-stroke-width', '2.25');
    expect(getComputedStyle(svgOf(icon)!).strokeWidth).toBe('2.25px');
  });

  it('mirrors a mirrorInRtl glyph under RTL only, live, and not in a dir="ltr" island', () => {
    const { fixture, host, icon } = setup();
    const island = fixture.nativeElement.querySelector('[data-testid="island"]') as HTMLElement;
    const transform = (element: HTMLElement) => getComputedStyle(svgOf(element)!).transform;

    host.name.set('chevron-right');
    fixture.detectChanges();
    expect(icon.classList).toContain('tm-icon--mirror');
    expect(transform(icon)).toBe('none');

    host.dir.set('rtl');
    fixture.detectChanges();
    expect(transform(icon)).toBe('matrix(-1, 0, 0, 1, 0, 0)');
    expect(transform(island)).toBe('none');

    // A glyph without the flag never mirrors.
    host.name.set('x');
    fixture.detectChanges();
    expect(icon.classList).not.toContain('tm-icon--mirror');
    expect(transform(icon)).toBe('none');
  });
});
