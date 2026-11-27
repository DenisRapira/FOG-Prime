const fs = require('node:fs');
const path = require('node:path');

const output = process.argv[2];
if (!output || path.extname(output) !== '.html') throw new Error('Supply an output HTML path outside the user package.');
const source = fs.readFileSync(path.join(__dirname, '../FOG.WebView2/wwwroot/index.html'), 'utf8');
const demo = `<style>
  html[data-demo-paused="true"] *, html[data-demo-paused="true"] *::before,
  html[data-demo-paused="true"] *::after { animation-play-state: paused !important; }
</style><script>
  let receiver;
  let step = 0;
  let timer = null;
  let playing = true;
  let gameFilterEnabled = false;
  const frames = [
    ['checking', 'Подключаемся', 'Проверяем соединение автоматически.'],
    ['starting', 'Подключаемся', 'Это займёт немного времени.'],
    ['testingDiscord', 'Проверяем соединение', 'Проверяем доступность Discord и YouTube.'],
    ['ready', 'Подключено', 'Discord и YouTube доступны.'],
    ['recovering', 'Восстанавливаем соединение', 'Повторяем подключение.'],
    ['ready', 'Подключено', 'Discord и YouTube доступны.'],
    ['needsAttention', 'Не удалось подключиться', 'Соединение пока недоступно.']
  ];
  function schedule() {
    clearTimeout(timer);
    timer = null;
    if (playing && !document.hidden) timer = setTimeout(nextFrame, frames[step][0] === 'ready' ? 4000 : 2400);
  }
  function showFrame() {
    const [status, title, message] = frames[step];
    receiver({ data: { type: 'prime', payload: { status, title, message, gameFilterEnabled } } });
    schedule();
  }
  function nextFrame() { step = (step + 1) % frames.length; showFrame(); }
  window.chrome = { webview: {
    addEventListener: (_, callback) => { receiver = callback; },
    postMessage: ({ command }) => {
      if (command === 'runPrime' || command === 'close') { step = 0; showFrame(); }
      if (command === 'toggleGameFilter') { gameFilterEnabled = !gameFilterEnabled; showFrame(); }
    }
  }};
  document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('demoPause').addEventListener('click', event => {
      playing = !playing;
      document.documentElement.dataset.demoPaused = String(!playing);
      event.currentTarget.textContent = playing ? 'Пауза' : 'Играть';
      event.currentTarget.setAttribute('aria-pressed', String(!playing));
      schedule();
    });
    document.getElementById('demoNext').addEventListener('click', nextFrame);
    showFrame();
  });
  document.addEventListener('visibilitychange', schedule);
</script>`;
const toolbar = `<span>ДЕМО · СЕТЬ НЕ МЕНЯЕТСЯ</span>
  <div style="display:flex;gap:8px"><button id="demoPause" type="button" aria-pressed="false" style="background:transparent;color:inherit;border:1px solid var(--wire);padding:6px 10px;border-radius:4px">Пауза</button>
  <button id="demoNext" type="button" style="background:transparent;color:inherit;border:1px solid var(--wire);padding:6px 10px;border-radius:4px">Далее</button></div>`;
const html = source.replace('<title>FOG Prime 2.0.3</title>', '<title>FOG Prime — демонстрация анимации</title>')
  .replace('<span>FOG PRIME</span>', toolbar)
  .replace('  <script>', demo + '\n  <script>');
fs.mkdirSync(path.dirname(path.resolve(output)), { recursive: true });
fs.writeFileSync(output, html);
console.log(`Motion-only preview (simulated states, no Agent or network): ${path.resolve(output)}`);
