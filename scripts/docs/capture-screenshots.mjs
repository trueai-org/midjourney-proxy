#!/usr/bin/env node
/**
 * 采集线上公益站 https://ai.trueai.org 的 midjourney-proxy 管理后台截图。
 *
 * 约定：
 * - 固定视口 1366×768、deviceScaleFactor=1、mobile=false，只截当前视口（不拼接整页）。
 * - 使用原生 Chrome DevTools Protocol（CDP）+ midjourney-proxy-webui/node_modules/ws，零额外 npm 依赖。
 * - 每页截图前通过 Runtime.evaluate 注入脱敏逻辑：隐藏系统信息与 Top IP、按表头清空敏感列、
 *   替换缩略图、隐藏绘画测试的最近任务、设置页只保留「其他设置」，并把可见文本中的
 *   邮箱、手机号、IPv4、长 Token、连接串替换为占位符。
 * - 每张 PNG 读取 IHDR 校验宽高必须精确为 1366×768。
 *
 * 环境变量：
 * - BASE_URL    默认 https://ai.trueai.org
 * - CHROME_PATH 默认 C:\Program Files\Google\Chrome\Application\chrome.exe
 * - OUT_DIR     默认 <repo>/docs/manual/images
 *
 * 用法：
 *   node scripts/docs/capture-screenshots.mjs
 */

import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REQUIRE = createRequire(import.meta.url);

// 脚本位于 <repo>/scripts/docs，仓库根目录需要向上两级
const REPO_ROOT = path.resolve(__dirname, '..', '..');
const BASE_URL = (process.env.BASE_URL || 'https://ai.trueai.org').replace(/\/+$/, '');
const CHROME_PATH =
  process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const OUT_DIR = process.env.OUT_DIR || path.join(REPO_ROOT, 'docs', 'manual', 'images');
const DEBUG_PORT = 9223;
// 手册统一使用 1366×768 的桌面视口，截图也只保留当前视口
const VIEWPORT = { width: 1366, height: 768 };

const WS_MODULE_PATH = path.resolve(
  __dirname,
  '../../../../midjourney-proxy-webui/node_modules/ws',
);

let WebSocketImpl;
try {
  WebSocketImpl = REQUIRE(WS_MODULE_PATH);
} catch (error) {
  console.error(`无法从 ${WS_MODULE_PATH} 加载 ws 模块：${error.message}`);
  process.exit(1);
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** 轮询等待条件成立，超时抛错。 */
async function waitFor(check, timeoutMs, label) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const result = await check();
      if (result) return result;
    } catch (error) {
      lastError = error;
    }
    await sleep(250);
  }
  const suffix = lastError ? `；最后错误：${lastError.message}` : '';
  throw new Error(`等待 ${label} 超时（${timeoutMs}ms）${suffix}`);
}

/** 发起 HTTP GET 并返回 JSON。 */
async function getJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`HTTP ${response.status} ${url}`);
  }
  return response.json();
}

/** 极简 CDP 客户端：连接 page target 的 WebSocket。 */
class CdpClient {
  constructor(ws) {
    this.ws = ws;
    this.nextId = 1;
    this.pending = new Map();
    this.consoleErrors = [];

    ws.on('message', (raw) => {
      let message;
      try {
        message = JSON.parse(raw.toString());
      } catch {
        return;
      }

      if (message.id && this.pending.has(message.id)) {
        const { resolve, reject, timer } = this.pending.get(message.id);
        clearTimeout(timer);
        this.pending.delete(message.id);
        if (message.error) {
          reject(new Error(`${message.error.message} (${message.error.code})`));
        } else {
          resolve(message.result);
        }
        return;
      }

      if (message.method === 'Runtime.exceptionThrown') {
        const details = message.params?.exceptionDetails;
        this.consoleErrors.push(
          `exception: ${details?.exception?.description || details?.text || 'unknown'}`,
        );
      } else if (message.method === 'Runtime.consoleAPICalled' && message.params?.type === 'error') {
        const text = (message.params.args || [])
          .map((arg) => arg.value ?? arg.description ?? '')
          .join(' ');
        this.consoleErrors.push(`console.error: ${text}`);
      } else if (message.method === 'Log.entryAdded' && message.params?.entry?.level === 'error') {
        this.consoleErrors.push(`log.error: ${message.params.entry.text}`);
      }
    });
  }

