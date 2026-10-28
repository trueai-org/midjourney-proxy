#!/usr/bin/env node
/**
 * midjourney-proxy 文档校验脚本。
 *
 * 零依赖，仅使用 node:fs、node:path、node:url。检查项：
 * 1. 手册 Markdown、根 README、docs/README.md、docs/_sidebar.md、docs/_navbar.md 的相对链接；
 * 2. docs/manual/images 下全部 PNG 与 07-admin-console.md 引用的 10 张截图尺寸；
 * 3. docs/manual/index.json 的字段、路径与 order 顺序；
 * 4. 英文默认 README、中文 README、英文别名 README 的关键内容；
 * 5. 文档与脚本中的敏感信息红线（内部授权实现词、凭据、邮箱、手机号）；
 * 6. 手册目录残留文件、commits/releases 归档规模、截图说明。
 *
 * 用法：node scripts/docs/verify-docs.mjs
 * 退出码：全部硬性检查通过为 0，存在失败为 1。
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** 校验脚本所在目录。 */
const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
/** 仓库根目录：scripts/docs 向上两级。 */
const REPO_ROOT = path.resolve(SCRIPT_DIR, '..', '..');
/** 文档根目录。 */
const DOCS_DIR = path.join(REPO_ROOT, 'docs');
/** 手册目录。 */
const MANUAL_DIR = path.join(DOCS_DIR, 'manual');
/** 手册截图目录。 */
const IMAGES_DIR = path.join(MANUAL_DIR, 'images');

/** 截图统一要求的分辨率。 */
const EXPECTED_IMAGE = { width: 1366, height: 768 };
/** 提交归档最少条目数。 */
const MIN_COMMIT_ITEMS = 1451;
/** 发布归档版本表最少行数。 */
const MIN_RELEASE_ROWS = 631;

/** 校验结果：通过、警告、失败分开收集，最终输出中文报告。 */
const report = {
  passed: [],
  warnings: [],
  failures: [],
};

/**
 * 记录一条硬性失败。
 * @param {string} message 失败描述，需包含文件与行号（如适用）。
 * @param {string} hint 修复建议。
 * @returns {void}
 */
function fail(message, hint) {
  report.failures.push({ message, hint });
}

/**
 * 记录一条警告。
 * @param {string} message 警告描述。
 * @param {string} [hint] 可选建议。
 * @returns {void}
 */
function warn(message, hint) {
  report.warnings.push({ message, hint });
}

/**
 * 把绝对路径转为相对仓库根的展示路径，统一使用正斜杠。
 * @param {string} file 绝对路径。
 * @returns {string} 展示用相对路径。
 */
function displayPath(file) {
  return path.relative(REPO_ROOT, file).split(path.sep).join('/');
}

/**
 * 递归遍历目录。
 * @param {string} dir 起始目录。
 * @param {(full: string, entry: fs.Dirent) => void} onEntry 每项回调。
 * @returns {void}
 */
function walk(dir, onEntry) {
  if (!fs.existsSync(dir)) return;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    onEntry(full, entry);
    if (entry.isDirectory()) walk(full, onEntry);
  }
}

/**
 * 收集目录下符合条件的文件。
 * @param {string} dir 起始目录。
 * @param {(full: string) => boolean} predicate 文件过滤函数。
 * @returns {string[]} 绝对路径数组。
 */
function collectFiles(dir, predicate) {
  const files = [];
  walk(dir, (full, entry) => {
    if (entry.isFile() && predicate(full)) files.push(full);
  });
  return files;
}

/**
 * 截取命中位置附近的片段，便于在报告中定位。
 * @param {string} line 原始行。
 * @param {number} index 命中下标；未知时传 -1。
 * @returns {string} 不超过 120 字符的片段。
 */
function snippet(line, index) {
  const start = Math.max(0, (index >= 0 ? index : 0) - 20);
  return line.slice(start, start + 120).trim();
}

/**
 * 从 Markdown 行中解析内联链接与图片，跳过代码围栏与行内代码。
 * @param {string} file Markdown 文件路径。
 * @returns {{line: number, image: boolean, target: string}[]} 链接列表。
 */
