const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const assert = require('node:assert/strict');
const output = 'E:/Fogprime/diagnostics/2.0.2-ui';
const states = {
  checking: ['Подключаемся', 'Проверяем соединение автоматически.'],
  starting: ['Подключаемся', 'Это займёт немного времени.'],
  testingDiscord: ['Проверяем соединение', 'Проверяем доступность Discord и YouTube.'],
  recovering: ['Восстанавливаем соединение', 'Повторяем подключение.'],
  ready: ['Подключено', 'Discord и YouTube доступны.'],
  vpnReady: ['Подключено через VPN', 'Discord и YouTube доступны.'],
  needsAttention: ['Не удалось подключиться', 'Соединение пока недоступно.']
};

(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    for (const [width, height] of [[520, 740], [460, 640], [680, 940]]) {
      const page = await browser.newPage({ viewport: { width, height } });
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(() => {
        window.commands = [];
        window.chrome = { webview: {
          postMessage: message => window.commands.push(message.command),
          addEventListener: (_, listener) => { window.receive = listener; }
        }};
      });
      const send = (type, payload) => page.evaluate(({ type, payload }) => window.receive({ data: { type, payload } }), { type, payload });
      const prime = (status, copy = states[status]) => send('prime', { status, title: copy[0], message: copy[1] });
      const focused = async id => assert.equal(await page.evaluate(() => document.activeElement.id), id);
      const shot = name => page.screenshot({ path: path.join(output, `${width}x${height}-${name}.png`), fullPage: true });
      const overflow = async label => {
        const failures = await page.evaluate(() => [...document.querySelectorAll('h1, p, h2, button, .wordmark, .header-cell')]
          .filter(node => node.getClientRects().length)
          .filter(node => node.scrollWidth > node.clientWidth + 1 || node.getBoundingClientRect().left < 0 || node.getBoundingClientRect().right > innerWidth + 1)
          .map(node => node.id || node.className));
        assert.deepEqual(failures, [], `${width} ${label}: text overflow`);
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), label);
      };
      await page.goto(pathToFileURL(path.join(__dirname, '../FOG.WebView2/wwwroot/index.html')).href);
      assert.deepEqual(await page.evaluate(() => window.commands), ['ready']);
      assert.equal(await page.title(), 'FOG Prime 2.0.2');
      const transform = await page.locator('.signal span').first().evaluate(node => getComputedStyle(node).transform);
      await page.waitForFunction(before => getComputedStyle(document.querySelector('.signal span')).transform !== before, transform);
      await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
      });
      assert.equal(await page.locator('.route').first().evaluate(node => getComputedStyle(node, '::before').animationPlayState), 'paused');
      await page.evaluate(() => { delete document.hidden; document.dispatchEvent(new Event('visibilitychange')); });
      await page.emulateMedia({ reducedMotion: 'reduce' });
      for (const [selector, pseudo] of [['.signal span', null], ['.route', '::before']])
        assert.equal(await page.locator(selector).first().evaluate((node, pseudo) => getComputedStyle(node, pseudo).animationName, pseudo), 'none');
      assert.equal(await page.locator('h1').count(), 1);
      for (const status of Object.keys(states)) {
        await prime(status);
        assert.equal(await page.locator('#title').textContent(), states[status][0]);
        assert.equal(await page.locator('#message').textContent(), states[status][1]);
        const actionable = ['ready', 'vpnReady', 'needsAttention'].includes(status);
        assert.equal(await page.locator('#actionButton').isVisible(), actionable);
        if (actionable) {
          assert.equal(await page.locator('#actionButton').textContent(), status === 'needsAttention' ? 'Повторить' : 'Выключить');
          await page.locator('#actionButton').click();
          assert.equal(await page.evaluate(() => window.commands.at(-1)), status === 'needsAttention' ? 'runPrime' : 'close');
          await prime(status);
        }
        await overflow(status);
        await shot(status);
        await page.evaluate(() => {
          window.mutations = 0;
          window.observer = new MutationObserver(records => window.mutations += records.length);
          window.observer.observe(document.querySelector('#statusText'), { subtree: true, childList: true, characterData: true });
        });
        await prime(status);
        assert.equal(await page.evaluate(() => { window.observer.disconnect(); return window.mutations; }), 0);
        await prime(status, [states[status][0] + ' ' + 'Оченьдлинныйзаголовок'.repeat(5), states[status][1].repeat(14)]);
        await overflow(`${status} long copy`);
        await shot(`${status}-long`);
      }
      await prime('ready');
      await page.locator('#actionButton').focus();
      await send('vpn', { active: true });
      await focused('vpnDismiss');
      assert(await page.locator('main').evaluate(node => node.closest('[inert]') !== null));
      await page.keyboard.press('Tab');
      await focused('vpnDismiss');
      await page.keyboard.press('Shift+Tab');
      await focused('vpnDismiss');
      await overflow('vpn');
      await shot('vpn');
      await send('update', { version: '2.0.3' });
      assert.equal(await page.locator('#updateDialog').isVisible(), false);
      await page.keyboard.press('Escape');
      await focused('updateOpen');
      await page.keyboard.press('Tab');
      await focused('updateLater');
      await page.keyboard.press('Shift+Tab');
      await focused('updateOpen');
      await overflow('update');
      await shot('update');
      await page.keyboard.press('Escape');
      await focused('actionButton');
      assert.equal(await page.locator('.app').evaluate(node => node.inert), false);
      await send('update', { version: '2.0.3' });
      assert.equal(await page.locator('#updateDialog').isVisible(), false);
      await send('update', { version: '2.0.4' });
      const beforeLater = await page.evaluate(() => window.commands.length);
      await page.locator('#updateLater').click();
      assert.equal(await page.evaluate(() => window.commands.length), beforeLater);
      await focused('actionButton');
      await send('update', { version: '2.0.5' });
      await page.locator('#updateOpen').click();
      assert.equal(await page.evaluate(() => window.commands.at(-1)), 'openUpdate');
      await focused('actionButton');
      await send('vpn', { active: true });
      assert.equal(await page.locator('#vpnDialog').isVisible(), false);
      await send('vpn', { active: false });
      await send('vpn', { active: true });
      await page.locator('#vpnDismiss').click();
      await focused('actionButton');
      for (const type of ['vpn', 'update']) {
        if (type === 'vpn') {
          await send('vpn', { active: false });
          await send('vpn', { active: true });
        } else await send('update', { version: '2.0.6' });
        await page.locator(`#${type}Title`).evaluate(node => node.textContent = 'Оченьдлинныйзаголовок'.repeat(6));
        await page.locator(`#${type}Message`).evaluate(node => node.textContent = 'Длинноесообщениебезпробелов'.repeat(70));
        await overflow(`${type} long`);
        await shot(`${type}-long`);
        await page.keyboard.press('Tab');
        assert(await page.evaluate(() => document.activeElement.closest('[role="dialog"]') !== null));
        await page.keyboard.press('Escape');
        await focused('actionButton');
      }
      assert.deepEqual(errors, []);
      console.log(`PASS ${width}x${height}: 7 states, long copy, commands, announcements, dialogs/keyboard/focus, motion, overflow`);
      await page.close();
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
