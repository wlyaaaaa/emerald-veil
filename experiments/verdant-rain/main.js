import { RainScene } from './scene.js';
import { RainSceneGL } from './scene-gl.js';

const $ = (id) => document.getElementById(id);
const defaults = Object.freeze({ vivid: 0.5, rain: 0.75, wind: 0, haze: 0.5, depth: 0 });
const settings = { ...defaults, rainStyle: 1 };
let scene, paused = false, holding = false, comparing = false, ready = false;
const search = new URLSearchParams(location.search);

function showMessage(text) { $('message').textContent = text; $('message').hidden = false; }
function updatePause() {
  if (scene) scene.paused = paused || holding || comparing;
  $('pause').setAttribute('aria-pressed', String(paused));
  $('pause-label').textContent = paused ? '让雨继续' : '停住雨幕';
  $('motion-info').textContent = comparing ? '原图 · 未调色' : paused || holding ? '雨被停住了' : '雨在落下';
  document.body.classList.toggle('holding', holding || paused);
}
function setCompare(value) {
  comparing = value;
  document.body.classList.toggle('comparing', value);
  $('compare').setAttribute('aria-pressed', String(value));
  $('compare-label').textContent = value ? '回到雨林' : '原图对照';
  updatePause();
}
function setRainStyle(value) {
  settings.rainStyle = value === 0 ? 0 : 1;
  const legacy = settings.rainStyle === 0;
  $('legacy-rain').setAttribute('aria-pressed', String(legacy));
  $('edition-label').textContent = legacy ? '原画实验 · 上一版雨丝' : '原画实验 · 第二幕';
  $('edition-caption').textContent = legacy ? '同样的颜色，同样的一场雨。' : '让雨落进画里。';
  $('scene').dataset.rainStyle = legacy ? 'legacy' : 'painterly';
}
function toggleTuning(open) {
  $('tuning').hidden = !open;
  $('adjust').setAttribute('aria-expanded', String(open));
}
for (const name of Object.keys(defaults)) {
  $(name).addEventListener('input', () => {
    settings[name] = Number($(name).value);
    $(name + '-value').textContent = Math.round(settings[name] * 100) + '%';
    if (scene) scene.settings = settings;
  });
}
$('reset').addEventListener('click', () => {
  Object.assign(settings, defaults);
  setRainStyle(1);
  for (const name of Object.keys(defaults)) {
    $(name).value = String(settings[name]);
    $(name + '-value').textContent = Math.round(settings[name] * 100) + '%';
  }
});
$('pause').addEventListener('click', () => { paused = !paused; updatePause(); });
$('legacy-rain').addEventListener('click', () => setRainStyle(settings.rainStyle === 0 ? 1 : 0));
$('compare').addEventListener('click', () => setCompare(!comparing));
$('adjust').addEventListener('click', () => toggleTuning($('tuning').hidden));
$('close-tuning').addEventListener('click', () => toggleTuning(false));
$('fullscreen').addEventListener('click', async () => {
  try {
    if (document.fullscreenElement) await document.exitFullscreen();
    else await document.documentElement.requestFullscreen();
  } catch { showMessage('浏览器没有进入全屏。可以按 F11 欣赏。'); }
});
$('stage').addEventListener('pointerdown', (event) => {
  if (!ready || event.button !== 0) return;
  holding = true;
  $('stage').setPointerCapture(event.pointerId);
  updatePause();
});
function releaseHold() { holding = false; updatePause(); }
$('stage').addEventListener('pointerup', releaseHold);
$('stage').addEventListener('pointercancel', releaseHold);
$('stage').addEventListener('lostpointercapture', releaseHold);
$('stage').addEventListener('pointermove', (event) => {
  if (!scene) return;
  const bounds = $('stage').getBoundingClientRect();
  scene.pointerTarget = [(event.clientX - bounds.left) / bounds.width * 2 - 1, (event.clientY - bounds.top) / bounds.height * 2 - 1];
});
$('stage').addEventListener('pointerleave', () => { if (scene && !holding) scene.pointerTarget = [0, 0]; });
document.addEventListener('visibilitychange', () => { if (scene) scene.visible = !document.hidden; });
document.addEventListener('keydown', (event) => {
  if (/INPUT|TEXTAREA/.test(event.target.tagName)) return;
  if (event.code === 'Space') { event.preventDefault(); paused = !paused; updatePause(); }
  if (event.key.toLowerCase() === 'h') document.body.classList.toggle('clean');
  if (event.key === 'Escape') { document.body.classList.remove('clean'); toggleTuning(false); }
});
$('export').addEventListener('click', async () => {
  if (!scene) return;
  const button = $('export');
  button.textContent = '正在保存…';
  try {
    let blob;
    if (comparing) {
      const originalCanvas = document.createElement('canvas');
      originalCanvas.width = 3840; originalCanvas.height = 2160;
      originalCanvas.getContext('2d').drawImage($('reference'), 0, 0, 3840, 2160);
      blob = await new Promise((resolve, reject) => originalCanvas.toBlob(
        value => value ? resolve(value) : reject(new Error('原图静帧编码失败。')), 'image/png'
      ));
    } else {
      blob = await scene.exportPNG(3840, 2160);
    }
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = comparing ? '青雨-原图-4K.png' : settings.rainStyle === 0 ? '青雨-上一版雨丝-4K.png' : '青雨-第二幕-4K.png';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
  } catch (error) { showMessage('静帧保存失败：' + error.message); }
  finally { button.textContent = '保存 4K 静帧'; }
});