  send(method, params = {}, timeoutMs = 30000) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`CDP ${method} 超时（${timeoutMs}ms）`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }

  resetConsoleErrors() {
    this.consoleErrors = [];
  }

  async evaluate(expression) {
    const result = await this.send('Runtime.evaluate', {
      expression,
      returnByValue: true,
      awaitPromise: true,
    });
    if (result.exceptionDetails) {
      const text =
        result.exceptionDetails.exception?.description || result.exceptionDetails.text || 'unknown';
      throw new Error(`页面脚本执行失败：${text}`);
    }
    return result.result?.value;
  }

  /** 把浏览器内函数和参数序列化成一次 evaluate 调用。 */
  async callInPage(fn, ...args) {
    const params = args.map((arg) => JSON.stringify(arg)).join(', ');
    return this.evaluate(`(${fn.toString()})(${params})`);
  }
}

/** 启动 Chrome 并返回进程句柄与临时用户目录。 */
async function launchChrome() {
  if (!fs.existsSync(CHROME_PATH)) {
    throw new Error(`未找到 Chrome：${CHROME_PATH}`);
  }

  // 端口被占用时明确报错，避免连到未知实例
  try {
    await getJson(`http://127.0.0.1:${DEBUG_PORT}/json/version`);
    throw new Error(`端口 ${DEBUG_PORT} 已被占用，请先关闭遗留的 Chrome 调试实例。`);
  } catch (error) {
    if (error.message.includes('已被占用')) throw error;
    // 连接失败属于正常现象
  }

  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'mjproxy-live-chrome-'));
  const args = [
    '--headless=new',
    `--remote-debugging-port=${DEBUG_PORT}`,
    `--user-data-dir=${userDataDir}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-gpu',
    '--hide-scrollbars',
    '--force-device-scale-factor=1',
    `--window-size=${VIEWPORT.width},${VIEWPORT.height}`,
    'about:blank',
  ];

  const child = spawn(CHROME_PATH, args, { stdio: 'ignore' });
  child.on('error', (error) => {
    console.error(`Chrome 启动失败：${error.message}`);
  });

  await waitFor(
    async () => {
      try {
        const version = await getJson(`http://127.0.0.1:${DEBUG_PORT}/json/version`);
        return Boolean(version.webSocketDebuggerUrl);
      } catch {
        return false;
      }
    },
    30000,
    'Chrome 调试端口',
  );

  return { child, userDataDir };
}

/** 连接 about:blank 页面 target，并把视口锁定为 1366×768。 */
async function connectPage() {
  const targets = await getJson(`http://127.0.0.1:${DEBUG_PORT}/json/list`);
  const page = targets.find((target) => target.type === 'page' && target.webSocketDebuggerUrl);
  if (!page) {
    throw new Error('未找到可用的页面 target');
  }

  const ws = new WebSocketImpl(page.webSocketDebuggerUrl, { perMessageDeflate: false });
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('WebSocket 连接超时')), 10000);
    ws.once('open', () => {
      clearTimeout(timer);
      resolve();
    });
    ws.once('error', (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });

  const client = new CdpClient(ws);
  await client.send('Page.enable');
  await client.send('Runtime.enable');
  await client.send('Log.enable');
  await client.send('Emulation.setDeviceMetricsOverride', {
    width: VIEWPORT.width,
    height: VIEWPORT.height,
    deviceScaleFactor: 1,
    mobile: false,
  });

  // 记录页面未捕获异常，截图失败时便于定位具体堆栈
  await client.send('Page.addScriptToEvaluateOnNewDocument', {
    source: `
      window.__captureErrors = [];
      // 线上站有全局公告弹窗，按站点自身逻辑写入“今日不再提醒”，避免遮挡截图
      try {
        localStorage.setItem('global_announcement_dismissed', new Date().toDateString());
      } catch (e) {}
      const originalConsoleError = console.error.bind(console);
      console.error = (...args) => {
        window.__captureErrors.push(args.map((arg) => (arg && arg.stack) || String(arg)).join(' '));
        originalConsoleError(...args);
      };
      window.addEventListener('error', (event) => {
        window.__captureErrors.push(String((event.error && event.error.stack) || event.message));
      });
      window.addEventListener('unhandledrejection', (event) => {
        window.__captureErrors.push(String((event.reason && event.reason.stack) || event.reason));
      });
    `,
  });

  return { client, ws };
}

