import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

export function FormPage({ title, children }: { title: string; children: ReactNode }) {
  return (
    <main className="auth">
      <Link className="brand" to="/">
        Tropicast
      </Link>
      <section>
        <h1>{title}</h1>
        {children}
      </section>
    </main>
  );
}
