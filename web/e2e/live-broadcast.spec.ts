import { execFileSync } from 'node:child_process';
import { expect, test, type APIRequestContext } from '@playwright/test';
import { newAccount, signUpWithStation, type Account } from './support/onboarding';
import { accepted, listen, publish, type PublishOptions } from './support/streaming';

// Sign-up to live broadcast against tests/e2e/compose.yaml (#17): the real tc-streaming Icecast asks this
// API's source-auth endpoint, and the API reads live status from the node's exporter.
test.skip(
  process.env.REAL_E2E_BROADCAST !== '1',
  'Requires REAL_E2E_BROADCAST=1 and the e2e stack (tests/e2e/run.sh).',
);
test.describe.configure({ retries: 0 });
test.setTimeout(240_000);

type Target = {
  publicId: string;
  username: string;
  password: string;
  maxBitrateKbps: number;
  outputs: {
    format: 'Mp3' | 'Opus';
    contentType: 'audio/mpeg' | 'audio/ogg';
    ingestUrl: string;
    listenerUrl: string;
  }[];
};

/** `docker compose` on the e2e stack, for what only an operator can do: plan changes and log checks. */
function compose(...args: string[]): string {
  const file = process.env.E2E_COMPOSE_FILE ?? '../tests/e2e/compose.yaml';
  const envFile = process.env.E2E_ENV_FILE;
  return execFileSync(
    'docker',
    ['compose', '-f', file, ...(envFile ? ['--env-file', envFile] : []), ...args],
    {
      encoding: 'utf8',
      maxBuffer: 64 * 1024 * 1024,
    },
  );
}

function sql(statement: string): void {
  compose(
    'exec',
    '-T',
    'db',
    'psql',
    '-U',
    'tropicast',
    '-d',
    'tropicast',
    '-v',
    'ON_ERROR_STOP=1',
    '-c',
    statement,
  );
}

/** The API's source-auth decisions, to check each refusal happened for the expected reason. */
function apiLogs(): string {
  return compose('logs', '--no-color', 'api');
}

async function signInDesktop(request: APIRequestContext, account: Account) {
  const response = await request.post('/api/v1/auth/token', {
    data: { email: account.email, password: account.password, deviceName: 'e2e desktop' },
  });
  expect(response.status()).toBe(200);
  return (await response.json()) as { accessToken: string; refreshToken: string };
}

async function broadcastTarget(
  request: APIRequestContext,
  token: string,
  tenantId: string,
  stationId: string,
): Promise<Target> {
  const response = await request.post(`/api/v1/stations/${stationId}/broadcast-target`, {
    headers: { Authorization: `Bearer ${token}`, 'X-Tenant-Id': tenantId },
  });
  expect(response.status()).toBe(200);
  return (await response.json()) as Target;
}

function output(target: Target, format: 'Mp3' | 'Opus') {
  const found = target.outputs.find((o) => o.format === format);
  expect(found, `${format} output`).toBeDefined();
  return found!;
}

/** Publishes as the target's device; `as` overrides the credentials or the mount for the negative cases. */
async function tryPublish(
  target: Target,
  format: 'Mp3' | 'Opus',
  as: Partial<
    Pick<PublishOptions, 'username' | 'password' | 'ingestUrl' | 'declaredKbps' | 'seconds'>
  > = {},
) {
  const out = output(target, format);
  return publish({
    ingestUrl: out.ingestUrl,
    username: target.username,
    password: target.password,
    contentType: out.contentType,
    declaredKbps: 64,
    seconds: 3,
    ...as,
  });
}

/** Expects Icecast to refuse the source, and the API to log one more refusal with this reason. */
async function expectRefused(
  attempt: () => ReturnType<typeof tryPublish>,
  reason: string,
): Promise<void> {
  const count = () => apiLogs().split(reason).length - 1;
  const before = count();
  const source = await attempt();
  expect(accepted(source), `source refused (${reason}), got ${source.status}`).toBe(false);
  await expect.poll(count, { timeout: 10_000 }).toBeGreaterThan(before);
}