function parseLinks(file) {
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  const links = [];
  let inFence = false;
  let fenceChar = '';

  lines.forEach((line, index) => {
    const fenceMatch = line.match(/^\s*(`{3,}|~{3,})/);
    if (fenceMatch) {
      const char = fenceMatch[1][0];
      if (!inFence) {
        inFence = true;
        fenceChar = char;
      } else if (char === fenceChar) {
        inFence = false;
        fenceChar = '';
      }
      return;
    }
    if (inFence) return;

    // 行内代码中的路径只是示例，不应作为链接校验
    const withoutInlineCode = line.replace(/`[^`]*`/g, '');
    const pattern = /(!?)\[[^\]]*\]\(\s*([^)]+?)\s*\)/g;
    let match;
    while ((match = pattern.exec(withoutInlineCode)) !== null) {
      links.push({ line: index + 1, image: match[1] === '!', target: match[2].trim() });
    }
  });

  return links;
}

/**
 * 清洗链接目标：去掉可选标题、尖括号与外层空白，并解码 URL。
 * @param {string} raw 原始链接目标。
 * @returns {string} 清洗后的目标。
 */
function cleanTarget(raw) {
  let target = raw.trim();
  const quotedTitle = target.match(/^(.*?)\s+["'][^"']*["']$/);
  if (quotedTitle) target = quotedTitle[1];
  if (target.startsWith('<') && target.endsWith('>')) target = target.slice(1, -1);
  try {
    target = decodeURIComponent(target);
  } catch {
    // 保留原值，后续按字面路径检查
  }
  return target.trim();
}

/** 外部协议或协议相对地址，不参与本地文件存在性检查。 */
const EXTERNAL_TARGET = /^(?:[a-z][a-z0-9+.-]*:|\/\/)/i;

/**
 * 把候选路径解析为已存在的文件；目录优先补 README.md / index.md，无扩展名时补 .md。
 * @param {string} candidate 绝对路径。
 * @returns {string|null} 存在的文件路径；不存在返回 null。
 */
function resolveExisting(candidate) {
  if (fs.existsSync(candidate)) {
    const stat = fs.statSync(candidate);
    if (stat.isDirectory()) {
      for (const name of ['README.md', 'index.md']) {
        const nested = path.join(candidate, name);
        if (fs.existsSync(nested)) return nested;
      }
    }
    return candidate;
  }

  if (!path.extname(candidate)) {
    const withMd = `${candidate}.md`;
    if (fs.existsSync(withMd)) return withMd;
    for (const name of ['README.md', 'index.md']) {
      const nested = path.join(candidate, name);
      if (fs.existsSync(nested)) return nested;
    }
  }

  return null;
}

/**
 * 解析本地链接目标：相对路径基于源文件目录，docsify 的绝对路径映射到 docs 根目录。
 * @param {string} sourceFile 链接所在文件。
 * @param {string} target 清洗后的链接目标。
 * @returns {string|null} 存在的目标文件；不存在返回 null。
 */
function resolveLocalTarget(sourceFile, target) {
  const clean = target.split('#')[0].split('?')[0].trim();
  if (!clean) return null;

  let candidate;
  if (clean.startsWith('/')) {
    const rel = clean.replace(/^\/+/, '');
    candidate = rel === '' ? path.join(DOCS_DIR, 'README.md') : path.join(DOCS_DIR, rel);
  } else {
    candidate = path.resolve(path.dirname(sourceFile), clean);
  }

  return resolveExisting(candidate);
}

/**
 * 检查相对链接。
 * @returns {void}
 */
