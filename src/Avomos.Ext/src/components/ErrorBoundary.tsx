import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false, error: null };

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('[Avomos] ErrorBoundary caught:', error, info.componentStack);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{
          position: 'fixed', inset: 0, zIndex: 2147483647,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          background: 'rgba(11,10,8,0.95)', color: '#f7f1dc',
          fontFamily: 'monospace', fontSize: 13,
        }}>
          <div style={{ textAlign: 'center', maxWidth: 400 }}>
            <div style={{ fontSize: 24, marginBottom: 8 }}>⚠</div>
            <div style={{ marginBottom: 12 }}>Avomos error</div>
            <pre style={{
              background: 'rgba(255,255,255,0.06)', borderRadius: 6,
              padding: 8, fontSize: 11, textAlign: 'left', overflow: 'auto',
              maxHeight: 200,
            }}>{this.state.error?.message}</pre>
            <button
              onClick={() => location.reload()}
              style={{
                marginTop: 12, border: 0, borderRadius: 6, padding: '6px 16px',
                background: '#f0c15c', color: '#19140c', cursor: 'pointer',
                font: 'inherit', fontWeight: 600,
              }}
            >Reload page</button>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
