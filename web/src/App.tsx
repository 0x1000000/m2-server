import type {FormEvent} from 'react';
import {useCallback, useEffect, useState} from 'react';

type DisplayMode = { width: number; height: number; refreshHz: number };
type MonitorSetting = {
  id: string;
  name: string;
  enabled: boolean;
  primary: boolean;
  order: number;
  mode: DisplayMode;
  rotation: number;
  dpiScale: number | null;
  hdrEnabled: boolean | null;
};
type Profile = { id: string; name: string; monitors: MonitorSetting[] };
type Script = { id: string; name: string };
type Section = 'profiles' | 'scripts';

function describeMonitor(monitor: MonitorSetting): string {
  if (!monitor.enabled) return 'Off';

  const rotation =
    ({1: 0, 2: 90, 3: 180, 4: 270} as Record<number, number>)[monitor.rotation] ?? 0;
  const scale = monitor.dpiScale == null ? 'Keep current scale' : `${monitor.dpiScale}% scale`;
  const hdr =
    monitor.hdrEnabled == null ? 'HDR unchanged' : `HDR ${monitor.hdrEnabled ? 'on' : 'off'}`;
  return `${monitor.mode.width} × ${monitor.mode.height} · ${monitor.mode.refreshHz} Hz · ${scale} · ${rotation}° · ${hdr}`;
}

function getErrorMessage(text: string, status: number): string {
  try {
    const error = JSON.parse(text) as { error?: string; detail?: string; message?: string };
    return (error.error ?? error.detail ?? error.message ?? text) || `Request failed: ${status}`;
  } catch {
    return text || `Request failed: ${status}`;
  }
}