/** 读取 PNG 前 24 字节（签名 + IHDR）并校验宽高。 */
function assertPngSize(file, expected) {
  const buffer = fs.readFileSync(file);
  if (buffer.length < 24) {
    throw new Error(`${file} 不是有效的 PNG（文件过小）`);
  }
  const signature = buffer.subarray(0, 8).toString('hex');
  if (signature !== '89504e470d0a1a0a') {
    throw new Error(`${file} 不是有效的 PNG（签名不匹配）`);
  }
  const width = buffer.readUInt32BE(16);
  const height = buffer.readUInt32BE(20);
  if (width !== expected.width || height !== expected.height) {
    throw new Error(
      `${path.basename(file)} 尺寸为 ${width}x${height}，预期 ${expected.width}x${expected.height}`,
    );
  }
  return { width, height, bytes: buffer.length };
}

/**
 * 页面内脱敏：只处理当前文档，返回各步骤的统计，便于日志确认脱敏确实执行。
 *
 * config 字段：
 * - maskColumns：表头包含任一关键字的列，整列单元格内容替换为 ***
 * - partialMaskColumns：表头包含任一关键字的标识列，按「前 6 位 + ... + 后 4 位」保留示意
 * - replaceThumbnails：把表格内缩略图替换为灰色占位块（保留列宽）
 * - hideSelectors：需要整块隐藏的选择器
 * - hideCardsContaining：整卡隐藏卡片文本中包含关键字（系统信息、程序信息等）的卡片
 * - hideTopIpBlock：隐藏“今日绘图 TOP 10”区块（避免展示访问 IP）
 * - settingsOnlyTitle：设置页只保留标题含该关键字的卡片
 * - probePlaceholder：把日志面板文本替换为指定占位文案
 * - generic：是否执行通用文本脱敏（邮箱、手机号、IPv4、长 Token、连接串）
 */