function checkLinks() {
  const files = [
    ...collectFiles(MANUAL_DIR, (file) => file.endsWith('.md')),
    path.join(REPO_ROOT, 'README.md'),
    path.join(REPO_ROOT, 'README.zh-CN.md'),
    path.join(REPO_ROOT, 'README.en.md'),
    path.join(DOCS_DIR, 'README.md'),
    path.join(DOCS_DIR, '_sidebar.md'),
    path.join(DOCS_DIR, '_navbar.md'),
  ].filter((file) => fs.existsSync(file));

  const before = report.failures.length;
  let total = 0;

  for (const file of files) {
    for (const link of parseLinks(file)) {
      const target = cleanTarget(link.target);
      if (!target || target.startsWith('#') || EXTERNAL_TARGET.test(target)) continue;
      total += 1;
      if (!resolveLocalTarget(file, target)) {
        fail(
          `${displayPath(file)}:${link.line} 相对链接失效：${link.target}`,
          '修正链接路径，或补充缺失的目标文件。',
        );
      }
    }
  }

  if (report.failures.length === before) {
    report.passed.push(`相对链接：${files.length} 个文件、${total} 个本地链接全部可解析`);
  }
}

/**
 * 读取 PNG 前 24 字节（签名 + IHDR）并解析宽高。
 * @param {string} file PNG 文件路径。
 * @returns {{width: number, height: number}|{error: string}} 尺寸或错误。
 */
