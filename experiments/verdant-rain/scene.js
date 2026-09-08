const sceneShader = `
struct Params {
  screen: vec4f,
  motion: vec4f,
  art: vec4f,
  pointer: vec4f
}
@group(0) @binding(0) var<uniform> u: Params;
@group(0) @binding(1) var image: texture_2d<f32>;
@group(0) @binding(2) var imageSampler: sampler;
@group(0) @binding(3) var depthImage: texture_2d<f32>;
@group(0) @binding(4) var clouds: texture_2d<f32>;
@group(0) @binding(5) var cloudSampler: sampler;

struct FullVertex { @builtin(position) position: vec4f, @location(0) uv: vec2f }
@vertex fn backgroundVertex(@builtin(vertex_index) i: u32) -> FullVertex {
  let p = array<vec2f, 3>(vec2f(-1., -1.), vec2f(3., -1.), vec2f(-1., 3.));
  var o: FullVertex;
  o.position = vec4f(p[i], 0., 1.);
  o.uv = p[i] * vec2f(.5, -.5) + .5;
  return o;
}
fn sceneUV(uv: vec2f, depth: f32) -> vec2f {
  let shift = u.pointer.xy * vec2f(.0030, -.0018) * u.motion.w * (depth - .25);
  let breath = sin(u.screen.z * .6 + uv.y * 3.2) * .00042 * u.motion.y * smoothstep(.45, .85, depth);
  return clamp(uv + shift + vec2f(breath, 0.), vec2f(0.), vec2f(1.));
}
@fragment fn backgroundFragment(v: FullVertex) -> @location(0) vec4f {
  let d = textureSample(depthImage, imageSampler, v.uv).r;
  let uv = sceneUV(v.uv, d);
  var colour = textureSample(image, imageSampler, uv).rgb;
  let luminance = dot(colour, vec3f(.2126, .7152, .0722));
  let chroma = max(colour.r, max(colour.g, colour.b)) - min(colour.r, min(colour.g, colour.b));
  let green = smoothstep(.008, .12, colour.g - max(colour.r, colour.b) * .96) * smoothstep(.015, .09, chroma);
  let intensity = u.art.x * green;
  colour = mix(vec3f(luminance), colour, 1. + intensity * .19);
  colour.r *= 1. - intensity * .02;
  colour.b *= 1. - intensity * .075;
  colour.g += intensity * .055 * (1. - luminance);
  let n1 = textureSample(clouds, cloudSampler, uv * vec2f(1.8, .9) + vec2f(u.screen.z * .004, -.015)).r;
  let n2 = textureSample(clouds, cloudSampler, uv * vec2f(.65, 1.6) - vec2f(u.screen.z * .002, .24)).r;
  let clearing = smoothstep(.3, .72, d) * smoothstep(.02, .35, uv.y) * (1. - smoothstep(.8, 1., uv.y));
  let mist = max(0., n1 * .65 + n2 * .35 - .25) * clearing * u.motion.z * .19;
  colour = mix(colour, vec3f(.20, .53, .245), mist);
  return vec4f(max(colour, vec3f(0.)), 1.);
}
fn hash(n: f32) -> f32 { return fract(sin(n * 127.1 + 311.7) * 43758.5453); }
struct RainVertex {
  @builtin(position) position: vec4f,
  @location(0) local: vec2f,
  @location(1) data: vec3f
}
@vertex fn rainVertex(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> RainVertex {
  let corners = array<vec2f, 6>(
    vec2f(-1., -1.), vec2f(1., -1.), vec2f(-1., 1.),
    vec2f(-1., 1.), vec2f(1., -1.), vec2f(1., 1.)
  );
  let seed = f32(ii) + 1.;
  let depth = .025 + hash(seed + 17.2) * .91;
  let near = 1. - depth;
  let speed = .15 + near * .34;
  let t = u.screen.z;
  let sway = sin(t * .75 + seed * .54) * .008 * u.motion.y;
  let y = fract(hash(seed + 6.73) + t * speed) * 1.3 - .15;
  let x = fract(hash(seed + 83.1) - t * speed * (.09 + u.motion.y * .23)) * 1.3 - .15 + sway;
  var centre = vec2f(x, y);
  centre -= u.pointer.xy * vec2f(.011, -.006) * u.motion.w * near;
  let direction = normalize(vec2f(-.22 - u.motion.y * .58, 1.));
  let length = (7. + near * 29.) * u.screen.y / 1080.;
  let width = (.22 + near * .66) * u.screen.y / 1080.;
  let q = corners[vi];
  let offset = direction * q.y * length + vec2f(direction.y, -direction.x) * q.x * width;
  let uv = centre + offset / u.screen.xy;
  var o: RainVertex;
  o.position = vec4f(uv * vec2f(2., -2.) + vec2f(-1., 1.), 0., 1.);
  o.local = q;
  o.data = vec3f(depth, hash(seed + 29.3), near);
  return o;
}
@fragment fn rainFragment(v: RainVertex) -> @location(0) vec4f {
  let uv = v.position.xy / u.screen.xy;
  let sceneDepth = textureSample(depthImage, imageSampler, uv).r;
  if (v.data.x > sceneDepth + .012) { discard; }
  let core = 1. - smoothstep(.05, 1., abs(v.local.x));
  let tip = pow(max(0., 1. - abs(v.local.y)), .65);
  let alpha = core * tip * (.09 + v.data.z * .19);
  let flash = select(1., 2.9, v.data.y > .987);
  let colour = vec3f(.47, .78, .405) * (.7 + v.data.z * .45) * flash;
  return vec4f(colour, alpha);
}
`;

