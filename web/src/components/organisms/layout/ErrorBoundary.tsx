import { Component, type ReactNode } from 'react';

export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? (
      <main className="auth">
        <section>
          <h1>Something went wrong</h1>
          <button onClick={() => this.setState({ failed: false })}>Try again</button>
        </section>
      </main>
    ) : (
      this.props.children
    );
  }
}