export default function App() {
  const [online, setOnline] = useState(true);
  const [loggedIn, setLoggedIn] = useState(() => Boolean(localStorage.getItem('mm-token')));
  const [profiles, setProfiles] = useState<Profile[]>([]);
  const [scripts, setScripts] = useState<Script[]>([]);
  const [section, setSection] = useState<Section>('profiles');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [password, setPassword] = useState('');

  const request = useCallback(async <T, >(path: string, init: RequestInit = {}): Promise<T> => {
    const headers = new Headers(init.headers);
    headers.set('Content-Type', 'application/json');
    const token = localStorage.getItem('mm-token');
    if (token) headers.set('Authorization', `Bearer ${token}`);
    const response = await fetch(`/api${path}`, {...init, headers, cache: 'no-store'});
    if (response.status === 401) {
      localStorage.removeItem('mm-token');
      setLoggedIn(false);
      throw new Error('Please sign in again.');
    }
    if (!response.ok) throw new Error(getErrorMessage(await response.text(), response.status));
    return response.json() as Promise<T>;
  }, []);

  const refresh = useCallback(async () => {
    let response: Response;
    try {
      response = await fetch('/api/health', {cache: 'no-store'});
    } catch {
      setOnline(false);
      return;
    }
    setOnline(response.ok);
    if (!response.ok || !localStorage.getItem('mm-token')) return;
    try {
      const [nextProfiles, nextScripts] = await Promise.all([
        request<Profile[]>('/presets'),
        request<Script[]>('/scripts'),
      ]);
      setProfiles(nextProfiles);
      setScripts(nextScripts);
    } catch (error) {
      setMessage(String(error));
    }
  }, [request]);

  useEffect(() => {
    void refresh();
    const interval = window.setInterval(() => void refresh(), 60_000);
    return () => window.clearInterval(interval);
  }, [refresh]);

  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      const result = await request<{ token: string }>('/login', {
        method: 'POST',
        body: JSON.stringify({password}),
      });
      localStorage.setItem('mm-token', result.token);
      setPassword('');
      setLoggedIn(true);
      await refresh();
    } catch {
      setMessage('The password was not accepted.');
    }
  }

  function logout() {
    localStorage.removeItem('mm-token');
    setLoggedIn(false);
    setProfiles([]);
    setScripts([]);
  }

  async function runAction(path: string, successFallback: string) {
    setBusy(true);
    try {
      const result = await request<{ message: string }>(path, {method: 'POST'});
      setMessage(result.message || successFallback);
    } catch (error) {
      setMessage(String(error));
    } finally {
      setBusy(false);
    }
  }

  if (!online) {
    return (
      <main className="state-page">
        <div className="brand-mark">MM</div>
        <h1>M2 Server is unavailable</h1>
        <p>Connect to your home network and make sure the M2 Server service is running.</p>
        <button onClick={() => void refresh()}>Retry connection</button>
      </main>
    );
  }

  if (!loggedIn) {
    return (
      <main className="state-page">
        <div className="brand-mark">MM</div>
        <h1>M2 Server</h1>
        <p>Enter the password configured on your PC.</p>
        <form className="login-form" onSubmit={login}>
          <input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            placeholder="Password"
            required
          />
          <button type="submit">Sign in</button>
        </form>
        {message && <small role="alert">{message}</small>}
      </main>
    );
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="topbar-brand">
          <span className="brand-mark small">MM</span>
          <strong>M2 Server</strong>
        </div>
        <div className="topbar-right">
          <span className="connection-status">Connected</span>
          <button className="signout" onClick={logout}>
            Sign out
          </button>
        </div>
        <nav className="tabs" aria-label="Main sections" role="tablist">
          <button
            role="tab"
            aria-selected={section === 'profiles'}
            className={section === 'profiles' ? 'selected' : ''}
            onClick={() => setSection('profiles')}
          >
            Profiles
          </button>
          <button
            role="tab"
            aria-selected={section === 'scripts'}
            className={section === 'scripts' ? 'selected' : ''}
            onClick={() => setSection('scripts')}
          >
            Scripts
          </button>
        </nav>
      </header>

      <main className="content" role="tabpanel">
        <div className="page-heading">
          <div>
            <div className="eyebrow">
              {section === 'profiles' ? 'DISPLAY PROFILES' : 'REMOTE ACTIONS'}
            </div>
            <h1>{section === 'profiles' ? 'Profiles' : 'Scripts'}</h1>
          </div>
          {section === 'scripts' && (
            <p className="page-subtitle">
              Scripts can be edited in the desktop app. Running a script may shut down the PC.
            </p>
          )}
        </div>

        {section === 'profiles' ? (
          profiles.length ? (
            <div className="tile-grid">
              {profiles.map((profile) => (
                <article className="tile" key={profile.id}>
                  <h2 className="tile-title" title={profile.name}>
                    {profile.name}
                  </h2>
                  <div className="tile-details">
                    {profile.monitors.length ? (
                      [...profile.monitors]
                        .sort((a, b) => a.order - b.order)
                        .map((monitor) => (
                          <div
                            className="monitor-row"
                            key={monitor.id}
                            title={`${monitor.name}${monitor.primary ? ' (Primary)' : ''}: ${describeMonitor(monitor)}`}
                          >
                            <strong className="monitor-name">
                              {monitor.name}
                              {monitor.primary ? ' (Primary)' : ''}
                            </strong>
                            <span className="monitor-status">{describeMonitor(monitor)}</span>
                          </div>
                        ))
                    ) : (
                      <span className="muted">No monitors saved</span>
                    )}
                  </div>
                  <div className="tile-actions">
                    <button
                      className="primary"
                      disabled={busy}
                      onClick={() =>
                        void runAction(`/presets/${profile.id}/activate`, 'Profile activated.')
                      }
                    >
                      Activate
                    </button>
                  </div>
                </article>
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <h2>No profiles yet</h2>
              <p>Create a profile in the desktop app.</p>
            </div>
          )
        ) : scripts.length ? (
          <div className="tile-grid">
            {scripts.map((script) => (
              <article className="tile script-tile" key={script.id}>
                <h2 className="tile-title" title={script.name}>
                  {script.name}
                </h2>
                <div className="tile-details"/>
                <div className="tile-actions">
                  <button
                    className="primary"
                    disabled={busy}
                    onClick={() => {
                      if (window.confirm(`Run ${script.name}? This may shut down the PC.`))
                        void runAction(`/scripts/${script.id}/run`, 'Script completed.');
                    }}
                  >
                    Run
                  </button>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <div className="empty-state">
            <h2>No scripts yet</h2>
            <p>Create a script in the desktop app.</p>
          </div>
        )}
      </main>

      {message && (
        <div className="notice" role="status">
          {message}
          <button aria-label="Dismiss message" onClick={() => setMessage('')}>
            ×
          </button>
        </div>
      )}
    </div>
  );
}
