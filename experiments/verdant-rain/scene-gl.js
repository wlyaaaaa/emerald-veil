// WebGL2 SDR renderer. All scene arithmetic and rain blending use linear light.
// SRGB8_ALPHA8 artwork is decoded by texture sampling, not by a shader pow().
const fullscreenVertex = `#version 300 es
precision highp float;
out vec2 v_uv;
void main() {
  const vec2 p[3] = vec2[3](vec2(-1., -1.), vec2(3., -1.), vec2(-1., 3.));
  gl_Position = vec4(p[gl_VertexID], 0., 1.);
  v_uv = p[gl_VertexID] * vec2(.5, -.5) + .5;
}`;

const sceneUniforms = `
uniform vec4 u_screen;
uniform vec4 u_motion;
uniform vec4 u_art;
uniform vec4 u_pointer;
`;

const sceneUV = `
vec2 sceneUV(vec2 uv, float depth) {
  vec2 shift = u_pointer.xy * vec2(.0030, -.0018) * u_motion.w * (depth - .25);
  float breath = sin(u_screen.z * .6 + uv.y * 3.2) * .00042 * u_motion.y
    * smoothstep(.45, .85, depth);
  return clamp(uv + shift + vec2(breath, 0.), vec2(0.), vec2(1.));
}
`;

const backgroundFragment = `#version 300 es
precision highp float;
in vec2 v_uv;
out vec4 outColour;
uniform highp sampler2D u_image;
uniform highp sampler2D u_depth;
uniform highp sampler2D u_cloud;
${sceneUniforms}
${sceneUV}
void main() {
  float d = texture(u_depth, v_uv).r;
  vec2 uv = sceneUV(v_uv, d);
  vec3 colour = texture(u_image, uv).rgb;
  float luminance = dot(colour, vec3(.2126, .7152, .0722));
  float chroma = max(colour.r, max(colour.g, colour.b))
    - min(colour.r, min(colour.g, colour.b));
  float green = smoothstep(.008, .12, colour.g - max(colour.r, colour.b) * .96)
    * smoothstep(.015, .09, chroma);
  float intensity = u_art.x * green;
  colour = mix(vec3(luminance), colour, 1. + intensity * .19);
  colour.r *= 1. - intensity * .02;
  colour.b *= 1. - intensity * .075;
  colour.g += intensity * .055 * (1. - luminance);
  float n1 = texture(u_cloud, uv * vec2(1.8, .9) + vec2(u_screen.z * .004, -.015)).r;
  float n2 = texture(u_cloud, uv * vec2(.65, 1.6) - vec2(u_screen.z * .002, .24)).r;
  float clearing = smoothstep(.3, .72, d) * smoothstep(.02, .35, uv.y)
    * (1. - smoothstep(.8, 1., uv.y));
  float mist = max(0., n1 * .65 + n2 * .35 - .25) * clearing * u_motion.z * .19;
  colour = mix(colour, vec3(.20, .53, .245), mist);
  outColour = vec4(max(colour, vec3(0.)), 1.);
}`;

