import { spawn, type ChildProcess } from 'node:child_process';
import http from 'node:http';
import net from 'node:net';

/**
 * A classic Icecast source client, ported from tc-streaming tests/raw_source.py: PUT with no length, then raw
 * audio. Credentials go in the Authorization header, never on a command line or in a URL.
 */
export type Source = {
  /** Icecast's first status line code: "100"/"200" accepted, anything else (or "closed") refused. */
  status: string;
  /** Resolves when the audio ends or the connection closes. */
  done: Promise<void>;
  stop: () => void;
};

export type PublishOptions = {
  ingestUrl: string;
  username: string;
  password: string;
  contentType: 'audio/mpeg' | 'audio/ogg';
  /** Sent as Ice-Bitrate; the encoder always uses 64 kbps (the Free plan's limit). */
  declaredKbps?: number;
  seconds?: number;
};

function tone(contentType: PublishOptions['contentType'], seconds: number): ChildProcess {
  const codec =
    contentType === 'audio/mpeg'
      ? ['-c:a', 'libmp3lame', '-b:a', '64k', '-f', 'mp3']
      : ['-c:a', 'libopus', '-b:a', '64k', '-f', 'ogg'];
  return spawn(
    'ffmpeg',
    [
      '-nostdin',
      '-loglevel',
      'error',
      '-re',
      '-f',
      'lavfi',
      '-i',
      'sine=frequency=440',
      '-t',
      `${seconds}`,
      ...codec,
      '-',
    ],
    // Quiet: stopping a source ends ffmpeg with "Broken pipe". A broken encoder still fails the test (no audio).
    { stdio: ['ignore', 'pipe', 'ignore'] },
  );
}

export async function publish(options: PublishOptions): Promise<Source> {
  const url = new URL(options.ingestUrl);
  const socket = net.connect(Number(url.port), url.hostname);
  await new Promise<void>((resolve, reject) => {
    socket.once('connect', resolve);
    socket.once('error', reject);
  });
  const auth = Buffer.from(`${options.username}:${options.password}`).toString('base64');
  const bitrate = options.declaredKbps ? `Ice-Bitrate: ${options.declaredKbps}\r\n` : '';
  socket.write(
    `PUT ${url.pathname} HTTP/1.1\r\nHost: ${url.host}\r\nAuthorization: Basic ${auth}\r\n` +
      `Content-Type: ${options.contentType}\r\nIce-Public: 0\r\nExpect: 100-continue\r\n${bitrate}\r\n`,
  );
  const status = await new Promise<string>((resolve) => {
    // The status line can arrive split over several TCP chunks: read up to its end.
    let head = '';
    const finish = (result: string) => {
      clearTimeout(timer);
      socket.off('data', onData);
      socket.off('close', onClose);
      resolve(result);
    };
    const onData = (chunk: Buffer) => {
      head += chunk.toString('latin1');
      const end = head.indexOf('\r\n');
      if (end >= 0) {
        const line = head.slice(0, end);
        finish(line.startsWith('HTTP/') ? (line.split(' ', 3)[1] ?? 'invalid') : 'invalid');
      } else if (head.length > 1024) {
        finish('invalid');
      }
    };
    const onClose = () => finish('closed');
    const timer = setTimeout(() => finish('timeout'), 10_000);
    socket.on('data', onData);
    socket.once('close', onClose);
    socket.once('error', () => undefined);
  });
  if (status !== '100' && status !== '200') {
    socket.destroy();
    return { status, done: Promise.resolve(), stop: () => undefined };
  }

  const encoder = tone(options.contentType, options.seconds ?? 60);
  encoder.stdout!.pipe(socket);
  socket.on('error', () => encoder.kill());
  const done = new Promise<void>((resolve) => {
    encoder.once('exit', () => {
      socket.end();
      resolve();
    });
    socket.once('close', () => resolve());
  });
  return {
    status,
    done,
    stop: () => {
      encoder.kill();
      socket.destroy();
    },
  };
}

export function accepted(source: Source): boolean {
  return source.status === '100' || source.status === '200';
}

/** A listener that stays connected until closed; counts the audio bytes received. */
export type Listener = { bytes: () => number; close: () => void };

export function listen(listenerUrl: string): Promise<Listener> {
  return new Promise((resolve, reject) => {
    const request = http.get(listenerUrl, (response) => {
      if (response.statusCode !== 200) {
        response.resume();
        reject(new Error(`listener got HTTP ${response.statusCode}`));
        return;
      }
      let bytes = 0;
      response.on('data', (chunk: Buffer) => (bytes += chunk.length));
      response.on('error', () => undefined);
      resolve({ bytes: () => bytes, close: () => request.destroy() });
    });
    request.on('error', reject);
  });
}