const presentShader = `
struct Params { screen: vec4f, motion: vec4f, art: vec4f, pointer: vec4f }
@group(0) @binding(0) var<uniform> u: Params;
@group(0) @binding(1) var source: texture_2d<f32>;
@group(0) @binding(2) var sourceSampler: sampler;
struct V { @builtin(position) position: vec4f, @location(0) uv: vec2f }
@vertex fn vs(@builtin(vertex_index) i: u32) -> V {
  let p = array<vec2f, 3>(vec2f(-1., -1.), vec2f(3., -1.), vec2f(-1., 3.));
  var o: V;
  o.position = vec4f(p[i], 0., 1.);
  o.uv = p[i] * vec2f(.5, -.5) + .5;
  return o;
}
fn encodeSRGB(linear: vec3f) -> vec3f {
  let hi = 1.055 * pow(max(linear, vec3f(0.)), vec3f(1. / 2.4)) - .055;
  return select(hi, linear * 12.92, linear <= vec3f(.0031308));
}
@fragment fn fs(v: V) -> @location(0) vec4f {
  var linear = max(textureSample(source, sourceSampler, v.uv).rgb, vec3f(0.));
  if (u.art.y < 1.5) { linear = min(linear, vec3f(1.)); }
  return vec4f(encodeSRGB(linear), 1.);
}
`;

function cloudPixels(size) {
  const out = new Uint8Array(size * size * 4);
  function random(x, y) { const n = Math.sin(x * 127.1 + y * 311.7 + 5.3) * 43758.5453; return n - Math.floor(n); }
  function noise(x, y, period) {
    const px = x * period, py = y * period;
    const ix = Math.floor(px), iy = Math.floor(py);
    let fx = px - ix, fy = py - iy;
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
    const a = random(ix % period, iy % period);
    const b = random((ix + 1) % period, iy % period);
    const c = random(ix % period, (iy + 1) % period);
    const d = random((ix + 1) % period, (iy + 1) % period);
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
  }
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const n = noise(x / size, y / size, 4) * .6 + noise(x / size, y / size, 8) * .27 + noise(x / size, y / size, 16) * .13;
    const at = (y * size + x) * 4;
    out[at] = out[at + 1] = out[at + 2] = Math.round(n * 255); out[at + 3] = 255;
  }
  return out;
}