try {
  setRainStyle(search.has('legacy') ? 0 : 1);
  const options = {
    image: $('reference').src,
    depth: new URL('./assets/depth.png', import.meta.url).href,
    forceSDR: search.has('sdr'),
    settings,
    onStatus(info) {
      if (info.error) {
        ready = false;
        $('motion-info').textContent = '动态渲染中断';
        showMessage(info.error);
      }
      $('display-info').textContent = info.width + ' × ' + info.height + (info.hdr ? ' · HDR 画布' : ' · 标准色彩');
      $('diagnostics').textContent =
        '原图：3840 × 2160 · 8-bit JPEG\n' +
        '绘制：' + info.width + ' × ' + info.height + '\n' +
        '画布：' + info.format + ' / ' + info.toneMapping + '\n' +
        (info.intermediateFormat ? '合成缓冲：' + info.intermediateFormat + '\n' : '') +
        '浏览器报告高动态范围：' + (info.highRange ? '是' : '否') + '\n' +
        '深度遮挡：' + info.depthSource + '\n' +
        '最终显示位深：尚未验证\n' +
        '按 H 隐藏界面，空格暂停。';
    }
  };
  try {
    if (search.has('gl')) throw new Error('选择标准色彩检查。');
    scene = await RainScene.create($('scene'), options);
  } catch (gpuError) {
    console.warn('WebGPU 未就绪，使用标准色彩分支：' + gpuError.message);
    const replacement = $('scene').cloneNode(false);
    $('scene').replaceWith(replacement);
    scene = await RainSceneGL.create(replacement, options);
  }
  ready = true;
  document.body.classList.add('ready');
  if (search.has('still') || matchMedia('(prefers-reduced-motion: reduce)').matches) paused = true;
  if (search.has('baseline')) {
    Object.assign(settings, { vivid: 0, rain: 0, wind: 0, haze: 0, depth: 0 });
    for (const name of Object.keys(defaults)) { $(name).value = String(settings[name]); $(name + '-value').textContent = '0%'; }
    paused = true;
  }
  if (search.has('clean')) document.body.classList.add('clean');
  if (search.has('original')) setCompare(true);
  scene.time = 8.4;
  updatePause();
  scene.start();
} catch (error) {
  console.error(error);
  $('display-info').textContent = '原图 · 3840 × 2160';
  $('motion-info').textContent = '动态渲染未就绪';
  const probe = document.createElement('canvas');
  const gl2 = probe.getContext('webgl2');
  const capability = gl2 ? '页面可以创建 WebGL2 画布。' : '页面也未能创建 WebGL2 画布。';
  gl2?.getExtension('WEBGL_lose_context')?.loseContext();
  showMessage('原画已保留。动态画面尚未启动：' + error.message + ' ' + capability);
}
