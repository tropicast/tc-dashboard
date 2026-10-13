import { Component, type ReactNode } from 'react';
import { AlertCircle, RefreshCw01 } from '@untitledui/icons';
import { Translation } from 'react-i18next';
import { Button } from '@/components/base/buttons/button';
import { FeaturedIcon } from '@/components/foundations/featured-icon/featured-icon';

export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? (
      <Translation>
        {(t) => (
          <main className="flex min-h-dvh items-center justify-center bg-primary px-4">
            <div className="flex max-w-90 flex-col items-center gap-6 text-center">
              <FeaturedIcon icon={AlertCircle} color="error" theme="light" size="lg" />
              <h1 className="text-display-xs font-semibold text-primary">{t('errors.title')}</h1>
              <Button
                size="lg"
                iconLeading={RefreshCw01}
                onPress={() => this.setState({ failed: false })}
              >
                {t('errors.retry')}
              </Button>
            </div>
          </main>
        )}
      </Translation>
    ) : (
      this.props.children
    );
  }
}