const rainVertex = `#version 300 es
precision highp float;
precision highp int;
out vec2 v_local;
flat out vec3 v_data;
${sceneUniforms}
float hash(float n) { return fract(sin(n * 127.1 + 311.7) * 43758.5453); }
void main() {
  const vec2 corners[6] = vec2[6](
    vec2(-1., -1.), vec2(1., -1.), vec2(-1., 1.),
    vec2(-1., 1.), vec2(1., -1.), vec2(1., 1.)
  );
  float seed = float(gl_InstanceID) + 1.;
  float depth = .025 + hash(seed + 17.2) * .91;
  float nearness = 1. - depth;
  float speed = .15 + nearness * .34;
  float t = u_screen.z;
  float sway = sin(t * .75 + seed * .54) * .008 * u_motion.y;
  float y = fract(hash(seed + 6.73) + t * speed) * 1.3 - .15;
  float x = fract(hash(seed + 83.1) - t * speed * (.09 + u_motion.y * .23)) * 1.3 - .15 + sway;
  vec2 centre = vec2(x, y) - u_pointer.xy * vec2(.011, -.006) * u_motion.w * nearness;
  vec2 direction = normalize(vec2(-.22 - u_motion.y * .58, 1.));
  float streakLength = (7. + nearness * 29.) * u_screen.y / 1080.;
  float streakWidth = (.22 + nearness * .66) * u_screen.y / 1080.;
  vec2 q = corners[gl_VertexID];
  vec2 offset = direction * q.y * streakLength + vec2(direction.y, -direction.x) * q.x * streakWidth;
  vec2 uv = centre + offset / u_screen.xy;
  gl_Position = vec4(uv * vec2(2., -2.) + vec2(-1., 1.), 0., 1.);
  v_local = q;
  v_data = vec3(depth, hash(seed + 29.3), nearness);
}`;

const rainFragment = `#version 300 es
precision highp float;
in vec2 v_local;
flat in vec3 v_data;
out vec4 outColour;
uniform highp sampler2D u_depth;
${sceneUniforms}
${sceneUV}
void main() {
  // Fragment coordinates have a bottom-left origin; the original and mask do not.
  vec2 uv = vec2(gl_FragCoord.x / u_screen.x, 1. - gl_FragCoord.y / u_screen.y);
  float d = texture(u_depth, uv).r;
  float sceneDepth = texture(u_depth, sceneUV(uv, d)).r;
  if (v_data.x > sceneDepth + .012) discard;
  float core = 1. - smoothstep(.05, 1., abs(v_local.x));
  float tip = pow(max(0., 1. - abs(v_local.y)), .65);
  float alpha = core * tip * (.09 + v_data.z * .19);
  float flash = v_data.y > .987 ? 2.9 : 1.;
  vec3 colour = vec3(.47, .78, .405) * (.7 + v_data.z * .45) * flash;
  outColour = vec4(colour, alpha);
}`;

const presentFragment = `#version 300 es
precision highp float;
in vec2 v_uv;
out vec4 outColour;
uniform highp sampler2D u_source;
vec3 encodeSRGB(vec3 linear) {
  vec3 hi = 1.055 * pow(linear, vec3(1. / 2.4)) - .055;
  return mix(hi, linear * 12.92, lessThanEqual(linear, vec3(.0031308)));
}
void main() {
  // The rendered FBO has the opposite orientation to uploaded image rows.
  vec3 linear = clamp(texture(u_source, vec2(v_uv.x, 1. - v_uv.y)).rgb, 0., 1.);
  outColour = vec4(encodeSRGB(linear), 1.);
}`;

function cloudPixels(size) {
  const out = new Uint8Array(size * size * 4);
  const random = (x, y) => { const n = Math.sin(x * 127.1 + y * 311.7 + 5.3) * 43758.5453; return n - Math.floor(n); };
  function noise(x, y, period) {
    const px = x * period, py = y * period, ix = Math.floor(px), iy = Math.floor(py);
    let fx = px - ix, fy = py - iy;
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
    const a = random(ix % period, iy % period), b = random((ix + 1) % period, iy % period);
    const c = random(ix % period, (iy + 1) % period), d = random((ix + 1) % period, (iy + 1) % period);
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
  }
  // This small procedural field is generated once; the artwork is never processed on the CPU.
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const n = noise(x / size, y / size, 4) * .6 + noise(x / size, y / size, 8) * .27 + noise(x / size, y / size, 16) * .13;
    const at = (y * size + x) * 4;
    out[at] = out[at + 1] = out[at + 2] = Math.round(n * 255); out[at + 3] = 255;
  }
  return out;
}

