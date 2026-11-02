// The address check of tools/smoke-test.mjs, apart so that it can be tested (tests/web/smoke-address.test.mjs).

export const expectedAddress = 'http://localhost:5190';

// What pusula says when the port of the default is held and it listens on another one instead.
const busyLine = /pusula: port 5190 is in use; listening on (http:\/\/127\.0\.0\.1:\d+)\/? instead/;

// Throws when the address is not the one the built-in settings give. A busy port 5190 is not a settings failure: pusula says so and
// listens on a free port of 127.0.0.1, and that address is accepted (the same one it said). Returns a note for that case.
export function checkAddress(address, output) {
  if (address === expectedAddress) {
    return null;
  }
  const busy = busyLine.exec(output);
  if (busy && busy[1] === address) {
    return `port 5190 is in use here; pusula listens on ${address} instead, as it should`;
  }
  if (/^http:\/\/127\.0\.0\.1:\d+$/.test(address) && !busy) {
    throw new Error(`listens on ${address}, not on ${expectedAddress}, and did not say that port 5190 is in use: the built-in settings were not read`);
  }
  throw new Error(`listens on ${address}, not on ${expectedAddress}: the built-in settings were not read`);
}
