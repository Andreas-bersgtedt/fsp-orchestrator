import assert from "node:assert/strict";
import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";

import {
  createConfigBuilderClient,
  createLiteClient,
  createManagerClient,
} from "../../generated/fsp/3.1.0/clients.js";


const source = process.env.FSP_TEST_SOURCE;
const python = process.env.PYTHON ?? "python";
const agentToken = "fsp-contract-harness-agent-token";
const operatorPassword = "fsp-contract-harness-operator-password";
const operatorHeaders = {
  Authorization: `Basic ${Buffer.from(`contract-operator:${operatorPassword}`).toString("base64")}`,
};
const maxLogLines = 200;
const processes: FspProcess[] = [];
let workRoot = "";
let liteBaseUrl = "";
let managerBaseUrl = "";
let failed = false;


class FspProcess {
  readonly child: ChildProcessWithoutNullStreams;
  readonly lines: string[] = [];

  constructor(
    readonly name: string,
    command: string[],
    cwd: string,
    environment: NodeJS.ProcessEnv,
  ) {
    this.child = spawn(command[0]!, command.slice(1), {
      cwd,
      env: environment,
      stdio: ["pipe", "pipe", "pipe"],
    });
    this.child.stdin.end();
    this.capture(this.child.stdout);
    this.capture(this.child.stderr);
  }

  private capture(stream: NodeJS.ReadableStream): void {
    stream.setEncoding("utf8");
    stream.on("data", (chunk: string) => {
      this.lines.push(...chunk.split(/\r?\n/).filter(Boolean));
      if (this.lines.length > maxLogLines) {
        this.lines.splice(0, this.lines.length - maxLogLines);
      }
    });
  }

  async stop(): Promise<void> {
    if (this.child.exitCode !== null) {
      return;
    }
    this.child.kill("SIGTERM");
    await Promise.race([
      new Promise<void>((resolve) => this.child.once("exit", () => resolve())),
      new Promise<void>((resolve) =>
        setTimeout(() => {
          if (this.child.exitCode === null) {
            this.child.kill("SIGKILL");
          }
          resolve();
        }, 5_000),
      ),
    ]);
  }

  redactedLog(): string {
    return this.lines
      .join("\n")
      .replaceAll(agentToken, "[REDACTED]")
      .replaceAll(operatorPassword, "[REDACTED]")
      .replace(
        /(authorization|password|secret|token)(["'=:\s]+)[^\s,;"']+/gi,
        "$1$2[REDACTED]",
      );
  }
}


async function freePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const server = createServer();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      assert(address && typeof address !== "string");
      const port = address.port;
      server.close((error) => (error ? reject(error) : resolve(port)));
    });
  });
}


