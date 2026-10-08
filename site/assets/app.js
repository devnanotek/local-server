/* DEVNANOTEK Local Server — proje sitesi
   - Tema: otomatik / gündüz / gece (localStorage: dn-theme)
   - Dil: Türkçe / English (localStorage: dn-lang)
   - Depo adı adresten bulunur: kullanici.github.io/depo → kullanici/depo
   - Sürüm, boyut ve indirme sayısı GitHub API'den; olmazsa CHANGELOG.md'den */
(function () {
  'use strict';

  var FALLBACK_REPO = 'devnanotek/local-server';
  var EXE = 'DevNanotek.exe';
  var root = document.documentElement;

  function store(k, v) { try { if (v === undefined) return localStorage.getItem(k); localStorage.setItem(k, v); } catch (e) { return null; } }
  function $(s, el) { return (el || document).querySelector(s); }
  function $$(s, el) { return Array.prototype.slice.call((el || document).querySelectorAll(s)); }
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }

  /* ---------------- Depo ---------------- */
  var repo = (function () {
    var h = location.hostname, m = /^([a-z0-9-]+)\.github\.io$/i.exec(h);
    if (!m) return FALLBACK_REPO;
    var seg = location.pathname.split('/').filter(Boolean)[0];
    if (!seg || /\.html?$/i.test(seg)) return m[1] + '/' + m[1] + '.github.io';
    return m[1] + '/' + seg;
  })();
  var repoUrl = 'https://github.com/' + repo;
  var latestDl = repoUrl + '/releases/latest/download/';

  $$('[data-repo-link]').forEach(function (a) { a.href = repoUrl + a.getAttribute('data-repo-link'); });
  $('#dlWin').href = latestDl + EXE;
  $('#lxCmd').textContent = 'curl -fsSLO ' + latestDl + 'devnanotek.sh && sudo bash devnanotek.sh install';

  /* ---------------- Tema ---------------- */
  var mq = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  function isDark() {
    var t = root.getAttribute('data-theme');
    return t ? t === 'dark' : !!(mq && mq.matches);
  }
  function paintTheme() {
    var dark = isDark();
    root.setAttribute('data-dark', dark ? '1' : '0');
    $$('.theme-img').forEach(function (img) {
      var src = img.getAttribute(dark ? 'data-dark' : 'data-light');
      if (src && img.getAttribute('src') !== src) img.setAttribute('src', src);
    });
    var meta = $('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', dark ? '#1B1C1D' : '#FAF9F7');
  }
  $('#themeBtn').addEventListener('click', function () {
    var next = isDark() ? 'light' : 'dark';
    // sistem temasıyla aynıysa elle seçimi kaldır (otomatik kalsın)
    var sys = mq && mq.matches ? 'dark' : 'light';
    if (next === sys) { root.removeAttribute('data-theme'); store('dn-theme', 'auto'); }
    else { root.setAttribute('data-theme', next); store('dn-theme', next); }
    paintTheme();
  });
  if (mq) {
    var onSys = function () { if (!root.getAttribute('data-theme')) paintTheme(); };
    if (mq.addEventListener) mq.addEventListener('change', onSys); else if (mq.addListener) mq.addListener(onSys);
  }

  /* ---------------- Dil ---------------- */
  var EN = {
    'skip': 'Skip to content',
    'nav.features': 'Features', 'nav.shots': 'Screenshots', 'nav.linux': 'Linux', 'nav.releases': 'Releases', 'nav.guide': 'Guide',
    'hero.pill': 'Free · Open source (MIT)',
    'hero.title': 'A <em>local PHP server</em> for Windows and Linux',
    'hero.lead': 'A replacement for XAMPP and Laragon: multiple PHP versions, Apache or Nginx, MariaDB, MySQL, PostgreSQL, phpMyAdmin, a test mailbox and HTTPS. One file, a few clicks to set up, no license nags.',
    'hero.win': 'Download for Windows', 'hero.linux': 'Install on Linux',
    'hero.req': 'Windows 10 / 11 · 64-bit, 32-bit, ARM64 · needs administrator rights and an internet connection',
    'stats.php': 'PHP versions (7.4–8.5)', 'stats.db': 'database engines', 'stats.web': 'web servers', 'stats.linux': 'Linux distributions',
    'feat.h': 'What\'s inside?',
    'feat.sub': 'Every component is downloaded from its official source and runs as a real Windows service, so your sites stay up even when the app is closed.',
    'f1.h': 'Multiple PHP versions', 'f1.p': 'As many versions as you like, from PHP 7.4 to 8.5; switch with one click. Extensions such as gd, zip and intl are simple on/off switches.',
    'f2.h': 'Apache or Nginx', 'f2.p': '.htaccess and mod_rewrite (same as cPanel / Plesk). Sub-folders: httpdocs\\GITHUB\\project1 → localhost/GITHUB/project1.',
    'f3.h': 'Every SQL database', 'f3.p': 'MariaDB or MySQL, plus PostgreSQL, SQLite and the SQL Server driver. phpMyAdmin, and Adminer to manage them all.',
    'f4.h': 'HTTPS without warnings', 'f4.p': 'With mkcert, https://localhost and project.test domains open with a green padlock.',
    'f5.h': 'Test mailbox', 'f5.p': 'Mailpit: e-mails sent by PHP never reach real recipients; you read them in the browser.',
    'f6.h': 'Linux compatibility check', 'f6.p': 'Scans your project before you deploy: letter case, backslashes, C:\\ paths, table names, php_value in .htaccess.',
    'f7.h': 'Keeps itself up to date', 'f7.p': 'Finds new PHP, MariaDB, Node.js… releases and new DEVNANOTEK versions, shows the RECOMMENDED one and updates with one click.',
    'f8.h': 'Colour status, tray icon', 'f8.p': 'Services are green / yellow / red. Start, stop and see versions from the icon next to the clock.',
    'f9.h': 'Repair and clean removal', 'f9.p': 'Four levels from quick repair to factory reset. Uninstalling leaves no services, hosts entries, PATH, certificates or firewall rules behind.',
    'f10.h': 'Node.js and Composer', 'f10.p': 'node, npm, npx, composer, mysql and psql are ready in the terminal. Great for socket.io and Vue/React builds.',
    'f11.h': 'English / Turkish, simple interface', 'f11.p': 'Opens in English or Turkish to match your Windows language; switch any time in Settings. Light / dark theme, explanations behind ? icons and a setup wizard.',
    'f12.h': 'One file, any PC', 'f12.p': 'A single exe that detects 64-bit, 32-bit and ARM64 by itself. Nothing else to install (.NET 4.8 ships with Windows).',
    'shots.h': 'Screenshots',
    'shots.t1': 'Overview', 'shots.t2': 'Versions', 'shots.t3': 'PHP extensions', 'shots.t4': 'Databases', 'shots.t5': 'Linux check', 'shots.t6': 'Setup wizard', 'shots.t7': 'Tray window',
    'lx.h': 'The same stack on Linux',
    'lx.p': 'Installs with a single command, then you manage it with the <code>devnanotek</code> command: switch PHP versions, enable extensions, add domains, take backups, uninstall.',
    'lx.note': 'Run it as your normal user (with sudo). Projects live in /opt/devnanotek/httpdocs, with a ~/httpdocs shortcut.',
    'copy': 'Copy', 'copied': 'Copied ✓',
    'rel.h': 'Releases',
    'rel.sub': 'The installed app notices new releases by itself and updates with one click. All files and SHA256 checksums are on GitHub Releases.',
    'rel.loading': 'Loading releases…', 'rel.all': 'All releases and files →',
    'rel.latest': 'Latest', 'rel.notes': 'Release notes', 'rel.none': 'No release has been published yet. You can build it from source in the meantime.',
    'rel.downloads': 'downloads',
    'faq.h': 'FAQ',
    'q1': '"Windows protected your PC" warning', 'a1': 'The app is free, so it has no paid code-signing certificate. Click "More info → Run anyway". You can verify the file with the SHA256 checksum on the release page.',
    'q2': 'The app or Apache won\'t start on Windows 11', 'a2': 'Smart App Control blocks unsigned programs. The app detects it and shows how to turn it off. It is a security setting; the decision is yours.',
    'q3': 'Can it run alongside XAMPP or Laragon?', 'a3': 'Not at the same time, because they use the same ports (80, 443, 3306). The app shows the conflict; stop the other one or change the port in Settings.',
    'q4': 'Where are my projects and databases?', 'a4': 'Projects are in C:\\devnanotek\\httpdocs, databases in C:\\devnanotek\\data. Both can be kept when you uninstall.',
    'q5': 'Will my project work on a Linux server (Plesk / cPanel)?', 'a5': 'Projects → pick the folder → Linux compatibility → Check. It lists, line by line, what would break on the server and how to fix it.',
    'q6': 'Is it paid? Can I use it for commercial work?', 'a6': 'Completely free and open source under the MIT license: use it freely for personal and commercial projects.',
    'foot.lic': 'MIT license', 'foot.issue': 'Report a bug'
  };
  var TR = {}; // sayfadaki Türkçe metinler (ilk açılışta toplanır)
  var SHOTS = {
    dashboard: { tr: 'Genel Bakış: servisler, sürümler ve hızlı düğmeler tek ekranda.', en: 'Overview: services, versions and quick actions on one screen.' },
    versions: { tr: 'Sürümler: her bileşenin ÖNERİLEN sürümü, tek tıkla kurma / güncelleme / kaldırma.', en: 'Versions: the RECOMMENDED release of each component, one-click install / update / remove.' },
    'php-extensions': { tr: 'PHP eklentileri: gd, zip, intl, pgsql… anahtarla aç / kapat.', en: 'PHP extensions: toggle gd, zip, intl, pgsql… with a switch.' },
    databases: { tr: 'Veritabanları: MariaDB / MySQL ve PostgreSQL; yedek, geri yükleme, Adminer.', en: 'Databases: MariaDB / MySQL and PostgreSQL; backup, restore, Adminer.' },
    'linux-check': { tr: 'Linux uyumluluk denetimi: sunucuda hata verecek satırlar ve çözümleri.', en: 'Linux compatibility check: lines that would break on the server, with fixes.' },
    setup: { tr: 'İlk açılışta kurulum sihirbazı: önerilen sürümler hazır seçili gelir.', en: 'First-run setup wizard: recommended versions come pre-selected.' },
    tray: { tr: 'Saatin yanındaki simgeden açılan küçük pencere.', en: 'The small window that opens from the icon next to the clock.' }
  };
  var lang = root.getAttribute('lang') === 'en' ? 'en' : 'tr';
  $$('[data-i18n]').forEach(function (el) { TR[el.getAttribute('data-i18n')] = el.innerHTML; });
  TR['copy'] = TR['copy'] || 'Kopyala';
  TR['copied'] = 'Kopyalandı ✓';
  TR['rel.latest'] = 'En yeni'; TR['rel.notes'] = 'Sürüm notları';
  TR['rel.none'] = 'Henüz yayınlanmış sürüm yok. Bu arada kaynak koddan derleyebilirsiniz.';
  TR['rel.downloads'] = 'indirme';
  var titles = {
    tr: [document.title, ($('meta[name="description"]') || {}).content],
    en: ['DEVNANOTEK Local Server — free local PHP server for Windows and Linux',
         'A free, open-source alternative to XAMPP and Laragon: multiple PHP versions, Apache/Nginx, MariaDB, MySQL, PostgreSQL, phpMyAdmin, Adminer, Mailpit, HTTPS. Windows and Linux.']
  };
  function t(k) { return (lang === 'en' ? EN[k] : TR[k]) || TR[k] || EN[k] || k; }
  function paintLang() {
    root.setAttribute('lang', lang);
    $$('[data-i18n]').forEach(function (el) { var v = t(el.getAttribute('data-i18n')); if (v != null) el.innerHTML = v; });
    document.title = titles[lang][0];
    var md = $('meta[name="description"]'); if (md && titles[lang][1]) md.setAttribute('content', titles[lang][1]);
    $$('[data-guide]').forEach(function (a) { a.setAttribute('href', lang === 'en' ? 'guide.html' : 'kilavuz.html'); });
    $('#langBtn').textContent = lang === 'en' ? 'TR' : 'EN';
    $('#langBtn').title = lang === 'en' ? 'Türkçe' : 'English';
    $('#themeBtn').title = lang === 'en' ? 'Light / dark' : 'Gündüz / gece';
    showShot(currentShot);
    if (relData) renderReleases(relData);
  }
  $('#langBtn').addEventListener('click', function () { lang = lang === 'en' ? 'tr' : 'en'; store('dn-lang', lang); paintLang(); });

  /* ---------------- Ekran görüntüleri ---------------- */
  var currentShot = 'dashboard';
  function showShot(key) {
    currentShot = key;
    var img = $('#shotImg');
    img.setAttribute('data-light', 'assets/img/' + key + '-light.png');
    img.setAttribute('data-dark', 'assets/img/' + key + '-dark.png');
    var s = SHOTS[key] || { tr: '', en: '' };
    img.alt = s[lang];
    $('#shotCap').textContent = s[lang];
    $$('.tab').forEach(function (b) { var on = b.getAttribute('data-shot') === key; b.classList.toggle('on', on); b.setAttribute('aria-selected', on ? 'true' : 'false'); });
    paintTheme();
  }
  $$('.tab').forEach(function (b) { b.addEventListener('click', function () { showShot(b.getAttribute('data-shot')); }); });

  // büyük görsel
  var lb = $('#lightbox');
  function openLb(src, alt) { $('img', lb).src = src; $('img', lb).alt = alt || ''; lb.hidden = false; }
  $$('.hero-shot img, .shot img').forEach(function (img) { img.addEventListener('click', function () { openLb(img.currentSrc || img.src, img.alt); }); });
  lb.addEventListener('click', function () { lb.hidden = true; });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') lb.hidden = true; });

  /* ---------------- Kopyala ---------------- */
  $$('[data-copy]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var text = $(btn.getAttribute('data-copy')).textContent;
      var done = function () { btn.textContent = t('copied'); setTimeout(function () { btn.textContent = t('copy'); }, 1800); };
      if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback); else fallback();
      function fallback() {
        var ta = document.createElement('textarea'); ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select(); try { document.execCommand('copy'); done(); } catch (e) {} document.body.removeChild(ta);
      }
    });
  });

  /* ---------------- Sürümler ---------------- */
  var relData = null;

  function fmtSize(b) { return b > 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(b / 1024)) + ' KB'; }
  function fmtDate(s) {
    var d = new Date(s); if (isNaN(d)) return s || '';
    try { return d.toLocaleDateString(lang === 'en' ? 'en-GB' : 'tr-TR', { day: 'numeric', month: 'long', year: 'numeric' }); } catch (e) { return d.toISOString().slice(0, 10); }
  }
  function fmtNum(n) { try { return n.toLocaleString(lang === 'en' ? 'en-GB' : 'tr-TR'); } catch (e) { return String(n); } }

  // Küçük ve güvenli Markdown: önce kaçışlanır, sonra başlık / (iç içe) liste / kalın / kod / bağlantı
  function md(text) {
    var out = [], depth = 0;
    function inline(s) {
      return esc(s)
        .replace(/`([^`]+)`/g, '<code>$1</code>')
        .replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>')
        .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');
    }
    function closeTo(d) { while (depth > d) { out.push('</li></ul>'); depth--; } }
    String(text || '').replace(/\r/g, '').split('\n').forEach(function (line) {
      var m = /^(\s*)[-*]\s+(.*)$/.exec(line);
      if (m) {
        var d = Math.min(3, Math.floor(m[1].length / 2) + 1);
        if (d > depth) { while (depth < d) { out.push('<ul>'); depth++; } }
        else { closeTo(d); out.push('</li>'); }
        out.push('<li>' + inline(m[2]));
        return;
      }
      if (!line.trim()) return;
      closeTo(0);
      if (/^\s*(-{3,}|\*{3,})\s*$/.test(line)) { out.push('<hr>'); return; }
      if ((m = /^#{1,6}\s+(.*)$/.exec(line))) { out.push('<h4>' + inline(m[1]) + '</h4>'); return; }
      out.push('<p>' + inline(line) + '</p>');
    });
    closeTo(0);
    return out.join('');
  }

  function setHeroVersion(ver, exeAsset) {
    if (ver) $$('[data-ver]').forEach(function (el) { el.textContent = 'v' + ver; });
    if (exeAsset) {
      $$('[data-size]').forEach(function (el) { el.textContent = EXE + ' · ' + fmtSize(exeAsset.size); });
      $('#dlWin').href = exeAsset.browser_download_url;
    }
  }

  function renderReleases(list) {
    var box = $('#releases');
    if (!list.length) { box.innerHTML = '<p class="muted">' + esc(t('rel.none')) + ' <a href="' + repoUrl + '#derleme" target="_blank" rel="noopener">GitHub →</a></p>'; return; }
    box.innerHTML = list.map(function (r, i) {
      var assets = (r.assets || []).map(function (a) {
        return '<a href="' + esc(a.url) + '"' + (a.external ? ' target="_blank" rel="noopener"' : '') + '>⬇ ' + esc(a.name) +
          (a.size ? ' <small>' + fmtSize(a.size) + (a.count != null ? ' · ' + fmtNum(a.count) + ' ' + esc(t('rel.downloads')) : '') + '</small>' : '') + '</a>';
      }).join('');
      var body = md(r.body);
      return '<article class="rel"><div class="rel-head"><h3>v' + esc(r.version) + '</h3>' +
        (i === 0 ? '<span class="badge">' + esc(t('rel.latest')) + '</span>' : '') +
        (r.date ? '<span class="rel-date">' + esc(fmtDate(r.date)) + '</span>' : '') + '</div>' +
        (assets ? '<div class="rel-assets">' + assets + '</div>' : '') +
        (body ? (i === 0 ? '<div class="rel-body">' + body + '</div>' : '<details><summary>' + esc(t('rel.notes')) + '</summary><div class="rel-body">' + body + '</div></details>') : '') +
        '</article>';
    }).join('');
  }

  function fromGitHub(arr) {
    return arr.filter(function (r) { return !r.draft && !r.prerelease; }).slice(0, 6).map(function (r) {
      var v = String(r.tag_name || '').replace(/^v/i, '');
      return {
        version: v, date: r.published_at, body: r.body,
        assets: (r.assets || []).filter(function (a) { return !/\.txt$/i.test(a.name) || /SHA256/i.test(a.name); })
          .map(function (a) { return { name: a.name, url: a.browser_download_url, size: a.size, count: a.download_count, raw: a }; })
      };
    });
  }

  // CHANGELOG.md yedeği (API sınırı aşıldığında veya henüz sürüm yoksa).
  // withLinks: GitHub'da sürüm olduğu biliniyorsa her sürüme indirme bağlantısı eklenir.
  function fromChangelog(text, withLinks) {
    var list = [], cur = null;
    text.replace(/\r/g, '').split('\n').forEach(function (line) {
      var m = /^##\s+\[?v?(\d+\.\d+\.\d+)\]?(?:\s*[-–]\s*(\d{4}-\d{2}-\d{2}))?/.exec(line);
      if (m) { cur = { version: m[1], date: m[2] || '', body: '', assets: [] }; list.push(cur); return; }
      if (cur && !/^\[.*\]:\s/.test(line)) cur.body += line + '\n';
    });
    // eski sürümlerin GitHub'da dosyası olmayabilir: bağlantı yalnızca en yeniye (latest) verilir
    return list.slice(0, 6).map(function (r, i) {
      if (withLinks && i === 0) r.assets = [{ name: EXE, url: latestDl + EXE }];
      return r;
    });
  }

  function loadChangelog(withLinks) {
    fetch('CHANGELOG.md', { cache: 'no-cache' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
      .then(function (text) {
        var list = fromChangelog(text, withLinks);
        relData = list;
        if (list.length) setHeroVersion(list[0].version, null);
        renderReleases(list);
      })
      .catch(function () { relData = []; renderReleases([]); });
  }

  function loadReleases() {
    fetch('https://api.github.com/repos/' + repo + '/releases?per_page=10', { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (arr) {
        var list = fromGitHub(arr);
        if (!list.length) { loadChangelog(false); return; } // depo var, sürüm henüz yayınlanmamış
        relData = list;
        var exe = null;
        (list[0].assets || []).forEach(function (a) { if (a.name === EXE) exe = a.raw; });
        setHeroVersion(list[0].version, exe);
        renderReleases(list);
      })
      .catch(function () { loadChangelog(true); }); // API sınırı / ağ hatası
  }

  paintTheme();
  paintLang();
  loadReleases();
})();
