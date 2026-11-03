import { test as base, expect } from '@playwright/test';
import { spawn, execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, createWriteStream } from 'node:fs';
import { resolve } from 'node:path';
import { createServer } from 'node:net';

type TestApi = { userName: string; password: string; databaseName: string };

export const test = base.extend<{}, { testApi: TestApi }>({
  testApi: [async ({}, use) => {
    const port = 5246;
    await new Promise<void>((done, fail) => {
      const probe = createServer(); probe.once('error', fail);
      probe.listen(port, '127.0.0.1', () => probe.close(error => error ? fail(error) : done()));
    });
    const apiDll = resolve('../Notflix.API/bin/Release/net10.0/Notflix.API.dll');
    if (!existsSync(apiDll)) throw new Error('Build the Notflix API in Release before browser tests.');
    const databaseName = 'NotflixBrowser_' + randomUUID().replaceAll('-', '');
    const userName = 'browser-tester';
    const password = 'Browser-Only!Password426';
    const environment = { ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      ConnectionStrings__NotflixConnectionString: `Server=(localdb)\\MSSQLLocalDB;Database=${databaseName};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15`,
      Seed__UserName: userName, Seed__Password: password,
    };
    const logDirectory = resolve('test-results', 'server'); mkdirSync(logDirectory, { recursive: true });
    const log = createWriteStream(resolve(logDirectory, `${databaseName}.log`));
    let processHandle: ReturnType<typeof spawn> | undefined;
    try {
      const setup = execFileSync('dotnet', [apiDll, '--setup', '--seed'], { env: environment, encoding: 'utf8', windowsHide: true, timeout: 60000 });
      log.write(setup);
      processHandle = spawn('dotnet', [apiDll], { env: environment, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
      processHandle.stdout?.pipe(log, { end: false }); processHandle.stderr?.pipe(log, { end: false });
      let ready = false;
      for (let attempt = 0; attempt < 100; attempt++) {
        if (processHandle.exitCode !== null) throw new Error('Isolated Notflix API exited before readiness. See server log.');
        try { const response = await fetch(`http://127.0.0.1:${port}/api/auth/csrf`, { signal: AbortSignal.timeout(1000) }); if (response.status === 204) { ready = true; break; } } catch { /* retry until startup deadline */ }
        await new Promise(done => setTimeout(done, 100));
      }
      if (!ready) throw new Error('Isolated Notflix API did not become ready.');
      await use({ userName, password, databaseName });
    } finally {
      if (processHandle && processHandle.exitCode === null) {
        const stopped = new Promise<void>(done => processHandle!.once('exit', () => done()));
        processHandle.kill();
        await Promise.race([stopped, new Promise<void>(done => setTimeout(done, 5000))]);
        if (processHandle.exitCode === null) processHandle.kill('SIGKILL');
      }
      log.end();
      if (!/^NotflixBrowser_[0-9a-f]{32}$/.test(databaseName)) throw new Error('Refusing cleanup outside browser test database prefix.');
      const sqlcmd = process.env.NOTFLIX_SQLCMD ?? 'C:\\Program Files\\Microsoft SQL Server\\Client SDK\\ODBC\\170\\Tools\\Binn\\SQLCMD.EXE';
      if (!existsSync(sqlcmd)) throw new Error('sqlcmd is required to clean the isolated browser-test database.');
      execFileSync(sqlcmd, ['-S', '(localdb)\\MSSQLLocalDB', '-E', '-C', '-d', 'master', '-b', '-l', '15', '-Q',
        `IF DB_ID(N'${databaseName}') IS NOT NULL BEGIN ALTER DATABASE [${databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${databaseName}]; END`],
        { windowsHide: true, timeout: 30000, stdio: 'pipe' });
    }
  }, { scope: 'worker', auto: true, timeout: 90000 }],
});

export { expect };
