const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { pathToFileURL, fileURLToPath } = require('node:url');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '..');
const docs = path.join(root, 'docs');
const output = process.env.WEBSITE_SCREENSHOT_DIR || process.env.WEBSITE_TEST_OUTPUT || path.join(os.tmpdir(), 'fog-website-audit');
const channel = process.env.PLAYWRIGHT_CHANNEL ?? process.env.WEBSITE_BROWSER_CHANNEL ?? 'msedge';
const status = JSON.parse(fs.readFileSync(path.join(docs, 'release-status.json'), 'utf8'));
const manifest = JSON.parse(fs.readFileSync(path.join(docs, 'site.webmanifest'), 'utf8'));

assert.equal(status.schemaVersion, 1);
assert.match(status.stable.version, /^\d+\.\d+\.\d+$/);
assert.match(status.stable.framework, /^\.NET \d+$/);
assert.match(status.candidate.version, /^\d+\.\d+\.\d+$/);
assert.match(status.candidate.framework, /^\.NET \d+$/);
assert.ok(['unreleased', 'prerelease', 'released'].includes(status.candidate.status));
assert.equal(status.stable.url, `${status.repository}/releases/tag/v${status.stable.version}`);
for (const release of [status.stable, status.candidate]) {
  assert.ok(manifest.description.includes(release.version));
  assert.ok(manifest.description.includes(release.framework));
}
assert.ok(fs.existsSync(fileURLToPath(new URL(manifest.start_url, pathToFileURL(path.join(docs, 'site.webmanifest'))))));

