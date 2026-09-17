const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    for (const [width, height] of [[520, 740], [460, 640], [680, 940]]) {
      const page = await browser.newPage({ viewport: { width, height } });
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(() => {
        window.chrome = { webview: {
          postMessage: message => { window.lastCommand = message.command; },
          addEventListener: (_, listener) => { window.receive = listener; }
        }};
      });
      await page.goto(require('node:url').pathToFileURL(path.join(__dirname, '../FOG.WebView2/wwwroot/index.html')).href);
      await page.waitForFunction(() => getComputedStyle(document.querySelector('.footer')).opacity === '1');
      const initialTransform = await page.locator('.signal span').first().evaluate(node => getComputedStyle(node).transform);
      await page.waitForFunction(before => getComputedStyle(document.querySelector('.signal span')).transform !== before, initialTransform);
      await page.emulateMedia({ reducedMotion: 'reduce' });
      assert.equal(await page.locator('.signal span').first().evaluate(node => getComputedStyle(node).animationName), 'none');
      assert.equal(await page.locator('.route').first().evaluate(node => getComputedStyle(node, '::before').animationName), 'none');
      await page.emulateMedia({ reducedMotion: 'no-preference' });
      await page.evaluate(() => document.documentElement.dataset.hidden = 'true');
      assert.equal(await page.locator('.route').first().evaluate(node => getComputedStyle(node, '::before').animationPlayState), 'paused');
      await page.evaluate(() => delete document.documentElement.dataset.hidden);
      for (const status of ['checking', 'starting', 'testingDiscord', 'ready', 'recovering', 'needsAttention']) {
        await page.evaluate(status => window.receive({ data: { type: 'prime', payload: {
          status, title: status === 'ready' ? 'FOG подключён' : 'Проверяем соединение',
          message: 'Discord и YouTube отвечают. Голосовой канал проверяется в Discord.', progress: 72
        }}}), status);
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Overflow at ${width}: ${status}`);
        const button = page.locator('#actionButton');
        if (status === 'ready' || status === 'needsAttention') {
          await button.click();
          assert.equal(await page.evaluate(() => window.lastCommand), status === 'ready' ? 'close' : 'runPrime');
        }
      }
      await page.evaluate(() => window.receive({ data: { type: 'vpn', payload: { active: true }}}));
      assert(await page.locator('#vpnDialog').isVisible());
      await page.locator('#vpnDismiss').click();
      assert(!await page.locator('#vpnDialog').isVisible());
      await page.evaluate(() => window.receive({ data: { type: 'prime', payload: {
        status: 'ready', title: 'FOG подключён',
        message: 'Discord и YouTube отвечают. Голосовой канал проверяется в Discord.', progress: 100
      }}}));
      assert.equal(await page.locator('.signal span').first().evaluate(node => getComputedStyle(node).animationName), 'none');
      assert.deepEqual(errors, []);
      await page.evaluate(() => Promise.all(document.getAnimations()
        .filter(animation => animation.effect.getTiming().iterations !== Infinity)
        .map(animation => animation.finished.catch(() => {}))));
      const output = path.join(__dirname, '../release/ui-review');
      fs.mkdirSync(output, { recursive: true });
      await page.screenshot({ path: path.join(output, `fog2-${width}.png`), fullPage: true });
      console.log(`PASS UI ${width}x${height}: states, commands, VPN dialog, overflow, animation, reduced motion, JS errors`);
      await page.close();
    }
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
