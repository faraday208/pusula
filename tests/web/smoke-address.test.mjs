// Tests for tools/smoke-address.mjs (run with: node --test "tests/web/*.test.mjs"). Synthetic inputs only.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';

import { checkAddress, expectedAddress } from '../../tools/smoke-address.mjs';

describe('checkAddress', () => {
  it('accepts the default address', () => {
    assert.equal(checkAddress(expectedAddress, ''), null);
  });

  it('accepts the announced fallback when 5190 is busy', () => {
    const output = 'pusula: port 5190 is in use; listening on http://127.0.0.1:43453/ instead\n';
    assert.match(checkAddress('http://127.0.0.1:43453', output), /port 5190 is in use/);
  });

  it('rejects another port that was not announced', () => {
    assert.throws(() => checkAddress('http://127.0.0.1:43453', ''), /did not say that port 5190 is in use/);
  });

  it('rejects an announced address that differs from the one listened on', () => {
    const output = 'pusula: port 5190 is in use; listening on http://127.0.0.1:1111/ instead\n';
    assert.throws(() => checkAddress('http://127.0.0.1:2222', output), /built-in settings were not read/);
  });

  it('rejects a foreign address', () => {
    assert.throws(() => checkAddress('http://localhost:5000', ''), /built-in settings were not read/);
  });
});
