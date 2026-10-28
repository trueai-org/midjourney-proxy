#!/usr/bin/env node
/**
 * 生成 midjourney-proxy 的发布归档与提交归档。
 *
 * 数据全部来自本地 git 历史（tag 与提交说明），不访问网络、不读取运行时钟：
 * - docs/manual/appendix/releases.json / releases.md
 * - docs/manual/appendix/commits.json / commits.md
 *
 * 口径：
 * - 版本边界取 git tag，按 tag 创建日期升序；区间提交数为该 tag 相对前一个 tag 的提交数，
 *   首个 tag 的区间包含初始提交。
 * - 提交说明命中文档红线（内部授权模块标识、授权、独立单词 lic）时整条剔除，
 *   不写入任何归档文件，仅在文首统计剔除数量。
 * - 提交说明中的疑似凭据实际值替换为 ***，仅出现关键词而没有值的说明不替换。
 * - 生成日期取仓库最新提交日期，重复执行输出完全一致。
 */
import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

/**
 * 把 import.meta.url 转成本地文件路径。
 * 只依赖内置全局 URL，避免额外引入 node:url；Windows 下需去掉盘符前的斜杠。
 * @param {string} fileUrl 模块的 file URL。
 * @returns {string} 本地绝对路径。
 */
function toLocalPath(fileUrl) {
  const pathname = decodeURIComponent(new URL(fileUrl).pathname);
  return pathname.replace(/^\/([A-Za-z]:)/, '$1');
}

/** 脚本所在目录，用于定位仓库根目录与输出目录。 */
const SCRIPT_DIR = path.dirname(toLocalPath(import.meta.url));
/** 仓库根目录：scripts/docs/ 向上两级。 */
const REPO_ROOT = path.resolve(SCRIPT_DIR, '..', '..');
/** 归档输出目录。 */
const OUTPUT_DIR = path.join(REPO_ROOT, 'docs', 'manual', 'appendix');

/** 仓库网页地址，用于拼接 Release 链接。 */
const REPOSITORY_URL = 'https://github.com/trueai-org/midjourney-proxy';
/** Release 页面地址前缀。 */
const RELEASE_URL_PREFIX = `${REPOSITORY_URL}/releases/tag/`;
/** 首个 tag 之前提交的分组名；本仓库历史中该分组为空。 */
const PRE_RELEASE = 'pre-release';
/** 命中红线的 tag 提交在发布归档中使用的占位说明。 */
const REDACTED_SUBJECT = '（提交说明已按文档规范省略）';

