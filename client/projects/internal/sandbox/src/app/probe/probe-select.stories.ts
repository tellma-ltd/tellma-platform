import type { Meta, StoryObj } from '@storybook/angular';

import { ProbeSelect } from './probe-select';

const meta: Meta<ProbeSelect> = {
  title: 'Spike/Probe Select',
  component: ProbeSelect,
};

export default meta;
type Story = StoryObj<ProbeSelect>;

export const Default: Story = {};

export const ManyOptions: Story = {
  args: {
    options: Array.from({ length: 30 }, (_, i) => `Option ${i + 1}`),
  },
};