export class RainSceneGL {
  static async create(canvas, options) {
    const scene = new RainSceneGL(canvas, options);
    try { await scene.initialize(); return scene; }
    catch (error) { scene.dispose(); throw error; }
  }
  constructor(canvas, options) {
    this.canvas = canvas; this.options = options; this.settings = options.settings;
    this.pointer = [0, 0]; this.pointerTarget = [0, 0];
    this.time = 8.4; this.paused = false; this.visible = true; this.running = false;
    this.hdr = false; this.exporting = false; this.programs = [];
    this.onContextLost = () => this.fail(new Error('图形会话中断，原画已保留。刷新页面可重新启动雨幕。'));
  }
  async initialize() {
    const gl = this.canvas.getContext('webgl2', {
      alpha: false, antialias: false, depth: false, stencil: false,
      premultipliedAlpha: false, preserveDrawingBuffer: false, powerPreference: 'high-performance'
    });
    if (!gl) throw new Error('当前浏览器无法创建 WebGL2 画布。');
    this.gl = gl;
    if ('drawingBufferColorSpace' in gl) gl.drawingBufferColorSpace = 'srgb';
    if ('unpackColorSpace' in gl) gl.unpackColorSpace = 'srgb';
    this.highRange = globalThis.matchMedia?.('(dynamic-range: high)').matches ?? false;
    const bits = [gl.RED_BITS, gl.GREEN_BITS, gl.BLUE_BITS].map((p) => gl.getParameter(p));
    this.format = 'WebGL2 RGB' + bits.join('/') + ' / sRGB';
    this.floatBuffer = Boolean(gl.getExtension('EXT_color_buffer_float'));
    this.maxTextureSize = gl.getParameter(gl.MAX_TEXTURE_SIZE);
    const viewport = gl.getParameter(gl.MAX_VIEWPORT_DIMS);
    this.maxWidth = Math.min(this.maxTextureSize, gl.getParameter(gl.MAX_RENDERBUFFER_SIZE), viewport[0]);
    this.maxHeight = Math.min(this.maxTextureSize, gl.getParameter(gl.MAX_RENDERBUFFER_SIZE), viewport[1]);
    gl.disable(gl.DEPTH_TEST); gl.disable(gl.CULL_FACE); gl.disable(gl.DITHER);
    this.vao = gl.createVertexArray(); gl.bindVertexArray(this.vao);
    this.backgroundProgram = this.program(fullscreenVertex, backgroundFragment, '原画与薄雾');
    this.rainProgram = this.program(rainVertex, rainFragment, '空间雨幕');
    this.presentProgram = this.program(fullscreenVertex, presentFragment, '标准色彩输出');
    this.image = await this.loadTexture(this.options.image, true);
    try {
      this.depth = await this.loadTexture(this.options.depth, false);
      this.depthSource = '分区深度蒙版';
    } catch (error) {
      if (gl.isContextLost()) throw error;
      console.warn('深度蒙版读取失败，使用均匀深度：', error);
      this.depth = this.dataTexture(1, 1, new Uint8Array([204, 204, 204, 255]));
      this.depthSource = '临时均匀深度';
    }
    this.cloud = this.dataTexture(256, 256, cloudPixels(256), true);
    this.canvas.addEventListener('webglcontextlost', this.onContextLost);
    this.resize();
    this.resizeObserver = new ResizeObserver(() => { try { this.resize(); } catch (error) { this.fail(error); } });
    this.resizeObserver.observe(this.canvas);
  }
  program(vertexSource, fragmentSource, label) {
    const gl = this.gl, shaders = [];
    const program = gl.createProgram();
    if (!program) throw new Error(label + '：无法创建着色程序。');
    try {
      for (const [type, source] of [[gl.VERTEX_SHADER, vertexSource], [gl.FRAGMENT_SHADER, fragmentSource]]) {
        const shader = gl.createShader(type);
        if (!shader) throw new Error(label + '：无法创建着色器。');
        shaders.push(shader); gl.shaderSource(shader, source); gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
          throw new Error(label + '着色器编译失败：' + gl.getShaderInfoLog(shader));
        }
        gl.attachShader(program, shader);
      }
      gl.linkProgram(program);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(label + '程序链接失败：' + gl.getProgramInfoLog(program));
      const uniforms = {};
      for (const key of ['screen', 'motion', 'art', 'pointer', 'image', 'depth', 'cloud', 'source']) {
        uniforms[key] = gl.getUniformLocation(program, 'u_' + key);
      }
      gl.useProgram(program);
      for (const [key, unit] of [['image', 0], ['depth', 1], ['cloud', 2], ['source', 3]]) gl.uniform1i(uniforms[key], unit);
      const result = { program, uniforms }; this.programs.push(result); return result;
    } catch (error) { gl.deleteProgram(program); throw error; }
    finally { for (const shader of shaders) gl.deleteShader(shader); }
  }
  textureParameters(repeat = false) {
    const gl = this.gl;
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, repeat ? gl.REPEAT : gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, repeat ? gl.REPEAT : gl.CLAMP_TO_EDGE);
  }
  async loadTexture(url, colour) {
    if (!url) throw new Error('素材地址缺失。');
    const response = await fetch(url);
    if (!response.ok) throw new Error('素材读取失败：' + response.status);
    const bitmap = await createImageBitmap(await response.blob(), {
      imageOrientation: 'none', premultiplyAlpha: 'none', colorSpaceConversion: colour ? 'default' : 'none'
    });
    const gl = this.gl;
    let texture;
    try {
      if (bitmap.width > this.maxTextureSize || bitmap.height > this.maxTextureSize) throw new Error('原图尺寸超过当前图形设备的纹理上限。');
      texture = gl.createTexture();
      if (!texture) throw new Error('无法创建素材纹理。');
      gl.bindTexture(gl.TEXTURE_2D, texture); this.textureParameters();
      // First uploaded row is the image's top row; scene shader UVs are top-origin.
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
      gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
      gl.texImage2D(gl.TEXTURE_2D, 0, colour ? gl.SRGB8_ALPHA8 : gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, bitmap);
      this.checkError('素材上传');
      return texture;
    } catch (error) { if (texture) gl.deleteTexture(texture); throw error; }
    finally { bitmap.close(); }
  }
  dataTexture(width, height, data, repeat = false) {
    const gl = this.gl, texture = gl.createTexture();
    if (!texture) throw new Error('无法创建数据纹理。');
    try {
      gl.bindTexture(gl.TEXTURE_2D, texture); this.textureParameters(repeat);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, data);
      this.checkError('数据纹理'); return texture;
    } catch (error) { gl.deleteTexture(texture); throw error; }
  }
  makeTarget(width, height) {
    const gl = this.gl;
    const allocate = (internalFormat, format) => {
      const target = { width, height, format, texture: gl.createTexture(), framebuffer: gl.createFramebuffer() };
      try {
        if (!target.texture || !target.framebuffer) throw new Error('无法分配画面缓冲。');
        gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, target.texture); this.textureParameters();
        gl.texStorage2D(gl.TEXTURE_2D, 1, internalFormat, width, height);
        gl.bindFramebuffer(gl.FRAMEBUFFER, target.framebuffer);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, target.texture, 0);
        const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
        this.checkError('画面缓冲分配');
        if (status !== gl.FRAMEBUFFER_COMPLETE) throw new Error(format + '缓冲不完整：0x' + status.toString(16));
        return target;
      } catch (error) { this.destroyTarget(target); throw error; }
      finally { gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.bindTexture(gl.TEXTURE_2D, null); gl.activeTexture(gl.TEXTURE0); }
    };
    if (this.floatBuffer) {
      try { return allocate(gl.RGBA16F, 'RGBA16F'); }
      catch (error) {
        if (gl.isContextLost()) throw error;
        this.floatBuffer = false;
        console.warn('浮点画面缓冲不可用，继续使用标准色彩缓冲：', error);
      }
    }
    // Core WebGL2 sRGB attachment: hardware decodes the destination before blending
    // and encodes after it. Sampling decodes again. Unlike linear RGBA8, this keeps
    // useful shadow precision; highlights above 1 are clipped in this SDR branch.
    return allocate(gl.SRGB8_ALPHA8, 'SRGB8_ALPHA8');
  }
  destroyTarget(target) {
    if (!target || !this.gl) return;
    this.gl.deleteFramebuffer(target.framebuffer); this.gl.deleteTexture(target.texture);
  }
  report(error) {
    this.options.onStatus?.({
      width: this.canvas.width, height: this.canvas.height, hdr: false,
      highRange: this.highRange, format: this.format, toneMapping: 'standard',
      depthSource: this.depthSource, intermediateFormat: this.target?.format,
      ...(error ? { error: error.message } : {})
    });
  }
  resize() {
    if (this.exporting || this.failed) return;
    const rect = this.canvas.getBoundingClientRect();
    const cssWidth = Math.max(1, rect.width), cssHeight = Math.max(1, rect.height);
    const scale = Math.min(globalThis.devicePixelRatio || 1, 3840 / cssWidth, 2160 / cssHeight, this.maxWidth / cssWidth, this.maxHeight / cssHeight);
    const width = Math.max(2, Math.round(cssWidth * scale)), height = Math.max(2, Math.round(cssHeight * scale));
    if (this.target && width === this.canvas.width && height === this.canvas.height) return;
    const target = this.makeTarget(width, height);
    const previous = this.target;
    this.target = target; this.canvas.width = width; this.canvas.height = height;
    this.destroyTarget(previous);
    try { this.draw(target); this.checkError('首帧绘制'); }
    catch (error) {
      if (target.format !== 'RGBA16F' || this.gl.isContextLost()) throw error;
      // A complete attachment alone does not prove that this driver's draw path works.
      this.floatBuffer = false;
      console.warn('浮点首帧绘制失败，继续使用标准色彩缓冲：', error);
      this.destroyTarget(target); this.target = this.makeTarget(width, height);
      this.draw(this.target); this.checkError('标准色彩首帧绘制');
    }
    this.report();
  }
  useSceneProgram(info, width, height) {
    const gl = this.gl, u = info.uniforms, s = this.settings;
    gl.useProgram(info.program);
    gl.uniform4f(u.screen, width, height, this.time, 0);
    gl.uniform4f(u.motion, s.rain, s.wind, s.haze, s.depth);
    gl.uniform4f(u.art, s.vivid, 1, 0, 0);
    gl.uniform4f(u.pointer, this.pointer[0], this.pointer[1], 0, 0);
  }
  draw(target) {
    const gl = this.gl;
    if (gl.isContextLost()) throw new Error('图形会话已中断，刷新页面可重试。');
    gl.bindVertexArray(this.vao);
    gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, null);
    gl.bindFramebuffer(gl.FRAMEBUFFER, target.framebuffer);
    gl.viewport(0, 0, target.width, target.height); gl.disable(gl.BLEND);
    for (const [unit, texture] of [[0, this.image], [1, this.depth], [2, this.cloud]]) {
      gl.activeTexture(gl.TEXTURE0 + unit); gl.bindTexture(gl.TEXTURE_2D, texture);
    }
    this.useSceneProgram(this.backgroundProgram, target.width, target.height);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    const count = Math.max(0, Math.round(2200 * this.settings.rain));
    if (count > 0) {
      this.useSceneProgram(this.rainProgram, target.width, target.height);
      gl.enable(gl.BLEND); gl.blendEquation(gl.FUNC_ADD);
      gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE, gl.ZERO, gl.ONE);
      gl.drawArraysInstanced(gl.TRIANGLES, 0, 6, count); gl.disable(gl.BLEND);
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    gl.useProgram(this.presentProgram.program);
    gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, target.texture);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }
  checkError(phase) {
    const gl = this.gl, errors = [];
    for (let i = 0; i < 8; i++) {
      const error = gl.getError();
      if (error === gl.NO_ERROR) break;
      errors.push('0x' + error.toString(16));
    }
    if (errors.length) throw new Error(phase + '失败（WebGL ' + errors.join(', ') + '）。');
  }
  fail(error) {
    if (this.failed) return;
    this.failed = true; this.running = false; cancelAnimationFrame(this.frameId);
    // Reveal the untouched reference under the canvas, including on context loss.
    this.canvas.style.visibility = 'hidden';
    console.error(error); this.report(error);
    this.canvas.dispatchEvent(new CustomEvent('rainsceneerror', { detail: { message: error.message } }));
  }
  start() {
    if (this.running || this.failed) return;
    this.running = true;
    let last = performance.now(), lastPaint = 0, frameCount = 0;
    const frame = (now) => {
      if (!this.running) return;
      const dt = Math.min(.05, Math.max(0, (now - last) / 1000)); last = now;
      try {
        if (this.visible && !this.exporting) {
          if (!this.paused) this.time += dt;
          const ease = 1 - Math.exp(-dt * 4.3);
          this.pointer[0] += (this.pointerTarget[0] - this.pointer[0]) * ease;
          this.pointer[1] += (this.pointerTarget[1] - this.pointer[1]) * ease;
          if (now - lastPaint >= 1000 / 61) {
            lastPaint = now; this.draw(this.target);
            if (++frameCount % 120 === 1) this.checkError('动态绘制');
          }
        }
      } catch (error) { this.fail(error); return; }
      this.frameId = requestAnimationFrame(frame);
    };
    this.frameId = requestAnimationFrame(frame);
  }
  async exportPNG(width, height) {
    if (this.exporting) throw new Error('上一张静帧仍在保存。');
    if (this.failed) throw new Error('图形会话不可用，请刷新页面后再保存。');
    if (!Number.isInteger(width) || !Number.isInteger(height) || width < 2 || height < 2 || width > this.maxWidth || height > this.maxHeight) {
      throw new Error('导出尺寸无效或超过当前图形设备上限。');
    }
    this.exporting = true;
    const oldWidth = this.canvas.width, oldHeight = this.canvas.height;
    let target;
    try {
      target = width === this.target.width && height === this.target.height ? this.target : this.makeTarget(width, height);
      this.canvas.width = width; this.canvas.height = height;
      this.draw(target); this.checkError('静帧绘制');
      // Call toBlob in this task immediately after drawing: it snapshots before
      // preserveDrawingBuffer:false may discard the presented default buffer.
      const blob = await new Promise((resolve, reject) => this.canvas.toBlob(
        (value) => value ? resolve(value) : reject(new Error('无法编码 PNG。')), 'image/png'
      ));
      if (this.gl.isContextLost()) throw new Error('保存时图形会话中断，请刷新页面重试。');
      return blob;
    } finally {
      if (target && target !== this.target) this.destroyTarget(target);
      this.canvas.width = oldWidth; this.canvas.height = oldHeight; this.exporting = false;
      if (!this.failed && !this.gl.isContextLost()) {
        try { this.resize(); this.draw(this.target); this.report(); } catch (error) { this.fail(error); }
      }
    }
  }
  dispose() {
    this.running = false; cancelAnimationFrame(this.frameId);
    this.resizeObserver?.disconnect(); this.canvas.removeEventListener('webglcontextlost', this.onContextLost);
    if (!this.gl) return;
    this.destroyTarget(this.target);
    for (const texture of [this.image, this.depth, this.cloud]) if (texture) this.gl.deleteTexture(texture);
    for (const info of this.programs) this.gl.deleteProgram(info.program);
    if (this.vao) this.gl.deleteVertexArray(this.vao);
  }
}
