"""Render the Cultist pages into a directory that any static web server can serve."""
import argparse
from datetime import datetime
from html import escape
from itertools import groupby
import json
from pathlib import Path
from shutil import copytree


SOURCE = Path(__file__).resolve().parent
WORKSHOP = "https://steamcommunity.com/sharedfiles/filedetails/?id=3845153052"
GITHUB = "https://github.com/linyuye/RimWorld-Cultist"
MAIN = "https://yycoding.cc/"
LOG = "https://cultist.yycoding.cc/"
TYPES = {"release": "版本", "fix": "修复", "work": "开发", "test": "实测"}


def page(title, description, canonical, active, body, script=""):
    overview = "/cultist/" if active == "overview" else MAIN + "cultist/"
    log = "/" if active == "log" else LOG
    overview_active = ' aria-current="page"' if active == "overview" else ""
    log_active = ' aria-current="page"' if active == "log" else ""
    return f"""<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="theme-color" content="#0c1314">
  <meta name="description" content="{escape(description)}">
  <meta property="og:title" content="{escape(title)}">
  <meta property="og:description" content="{escape(description)}">
  <meta property="og:image" content="{canonical}assets/hero.webp">
  <link rel="canonical" href="{canonical}">
  <link rel="icon" href="assets/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="assets/site.css">
  <title>{escape(title)}</title>
  {script}
</head>
<body>
  <a class="skip" href="#main">跳到正文</a>
  <header class="shell bar">
    <a class="brand" href="{overview}"><span class="mark" aria-hidden="true">✦</span>cultist</a>
    <nav aria-label="主导航">
      <a class="nav-link" href="{overview}"{overview_active}>Mod 介绍</a>
      <a class="nav-link" href="{log}"{log_active}>更新日志</a>
    </nav>
  </header>
  <main class="shell" id="main">{body}</main>
  <footer class="shell footer">
    <span>cultist · RimWorld 1.7 Mod</span>
    <nav aria-label="相关链接"><a href="{MAIN}">yycoding.cc</a><a href="{GITHUB}">GitHub</a><a href="{WORKSHOP}">创意工坊</a></nav>
  </footer>
</body>
</html>
"""


def hero(kicker, title, note, primary_href, primary_text, secondary_href, secondary_text):
    return f"""
    <section class="hero" aria-labelledby="hero-title">
      <div class="hero-copy">
        <p class="kicker">{kicker}</p>
        <h1 id="hero-title">{title}</h1>
        <p>{note}</p>
        <div class="actions">
          <a class="pill primary" href="{primary_href}">{primary_text}</a>
          <a class="pill" href="{secondary_href}">{secondary_text}</a>
        </div>
      </div>
    </section>"""


def render_overview():
    body = hero(
        "RimWorld 1.7 · 密教模拟器扩展",
        "边缘世界的<br>密教结社",
        "从《夜游漫记》开始，在殖民地创立教团，入梦探索漫宿，招募追随者并举行飞升仪式。",
        WORKSHOP, "Steam 创意工坊", LOG, "查看更新日志",
    ) + f"""
    <section class="overview" aria-labelledby="play">
      <h2 id="play">进游戏可以做什么</h2>
      <div class="feature-grid">
        <article class="feature"><h3>创立教团</h3><p>选择教会性相，招募具名追随者，以秘传和仪式晋升门徒。</p></article>
        <article class="feature"><h3>入梦探索</h3><p>从林地进入漫宿，逐步解锁诸门，选择带回殖民地的影响与秘传。</p></article>
        <article class="feature"><h3>完成飞升</h3><p>推进长生者与使徒路线，决定教主飞升离开还是以长生者身份留在殖民地。</p></article>
      </div>
    </section>
    <section class="overview install" aria-labelledby="install">
      <h2 id="install">安装</h2>
      <ol>
        <li>启用 RimWorld 1.7 和文化（Ideology）DLC。</li>
        <li>订阅 Steam 创意工坊，或从 GitHub Releases 下载 Mod。</li>
        <li>启用 cultist，并将它排在文化 DLC 之后。</li>
      </ol>
      <a class="pill" href="{GITHUB}/releases">GitHub Releases</a>
    </section>"""
    return page("cultist · 环世界密教模拟器 Mod", "Cultist 为 RimWorld 加入密教结社、漫宿、秘传与飞升路线。", MAIN + "cultist/", "overview", body)


def date_label(day):
    date = datetime.strptime(day, "%Y-%m-%d").date()
    return f"{date.year} 年 {date.month} 月 {date.day} 日"


def render_entry(entry):
    kind = entry["kind"]
    stamp = datetime.fromisoformat(f'{entry["date"]}T{entry["time"]}:00+08:00')
    tag = f'v{entry["version"]}' if "version" in entry else TYPES[kind]
    items = "".join(f"<li>{escape(value)}</li>" for value in entry.get("items", []))
    details = f"<ul>{items}</ul>" if items else ""
    return f"""
        <article class="entry" data-kind="{kind}">
          <div class="entry-meta"><time datetime="{stamp.isoformat()}">{stamp:%H:%M}</time><span class="tag">{escape(tag)}</span></div>
          <div><h4>{escape(entry["title"])}</h4><p>{escape(entry["summary"])}</p>{details}</div>
        </article>"""


def render_log(entries):
    ordered = sorted(entries, key=lambda row: (row["date"], row["time"]), reverse=True)
    days = [(day, list(group)) for day, group in groupby(ordered, key=lambda row: row["date"])]
    date_links = "".join(f'<a data-date-link href="#day-{day}">{date_label(day)}</a>' for day, _ in days)
    day_sections = "".join(
        f'<section class="day" id="day-{day}" data-day aria-labelledby="heading-{day}">'
        f'<h3 id="heading-{day}"><time datetime="{day}">{date_label(day)}</time></h3>'
        f'<div class="entry-list">{"".join(map(render_entry, records))}</div></section>'
        for day, records in days
    )
    filters = "".join(
        f'<button type="button" data-filter="{kind}" aria-pressed="{str(kind == "all").lower()}">{label}</button>'
        for kind, label in {"all": "全部", **TYPES}.items()
    )
    body = hero(
        "cultist · RimWorld Mod",
        "更新日志",
        "按日期查看每次版本更新、玩法改动、修复与实机测试。",
        "#updates", "浏览更新", MAIN + "cultist/", "Mod 介绍",
    ) + f"""
    <div class="timeline" id="updates">
      <aside class="index"><h2>按日期</h2>{date_links}</aside>
      <section aria-labelledby="timeline-title">
        <div class="section-head">
          <h2 id="timeline-title">开发记录</h2>
          <div class="segment" role="group" aria-label="按更新类别筛选" data-filters hidden>{filters}</div>
        </div>
        <p class="section-note"><span data-status aria-live="polite">显示 {len(entries)} 条记录</span> · 北京时间</p>
        {day_sections}
      </section>
    </div>"""
    return page("cultist · 更新日志", "按日记录 cultist 的版本更新、开发改动、修复与实机测试。", LOG, "log", body, '<script src="assets/filter.js" defer></script>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    if output == SOURCE or SOURCE in output.parents and output.name == "assets":
        parser.error("choose a separate output directory")
    entries = json.loads((SOURCE / "updates.json").read_text(encoding="utf-8"))
    for subdir, html in (("log", render_log(entries)), ("overview", render_overview())):
        target = output / subdir
        target.mkdir(parents=True, exist_ok=True)
        copytree(SOURCE / "assets", target / "assets", dirs_exist_ok=True)
        (target / "index.html").write_text(html, encoding="utf-8")


if __name__ == "__main__":
    main()