export class RainScene {
  static async create(canvas, options) {
    const scene = new RainScene(canvas, options);
    await scene.initialize();
    return scene;
  }
  constructor(canvas, options) {
    this.canvas = canvas; this.options = options; this.settings = options.settings;
    this.pointer = [0, 0]; this.pointerTarget = [0, 0];
    this.time = 8.4; this.paused = false; this.visible = true; this.running = false;
    this.presentPipelines = new Map(); this.uniformValues = new Float32Array(16);
  }
  async initialize() {
    if (!navigator.gpu) throw new Error('当前浏览器没有提供 WebGPU。请在支持 WebGPU 的 Chrome 中打开本地预览。');
    this.adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' })
      || await navigator.gpu.requestAdapter();
    if (!this.adapter) throw new Error('没有可用的 WebGPU 图形设备。');
    this.device = await this.adapter.requestDevice();
    this.device.addEventListener('uncapturederror', (event) => { this.running = false; console.error('绘制错误：' + event.error.message); });
    this.device.lost.then((info) => { this.running = false; console.error('图形设备中断：' + info.message); });
    this.context = this.canvas.getContext('webgpu');
    this.highRange = matchMedia('(dynamic-range: high)').matches;
    this.configure();
    const d = this.device;
    this.sampler = d.createSampler({ minFilter: 'linear', magFilter: 'linear', addressModeU: 'clamp-to-edge', addressModeV: 'clamp-to-edge' });
    this.cloudSampler = d.createSampler({ minFilter: 'linear', magFilter: 'linear', addressModeU: 'repeat', addressModeV: 'repeat' });
    this.uniform = d.createBuffer({ size: 64, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    this.image = await this.loadTexture(this.options.image, true);
    try {
      this.depth = await this.loadTexture(this.options.depth, false);
      this.depthSource = '分区深度蒙版';
    } catch {
      this.depth = this.createTexture(1, 1, new Uint8Array([204, 204, 204, 255]));
      this.depthSource = '临时均匀深度';
    }
    this.cloud = this.createTexture(256, 256, cloudPixels(256));
    const shader = d.createShaderModule({ label: '原色与空间雨幕', code: sceneShader });
    await this.checkShader(shader);
    this.sceneLayout = d.createBindGroupLayout({ entries: [
      { binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
      { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: {} },
      { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: {} },
      { binding: 3, visibility: GPUShaderStage.FRAGMENT, texture: {} },
      { binding: 4, visibility: GPUShaderStage.FRAGMENT, texture: {} },
      { binding: 5, visibility: GPUShaderStage.FRAGMENT, sampler: {} }
    ] });
    const layout = d.createPipelineLayout({ bindGroupLayouts: [this.sceneLayout] });
    this.backgroundPipeline = d.createRenderPipeline({
      layout, vertex: { module: shader, entryPoint: 'backgroundVertex' },
      fragment: { module: shader, entryPoint: 'backgroundFragment', targets: [{ format: 'rgba16float' }] },
      primitive: { topology: 'triangle-list' }
    });
    this.rainPipeline = d.createRenderPipeline({
      layout, vertex: { module: shader, entryPoint: 'rainVertex' },
      fragment: { module: shader, entryPoint: 'rainFragment', targets: [{
        format: 'rgba16float',
        blend: { color: { srcFactor: 'src-alpha', dstFactor: 'one', operation: 'add' }, alpha: { srcFactor: 'zero', dstFactor: 'one', operation: 'add' } }
      }] },
      primitive: { topology: 'triangle-list' }
    });
    this.sceneGroup = d.createBindGroup({ layout: this.sceneLayout, entries: [
      { binding: 0, resource: { buffer: this.uniform } }, { binding: 1, resource: this.image.createView() },
      { binding: 2, resource: this.sampler }, { binding: 3, resource: this.depth.createView() },
      { binding: 4, resource: this.cloud.createView() }, { binding: 5, resource: this.cloudSampler }
    ] });
    this.presentModule = d.createShaderModule({ label: '线性光到显示颜色', code: presentShader });
    await this.checkShader(this.presentModule);
    this.presentLayout = d.createBindGroupLayout({ entries: [
      { binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
      { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: {} },
      { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: {} }
    ] });
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.canvas);
    this.resize();
  }
  configure() {
    const preferHDR = this.highRange && !this.options.forceSDR;
    this.format = preferHDR ? 'rgba16float' : navigator.gpu.getPreferredCanvasFormat();
    const configuration = { device: this.device, format: this.format, alphaMode: 'opaque', colorSpace: 'srgb', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC };
    if (preferHDR) configuration.toneMapping = { mode: 'extended' };
    this.context.configure(configuration);
    this.hdr = preferHDR && this.context.getConfiguration().toneMapping?.mode === 'extended';
    if (preferHDR && !this.hdr) {
      this.format = navigator.gpu.getPreferredCanvasFormat();
      this.context.configure({ ...configuration, format: this.format, toneMapping: { mode: 'standard' } });
    }
  }
  async checkShader(module) {
    const result = await module.getCompilationInfo();
    const errors = result.messages.filter((message) => message.type === 'error');
    if (errors.length) throw new Error(errors.map((message) => 'L' + message.lineNum + ': ' + message.message).join('\n'));
  }
  async loadTexture(url, colour) {
    const response = await fetch(url);
    if (!response.ok) throw new Error('素材读取失败：' + response.status);
    const bitmap = await createImageBitmap(await response.blob());
    const texture = this.device.createTexture({ size: [bitmap.width, bitmap.height], format: colour ? 'rgba8unorm-srgb' : 'rgba8unorm', usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST | GPUTextureUsage.RENDER_ATTACHMENT });
    this.device.queue.copyExternalImageToTexture({ source: bitmap }, { texture, colorSpace: 'srgb', premultipliedAlpha: false }, [bitmap.width, bitmap.height]);
    bitmap.close();
    return texture;
  }
  createTexture(width, height, data) {
    const texture = this.device.createTexture({ size: [width, height], format: 'rgba8unorm', usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST });
    this.device.queue.writeTexture({ texture }, data, { bytesPerRow: width * 4 }, [width, height]);
    return texture;
  }
  makeTarget(width, height) {
    const texture = this.device.createTexture({ size: [width, height], format: 'rgba16float', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING });
    const group = this.device.createBindGroup({ layout: this.presentLayout, entries: [
      { binding: 0, resource: { buffer: this.uniform } }, { binding: 1, resource: texture.createView() }, { binding: 2, resource: this.sampler }
    ] });
    return { texture, group, width, height };
  }
  presentation(format) {
    if (!this.presentPipelines.has(format)) {
      this.presentPipelines.set(format, this.device.createRenderPipeline({
        layout: this.device.createPipelineLayout({ bindGroupLayouts: [this.presentLayout] }),
        vertex: { module: this.presentModule, entryPoint: 'vs' },
        fragment: { module: this.presentModule, entryPoint: 'fs', targets: [{ format }] },
        primitive: { topology: 'triangle-list' }
      }));
    }
    return this.presentPipelines.get(format);
  }
  resize() {
    if (!this.presentLayout) return;
    const rect = this.canvas.getBoundingClientRect();
    const scale = Math.min(devicePixelRatio || 1, 3840 / Math.max(1, rect.width));
    const width = Math.max(2, Math.round(rect.width * scale));
    const height = Math.max(2, Math.round(rect.height * scale));
    if (width === this.canvas.width && height === this.canvas.height && this.target) return;
    this.canvas.width = width; this.canvas.height = height;
    if (this.target) this.target.texture.destroy();
    this.target = this.makeTarget(width, height);
    this.options.onStatus?.({ width, height, hdr: this.hdr, highRange: this.highRange, format: this.format, toneMapping: this.hdr ? 'extended' : 'standard', depthSource: this.depthSource });
  }
  uniforms(width, height, hdr) {
    const s = this.settings;
    this.uniformValues.set([width, height, this.time, 0, s.rain, s.wind, s.haze, s.depth, s.vivid, hdr ? 3 : 1, 0, 0, this.pointer[0], this.pointer[1], 0, 0]);
    this.device.queue.writeBuffer(this.uniform, 0, this.uniformValues);
  }
  draw(target, context, format, hdr) {
    this.uniforms(target.width, target.height, hdr);
    const encoder = this.device.createCommandEncoder();
    const paint = encoder.beginRenderPass({ colorAttachments: [{ view: target.texture.createView(), clearValue: { r: 0, g: 0, b: 0, a: 1 }, loadOp: 'clear', storeOp: 'store' }] });
    paint.setBindGroup(0, this.sceneGroup);
    paint.setPipeline(this.backgroundPipeline); paint.draw(3);
    const count = Math.round(2200 * this.settings.rain);
    if (count > 0) { paint.setPipeline(this.rainPipeline); paint.draw(6, count); }
    paint.end();
    const display = encoder.beginRenderPass({ colorAttachments: [{ view: context.getCurrentTexture().createView(), loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }] });
    display.setPipeline(this.presentation(format)); display.setBindGroup(0, target.group); display.draw(3); display.end();
    this.device.queue.submit([encoder.finish()]);
  }
  start() {
    this.running = true;
    let last = performance.now(), lastPaint = 0;
    const frame = (now) => {
      if (!this.running) return;
      requestAnimationFrame(frame);
      const dt = Math.min(.05, (now - last) / 1000); last = now;
      if (!this.visible || this.exporting) return;
      if (!this.paused) this.time += dt;
      const ease = 1 - Math.exp(-dt * 4.3);
      this.pointer[0] += (this.pointerTarget[0] - this.pointer[0]) * ease;
      this.pointer[1] += (this.pointerTarget[1] - this.pointer[1]) * ease;
      if (now - lastPaint < 1000 / 61) return;
      lastPaint = now;
      this.draw(this.target, this.context, this.format, this.hdr);
    };
    requestAnimationFrame(frame);
  }
  async exportPNG(width, height) {
    this.exporting = true;
    const canvas = document.createElement('canvas');
    canvas.width = width; canvas.height = height;
    const context = canvas.getContext('webgpu');
    const format = navigator.gpu.getPreferredCanvasFormat();
    context.configure({ device: this.device, format, alphaMode: 'opaque', colorSpace: 'srgb' });
    const target = this.makeTarget(width, height);
    try {
      this.draw(target, context, format, false);
      await this.device.queue.onSubmittedWorkDone();
      return await new Promise((resolve, reject) => canvas.toBlob((blob) => blob ? resolve(blob) : reject(new Error('无法编码 PNG。')), 'image/png'));
    } finally { target.texture.destroy(); context.unconfigure(); this.exporting = false; }
  }
}