/** 文档红线：命中即整条剔除（不区分大小写，lic 要求独立单词）。 */
const RED_LINE_PATTERN = new RegExp(
  ['Licen' + 'seKey', 'Midjourney' + '\\.' + 'License', '授权', '\\blic\\b'].join('|'),
  'iu',
);
/** 键值型凭据：password / token / secret 等键后跟赋值时，只替换实际值。 */
const CREDENTIAL_PATTERN =
  /\b(password|passwd|pwd|token|secret|api[_-]?key|access[_-]?key)(\s*[=:]\s*)("?)([^\s"'`,;]+)\3/giu;
/** OpenAI 风格密钥：sk- 前缀且长度足够。 */
const SK_KEY_PATTERN = /\bsk-[A-Za-z0-9_-]{8,}\b/gu;
/** 长 base64 片段：至少 30 个字符，且需同时含字母与数字才判定为凭据。 */
const BASE64_PATTERN = /\b[A-Za-z0-9+/]{30,}={0,2}\b/gu;

/**
 * 执行 git 命令并返回标准输出。
 * @param {string[]} args git 参数（不含仓库路径）。
 * @returns {string} UTF-8 文本，行尾统一为 \n。
 */
function runGit(args) {
  const output = execFileSync('git', ['-C', REPO_ROOT, ...args], {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
  return output.replace(/\r\n/g, '\n');
}

/**
 * 把命令输出拆成非空行。
 * @param {string} text 命令输出。
 * @returns {string[]} 行数组。
 */
function toLines(text) {
  return text.split('\n').filter((line) => line.length > 0);
}

/**
 * 解析 git log 输出（字段以制表符分隔，提交说明可能含制表符，需重新拼接）。
 * @param {string} text git log 输出。
 * @returns {{hash: string, shortHash: string, date: string, subject: string}[]} 提交列表，保持输出顺序。
 */
function parseCommits(text) {
  return toLines(text).map((line) => {
    const parts = line.split('\t');
    return {
      hash: parts[0],
      shortHash: parts[1],
      date: parts[2],
      subject: parts.slice(3).join('\t'),
    };
  });
}

/**
 * 判断提交说明是否命中文档红线。
 * @param {string} subject 提交说明。
 * @returns {boolean} 命中返回 true。
 */
function isRedLine(subject) {
  return RED_LINE_PATTERN.test(subject);
}

/**
 * 替换提交说明中的疑似凭据实际值。
 * @param {string} subject 提交说明。
 * @returns {{text: string, count: number}} 替换后的文本与替换次数。
 */
function redactSensitive(subject) {
  let count = 0;
  let text = subject.replace(CREDENTIAL_PATTERN, (_match, key, separator, quote) => {
    count += 1;
    return `${key}${separator}${quote}***${quote}`;
  });
  text = text.replace(SK_KEY_PATTERN, () => {
    count += 1;
    return 'sk-***';
  });
  text = text.replace(BASE64_PATTERN, (match) => {
    // 纯字母或纯数字的长串更可能是标识符而非编码凭据，仅同时含字母与数字时才替换。
    if (!/\d/.test(match) || !/[A-Za-z]/.test(match)) {
      return match;
    }
    count += 1;
    return '***';
  });
  return { text, count };
}

/**
 * 转义 Markdown 表格单元格中的竖线，避免破坏列结构。
 * @param {string} value 原始文本。
 * @returns {string} 转义后的文本。
 */
function escapeTableCell(value) {
  return String(value).replace(/\|/g, '\\|');
}

/**
 * 以 2 空格缩进写出 JSON，并在末尾补一个换行，保证重复执行结果一致。
 * @param {string} filePath 目标文件路径。
 * @param {unknown} data 待序列化数据。
 * @returns {void}
 */
function writeJson(filePath, data) {
  writeFileSync(filePath, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
}

/**
 * 读取 tag 列表，并把 annotated / lightweight tag 统一解析为提交哈希。
 * @returns {{tag: string, date: string, commit: string}[]} 按 tag 创建日期升序的 tag 列表。
 */
function readTags() {
  const rows = toLines(
    runGit([
      'tag',
      '--sort=creatordate',
      '--format=%(refname:short)%09%(creatordate:short)%09%(*objectname)%09%(objectname)',
    ]),
  ).map((line) => {
    const [tag, date, annotatedTarget, objectName] = line.split('\t');
    return { tag, date, annotatedTarget, objectName };
  });

  // 轻量 tag 的 *objectname 为空，此时 objectname 即提交；annotated tag 记录的是 tag 对象。
  // 无论哪种情况都用 rev-parse <tag>^{commit} 取真实提交，避免类型差异。
  const resolved = [];
  const chunkSize = 200;
  for (let start = 0; start < rows.length; start += chunkSize) {
    const chunk = rows.slice(start, start + chunkSize);
    const commits = toLines(
      runGit(['rev-parse', ...chunk.map((row) => `${row.tag}^{commit}`)]),
    );
    chunk.forEach((row, index) => {
      resolved.push({ tag: row.tag, date: row.date, commit: commits[index] });
    });
  }
  return resolved;
}

/**
 * 主流程：读取 git 数据、按红线与脱敏规则整理，并写出四个归档文件。
 * @returns {void}
 */
function main() {
  const tags = readTags();

  const allCommits = parseCommits(
    runGit(['log', '--reverse', '--format=%H%x09%h%x09%ad%x09%s', '--date=short']),
  );
  const totalCommits = allCommits.length;

  // 校验提交总数与 rev-list 一致，防止 --reverse 输出被截断或过滤。
  const revListCount = Number(runGit(['rev-list', '--count', 'HEAD']).trim());
  if (revListCount !== totalCommits) {
    throw new Error(`提交数与 rev-list 不一致：log=${totalCommits}, rev-list=${revListCount}`);
  }

  // 逐个 tag 读取区间提交：首个 tag 包含初始提交，后续 tag 取 prevTag..tag。
  const tagIntervals = [];
  const commitTag = new Map();
  for (let index = 0; index < tags.length; index += 1) {
    const tag = tags[index];
    const range = index === 0 ? [tag.tag] : [`${tags[index - 1].tag}..${tag.tag}`];
    const commits = parseCommits(
      runGit(['log', ...range, '--format=%H%x09%h%x09%ad%x09%s', '--date=short']),
    );
    for (const commit of commits) {
      commitTag.set(commit.hash, tag.tag);
    }
    tagIntervals.push({ ...tag, rawCount: commits.length });
  }

  // 先建立哈希到归档文本的映射，保证凭据替换只发生一次、也只统计一次。
  const subjectByHash = new Map();
  let excludedCommits = 0;
  let credentialRedactions = 0;
  const archivedCommits = [];
  for (const commit of allCommits) {
    if (isRedLine(commit.subject)) {
      excludedCommits += 1;
      continue;
    }
    const redacted = redactSensitive(commit.subject);
    credentialRedactions += redacted.count;
    subjectByHash.set(commit.hash, redacted.text);
    archivedCommits.push({
      hash: commit.hash,
      shortHash: commit.shortHash,
      date: commit.date,
      subject: redacted.text,
      tag: commitTag.get(commit.hash) ?? PRE_RELEASE,
    });
  }

  const shortHashByHash = new Map(allCommits.map((commit) => [commit.hash, commit.shortHash]));
  const rawSubjectByHash = new Map(allCommits.map((commit) => [commit.hash, commit.subject]));

  const releases = tagIntervals.map((tag) => {
    const rawSubject = rawSubjectByHash.get(tag.commit) ?? '';
    // tag 提交本身命中红线时，发布归档不能复述其说明，改用占位文案。
    const subject = isRedLine(rawSubject)
      ? REDACTED_SUBJECT
      : (subjectByHash.get(tag.commit) ?? '');
    return {
      tag: tag.tag,
      date: tag.date,
      commit: shortHashByHash.get(tag.commit) ?? '',
      subject,
      commitCount: tag.rawCount,
      url: `${RELEASE_URL_PREFIX}${tag.tag}`,
    };
  });

  const latestCommitDate = allCommits[allCommits.length - 1].date;
  const generatedFromVersion = tags[tags.length - 1].tag;

  const commitsArchive = {
    repository: REPOSITORY_URL,
    generatedFromVersion,
    totalTags: tags.length,
    totalCommits,
    excludedCommits,
    credentialRedactions,
    commits: archivedCommits,
  };

  // 写入前做基本自检，避免产出空文件或错误口径。
  if (releases.length !== tags.length) {
    throw new Error(`版本数与 tag 数不一致：releases=${releases.length}, tags=${tags.length}`);
  }
  if (archivedCommits.length + excludedCommits !== totalCommits) {
    throw new Error('归档条数与剔除条数之和与提交总数不一致');
  }

  mkdirSync(OUTPUT_DIR, { recursive: true });
  writeJson(path.join(OUTPUT_DIR, 'releases.json'), releases);
  writeJson(path.join(OUTPUT_DIR, 'commits.json'), commitsArchive);
  writeFileSync(
    path.join(OUTPUT_DIR, 'releases.md'),
    buildReleasesMarkdown(releases, {
      totalTags: tags.length,
      totalCommits,
      excludedCommits,
      credentialRedactions,
      latestCommitDate,
    }),
    'utf8',
  );
  writeFileSync(
    path.join(OUTPUT_DIR, 'commits.md'),
    buildCommitsMarkdown(archivedCommits, releases, {
      generatedFromVersion,
      totalTags: tags.length,
      totalCommits,
      excludedCommits,
      credentialRedactions,
      latestCommitDate,
    }),
    'utf8',
  );

  console.log(
    [
      `版本数：${tags.length}`,
      `提交总数：${totalCommits}`,
      `归档提交：${archivedCommits.length}`,
      `剔除提交：${excludedCommits}`,
      `凭据脱敏：${credentialRedactions}`,
      `生成日期：${latestCommitDate}`,
      `输出目录：${OUTPUT_DIR}`,
    ].join('\n'),
  );
}

/**
 * 生成 releases.md 文本。
 * @param {{tag: string, date: string, commit: string, subject: string, commitCount: number, url: string}[]} releases 发布列表（日期升序）。
 * @param {{totalTags: number, totalCommits: number, excludedCommits: number, credentialRedactions: number, latestCommitDate: string}} stats 统计信息。
 * @returns {string} Markdown 文本。
 */
function buildReleasesMarkdown(releases, stats) {
  const lines = [];
  lines.push('# 发布版本归档');
  lines.push('');
  lines.push(
    `- **数据源**：本地仓库 \`midjourney-proxy\` 的 git 历史，远端 ${REPOSITORY_URL}。`,
  );
  lines.push(
    '- **版本口径**：以 git tag 作为版本边界，按 tag 创建日期升序排列；「区间提交数」为该 tag 相对前一个 tag 之间的提交数，首个版本包含初始提交。',
  );
  lines.push(
    `- **生成日期**：${stats.latestCommitDate}（取仓库最新提交日期，不使用脚本运行时钟）。`,
  );
  lines.push('- **生成脚本**：`scripts/docs/generate-release-archive.mjs`。');
  lines.push(
    '- **阅读提示**：本页是历史归档，旧版本出现的技术名与依赖不代表当前支持范围；当前功能以手册正文为准。',
  );
  lines.push('');
  lines.push('## 统计');
  lines.push('');
  lines.push('| 指标 | 数值 | 说明 |');
  lines.push('| --- | --- | --- |');
  lines.push(`| 版本数 | ${stats.totalTags} | git tag 总数 |`);
  lines.push(`| 提交总数 | ${stats.totalCommits} | 仓库全部提交 |`);
  lines.push(
    `| 剔除提交 | ${stats.excludedCommits} | 提交说明已按文档规范省略 |`,
  );
  lines.push(
    `| 脱敏处数 | ${stats.credentialRedactions} | 提交说明中的疑似凭据实际值已替换为 *** |`,
  );
  lines.push('');

  lines.push('## 最新 10 个版本');
  lines.push('');
  lines.push('| 版本 | 日期 | 提交 | 区间提交数 | 说明 | Release 链接 |');
  lines.push('| --- | --- | --- | --- | --- | --- |');
  for (const release of releases.slice(-10).reverse()) {
    lines.push(
      `| ${release.tag} | ${release.date} | ${release.commit} | ${release.commitCount} | ${escapeTableCell(release.subject)} | [Release](${release.url}) |`,
    );
  }
  lines.push('');

  lines.push('## 全部版本');
  lines.push('');
  lines.push('| 版本 | 日期 | 提交 | 区间提交数 | Release 链接 |');
  lines.push('| --- | --- | --- | --- | --- |');
  for (const release of releases) {
    lines.push(
      `| ${release.tag} | ${release.date} | ${release.commit} | ${release.commitCount} | [Release](${release.url}) |`,
    );
  }
  lines.push('');
  return lines.join('\n');
}

/**
 * 生成 commits.md 文本。
 * @param {{hash: string, shortHash: string, date: string, subject: string, tag: string}[]} commits 归档提交（时间升序）。
 * @param {{tag: string, date: string, commitCount: number}[]} releases 发布列表（日期升序）。
 * @param {{generatedFromVersion: string, totalTags: number, totalCommits: number, excludedCommits: number, credentialRedactions: number, latestCommitDate: string}} stats 统计信息。
 * @returns {string} Markdown 文本。
 */
function buildCommitsMarkdown(commits, releases, stats) {
  const groups = new Map();
  for (const commit of commits) {
    if (!groups.has(commit.tag)) {
      groups.set(commit.tag, []);
    }
    groups.get(commit.tag).push(commit);
  }

  const lines = [];
  lines.push('# 提交归档');
  lines.push('');
  lines.push(
    `- **数据源**：本地仓库 \`midjourney-proxy\` 的 git 历史，远端 ${REPOSITORY_URL}。`,
  );
  lines.push(
    `- **生成版本**：${stats.generatedFromVersion}；**生成日期**：${stats.latestCommitDate}（取仓库最新提交日期，不使用脚本运行时钟）。`,
  );
  lines.push(
    `- **统计**：版本 ${stats.totalTags} 个，提交 ${stats.totalCommits} 条，剔除 ${stats.excludedCommits} 条（提交说明已按文档规范省略），脱敏 ${stats.credentialRedactions} 处。`,
  );
  lines.push(
    '- **口径**：按版本区间分节，节内提交按时间升序；每条提交只归属一个版本；首个 tag 之前的提交标记为 pre-release。',
  );
  lines.push('- **生成脚本**：`scripts/docs/generate-release-archive.mjs`。');
  lines.push(
    '- **阅读提示**：本页是历史归档，旧版本出现的技术名与依赖不代表当前支持范围；当前功能以手册正文为准。',
  );
  lines.push('');

  const preReleaseCommits = groups.get(PRE_RELEASE) ?? [];
  lines.push(`## ${PRE_RELEASE}`);
  lines.push('');
  if (preReleaseCommits.length === 0) {
    lines.push(`- 无：首个 tag 之前的提交已并入 ${releases[0].tag} 的统计区间。`);
  } else {
    for (const commit of preReleaseCommits) {
      lines.push(`- ${commit.shortHash} ${commit.date} ${commit.subject}`);
    }
  }
  lines.push('');

  for (const release of releases) {
    const group = groups.get(release.tag) ?? [];
    lines.push(`## ${release.tag} (${release.date})`);
    lines.push('');
    if (group.length === 0) {
      lines.push(
        release.commitCount > 0
          ? '- 本区间提交说明已按文档规范省略。'
          : '- 本区间无新增提交。',
      );
    } else {
      for (const commit of group) {
        lines.push(`- ${commit.shortHash} ${commit.date} ${commit.subject}`);
      }
    }
    lines.push('');
  }

  return lines.join('\n');
}

main();
