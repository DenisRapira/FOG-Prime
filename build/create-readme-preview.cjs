const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const assert = require('node:assert/strict');

// Documentation only: mock the host bridge; never start Agent or network probes.
const output = process.argv[2];
if (!output) throw new Error('Supply a temporary frame output directory.');
const states = [
  ['checking', 'Checking your network', 'Finding a connection that works.'],
  ['starting', 'Connecting', 'Applying the connection profile.'],
  ['testingDiscord', 'Checking access', 'Verifying Discord and YouTube endpoints.'],
  ['ready', 'Connected', 'Discord and YouTube checks passed.']
];

(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 460, height: 640 }, deviceScaleFactor: 1 });
    await page.route('**/*', route => route.request().url().startsWith('file:') ? route.continue() : route.abort());
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      window.chrome = { webview: {
        postMessage() {},
        addEventListener(_, callback) { window.previewReceive = callback; }
      }};
    });
    await page.goto(pathToFileURL(path.join(__dirname, '../FOG.WebView2/wwwroot/index.html')).href);
    await page.evaluate(() => {
      document.documentElement.lang = 'en';
      document.querySelectorAll('.stage-label').forEach((node, i) => { node.textContent = ['System', 'Connection', 'Access'][i]; });
      document.getElementById('gameFilterLabel').textContent = 'Game Filter';
      document.querySelector('.footer span').textContent = 'PREVIEW / SIMULATED STATES';
      document.getElementById('clock').style.visibility = 'hidden';
    });
    for (let frame = 0; frame < 48; frame++) {
      const [status, title, message] = states[Math.floor(frame / 12)];
      await page.evaluate(({ status, title, message, frame }) => {
        window.previewReceive({ data: { type: 'prime', payload: { status, title, message, gameFilterEnabled: false } } });
        document.getElementById('actionButton').textContent = 'Disconnect';
        for (const animation of document.getAnimations()) {
          animation.pause();
          animation.currentTime = frame * 100;
        }
      }, { status, title, message, frame });
      assert(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight), 'Preview must fit its viewport.');
      assert(await page.evaluate(() => [...document.querySelectorAll('h1, .message, .stage-label, .footer, button')]
        .filter(node => node.getClientRects().length)
        .every(node => node.scrollWidth <= node.clientWidth + 1)), 'Preview text must fit.');
      await page.screenshot({ path: path.join(output, `frame-${String(frame).padStart(2, '0')}.png`) });
    }
    assert.deepEqual(errors, []);
    console.log('Captured 48 mocked UI frames. English labels are documentation-only; no network or Agent was started.');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