function readPngSize(file) {
  const buffer = Buffer.alloc(24);
  let fd;
  try {
    fd = fs.openSync(file, 'r');
    if (fs.readSync(fd, buffer, 0, 24, 0) < 24) return { error: '文件小于 24 字节' };
  } catch (error) {
    return { error: `无法读取文件：${error.message}` };
  } finally {
    if (fd !== undefined) fs.closeSync(fd);
  }
  if (buffer.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') {
    return { error: 'PNG 签名不匹配' };
  }
  if (buffer.subarray(12, 16).toString('ascii') !== 'IHDR') {
    return { error: '前 24 字节中缺少 IHDR' };
  }
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

/**
 * 检查截图目录与 07-admin-console.md 引用的截图。
 * @returns {void}
 */
function checkImages() {
  const before = report.failures.length;
  const pngs = fs.existsSync(IMAGES_DIR)
    ? fs.readdirSync(IMAGES_DIR).filter((name) => name.toLowerCase().endsWith('.png')).sort()
    : [];

  if (pngs.length === 0) {
    fail('docs/manual/images 下没有找到 PNG 截图', '运行 scripts/docs/capture-screenshots.mjs 采集截图。');
    return;
  }

  for (const name of pngs) {
    const size = readPngSize(path.join(IMAGES_DIR, name));
    if (size.error) {
      fail(`docs/manual/images/${name} 不是有效 PNG：${size.error}`, '重新采集该截图。');
    } else if (size.width !== EXPECTED_IMAGE.width || size.height !== EXPECTED_IMAGE.height) {
      fail(
        `docs/manual/images/${name} 尺寸为 ${size.width}x${size.height}，要求 ${EXPECTED_IMAGE.width}x${EXPECTED_IMAGE.height}`,
        '按 1366×768 视口重新采集。',
      );
    }
  }

  const adminFile = path.join(MANUAL_DIR, '07-admin-console.md');
  if (!fs.existsSync(adminFile)) {
    fail('docs/manual/07-admin-console.md 不存在', '补充管理后台章节。');
    return;
  }

  const referenced = new Set();
  for (const link of parseLinks(adminFile)) {
    const target = cleanTarget(link.target);
    if (!link.image || !/\.png$/i.test(target)) continue;
    const resolved = resolveLocalTarget(adminFile, target);
    if (!resolved) {
      fail(`${displayPath(adminFile)}:${link.line} 引用的截图不存在：${link.target}`, '补充截图文件或修正路径。');
      continue;
    }
    referenced.add(resolved);
    const size = readPngSize(resolved);
    if (size.error) {
      fail(`${displayPath(adminFile)}:${link.line} 引用的截图无效：${size.error}`, '重新采集该截图。');
    } else if (size.width !== EXPECTED_IMAGE.width || size.height !== EXPECTED_IMAGE.height) {
      fail(
        `${displayPath(adminFile)}:${link.line} 引用的截图尺寸为 ${size.width}x${size.height}，要求 ${EXPECTED_IMAGE.width}x${EXPECTED_IMAGE.height}`,
        '按 1366×768 视口重新采集。',
      );
    }
  }

  if (referenced.size !== 10) {
    fail(
      `docs/manual/07-admin-console.md 引用 ${referenced.size} 张图片，要求 10 张`,
      '补齐或调整截图引用，保持 10 张管理后台截图。',
    );
  }

  for (const name of pngs) {
    const full = path.join(IMAGES_DIR, name);
    if (!referenced.has(full)) {
      warn(`docs/manual/images/${name} 未被 07-admin-console.md 引用`, '确认是否为多余截图。');
    }
  }

  if (report.failures.length === before) {
    report.passed.push(
      `截图：${pngs.length} 张 PNG 与 07-admin-console.md 引用的 ${referenced.size} 张图片均为 ${EXPECTED_IMAGE.width}×${EXPECTED_IMAGE.height}`,
    );
  }
}

/**
 * 检查 index.json 的字段、路径与 order。
 * @returns {void}
 */
function checkIndexJson() {
  const before = report.failures.length;
  const file = path.join(MANUAL_DIR, 'index.json');
  if (!fs.existsSync(file)) {
    fail('docs/manual/index.json 不存在', '补充手册索引文件。');
    return;
  }

  let entries;
  try {
    entries = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    fail(`docs/manual/index.json 不是合法 JSON：${error.message}`, '修正 JSON 语法。');
    return;
  }

  if (!Array.isArray(entries)) {
    fail('docs/manual/index.json 顶层必须是数组', '按数组格式组织索引条目。');
    return;
  }

  const requiredFields = ['id', 'title', 'path', 'lang', 'tags', 'summary', 'order', 'version', 'lastUpdated'];
  const ids = new Set();
  const orders = [];

  entries.forEach((entry, index) => {
    const prefix = `docs/manual/index.json 第 ${index + 1} 条`;
    if (!entry || typeof entry !== 'object' || Array.isArray(entry)) {
      fail(`${prefix} 不是对象`, '每条索引必须是对象。');
      return;
    }

    for (const field of requiredFields) {
      const value = entry[field];
      const missing =
        !Object.prototype.hasOwnProperty.call(entry, field) ||
        value === null ||
        value === undefined ||
        (typeof value === 'string' && value.trim() === '');
      if (missing) {
        fail(`${prefix} 缺少字段 ${field}`, `补充 ${field}。`);
      }
    }

    if (typeof entry.id === 'string' && entry.id.trim() !== '') {
      if (ids.has(entry.id)) fail(`${prefix} 的 id 重复：${entry.id}`, '保证 id 唯一。');
      ids.add(entry.id);
    }

    if (!Array.isArray(entry.tags)) {
      fail(`${prefix} 的 tags 必须是数组`, '把 tags 改为字符串数组。');
    }

    if (typeof entry.path === 'string' && entry.path.trim() !== '') {
      const target = path.resolve(MANUAL_DIR, entry.path);
      if (!fs.existsSync(target)) {
        fail(`${prefix} 的 path 不存在：${entry.path}`, '修正 path 或补充对应文件。');
      }
    }

    if (Number.isInteger(entry.order)) {
      orders.push(entry.order);
    } else {
      fail(`${prefix} 的 order 必须是整数`, 'order 用于稳定排序，必须是整数。');
    }
  });

  const uniqueOrders = new Set(orders);
  if (uniqueOrders.size !== orders.length) {
    fail('docs/manual/index.json 的 order 存在重复', '保证每条 order 唯一。');
  }

  const sorted = [...orders].sort((a, b) => a - b);
  const notContiguous = sorted.some((value, index) => value !== index);
  const notAscending = orders.some((value, index) => index > 0 && value <= orders[index - 1]);
  if (notContiguous || notAscending) {
    fail('docs/manual/index.json 的 order 必须从 0 开始连续且按文件顺序递增', '按 0、1、2… 重排 order。');
  }

  if (report.failures.length === before) {
    report.passed.push(`索引：docs/manual/index.json ${entries.length} 条，字段齐全且 order 连续唯一`);
  }
}

/**
 * 检查双语 README 的关键内容。
 * @returns {void}
 */
function checkReadmes() {
  const before = report.failures.length;

  /**
   * 检查指定文件前 40 行是否包含全部关键词。
   * @param {string} file README 路径。
   * @param {string[]} keywords 必须出现的关键词。
   * @returns {void}
   */
  const checkFirst40 = (file, keywords) => {
    if (!fs.existsSync(file)) {
      fail(`${displayPath(file)} 不存在`, '补充该 README。');
      return;
    }
    const head = fs.readFileSync(file, 'utf8').split(/\r?\n/).slice(0, 40).join('\n');
    for (const keyword of keywords) {
      if (!head.includes(keyword)) {
        fail(`${displayPath(file)} 前 40 行缺少：${keyword}`, '在企业级 API 区块补充链接或截图引用。');
      }
    }
  };

  checkFirst40(path.join(REPO_ROOT, 'README.md'), [
    'Enterprise Midjourney API',
    'https://huanwangai.com',
    'https://api.huanwangai.com',
    'api.huanwangai.com.png',
    'huanwangai.com.png',
  ]);

  checkFirst40(path.join(REPO_ROOT, 'README.zh-CN.md'), [
    '企业级 API',
    'https://huanwangai.com',
    'https://api.huanwangai.com',
    'api.huanwangai.com.png',
    'huanwangai.com.png',
  ]);

  const alias = path.join(REPO_ROOT, 'README.en.md');
  if (!fs.existsSync(alias)) {
    fail('README.en.md 不存在', '保留英文别名 README，避免旧链接失效。');
  } else {
    const text = fs.readFileSync(alias, 'utf8');
    if (!/\]\(\s*(?:\.\/)?README\.md\s*\)/.test(text)) {
      fail('README.en.md 缺少指向 README.md 的链接', '补充 [English README](README.md)。');
    }
  }

  if (report.failures.length === before) {
    report.passed.push('README：英文默认、中文说明与英文别名 README 的关键内容齐全');
  }
}

