const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const assert = require('node:assert/strict');
const output = process.env.UI_SCREENSHOT_DIR || path.join(require('node:os').tmpdir(), 'fog-ui-audit');
const channel = process.env.PLAYWRIGHT_CHANNEL ?? 'msedge';
const games = ['ROB RUN', 'Warframe'];
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
  const browser = await chromium.launch({ ...(channel ? { channel } : {}), headless: true });
  try {
    for (const [width, height] of [[520, 740], [460, 640], [680, 940]]) {
      const page = await browser.newPage({ viewport: { width, height } });
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(() => {
        const setInterval = window.setInterval.bind(window);
        const clearInterval = window.clearInterval.bind(window);
        window.clockIntervals = new Map();
        window.setInterval = (callback, delay, ...args) => {
          const id = setInterval(callback, delay, ...args);
          window.clockIntervals.set(id, callback);
          return id;
        };
        window.clearInterval = id => {
          window.clockIntervals.delete(id);
          clearInterval(id);
        };
        window.commands = [];
        window.chrome = { webview: {
          postMessage: message => window.commands.push(message.command),
          addEventListener: (_, listener) => { window.receive = listener; }
        }};
      });
      const send = (type, payload) => page.evaluate(({ type, payload }) => window.receive({ data: { type, payload } }), { type, payload });
      const prime = (status, copy = states[status]) => send('prime', { status, title: copy[0], message: copy[1], gameFilterGames: games });
      const focused = async id => assert.equal(await page.evaluate(() => document.activeElement.id), id);
      const shot = name => page.screenshot({ path: path.join(output, `${width}x${height}-${name}.png`), fullPage: true, animations: 'disabled' });
      const overflow = async label => {
        const failures = await page.evaluate(() => [...document.querySelectorAll('h1, p, h2, button, #gameFilterLabel, .wordmark, .header-cell, .stage-label')]
          .filter(node => node.getClientRects().length)
          .filter(node => node.scrollWidth > node.clientWidth + 1 || node.getBoundingClientRect().left < 0 || node.getBoundingClientRect().right > innerWidth + 1)
          .map(node => node.id || node.className));
        assert.deepEqual(failures, [], `${width} ${label}: text overflow`);
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), label);
      };
      await page.goto(pathToFileURL(path.join(__dirname, '../FOG.WebView2/wwwroot/index.html')).href);
      assert.deepEqual(await page.evaluate(() => window.commands), ['ready']);
      assert.equal(await page.title(), 'FOG Prime 2.0.3');
      assert.equal(await page.locator('.header-cell').first().textContent(), '2.0.3');
      const gameSwitch = page.getByRole('switch', { name: 'Игровой фильтр' });
      assert.equal(await gameSwitch.getAttribute('aria-checked'), 'false');
      assert.equal(await gameSwitch.isEnabled(), true);
      const noGameList = async () => {
        assert.equal(await page.locator('#gameList, .game-list, ul, ol, li, [role="list"], [role="listitem"]').count(), 0);
        assert.equal(await gameSwitch.getAttribute('aria-describedby'), null);
        assert.equal(await page.locator('.game-filter').innerText(), 'Игровой фильтр');
        const text = await page.locator('body').innerText();
        for (const name of [...games, 'LongGameName', '<img']) assert.equal(text.includes(name), false);
        assert.equal(await page.evaluate(() => window.injected), undefined);
      };
      await noGameList();
      for (const angle of [0, 45, 90, 135]) {
        const clearOfText = await page.evaluate(angle => {
          const contours = [...document.querySelectorAll('.route')];
          for (const contour of contours) { contour.style.animation = 'none'; contour.style.transform = `rotate(${angle}deg)`; }
          const heading = document.getElementById('title').getBoundingClientRect();
          const header = document.querySelector('.header').getBoundingClientRect();
          const clear = contours.every(contour => {
            const bounds = contour.getBoundingClientRect();
            return bounds.bottom <= heading.top + 1 && bounds.top >= header.bottom - 1;
          });
          for (const contour of contours) { contour.style.removeProperty('animation'); contour.style.removeProperty('transform'); }
          return clear;
        }, angle);
        assert.equal(clearOfText, true, `${width}: rotated contours must not overlap header or status text`);
      }
      const malicious = '<img src=x onerror="window.injected=true"><script>window.injected=true</script>';
      const manyGames = Array.from({ length: 16 }, (_, index) => `${index + 1} ${'LongGameName'.repeat(12)}`);
      for (const gameFilterGames of [undefined, null, [], games, 'ROB RUN', [null, 42, {}], [malicious, 'Warframe'], manyGames]) {
        await send('prime', { status: 'ready', title: states.ready[0], message: states.ready[1], gameFilterGames });
        await noGameList();
        assert.equal(await page.locator('.game-filter img').count(), 0);
        assert.equal(await gameSwitch.isEnabled(), true);
        assert(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight), 'Switch-only UI must not overflow the viewport vertically');
        await overflow('ignored game names');
      }
      await shot('game-filter-switch-only');
      await prime('checking');
      assert.equal(await page.locator('.connection-stage[aria-current="step"]').count(), 1);
      const transform = await page.locator('.signal span').first().evaluate(node => getComputedStyle(node).transform);
      await page.waitForFunction(before => getComputedStyle(document.querySelector('.signal span')).transform !== before, transform);
      const orbit = await page.locator('.route').first().evaluate(node => getComputedStyle(node).transform);
      await page.waitForFunction(before => getComputedStyle(document.querySelector('.route')).transform !== before, orbit);
      assert.equal(await page.evaluate(() => window.clockIntervals.size), 1);
      await page.evaluate(() => {
        Object.defineProperty(document, 'hidden', { configurable: true, value: true });
        document.dispatchEvent(new Event('visibilitychange'));
        document.getElementById('clock').textContent = 'hidden-clock';
        updateClock();
      });
      assert.equal(await page.evaluate(() => window.clockIntervals.size), 0, 'Hidden document has no clock timer');
      assert.equal(await page.locator('#clock').textContent(), 'hidden-clock');
      assert.equal(await page.locator('.route').first().evaluate(node => getComputedStyle(node, '::before').animationPlayState), 'paused');
      assert.equal(await page.locator('.route').first().evaluate(node => getComputedStyle(node).animationPlayState), 'paused');
      assert.equal(await page.locator('.connection-stage[data-state="active"] .stage-marker').evaluate(node => getComputedStyle(node).animationPlayState), 'paused');
      assert.equal(await page.locator('.signal span').first().evaluate(node => getComputedStyle(node).animationPlayState), 'paused');
      assert.equal(await page.locator('.connection-stage[data-state="active"] .stage-marker').evaluate(node => getComputedStyle(node, '::before').animationPlayState), 'paused');
      await page.evaluate(() => { delete document.hidden; document.dispatchEvent(new Event('visibilitychange')); });
      assert.equal(await page.evaluate(() => window.clockIntervals.size), 1);
      assert.notEqual(await page.locator('#clock').textContent(), 'hidden-clock', 'Visible clock refreshes immediately');
      assert.equal(await page.locator('.route').first().evaluate(node => getComputedStyle(node, '::before').animationPlayState), 'running');
      await page.evaluate(() => {
        document.dispatchEvent(new Event('visibilitychange'));
        document.dispatchEvent(new Event('visibilitychange'));
      });
      assert.equal(await page.evaluate(() => window.clockIntervals.size), 1, 'Visibility events cannot accumulate timers');
      for (const status of Object.keys(states)) {
        await prime(status);
        const ready = ['ready', 'vpnReady'].includes(status);
        await page.waitForFunction(expected => getComputedStyle(document.querySelector('.connection-confirm')).opacity === expected, ready ? '1' : '0');
        assert.equal(await page.locator('.hero').evaluate(node => node.getAnimations({ subtree: true })
          .filter(animation => animation.effect.getTiming().iterations === Infinity).length),
          ['ready', 'vpnReady', 'needsAttention'].includes(status) ? 0 : 6,
          'Only busy states keep the two contours, three bars and active-stage ring moving');
      }
      await prime('checking');
      await page.emulateMedia({ reducedMotion: 'reduce' });
      for (const [selector, pseudo] of [['.signal span', null], ['.route', null], ['.route', '::before'], ['.connection-stage[data-state="active"] .stage-marker', null]])
        assert.equal(await page.locator(selector).first().evaluate((node, pseudo) => getComputedStyle(node, pseudo).animationName, pseudo), 'none');
      assert.equal(await page.locator('h1').count(), 1);
      for (const status of Object.keys(states)) {
        await prime(status);
        assert.equal(await page.locator('#title').textContent(), states[status][0]);
        assert.equal(await page.locator('#message').textContent(), states[status][1]);
        const expectedStages = {
          checking: ['active', 'pending', 'pending'], starting: ['done', 'active', 'pending'],
          testingDiscord: ['done', 'done', 'active'], recovering: ['done', 'active', 'pending'],
          ready: ['done', 'done', 'done'], vpnReady: ['done', 'done', 'done'],
          needsAttention: ['pending', 'pending', 'pending']
        }[status];
        assert.deepEqual(await page.locator('.connection-stage').evaluateAll(nodes => nodes.map(node => node.dataset.state)), expectedStages);
        assert.equal(await page.locator('.connection-stage[aria-current="step"]').count(), expectedStages.includes('active') ? 1 : 0);
        assert.equal(await page.getByRole('group', { name: 'Этапы подключения' }).count(), 1);
        assert.equal(await page.getByRole('progressbar').count(), 0, 'No invented numeric progress');
        assert.equal(await page.locator('.hero').evaluate(node => node.getAnimations({ subtree: true })
          .filter(animation => animation.effect.getTiming().iterations === Infinity).length), 0,
          'Reduced motion disables all continuous animation');
        assert.equal(await page.locator('.connection-confirm').evaluate(node => getComputedStyle(node).opacity), ['ready', 'vpnReady'].includes(status) ? '1' : '0');
        const actionable = ['ready', 'vpnReady', 'needsAttention'].includes(status);
        assert.equal(await page.locator('#actionButton').isVisible(), actionable);
        if (actionable) {
          assert.equal(await page.locator('#actionButton').textContent(), status === 'needsAttention' ? 'Повторить' : 'Выключить');
          await page.locator('#actionButton').click();
          assert.equal(await page.evaluate(() => window.commands.at(-1)), status === 'needsAttention' ? 'runPrime' : 'close');
          await prime(status);
          assert.equal(await page.locator('#actionButton').isVisible(), true, 'Retry can receive the same error again');
        }
        await overflow(status);
        await shot(status);
        for (const [enabled, busy] of [[false, false], [true, false], [false, true], [true, true]]) {
          await send('prime', { status, title: states[status][0], message: states[status][1], gameFilterEnabled: enabled, gameFilterBusy: busy, gameFilterGames: games });
          await noGameList();
          assert.equal(await gameSwitch.getAttribute('aria-checked'), String(enabled));
          assert.equal(await gameSwitch.isDisabled(), busy);
          assert.equal(await gameSwitch.getAttribute('aria-busy'), String(busy));
          const before = await page.evaluate(() => window.commands.length);
          if (busy) {
            await gameSwitch.evaluate(node => node.click());
            assert.equal(await page.evaluate(() => window.commands.length), before);
          } else {
            await gameSwitch.click();
            assert.equal(await page.evaluate(() => window.commands.length), before + 1);
            assert.equal(await page.evaluate(() => window.commands.at(-1)), 'toggleGameFilter');
            assert.equal(await gameSwitch.getAttribute('aria-checked'), String(enabled), 'Wait for backend confirmation');
          }
          for (const key of ['Space', 'Enter']) {
            if (busy) {
              await page.locator('main').focus();
              await gameSwitch.focus();
              assert.notEqual(await page.evaluate(() => document.activeElement.id), 'gameFilterSwitch');
            } else {
              await gameSwitch.focus();
              await focused('gameFilterSwitch');
            }
            const beforeKey = await page.evaluate(() => window.commands.length);
            await page.keyboard.press(key);
            assert.equal(await page.evaluate(() => window.commands.length), beforeKey + (busy ? 0 : 1));
            if (!busy) assert.equal(await page.evaluate(() => window.commands.at(-1)), 'toggleGameFilter');
            assert.equal(await gameSwitch.getAttribute('aria-checked'), String(enabled));
          }
          await overflow(`${status} game filter ${enabled}/${busy}`);
          if (status === 'ready') await shot(`game-filter-${enabled ? 'on' : 'off'}${busy ? '-busy' : ''}`);
        }
        await prime(status);
        await page.evaluate(() => {
          window.mutations = 0;
          window.observer = new MutationObserver(records => window.mutations += records.length);
          window.observer.observe(document.body, { attributes: true, subtree: true, childList: true, characterData: true });
        });
        for (let repeat = 0; repeat < 20; repeat++) await prime(status);
        assert.equal(await page.evaluate(() => { window.observer.disconnect(); return window.mutations; }), 0);
        await prime(status, [states[status][0] + ' ' + 'Оченьдлинныйзаголовок'.repeat(5), states[status][1].repeat(14)]);
        await overflow(`${status} long copy`);
        await shot(`${status}-long`);
      }
      await prime('ready');
      await gameSwitch.focus();
      for (const key of ['Space', 'Enter']) {
        const before = await page.evaluate(() => window.commands.length);
        await page.keyboard.press(key);
        assert.equal(await page.evaluate(() => window.commands.length), before + 1);
        assert.equal(await page.evaluate(() => window.commands.at(-1)), 'toggleGameFilter');
        assert.equal(await gameSwitch.getAttribute('aria-checked'), 'false');
      }
      await send('vpn', { active: true });
      await focused('vpnDismiss');
      await page.keyboard.press('Escape');
      await focused('gameFilterSwitch');
      await send('vpn', { active: false });
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
      await send('vpn', { active: true });
      await focused('vpnDismiss');
      await page.keyboard.press('Escape');
      await focused('updateOpen');
      await page.keyboard.press('Tab');
      await focused('updateLater');
      await prime('ready');
      await send('vpn', { active: true });
      await send('update', { version: '2.0.3' });
      await focused('updateLater');
      assert.equal(await page.locator('#updateDialog').isVisible(), true, 'Duplicate snapshots preserve pending update and focus');
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
      console.log(`PASS ${width}x${height}: 7 states, 20 duplicate prime payloads/state cause zero DOM mutations, hidden clock stopped/resumed without duplicate timers, contour motion, confirmed-only success mark, no infinite motion on success/error, no game list/names, switch-only on/off/busy/keyboard/backend confirmation, retry, long copy, commands, announcements, dialogs/update focus, reduced motion, overflow`);
      await page.close();
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