(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({ ...(channel ? { channel } : {}), headless: true });
  const results = [];
  try {
    for (const lang of ['en', 'ru']) {
      const filename = path.join(docs, ...(lang === 'ru' ? ['ru'] : []), 'index.html');
      const source = fs.readFileSync(filename, 'utf8');
      for (const kind of ['stable', 'candidate']) {
        assert.ok(source.includes(`data-${kind}-version="${status[kind].version}"`));
      }
      assert.ok(!/releases\/latest|Local service ready|Локальная служба готова|full support|полная поддержка/i.test(source));
      for (const [width, height] of [[1366, 900], [390, 844]]) {
        const page = await browser.newPage({ viewport: { width, height }, reducedMotion: 'reduce' });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('requestfailed', request => errors.push(`${request.url()}: ${request.failure()?.errorText}`));
        await page.goto(pathToFileURL(filename).href);
        await page.evaluate(() => document.fonts.ready);
        assert.equal(await page.locator('html').getAttribute('lang'), lang);
        assert.equal(await page.locator('main').count(), 1);
        assert.equal(await page.locator('h1').count(), 1);
        const diagnostics = await page.locator('.feature').first().textContent();
        assert.match(diagnostics, /HTTPS/);
        assert.match(diagnostics, /WebSocket Gateway/);
        assert.match(diagnostics, /HELLO \+ heartbeat ACK/);
        assert.match(diagnostics, /DNS/);
        assert.match(diagnostics, lang === 'en' ? /Fresh local Discord logs/ : /Свежие локальные журналы Discord/);
        // Reject affirmative diagnostic claims, not QUIC/voice limitations elsewhere on the page.
        assert.doesNotMatch(diagnostics, /Checks DNS,|CDN paths|real-time transports|TLS and QUIC availability|Проверка DNS,|CDN-маршрутов|транспортов реального времени|TLS и QUIC|QUIC availability|доступност[ьи] QUIC/i);
        assert.match(diagnostics, lang === 'en' ? /no independent QUIC or voice-audibility probe/ : /отдельной проверки QUIC или слышимости голоса нет/);
        assert.ok((await page.title()).includes(status.stable.version));
        for (const selector of ['meta[name="description"]', 'meta[property="og:description"]', 'meta[name="twitter:description"]']) {
          const value = await page.locator(selector).getAttribute('content');
          for (const release of [status.stable, status.candidate]) {
            assert.ok(value.includes(release.version), `${lang} ${selector}: version`);
            assert.ok(value.includes(release.framework), `${lang} ${selector}: framework`);
          }
        }
        const structured = JSON.parse(await page.locator('script[type="application/ld+json"]').textContent());
        assert.equal(structured.softwareVersion, status.stable.version);
        assert.equal(structured.downloadUrl, status.stable.url);
        assert.ok(structured.softwareRequirements.includes(status.stable.framework));
        assert.ok(structured.description.includes(status.candidate.version));
        assert.ok(structured.description.includes(status.candidate.framework));
        const canonical = `https://denisrapira.github.io/FOG-Prime/${lang === 'ru' ? 'ru/' : ''}`;
        assert.equal(await page.locator('link[rel="canonical"]').getAttribute('href'), canonical);
        assert.equal(await page.locator('meta[property="og:url"]').getAttribute('content'), canonical);
        for (const language of ['en', 'ru', 'x-default']) {
          assert.equal(await page.locator(`link[hreflang="${language}"]`).count(), 1);
        }
        for (const kind of ['stable', 'candidate']) {
          assert.equal(await page.locator(`[data-${kind}-version]`).textContent(), status[kind].version);
        }
        for (const link of await page.locator('a.download').all()) {
          assert.equal(await link.getAttribute('href'), status.stable.url);
          for (const hover of [false, true]) {
            if (hover) await link.hover();
            const contrast = await link.evaluate(el => {
              const luminance = color => {
                const rgb = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(value => {
                  const s = value / 255;
                  return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
                });
                return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
              };
              const style = getComputedStyle(el);
              const a = luminance(style.color);
              const b = luminance(style.backgroundColor);
              return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
            });
            assert.ok(contrast >= 4.5, `${lang}: download contrast ${contrast}, hover=${hover}`);
            await page.mouse.move(0, 0);
          }
        }

        const inspection = await page.evaluate(() => {
          const issues = [];
          const ids = new Set();
          for (const el of document.querySelectorAll('[id]')) {
            if (ids.has(el.id)) issues.push(`Duplicate id: ${el.id}`);
            ids.add(el.id);
          }
          for (const el of document.querySelectorAll('a,button')) {
            if (!(el.getAttribute('aria-label') || el.textContent || '').trim()) issues.push('Unnamed control');
          }
          for (const el of document.querySelectorAll('img')) {
            if (!el.hasAttribute('alt') || !el.complete || !el.naturalWidth) issues.push('Invalid image');
          }
          for (const el of document.querySelectorAll('.requirements th')) {
            if (el.scope !== 'row') issues.push('Missing row header scope');
          }
          for (const el of document.querySelectorAll('body *')) {
            if (getComputedStyle(el).position === 'fixed' || !el.getClientRects().length) continue;
            const box = el.getBoundingClientRect();
            if (box.right > innerWidth + 1 || box.left < -1) issues.push(`Overflow: ${el.tagName}.${el.className}`);
          }
          if (document.documentElement.scrollWidth > innerWidth) issues.push('Horizontal page overflow');
          const refs = [...document.querySelectorAll('[href],[src]')].map(el => el.getAttribute('href') || el.getAttribute('src'));
          refs.push(...[...document.querySelectorAll('meta[property="og:image"],meta[name="twitter:image"]')].map(el => el.content));
          for (const el of document.querySelectorAll('*')) {
            for (const match of getComputedStyle(el).backgroundImage.matchAll(/url\(["']?(.*?)["']?\)/g)) refs.push(match[1]);
          }
          return { issues, refs };
        });
        assert.deepEqual(inspection.issues, [], `${lang} ${width}: accessibility/layout`);
        for (const ref of inspection.refs) {
          const url = new URL(ref, page.url());
          if (url.protocol === 'file:') {
            const target = fileURLToPath(url);
            assert.ok(fs.existsSync(target), `Missing local target: ${target}`);
            if (url.hash && path.resolve(target) === path.resolve(filename)) {
              assert.equal(await page.locator(`[id="${decodeURIComponent(url.hash.slice(1))}"]`).count(), 1);
            }
          } else {
            assert.equal(url.protocol, 'https:', `Insecure link: ${ref}`);
            if (url.hostname === 'denisrapira.github.io' && url.pathname.startsWith('/FOG-Prime/assets/')) {
              assert.ok(fs.existsSync(path.join(docs, decodeURIComponent(url.pathname.slice('/FOG-Prime/'.length)))));
            }
            if (url.hostname === 'github.com' && url.pathname.startsWith('/DenisRapira/FOG-Prime/blob/main/')) {
              assert.ok(fs.existsSync(path.join(root, decodeURIComponent(url.pathname.slice('/DenisRapira/FOG-Prime/blob/main/'.length)))));
            }
            assert.ok(!url.pathname.includes(`/releases/tag/v${status.candidate.version}`));
          }
        }

        await page.keyboard.press('Tab');
        assert.equal(await page.locator(':focus').getAttribute('class'), 'skip-link');
        assert.ok((await page.locator('.skip-link').boundingBox()).y >= 0);
        await page.keyboard.press('Enter');
        assert.equal(await page.locator(':focus').getAttribute('id'), 'top');
        const tabbed = new Set();
        const visibleLinks = await page.locator('a:visible').count();
        for (let i = 0; i < visibleLinks + 2; i++) {
          await page.keyboard.press('Tab');
          const focused = await page.evaluate(() => {
            const el = document.activeElement;
            return el.tagName === 'A' ? { href: el.href, outline: getComputedStyle(el).outlineStyle } : null;
          });
          if (focused) {
            assert.notEqual(focused.outline, 'none');
            tabbed.add(focused.href);
          }
        }
        assert.ok(tabbed.has(status.stable.url), 'Download reachable by keyboard');
        for (const href of await page.locator('a:visible').evaluateAll(links => links.map(link => link.href))) {
          assert.ok(tabbed.has(href), `Link not reachable by keyboard: ${href}`);
        }
        const languageLink = page.locator('.nav-links a[lang]');
        assert.ok(await languageLink.isVisible(), 'Language switch visible on mobile');
        await page.evaluate(() => { document.activeElement.blur(); scrollTo(0, 0); });
        const screenshot = path.join(output, `${lang}-${width}.png`);
        await page.screenshot({ path: screenshot, fullPage: true });
        await languageLink.click();
        assert.equal(await page.locator('html').getAttribute('lang'), lang === 'en' ? 'ru' : 'en');
        assert.deepEqual(errors, []);
        results.push({ lang, width, height, screenshot, status: 'passed' });
        await page.close();
      }
    }
  } finally {
    await browser.close();
  }
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ channel: channel || 'chromium', results, externalLinks: 'Source targets checked locally; remote availability is not tested.' }, null, 2));
  console.log(`Website checks passed: ${results.length} EN/RU desktop/mobile views. Screenshots: ${output}`);
})().catch(error => { console.error(error); process.exitCode = 1; });