test('signs up, goes live with a device credential, and shows the listener', async ({
  page,
  request,
}) => {
  const account = newAccount('broadcast');
  await signUpWithStation(page, request, account);

  const desktop = await signInDesktop(request, account);
  const bearer = { Authorization: `Bearer ${desktop.accessToken}` };
  const mine = await request.get('/api/v1/me/stations', { headers: bearer });
  expect(mine.status()).toBe(200);
  const [station] = (await mine.json()) as {
    tenantId: string;
    stationId: string;
    publicId: string;
  }[];
  const first = await broadcastTarget(
    request,
    desktop.accessToken,
    station.tenantId,
    station.stationId,
  );
  expect(first.username).toBe(station.publicId);
  expect(first.maxBitrateKbps).toBe(64);
  const passwords = [first.password];

  const live = await test.step('publish MP3 and play it', async () => {
    const source = await tryPublish(first, 'Mp3', { seconds: 120 });
    expect(source.status, 'Icecast accepted the source').toMatch(/^(100|200)$/);
    // Icecast needs a moment of audio before it serves the mount.
    let listener: Awaited<ReturnType<typeof listen>> | undefined;
    await expect
      .poll(
        async () => {
          listener ??= await listen(output(first, 'Mp3').listenerUrl).catch(() => undefined);
          return listener?.bytes() ?? 0;
        },
        { timeout: 20_000 },
      )
      .toBeGreaterThan(0);
    return { source, listener: listener! };
  });

  try {
    await test.step('dashboard shows the station live with one listener', async () => {
      await page.goto(`/stations/${station.stationId}`);
      await expect(page.getByText('Live', { exact: true })).toBeVisible({ timeout: 45_000 });
      await expect(page.getByText('1 listener', { exact: true })).toBeVisible({ timeout: 45_000 });
    });

    await test.step('bitrate above the plan is refused', async () => {
      await expectRefused(
        () => tryPublish(first, 'Opus', { declaredKbps: 128 }),
        'bitrate 128 kbps above',
      );
    });

    await test.step("another station's credential is refused", async () => {
      // A second workspace of the same user, with its own station and device credential.
      const tenant = await request.post('/api/v1/tenants', {
        headers: bearer,
        data: { name: `Other ${account.slug}`, slug: `other-${account.slug}`.slice(0, 63) },
      });
      expect(tenant.status()).toBe(201);
      const { id: otherTenantId } = (await tenant.json()) as { id: string };
      const created = await request.post('/api/v1/stations', {
        headers: { ...bearer, 'X-Tenant-Id': otherTenantId },
        data: { name: 'Other station', slug: `other-${account.slug}`.slice(0, 63) },
      });
      expect(created.status()).toBe(201);
      const { id: otherStationId } = (await created.json()) as { id: string };
      const other = await broadcastTarget(
        request,
        desktop.accessToken,
        otherTenantId,
        otherStationId,
      );
      passwords.push(other.password);
      const otherMount = output(other, 'Opus').ingestUrl;

      // This station's credential on the other station's mount, under either username.
      await expectRefused(
        () => tryPublish(first, 'Opus', { ingestUrl: otherMount }),
        'unknown station or wrong user',
      );
      await expectRefused(
        () => tryPublish(first, 'Opus', { ingestUrl: otherMount, username: other.username }),
        'wrong or revoked credential',
      );
    });

    const second = await test.step('a replaced credential is refused', async () => {
      // Asking again replaces the device's credential: the first password stops working.
      const second = await broadcastTarget(
        request,
        desktop.accessToken,
        station.tenantId,
        station.stationId,
      );
      expect(second.password).not.toBe(first.password);
      passwords.push(second.password);
      await expectRefused(() => tryPublish(first, 'Opus'), 'wrong or revoked credential');
      const opus = await tryPublish(second, 'Opus', { seconds: 2 });
      expect(accepted(opus), `new credential accepted, got ${opus.status}`).toBe(true);
      await opus.done;
      return second;
    });

    await test.step('a format outside the plan is refused', async () => {
      // Every plan sells both formats, so an operator narrows Free to MP3 for this check.
      sql("UPDATE plans SET formats = 1 WHERE id = 'free'");
      try {
        await expectRefused(() => tryPublish(second, 'Opus'), 'format opus not in the Free plan');
      } finally {
        sql("UPDATE plans SET formats = 3 WHERE id = 'free'");
      }
    });

    await test.step('signing the device out revokes its credential', async () => {
      const revoked = await request.post('/api/v1/auth/token/revoke', {
        data: { refreshToken: desktop.refreshToken },
      });
      expect(revoked.status()).toBe(204);
      await expectRefused(() => tryPublish(second, 'Opus'), 'wrong or revoked credential');
    });
  } finally {
    live.listener.close();
    live.source.stop();
  }

  await test.step('logs never contain a source password', async () => {
    const logs = compose('logs', '--no-color');
    for (const password of passwords) expect(logs).not.toContain(password);
  });
});
