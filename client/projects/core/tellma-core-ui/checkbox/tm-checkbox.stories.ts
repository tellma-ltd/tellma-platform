import type { Meta, StoryObj } from '@storybook/angular';

import { TmCheckbox } from './tm-checkbox';

const meta: Meta<TmCheckbox> = {
  title: 'Forms/Checkbox',
  component: TmCheckbox,
};

export default meta;
type Story = StoryObj<TmCheckbox>;

export const Unchecked: Story = {
  render: () => ({ template: `<tm-checkbox>Email me updates</tm-checkbox>` }),
};

export const Checked: Story = {
  render: () => ({ template: `<tm-checkbox [checked]="true">Email me updates</tm-checkbox>` }),
};

export const Indeterminate: Story = {
  render: () => ({
    template: `<tm-checkbox [indeterminate]="true">Select all rows</tm-checkbox>`,
  }),
};

export const Disabled: Story = {
  render: () => ({
    template: `
      <tm-checkbox disabled>Locked option</tm-checkbox>
      <tm-checkbox disabled [checked]="true">Locked and checked</tm-checkbox>
    `,
  }),
};
