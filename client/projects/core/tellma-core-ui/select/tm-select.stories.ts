import type { Meta, StoryObj } from '@storybook/angular';
import { moduleMetadata } from '@storybook/angular';

import { TmOption } from './tm-option';
import { TmSelect } from './tm-select';

const meta: Meta<TmSelect<unknown>> = {
  title: 'Forms/Select',
  component: TmSelect,
  decorators: [moduleMetadata({ imports: [TmSelect, TmOption] })],
};

export default meta;
type Story = StoryObj<TmSelect<unknown>>;

export const Basic: Story = {
  render: () => ({
    template: `
      <tm-select placeholder="Pick a country" style="max-width: 260px; display: block;">
        <tm-option [value]="1" label="Saudi Arabia">Saudi Arabia</tm-option>
        <tm-option [value]="2" label="United Arab Emirates">United Arab Emirates</tm-option>
        <tm-option [value]="3" label="Ethiopia">Ethiopia</tm-option>
        <tm-option [value]="4" label="Jordan">Jordan</tm-option>
      </tm-select>
    `,
  }),
};

export const Disabled: Story = {
  render: () => ({
    template: `
      <tm-select disabled placeholder="Cannot open" style="max-width: 260px; display: block;">
        <tm-option [value]="1" label="One">One</tm-option>
      </tm-select>
    `,
  }),
};

export const RichOptions: Story = {
  render: () => ({
    template: `
      <tm-select placeholder="Assign to" style="max-width: 260px; display: block;">
        <tm-option [value]="'aa'" label="Ahmad Akra">
          <strong>Ahmad Akra</strong>&nbsp;— Finance
        </tm-option>
        <tm-option [value]="'mb'" label="Mariam B">
          <strong>Mariam B</strong>&nbsp;— Operations
        </tm-option>
      </tm-select>
    `,
  }),
};