async function waitFor(
  url: string,
  predicate: (response: Response) => Promise<boolean> = async (response) =>
    response.ok,
): Promise<void> {
  const deadline = Date.now() + 45_000;
  let lastError: unknown;
  while (Date.now() < deadline) {
    const exited = processes.find((process) => process.child.exitCode !== null);
    if (exited) {
      failed = true;
      throw new Error(
        `${exited.name} exited with code ${exited.child.exitCode}\n${exited.redactedLog()}`,
      );
    }
    try {
      const response = await fetch(url);
      if (await predicate(response)) {
        return;
      }
      lastError = new Error(`${url} returned ${response.status}`);
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`Timed out waiting for ${url}`, { cause: lastError });
}


function commonEnvironment(): NodeJS.ProcessEnv {
  assert(source);
  return {
    ...process.env,
    AGENT_AUTH_MODE: "required",
    AGENT_TOKEN: agentToken,
    MANAGER_AUTH_ENABLED: "1",
    MANAGER_AUTH_PASSWORD: operatorPassword,
    MANAGER_AUTH_USERNAME: "contract-operator",
    PYTHONPATH: [path.join(source, "src"), source].join(path.delimiter),
    REQUIRE_SIGV4: "0",
  };
}


function contractTest(name: string, body: () => Promise<void>): void {
  test(name, async () => {
    try {
      await body();
    } catch (error) {
      failed = true;
      throw error;
    }
  });
}


before(async () => {
  try {
    assert(
      source,
      "FSP_TEST_SOURCE must point to an exact test-only FSP 3.1.0 source fixture",
    );
    workRoot = await mkdtemp(path.join(tmpdir(), "fsp-contract-"));
    const managerRoot = path.join(workRoot, "manager");
    const agentRoot = path.join(workRoot, "agent");
    const liteRoot = path.join(workRoot, "lite");
    await Promise.all([
      mkdir(managerRoot),
      mkdir(agentRoot),
      mkdir(liteRoot),
    ]);

    const managerPort = await freePort();
    const agentPort = await freePort();
    const litePort = await freePort();
    managerBaseUrl = `http://127.0.0.1:${managerPort}`;
    liteBaseUrl = `http://127.0.0.1:${litePort}`;

    const manager = new FspProcess(
      "manager",
      [python, "-m", "enterprise.manager"],
      managerRoot,
      {
        ...commonEnvironment(),
        CONTROL_HOST: "127.0.0.1",
        CONTROL_PORT: String(managerPort),
        MANAGER_SUPERVISION_MODE: "external",
        PORT: String(agentPort),
      },
    );
    processes.push(manager);
    await waitFor(`${managerBaseUrl}/healthz`);

    const agent = new FspProcess(
      "agent",
      [python, path.join(source, "main.py")],
      agentRoot,
      {
        ...commonEnvironment(),
        AGENT_ID: "contract-agent",
        HOST: "127.0.0.1",
        MANAGER_URL: managerBaseUrl,
        PORT: String(agentPort),
      },
    );
    processes.push(agent);

    const lite = new FspProcess(
      "lite",
      [python, path.join(source, "main.py")],
      liteRoot,
      {
        ...commonEnvironment(),
        ENABLE_CONFIG_BUILDER: "1",
        HOST: "127.0.0.1",
        MANAGER_URL: "",
        PORT: String(litePort),
      },
    );
    processes.push(lite);

    await Promise.all([
      waitFor(`${liteBaseUrl}/readyz`),
      waitFor(`${managerBaseUrl}/agents`, async (response) => {
        const authenticated = await fetch(response.url, { headers: operatorHeaders });
        if (!authenticated.ok) {
          return false;
        }
        const body = (await authenticated.json()) as { agents?: unknown[] };
        return body.agents?.length === 1;
      }),
    ]);
  } catch (error) {
    failed = true;
    throw error;
  }
});


after(async (context) => {
  await Promise.all(processes.map((process) => process.stop()));
  if (failed || context.signal.aborted) {
    const logRoot = path.resolve("artifacts", "fsp-contract-logs");
    await mkdir(logRoot, { recursive: true });
    await Promise.all(
      processes.map((process) =>
        writeFile(
          path.join(logRoot, `${process.name}.log`),
          `${process.redactedLog()}\n`,
          "utf8",
        ),
      ),
    );
  }
  if (workRoot) {
    await rm(workRoot, { recursive: true, force: true });
  }
});


contractTest("generated clients cover health, readiness, fleet, and queue status", async () => {
  const lite = createLiteClient(liteBaseUrl);
  const manager = createManagerClient(managerBaseUrl, operatorHeaders);

  const liteHealth = await lite.GET("/healthz");
  assert.equal(liteHealth.response.status, 200);
  assert.equal(liteHealth.data?.status, "ok");

  const liteReady = await lite.GET("/readyz");
  assert.equal(liteReady.response.status, 200);
  assert.equal(liteReady.data?.status, "ready");

  const managerHealth = await manager.GET("/healthz");
  assert.equal(managerHealth.response.status, 200);
  assert.equal(managerHealth.data?.status, "ok");

  const fleet = await manager.GET("/agents");
  assert.equal(fleet.response.status, 200);
  assert.equal(fleet.data?.agents.length, 1);

  const queue = await manager.GET("/control/work-queue");
  assert.equal(queue.response.status, 200);
  assert(queue.data);
});


contractTest("generated clients cover configuration read, write, and source validation", async () => {
  const config = createConfigBuilderClient(liteBaseUrl, operatorHeaders);

  const current = await config.GET("/_config/api/current");
  assert.equal(current.response.status, 200);
  assert(current.data?.settings);

  const apply = await config.POST("/_config/api/apply", {
    body: { settings: { refresh_poll_seconds: 601 } },
    params: { header: { "If-Match": "\"runtime-3.1.0\"" } },
  });
  assert.equal(apply.response.status, 200);
  assert.equal(apply.data?.ok, true);

  const sourceValidation = await config.POST(
    "/_config/api/sources/{source_id}/validate",
    { params: { path: { source_id: "database:default" } } },
  );
  assert.equal(sourceValidation.response.status, 200);
  assert.equal(sourceValidation.data?.ok, true);
});


contractTest("generated clients cover backup and supported queue actions", async () => {
  const config = createConfigBuilderClient(liteBaseUrl, operatorHeaders);
  const manager = createManagerClient(managerBaseUrl, operatorHeaders);

  const backup = await config.POST("/_config/api/backup", {
    body: { password: "contract-test-backup-password" },
    parseAs: "blob",
  });
  assert.equal(backup.response.status, 200);
  assert(backup.data);

  const cancel = await manager.POST(
    "/control/work-queue/requests/{request_id}/cancel",
    { params: { path: { request_id: "missing-request" } } },
  );
  assert.equal(cancel.response.status, 404);

  const retry = await manager.POST(
    "/control/work-queue/tasks/{task_id}/retry",
    { params: { path: { task_id: "missing-task" } } },
  );
  assert.equal(retry.response.status, 409);
});