function applyDesensitize(config) {
  const MASK = '***';
  const log = {
    maskedColumns: [],
    maskedCells: 0,
    thumbnails: 0,
    hiddenBlocks: [],
    textMasks: 0,
    probeReplaced: false,
  };
  const textOf = (el) => ((el && (el.innerText || el.textContent)) || '').replace(/\s+/g, ' ').trim();
  const hideEl = (el, label) => {
    if (!el || el.dataset.__maskedHidden === '1') return;
    el.dataset.__maskedHidden = '1';
    el.style.setProperty('display', 'none', 'important');
    log.hiddenBlocks.push(label);
  };
  /** 标识列部分脱敏：保留前 6 位与后 4 位，过短的标识直接替换为 ***。 */
  const partialMask = (value) => {
    const text = (value || '').replace(/\s+/g, ' ').trim();
    if (!text) return text;
    if (text.length <= 10) return MASK;
    return `${text.slice(0, 6)}...${text.slice(-4)}`;
  };

  // 1) 整块隐藏（例如绘画测试的“最近任务”面板）
  for (const selector of config.hideSelectors || []) {
    for (const el of document.querySelectorAll(selector)) {
      hideEl(el, selector);
    }
  }

  // 2) 按表头脱敏：先固定列宽，再清空单元格，保证表格布局与列宽不变
  const fullWanted = (config.maskColumns || []).map((item) => item.toLowerCase());
  const partWanted = (config.partialMaskColumns || []).map((item) => item.toLowerCase());
  if (fullWanted.length || partWanted.length) {
    for (const table of document.querySelectorAll('.ant-table')) {
      const headRow = table.querySelector('thead tr');
      if (!headRow) continue;
      const headers = Array.from(headRow.children);
      const targets = [];
      headers.forEach((th, index) => {
        const name = textOf(th).toLowerCase();
        if (!name) return;
        if (fullWanted.some((word) => name.includes(word))) {
          targets.push({ index, mode: 'full' });
        } else if (partWanted.some((word) => name.includes(word))) {
          targets.push({ index, mode: 'partial' });
        }
      });
      if (!targets.length) continue;

      const cols = table.querySelectorAll('colgroup col');
      for (const { index, mode } of targets) {
        const headerText = textOf(headers[index]);
        log.maskedColumns.push(mode === 'partial' ? `${headerText}(部分)` : headerText);
        const columnWidth = Math.round(headers[index].getBoundingClientRect().width);
        if (cols[index] && columnWidth) {
          cols[index].style.width = `${columnWidth}px`;
          cols[index].style.minWidth = `${columnWidth}px`;
        }
        for (const row of table.querySelectorAll('tbody tr.ant-table-row')) {
          // 只有单元格数量与表头一致时才按下标映射，避免展开行/测量行错位
          if (row.children.length !== headers.length) continue;
          const cell = row.children[index];
          if (!cell || cell.tagName !== 'TD') continue;
          const cellWidth = Math.round(cell.getBoundingClientRect().width);
          const originalText = textOf(cell);
          cell.innerHTML = '';
          const span = document.createElement('span');
          span.textContent = mode === 'partial' ? partialMask(originalText) : MASK;
          span.style.color = '#8c8c8c';
          cell.appendChild(span);
          if (cellWidth) {
            cell.style.width = `${cellWidth}px`;
            cell.style.maxWidth = `${cellWidth}px`;
          }
          log.maskedCells += 1;
        }
      }
    }
  }

  // 3) 缩略图替换为灰色占位块
  if (config.replaceThumbnails) {
    for (const cell of document.querySelectorAll('.ant-table tbody td')) {
      if (!cell.querySelector('img')) continue;
      const cellWidth = Math.round(cell.getBoundingClientRect().width);
      const placeholder = document.createElement('div');
      const width = Math.max(24, Math.min(cellWidth - 24, 64));
      placeholder.style.cssText = `width:${width}px;height:40px;border-radius:2px;background:#d9d9d9;`;
      cell.innerHTML = '';
      cell.appendChild(placeholder);
      log.thumbnails += 1;
    }
  }

  // 4) 整卡隐藏（欢迎页的程序信息 / 系统信息 / 服务器信息）
  for (const card of document.querySelectorAll('.ant-card')) {
    const text = textOf(card).slice(0, 4000);
    for (const keyword of config.hideCardsContaining || []) {
      if (text.includes(keyword)) {
        hideEl(card, `卡片含「${keyword}」`);
        break;
      }
    }
  }

  // 5) 隐藏“今日绘图 TOP 10”区块：取包含标题且有实际高度（含数据行）的最小容器
  if (config.hideTopIpBlock) {
    for (const card of document.querySelectorAll('.ant-card')) {
      if (!/TOP\s*10|Top\s*IP|访问\s*IP/i.test(textOf(card))) continue;
      const candidates = Array.from(card.querySelectorAll('div')).filter((el) => {
        const text = textOf(el);
        if (!/TOP\s*10|Top\s*IP|访问\s*IP/i.test(text)) return false;
        if (/昨日绘图/.test(text)) return false;
        return el.getBoundingClientRect().height > 60;
      });
      candidates.sort(
        (a, b) => a.querySelectorAll('*').length - b.querySelectorAll('*').length,
      );
      if (candidates.length) {
        hideEl(candidates[0], '今日绘图 Top IP 区块');
      } else {
        hideEl(card, '今日绘图 Top IP 卡片');
      }
    }
  }

  // 6) 设置页只保留「其他设置」：其余卡片全部隐藏，并把多列网格改成单列
  if (config.settingsOnlyTitle) {
    const items = Array.from(document.querySelectorAll('.settings-grid-item'));
    const target = items.find((item) => {
      const title = item.querySelector('.ant-card-head-title');
      return title && textOf(title).includes(config.settingsOnlyTitle);
    });
    if (!target) {
      throw new Error(`未找到「${config.settingsOnlyTitle}」设置卡片`);
    }
    for (const item of items) {
      if (item !== target) hideEl(item, '非目标设置分组');
    }
    for (const card of document.querySelectorAll('.ant-card')) {
      if (target.contains(card)) continue;
      const title = card.querySelector('.ant-card-head-title');
      if (!title || !textOf(title).includes(config.settingsOnlyTitle)) {
        hideEl(card, '非目标设置卡片');
      }
    }
    const grid = target.parentElement;
    if (grid) {
      grid.style.setProperty('display', 'block', 'important');
      grid.style.setProperty('column-count', '1', 'important');
    }
    target.style.width = '100%';
    target.style.breakInside = 'auto';
  }

  // 7) 日志面板：替换为占位文案，避免出现本机路径、IP、主机名
  if (config.probePlaceholder) {
    for (const pre of document.querySelectorAll('pre')) {
      if (!textOf(pre)) continue;
      pre.textContent = config.probePlaceholder;
      log.probeReplaced = true;
    }
  }

  // 8) 通用文本脱敏：只处理可见文本节点，避免误改属性与脚本
  if (config.generic !== false) {
    const patterns = [
      // 邮箱
      { re: /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g, mask: MASK },
      // 中国大陆手机号
      { re: /(?<!\d)1[3-9]\d{9}(?!\d)/g, mask: MASK },
      // IPv4（排除 v12.1.6.0 这类版本号：前面不能是字母或点）
      {
        re: /(?<![\w.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?![\d.])/g,
        mask: MASK,
      },
      // 长 Token / API Key（连续 24 位以上的字母数字）
      { re: /(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{24,}(?![A-Za-z0-9_-])/g, mask: MASK },
      // 连接串键值对
      {
        re: /(?:Server|Host|Data Source|User Id|Uid|Password|Pwd)\s*=[^;\s]+/gi,
        mask: '****',
      },
    ];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        const parent = node.parentElement;
        if (!parent) return NodeFilter.FILTER_REJECT;
        if (['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEXTAREA'].includes(parent.tagName)) {
          return NodeFilter.FILTER_REJECT;
        }
        const rect = parent.getBoundingClientRect();
        // 只处理可见文本，避免把隐藏的弹窗、下拉内容误判为泄露
        if (rect.width === 0 || rect.height === 0) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      },
    });
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    for (const node of nodes) {
      const value = node.nodeValue;
      if (!value) continue;
      let next = value;
      for (const { re, mask } of patterns) next = next.replace(re, mask);
      if (next !== value) {
        node.nodeValue = next;
        log.textMasks += 1;
      }
    }
  }

  // 截图统一从页面顶部开始，保证标题栏与首屏内容完整
  if (config.scrollToTop !== false) {
    document.documentElement.style.scrollBehavior = 'auto';
    window.scrollTo(0, 0);
  }

  return log;
}