/**
 * 转义正则字面量。
 * @param {string} value 原始文本。
 * @returns {string} 转义后的文本。
 */
function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * 构造敏感红线词正则。
 * 词表用片段拼接，避免校验脚本把自身源码当成命中项。
 * @returns {RegExp} 不区分大小写的全局正则。
 */
function buildHardWordPattern() {
  const plain = [
    'Licen' + 'seKey',
    'Midjourney' + '.' + 'License',
    '授权' + '码',
    '授权' + '策略',
    '授权' + '算法',
    '机器' + '码',
    'license' + '.' + 'trueai',
    'license' + '.' + 'circle',
  ].map(escapeRegExp);
  const bounded = [
    'A' + 'ES',
    'H' + 'MAC',
  ].map((word) => `(?<![A-Za-z0-9])${escapeRegExp(word)}(?![A-Za-z0-9])`);

  return new RegExp([...plain, ...bounded].join('|'), 'gi');
}

/**
 * 执行一次全局匹配并返回命中文本。
 * @param {RegExp} pattern 正则（需带 g 标志）。
 * @param {string} line 被扫描的行。
 * @returns {string[]} 命中文本。
 */
function findAll(pattern, line) {
  const results = [];
  let match;
  pattern.lastIndex = 0;
  while ((match = pattern.exec(line)) !== null) {
    results.push(match[0]);
    if (match[0].length === 0) pattern.lastIndex += 1;
  }
  return results;
}

