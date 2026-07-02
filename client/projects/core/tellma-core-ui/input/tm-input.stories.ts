import type { Meta, StoryObj } from '@storybook/angular';
import { moduleMetadata } from '@storybook/angular';

import { TmFormField } from '@tellma/core-ui/form-field';

import { TmInput } from './tm-input';

const meta: Meta<TmInput> = {
  title: 'Forms/Input',
  component: TmInput,
  decorators: [moduleMetadata({ imports: [TmFormField, TmInput] })],
};

export default meta;
type Story = StoryObj<TmInput>;

export const InField: Story = {
  render: () => ({
    template: `
      <tm-form-field label="Email" hint="Your work email">
        <input tmInput placeholder="name@company.com" />
      </tm-form-field>
    `,
  }),
};

export const WithAdornments: Story = {
  render: () => ({
    template: `
      <tm-form-field label="Search">
        <svg tmPrefix width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <circle cx="7" cy="7" r="4.5" stroke="currentColor" />
          <path d="m10.5 10.5 3 3" stroke="currentColor" stroke-linecap="round" />
        </svg>
        <input tmInput placeholder="Search records" />
      </tm-form-field>
    `,
  }),
};

export const Sizes: Story = {
  render: () => ({
    template: `
      <tm-form-field label="Small" size="sm"><input tmInput /></tm-form-field>
      <tm-form-field label="Medium"><input tmInput /></tm-form-field>
      <tm-form-field label="Large" size="lg"><input tmInput /></tm-form-field>
    `,
  }),
};

export const Disabled: Story = {
  render: () => ({
    template: `
      <tm-form-field label="Disabled">
        <input tmInput disabled value="Cannot touch this" />
      </tm-form-field>
    `,
  }),
};

export const NonFormError: Story = {
  render: () => ({
    template: `
      <tm-form-field label="Code" error="That code is not valid">
        <input tmInput value="XYZ" />
      </tm-form-field>
    `,
  }),
};

/** The bare directive — what a grid cell mounts, no chrome to strip (§3.2). */
export const BareInGridCell: Story = {
  render: () => ({
    template: `<input tmInput placeholder="Bare input" />`,
  }),
};
