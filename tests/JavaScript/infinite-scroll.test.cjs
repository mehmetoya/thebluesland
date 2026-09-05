const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

test('a failed automatic load preserves the manual link without retrying', async () => {
  let requests = 0;
  let observations = 0;
  let removed = false;
  const attributes = new Map();
  const trigger = {
    href: 'https://example.test/?page=2',
    textContent: 'Show more playlists',
    closest: () => ({ querySelectorAll: () => Array(24) }),
    setAttribute: (name, value) => attributes.set(name, value),
    removeAttribute: (name) => attributes.delete(name),
    remove: () => { removed = true; },
  };
  class Observer {
    constructor(callback) { this.callback = callback; }
    observe() {
      observations++;
      // Model a trigger that remains visible after a failed request. The cap prevents
      // a regressed implementation from keeping this test process alive forever.
      if (observations < 5) queueMicrotask(() => this.callback([{ isIntersecting: true }]));
    }
    disconnect() {}
  }
  const fetch = async () => { requests++; return { ok: false, status: 503 }; };
  const context = {
    window: { IntersectionObserver: Observer, fetch },
    IntersectionObserver: Observer,
    fetch,
    document: { addEventListener: (_, callback) => callback(), querySelector: () => trigger },
  };
  const script = fs.readFileSync(path.join(__dirname, '../../src/TheBluesland.Web/wwwroot/js/infinite-scroll.js'), 'utf8');
  vm.runInNewContext(script, context);
  await new Promise(setImmediate);
  assert.equal(requests, 1);
  assert.equal(observations, 1);
  assert.equal(removed, false);
  assert.equal(trigger.textContent, 'Show more playlists');
  assert.equal(attributes.has('aria-disabled'), false);
  assert.equal(trigger.href, 'https://example.test/?page=2');
});
