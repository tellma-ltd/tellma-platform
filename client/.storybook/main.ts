import type { StorybookConfig } from '@storybook/angular';

const config: StorybookConfig = {
  stories: ['../projects/**/*.stories.ts'],
  addons: [],
  framework: {
    name: '@storybook/angular',
    options: {},
  },
};

export default config;
