const { execSync } = require('child_process');

const PORT = 3001;

function getListeningPidsOnPort(port) {
  try {
    const output = execSync(`netstat -ano -p tcp | findstr :${port}`, {
      stdio: ['ignore', 'pipe', 'ignore'],
    }).toString();

    const pids = new Set();
    for (const line of output.split(/\r?\n/)) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      if (!/LISTENING/i.test(trimmed)) continue;

      const cols = trimmed.split(/\s+/);
      const localAddress = cols[1] || '';
      const pid = cols[cols.length - 1];

      if (!localAddress.endsWith(`:${port}`)) continue;
      if (!/^\d+$/.test(pid)) continue;
      pids.add(pid);
    }

    return Array.from(pids);
  } catch {
    return [];
  }
}

function killPids(pids) {
  for (const pid of pids) {
    try {
      execSync(`taskkill /PID ${pid} /F`, { stdio: ['ignore', 'pipe', 'pipe'] });
    } catch {
      // Ignore kill failures for already-exited processes.
    }
  }
}

const pids = getListeningPidsOnPort(PORT);
if (pids.length === 0) {
  console.log(`No active listener on ${PORT}.`);
  process.exit(0);
}

killPids(pids);
console.log(`Stopped listener(s) on ${PORT}: ${pids.join(', ')}`);