/** 每页定义：文件名、路由、等待条件与脱敏配置。 */
const PAGES = [
  {
    file: '01-login.png',
    route: '/#/user/login',
    selector: 'input[type="password"]',
  },
  {
    file: '02-welcome.png',
    route: '/#/welcome',
    selector: '.ant-card',
    text: '今日可用绘图',
    desensitize: {
      // 系统信息卡包含主机名与私有 IP，Top IP 卡包含访问来源 IP
      hideCardsContaining: ['程序信息', '系统信息', '服务器信息', '主机名', '私有IP'],
      hideTopIpBlock: true,
    },
  },
  {
    file: '03-accounts.png',
    route: '/#/account',
    selector: '.ant-table-row',
    desensitize: {
      maskColumns: ['备注', 'Remark'],
      // 服务器ID/频道ID 是真实 Discord 标识，保留前后片段便于示意，其余用省略号遮蔽
      partialMaskColumns: ['服务器ID', '频道ID'],
    },
  },
  {
    file: '04-tasks.png',
    route: '/#/task',
    selector: '.ant-table-row',
    desensitize: {
      maskColumns: ['描述', '提示词', 'Prompt'],
      partialMaskColumns: ['实例ID'],
      replaceThumbnails: true,
    },
  },
  {
    file: '05-draw-test.png',
    route: '/#/draw-test',
    selector: 'textarea',
    desensitize: {
      // 最近任务面板含真实提示词与图片，只保留提交表单与参数区
      hideSelectors: ['#task-panel'],
    },
  },
  {
    file: '06-users.png',
    route: '/#/user-list',
    selector: '.ant-table-row',
    desensitize: { maskColumns: ['邮箱', '手机', '最后登录IP', '注册IP', 'IP'] },
  },
  {
    file: '07-banned-words.png',
    route: '/#/banned-word',
    selector: '.ant-table-row',
    desensitize: { maskColumns: ['名称', '描述'] },
  },
  {
    file: '08-settings.png',
    route: '/#/setting',
    selector: '.settings-grid-item',
    desensitize: { settingsOnlyTitle: '其他设置' },
    settleMs: 800,
  },
  {
    file: '09-probe.png',
    route: '/#/probe',
    selector: '.ant-card',
    desensitize: { probePlaceholder: '演示模式，日志内容已隐藏' },
  },
  {
    file: '10-swagger.png',
    route: '/swagger/index.html',
    selector: '.swagger-ui',
    waitAfterSelectorMs: 2000,
    desensitize: {
      // 隐藏信息区里的文档地址（含本地端口），只保留标题与接口列表
      hideSelectors: ['.swagger-ui .info .url', '.swagger-ui .info a.link'],
    },
    beforeCapture: async (client) => {
      // 线上演示站尚未部署最新修复时，/swagger/v1/swagger.json 仍可能返回 500，
      // 页面只会显示「Failed to load API definition」，不能作为手册插图。
      // 这里检测到失败就跳过，保留 images 目录中现有的图片，避免把报错页覆盖进手册。
      const failed = await client.callInPage(() => {
        const text = (document.body.innerText || '').replace(/\s+/g, ' ');
        return text.includes('Failed to load API definition');
      });
      return failed
        ? '线上 Swagger 文档加载失败（/swagger/v1/swagger.json 返回 500），页面只有报错内容'
        : null;
    },
  },
];