/** 凭据与个人信息检测规则；find 返回该行命中的原始文本。 */
const CREDENTIAL_RULES = [
  {
    label: '明文口令',
    find(line) {
      const results = [];
      const pattern = /password\s*=\s*([^\s;,`]+)/gi;
      let match;
      while ((match = pattern.exec(line)) !== null) {
        const value = match[1].replace(/^["']|["']$/g, '');
        // 允许 <YOUR_...>、***、**** 等占位值
        if (/^(?:<[^<>]*>|\*{3,})$/.test(value)) continue;
        results.push(match[0]);
      }
      return results;
    },
  },
  {
    label: 'sk- 密钥',
    find: (line) => findAll(/\bsk-[A-Za-z0-9_-]{10,}/g, line),
  },
  {
    label: 'MTI1 长串',
    find: (line) => findAll(/MTI1[A-Za-z0-9+/=]{10,}/g, line),
  },
  {
    label: 'Bearer Token',
    find: (line) => findAll(/\bBearer\s+[A-Za-z0-9._~+/=-]{20,}/gi, line),
  },
  {
    label: '邮箱',
    find: (line) =>
      findAll(/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g, line).filter(
        (value) => !/@example\.invalid$/i.test(value),
      ),
  },
  {
    label: '手机号',
    find: (line) => findAll(/(?<!\d)1[3-9]\d{9}(?!\d)/g, line),
  },
];

/**
 * 扫描文档与脚本中的红线词和疑似凭据。
 * @returns {void}
 */
function checkRedLines() {
  const before = report.failures.length;
  const files = new Set([
    ...collectFiles(MANUAL_DIR, (file) => file.endsWith('.md')),
    ...fs
      .readdirSync(REPO_ROOT)
      .filter((name) => /^README.*\.md$/i.test(name))
      .map((name) => path.join(REPO_ROOT, name)),
    ...collectFiles(path.join(REPO_ROOT, 'scripts', 'docs'), (file) => file.endsWith('.mjs')),
  ]);

  for (const file of files) {
    if (!fs.existsSync(file)) continue;
    const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
    const hardPattern = buildHardWordPattern();

    lines.forEach((line, index) => {
      const lineNumber = index + 1;

      hardPattern.lastIndex = 0;
      let match;
      while ((match = hardPattern.exec(line)) !== null) {
        fail(
          `${displayPath(file)}:${lineNumber} 命中文档红线词「${match[0]}」：${snippet(line, match.index)}`,
          '删除内部实现细节，改用公开能力描述。',
        );
        if (match[0].length === 0) hardPattern.lastIndex += 1;
      }

      for (const rule of CREDENTIAL_RULES) {
        for (const hit of rule.find(line)) {
          fail(
            `${displayPath(file)}:${lineNumber} 命中疑似凭据（${rule.label}）：${snippet(line, line.indexOf(hit))}`,
            '替换为 <YOUR_...>、*** 等占位符，不要写入真实值。',
          );
        }
      }
    });
  }

  if (report.failures.length === before) {
    report.passed.push(`红线：${files.size} 个文件未命中敏感词、凭据、邮箱或手机号`);
  }
}

/**
 * 统计 releases.md「全部版本」表格的数据行数。
 * 只统计同时含版本号与日期两列的行，忽略表头、分隔行与「最新 10 个版本」表。
 * @param {string} text releases.md 全文。
 * @returns {number} 数据行数；找不到「全部版本」章节时返回 0。
 */
function countReleaseVersionRows(text) {
  const lines = text.split(/\r?\n/);
  const start = lines.findIndex((line) => /^##\s+全部版本\s*$/.test(line));
  if (start === -1) return 0;

  let rows = 0;
  for (let index = start + 1; index < lines.length; index += 1) {
    const line = lines[index];
    if (/^##\s+/.test(line)) break;
    if (/^\|\s*[^|]+\s*\|\s*\d{4}-\d{2}-\d{2}\s*\|/.test(line)) rows += 1;
  }
  return rows;
}

/**
 * 检查手册目录残留文件与归档规模、截图说明。
 * @returns {void}
 */
function checkArtifactsAndArchives() {
  const before = report.failures.length;
  let commitItems = 0;
  let releaseRows = 0;

  const forbiddenDirs = new Set(['bin', 'obj', '__pycache__']);
  const forbiddenFiles = /\.(pyc|db|log)$/i;
  walk(MANUAL_DIR, (full, entry) => {
    if (entry.isDirectory() && forbiddenDirs.has(entry.name)) {
      fail(`${displayPath(full)} 不允许出现在 docs/manual 下`, '删除构建或运行残留目录。');
    }
    if (entry.isFile() && forbiddenFiles.test(entry.name)) {
      fail(`${displayPath(full)} 不允许出现在 docs/manual 下`, '删除日志、数据库或缓存文件。');
    }
  });

  const commitsFile = path.join(MANUAL_DIR, 'appendix', 'commits.md');
  if (!fs.existsSync(commitsFile)) {
    fail('docs/manual/appendix/commits.md 不存在', '重新生成提交归档。');
  } else {
    const lines = fs.readFileSync(commitsFile, 'utf8').split(/\r?\n/);
    commitItems = lines.filter((line) => /^- .*\b[0-9a-f]{7,}\b/.test(line)).length;
    if (commitItems < MIN_COMMIT_ITEMS) {
      fail(
        `docs/manual/appendix/commits.md 提交条目 ${commitItems} 条，少于要求的 ${MIN_COMMIT_ITEMS} 条`,
        '运行 scripts/docs/generate-release-archive.mjs 重新生成。',
      );
    }
  }

  const releasesFile = path.join(MANUAL_DIR, 'appendix', 'releases.md');
  if (!fs.existsSync(releasesFile)) {
    fail('docs/manual/appendix/releases.md 不存在', '重新生成发布归档。');
  } else {
    releaseRows = countReleaseVersionRows(fs.readFileSync(releasesFile, 'utf8'));
    if (releaseRows < MIN_RELEASE_ROWS) {
      fail(
        `docs/manual/appendix/releases.md 版本表 ${releaseRows} 行，少于要求的 ${MIN_RELEASE_ROWS} 行`,
        '运行 scripts/docs/generate-release-archive.mjs 重新生成。',
      );
    }
  }

  const imagesReadme = path.join(IMAGES_DIR, 'README.md');
  if (!fs.existsSync(imagesReadme)) {
    fail('docs/manual/images/README.md 不存在', '补充截图说明文件。');
  } else {
    const text = fs.readFileSync(imagesReadme, 'utf8');
    if (!text.includes('1366×768')) {
      fail('docs/manual/images/README.md 未写明 1366×768', '补充截图尺寸说明。');
    }
    if (!text.includes('https://ai.trueai.org')) {
      fail('docs/manual/images/README.md 未写明 https://ai.trueai.org', '补充截图来源站点。');
    }
    if (!/Swagger/i.test(text) || !/(例外|500|本地实例|已知限制)/.test(text)) {
      fail('docs/manual/images/README.md 未说明 Swagger 截图的例外情况', '补充线上 Swagger 失败与本地补采说明。');
    }
  }

  if (report.failures.length === before) {
    report.passed.push(
      `归档：commits.md ${commitItems} 条、releases.md 全部版本 ${releaseRows} 行，手册目录无残留文件，截图说明齐全`,
    );
  }
}

/**
 * 输出中文校验报告。
 * @returns {void}
 */
function printReport() {
  const divider = '='.repeat(72);
  console.log(divider);
  console.log('midjourney-proxy 文档校验报告');
  console.log(divider);

  console.log(`\n[通过] ${report.passed.length} 项`);
  for (const item of report.passed) console.log(`  - ${item}`);

  console.log(`\n[警告] ${report.warnings.length} 项`);
  for (const item of report.warnings) {
    console.log(`  - ${item.message}`);
    if (item.hint) console.log(`    建议：${item.hint}`);
  }

  console.log(`\n[失败] ${report.failures.length} 项`);
  for (const item of report.failures) {
    console.log(`  - ${item.message}`);
    if (item.hint) console.log(`    建议：${item.hint}`);
  }

  console.log('');
  if (report.failures.length === 0) {
    console.log('结论：全部硬性检查通过。');
  } else {
    console.log('结论：存在硬性失败，请按上述建议修复后重新运行。');
  }
}

checkLinks();
checkImages();
checkIndexJson();
checkReadmes();
checkRedLines();
checkArtifactsAndArchives();
printReport();

process.exit(report.failures.length === 0 ? 0 : 1);
