import { Component, type ReactNode } from 'react';
import { Translation } from 'react-i18next';

export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? (
      <Translation>
        {(t) => (
          <main className="auth">
            <section>
              <h1>{t('errors.title')}</h1>
              <button onClick={() => this.setState({ failed: false })}>{t('errors.retry')}</button>
            </section>
          </main>
        )}
      </Translation>
    ) : (
      this.props.children
    );
  }
}