let shotSequence = 0;
/** 本次因线上页面异常而跳过的文件，便于结尾汇总。 */
const skippedPages = [];

/**
 * 解析 PAGES 环境变量（逗号分隔的文件名），用于只补采指定页面。
 * 例如：PAGES=03-accounts.png,04-tasks.png，或给本地实例补采 10-swagger.png。
 */
function selectPages() {
  const raw = (process.env.PAGES || '').trim();
  if (!raw) return PAGES;
  const wanted = raw
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
  const unknown = wanted.filter((file) => !PAGES.some((page) => page.file === file));
  if (unknown.length > 0) {
    throw new Error(`PAGES 中存在未知页面：${unknown.join('、')}`);
  }
  return PAGES.filter((page) => wanted.includes(page.file));
}

/** 采集单页并校验输出尺寸。 */
async function capturePage(client, page) {
  // 每个页面都完整加载一次，避免上一页的异步请求或错误边界污染下一页
  const url = page.route.startsWith('/#')
    ? `${BASE_URL}/?shot=${++shotSequence}${page.route.slice(1)}`
    : `${BASE_URL}${page.route}`;
  client.resetConsoleErrors();

  console.log(`采集 ${page.file} -> ${url}`);
  await client.send('Page.navigate', { url });

  await waitFor(
    async () => (await client.evaluate('document.readyState')) === 'complete',
    30000,
    `${page.file} 的 document.readyState`,
  );

  await waitFor(
    async () => client.evaluate(`Boolean(document.querySelector(${JSON.stringify(page.selector)}))`),
    30000,
    `${page.file} 的选择器 ${page.selector}`,
  );

  if (page.text) {
    await waitFor(
      async () =>
        client.evaluate(
          `(document.body.innerText || '').includes(${JSON.stringify(page.text)})`,
        ),
      30000,
      `${page.file} 的文本「${page.text}」`,
    );
  }

  // 等待异步数据渲染稳定
  await sleep(1500);
  if (page.waitAfterSelectorMs) {
    await sleep(page.waitAfterSelectorMs);
  }

  // 兜底关闭公告弹窗（正常情况下前置脚本已写入“今日不再提醒”）
  await client.callInPage(() => {
    const buttons = Array.from(document.querySelectorAll('.ant-modal button'));
    const target = buttons.find((btn) => (btn.innerText || '').includes('我已知晓'));
    if (target) target.click();
  });

  const log = await client.callInPage(applyDesensitize, page.desensitize || {});
  console.log(
    `  脱敏：列[${log.maskedColumns.join('、') || '无'}] 单元格 ${log.maskedCells} 个，` +
      `缩略图 ${log.thumbnails} 个，隐藏区块 ${log.hiddenBlocks.length} 个，` +
      `文本替换 ${log.textMasks} 处${log.probeReplaced ? '，日志已替换为占位文案' : ''}`,
  );

  if (page.settleMs) {
    await sleep(page.settleMs);
  } else {
    await sleep(300);
  }

  // 页面级前置校验：线上页面本身异常（例如 Swagger 文档 500）时跳过，不覆盖已有图片
  if (page.beforeCapture) {
    const reason = await page.beforeCapture(client);
    if (reason) {
      console.warn(`  跳过 ${page.file}：${reason}`);
      console.warn(`  已保留 ${path.join(OUT_DIR, page.file)} 的现有文件。`);
      skippedPages.push(page.file);
      return;
    }
  }

  // 只截当前视口：不传 clip、不开启 captureBeyondViewport
  const screenshot = await client.send('Page.captureScreenshot', {
    format: 'png',
    fromSurface: true,
    captureBeyondViewport: false,
  });

  fs.mkdirSync(OUT_DIR, { recursive: true });
  const filePath = path.join(OUT_DIR, page.file);
  fs.writeFileSync(filePath, Buffer.from(screenshot.data, 'base64'));

  const size = assertPngSize(filePath, VIEWPORT);
  console.log(
    `  已保存 ${filePath}（${size.width}x${size.height}，${Math.round(size.bytes / 1024)} KB）`,
  );
}

/** 出错时输出页面标题、URL 与控制台错误。 */
async function describeFailure(client) {
  try {
    const info = await client.evaluate(
      '({ title: document.title, url: location.href, body: (document.body && document.body.innerText || "").slice(0, 500) })',
    );
    console.error(`  页面标题：${info.title}`);
    console.error(`  页面地址：${info.url}`);
    console.error(`  页面文本：${info.body.replace(/\s+/g, ' ').slice(0, 300)}`);
  } catch {
    // 页面可能已经崩溃，忽略
  }
  if (client.consoleErrors.length > 0) {
    console.error('  控制台错误：');
    for (const error of client.consoleErrors.slice(0, 20)) {
      console.error(`    - ${error}`);
    }
  }
  try {
    const pageErrors = await client.evaluate('(window.__captureErrors || []).slice(0, 20)');
    if (pageErrors && pageErrors.length > 0) {
      console.error('  页面异常：');
      for (const error of pageErrors) {
        console.error(`    - ${error}`);
      }
    }
  } catch {
    // 忽略
  }
}

/** 关闭 Chrome、删除临时 profile，并确认调试端口已释放。 */
async function shutdown(chrome) {
  try {
    chrome.child.kill('SIGKILL');
  } catch {
    // 忽略
  }

  await waitFor(
    async () => {
      try {
        await getJson(`http://127.0.0.1:${DEBUG_PORT}/json/version`);
        return false;
      } catch {
        return true;
      }
    },
    10000,
    '调试端口释放',
  ).catch(() => {
    console.warn(`  警告：端口 ${DEBUG_PORT} 仍在监听，请检查是否有遗留 Chrome 进程。`);
  });

  // Chrome 可能仍短暂持有临时目录文件锁，重试几次再放弃
  for (let attempt = 0; attempt < 5; attempt += 1) {
    try {
      fs.rmSync(chrome.userDataDir, { recursive: true, force: true });
      return;
    } catch {
      await sleep(400);
    }
  }
  console.warn(`  警告：临时 profile 未能删除：${chrome.userDataDir}`);
}

async function main() {
  const pages = selectPages();
  console.log(`线上地址：${BASE_URL}`);
  console.log(`输出目录：${OUT_DIR}`);
  console.log(`视口：${VIEWPORT.width}x${VIEWPORT.height}（仅截当前视口）`);

  const chrome = await launchChrome();
  let connection;

  try {
    connection = await connectPage();
    const { client, ws } = connection;

    for (const page of pages) {
      try {
        await capturePage(client, page);
      } catch (error) {
        console.error(`采集 ${page.file} 失败：${error.message}`);
        await describeFailure(client);
        throw error;
      }
    }

    try {
      ws.close();
    } catch {
      // 忽略关闭异常
    }
  } finally {
    try {
      fs.rmSync(chrome.userDataDir, { recursive: true, force: true });
    } catch {
      // 忽略；shutdown 中会再次尝试
    }
    await shutdown(chrome);
  }

  console.log(`截图采集完成，输出目录：${OUT_DIR}`);
  if (skippedPages.length > 0) {
    console.warn(
      `以下页面因线上异常被跳过，未覆盖现有图片：${skippedPages.join('、')}；` +
        '处理方式见 docs/manual/images/README.md 的「Swagger 截图来源」。',
    );
  }
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
