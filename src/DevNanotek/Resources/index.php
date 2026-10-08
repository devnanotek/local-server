<?php
/**
 * DEVNANOTEK — localhost açılış sayfası
 * Bu dosyayı silebilir veya değiştirebilirsiniz. (Ayarlar > Sistem > "Açılış sayfasını geri yükle" ile geri gelir.)
 */
if (isset($_GET['phpinfo'])) { phpinfo(); exit; }

function dn_env(string $k, string $def): string {
    $v = getenv($k);
    if ($v === false || $v === '') $v = $_SERVER[$k] ?? $_SERVER['REDIRECT_' . $k] ?? $def;
    return (string)$v;
}
$root     = __DIR__;
$dnRoot   = rtrim(str_replace('\\', '/', dn_env('DEVNANOTEK_ROOT', dirname(__DIR__))), '/');
$dbPort   = (int)dn_env('DEVNANOTEK_DB_PORT', '3306');
$pgPort   = (int)dn_env('DEVNANOTEK_PG_PORT', '5432');
$smtpPort = (int)dn_env('DEVNANOTEK_SMTP_PORT', '1025');
$mailPort = (int)dn_env('DEVNANOTEK_MAILPIT_PORT', '8025');
$host     = $_SERVER['HTTP_HOST'] ?? 'localhost';
$hostOnly = preg_replace('/:\d+$/', '', $host);
$https    = !empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off';
$isWin    = DIRECTORY_SEPARATOR === '\\';
$hasPma     = is_dir("$dnRoot/bin/phpmyadmin") || is_dir("$dnRoot/phpmyadmin");
$hasAdminer = is_dir("$dnRoot/bin/adminer") || is_dir("$dnRoot/adminer");

function dn_port_open(int $port): bool {
    $s = @fsockopen('127.0.0.1', $port, $no, $str, 0.3);
    if ($s) { fclose($s); return true; }
    return false;
}

// ---- MariaDB / MySQL ----
$db = ['ok' => false, 'ver' => '', 'msg' => ''];
if (!function_exists('mysqli_connect')) {
    $db['msg'] = 'PHP mysqli eklentisi kapalı';
} elseif (!dn_port_open($dbPort)) {
    $db['msg'] = 'Veritabanı çalışmıyor — DEVNANOTEK\'ten başlatın';
} else {
    mysqli_report(MYSQLI_REPORT_OFF);
    $c = @mysqli_connect('127.0.0.1', 'root', '', '', $dbPort);
    if ($c) { $db['ok'] = true; $db['ver'] = mysqli_get_server_info($c); mysqli_close($c); }
    elseif (mysqli_connect_errno() === 1045) { $db['msg'] = 'Çalışıyor · root şifresi tanımlı (phpMyAdmin\'de girin)'; $db['ok'] = null; }
    else { $db['msg'] = mysqli_connect_error() ?: 'Bağlanılamadı'; }
}

// ---- PostgreSQL (isteğe bağlı: yalnız port açıksa gösterilir) ----
$pg = null;
if (dn_port_open($pgPort)) {
    $pg = ['ok' => false, 'ver' => '', 'msg' => ''];
    if (function_exists('pg_connect')) {
        $pc = @pg_connect('host=127.0.0.1 port=' . $pgPort . ' user=postgres dbname=postgres connect_timeout=2');
        if ($pc) { $pg['ok'] = true; $pg['ver'] = pg_parameter_status($pc, 'server_version') ?: ''; pg_close($pc); }
        else { $pg['msg'] = 'Çalışıyor · bağlanılamadı'; }
    } else { $pg['msg'] = 'Çalışıyor · PHP pgsql eklentisi kapalı'; }
}
$mailUp = dn_port_open($smtpPort);

function dn_dirs(string $dir): array {
    $out = [];
    foreach (@scandir($dir) ?: [] as $d) {
        if ($d === '.' || $d === '..' || $d[0] === '.' || $d[0] === '_') continue;
        if (is_dir("$dir/$d")) $out[] = $d;
    }
    natcasesort($out);
    return array_values($out);
}
function dn_link(string $rel, string $abs): string {
    $hasIndex = is_file("$abs/index.php") || is_file("$abs/index.html") || is_file("$abs/index.htm");
    $enc = implode('/', array_map('rawurlencode', explode('/', $rel)));
    if (!$hasIndex && is_file("$abs/public/index.php")) return "/$enc/public/";
    return "/$enc/";
}
$projects = dn_dirs($root);
$ext = get_loaded_extensions(); sort($ext, SORT_FLAG_CASE | SORT_STRING);
$h = fn($s) => htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
?>
<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>DEVNANOTEK · localhost</title>
<link rel="icon" type="image/svg+xml" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='16' fill='%23232425'/%3E%3Cpath d='M16 14h17c11 0 18 7 18 18S44 50 33 50H16V14zm10 9v18h6c6 0 9-3 9-9s-3-9-9-9h-6z' fill='%23FAA41A'/%3E%3C/svg%3E">
<style>
  /* DEVNANOTEK paleti (devnanotek.net) — gündüz / gece */
  :root{--bg:#FAF9F7;--card:#FFFFFF;--line:#E7E1D5;--txt:#232425;--muted:#6E675B;--acc:#9A6400;--gold:#FAA41A;--chip:#F4F1EB;--ok:#22B573;--warn:#F5A524;--bad:#E5484D;--shadow:0 1px 2px rgba(35,36,37,.05)}
  @media (prefers-color-scheme: dark){:root{--bg:#1B1C1D;--card:#232425;--line:#333436;--txt:#F2EFE8;--muted:#A49B8C;--acc:#FFB43A;--chip:#2A2B2D;--shadow:none}}
  *{box-sizing:border-box}
  body{margin:0;font:15px/1.55 "Segoe UI",system-ui,-apple-system,Roboto,"Helvetica Neue",Arial,sans-serif;background:var(--bg);color:var(--txt);-webkit-font-smoothing:antialiased}
  a{color:var(--acc);text-decoration:none}
  a:hover{text-decoration:underline}
  .wrap{max-width:1120px;margin:0 auto;padding:28px 20px 56px}
  header{display:flex;align-items:center;gap:14px;flex-wrap:wrap;margin-bottom:26px}
  .mark{width:48px;height:48px;flex:none;display:block}
  .brand{display:flex;flex-direction:column;gap:6px;min-width:0}
  .wordmark{height:26px;width:auto;display:block}
  .wordmark .n{fill:var(--txt)}
  .sub{color:var(--muted);font-size:13px}
  .pill{display:inline-block;background:var(--chip);border:1px solid var(--line);border-radius:999px;padding:1px 9px;font-size:12px;color:var(--muted);margin-left:6px;vertical-align:1px}
  code{font:12.5px/1.4 "Cascadia Mono",Consolas,ui-monospace,monospace;background:var(--chip);border:1px solid var(--line);border-radius:6px;padding:1px 6px;word-break:break-all}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:14px}
  .card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px 18px;box-shadow:var(--shadow);min-width:0}
  .label{color:var(--muted);font-size:12.5px;font-weight:600;letter-spacing:.02em;margin-bottom:6px}
  .val{font-weight:600;font-size:16px;overflow-wrap:anywhere}
  .note{color:var(--muted);font-size:13px;margin-top:2px;overflow-wrap:anywhere}
  .dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:7px;vertical-align:1px;background:var(--bad)}
  .dot.ok{background:var(--ok)}.dot.warn{background:var(--warn)}
  .links{display:flex;flex-wrap:wrap;gap:6px;margin-top:10px}
  .btn{display:inline-flex;align-items:center;gap:6px;background:var(--chip);border:1px solid var(--line);border-radius:9px;padding:5px 11px;font-size:13px;font-weight:600;color:var(--acc)}
  .btn:hover{text-decoration:none;border-color:var(--gold)}
  h2{font-size:15px;margin:32px 0 12px;color:var(--muted);font-weight:600;display:flex;align-items:center;gap:8px}
  .proj{display:flex;flex-direction:column;gap:10px}
  .proj .name{display:flex;align-items:center;gap:9px;font-weight:700;font-size:15.5px;color:var(--txt)}
  .proj .name svg{flex:none;width:20px;height:20px;fill:var(--gold)}
  .chips{display:flex;flex-wrap:wrap;gap:6px}
  .chips a{background:var(--chip);border:1px solid var(--line);border-radius:999px;padding:2px 10px;font-size:12.5px}
  .empty{color:var(--muted);padding:28px;text-align:center;border:1px dashed var(--line);border-radius:14px;line-height:1.8}
  details.card summary{cursor:pointer;font-weight:600;list-style:none;display:flex;justify-content:space-between;align-items:center}
  details.card summary::-webkit-details-marker{display:none}
  details.card summary::after{content:"＋";color:var(--muted)}
  details[open].card summary::after{content:"－"}
  .ext{color:var(--muted);font-size:12.5px;line-height:1.9;margin-top:10px}
  footer{margin-top:40px;color:var(--muted);font-size:12.5px;display:flex;justify-content:space-between;flex-wrap:wrap;gap:8px}
</style>
</head>
<body>
<div class="wrap">
  <header>
    <svg class="mark" viewBox="0 0 64 64" aria-hidden="true"><rect width="64" height="64" rx="16" fill="#232425"/><path d="M16 14h17c11 0 18 7 18 18S44 50 33 50H16V14zm10 9v18h6c6 0 9-3 9-9s-3-9-9-9h-6z" fill="#FAA41A"/></svg>
    <div class="brand">
      <!-- DEVNANOTEK yazı logosu (Bebas Neue çizimi — yazı tipi kurulu olmasa da aynı görünür) -->
      <svg class="wordmark" viewBox="4 24 426 72" role="img" aria-label="DEVNANOTEK">
        <path fill="#FAA41A" d="M15.1,35L15.1,85 20.7,85C22.5,85 23.9,84.5 24.9,83.4 25.8,82.3 26.3,80.6 26.3,78.2L26.3,41.8C26.3,39.4 25.8,37.7 24.9,36.6 23.9,35.5 22.5,35 20.7,35L15.1,35z M4.1,25L20.9,25C26.4,25 30.5,26.5 33.2,29.4 35.9,32.3 37.3,36.6 37.3,42.3L37.3,77.7C37.3,83.4 35.9,87.7 33.2,90.6 30.5,93.5 26.4,95 20.9,95L4.1,95 4.1,25z M48.7,25L78.7,25 78.7,35 59.7,35 59.7,53.5 74.8,53.5 74.8,63.5 59.7,63.5 59.7,85 78.7,85 78.7,95 48.7,95 48.7,25z M86.1,25L97.2,25 104.4,79.3 104.6,79.3 111.8,25 121.9,25 111.3,95 96.7,95 86.1,25z"/>
        <path class="n" d="M131.2,25L145,25 155.7,66.9 155.9,66.9 155.9,25 165.7,25 165.7,95 154.4,95 141.2,43.9 141,43.9 141,95 131.2,95 131.2,25z M193.3,37.2L188.5,71.8 198.4,71.8 193.5,37.2 193.3,37.2z M186.4,25L201.3,25 212.7,95 201.7,95 199.7,81.1 199.7,81.3 187.2,81.3 185.2,95 175,95 186.4,25z M222,25L235.8,25 246.5,66.9 246.7,66.9 246.7,25 256.5,25 256.5,95 245.2,95 232,43.9 231.8,43.9 231.8,95 222,95 222,25z M284.6,34C280.8,34,278.9,36.3,278.9,40.9L278.9,79.1C278.9,83.7 280.8,86 284.6,86 288.4,86 290.3,83.7 290.3,79.1L290.3,40.9C290.3,36.3,288.4,34,284.6,34z M284.6,24C290,24 294.1,25.5 297,28.6 299.9,31.7 301.3,36 301.3,41.6L301.3,78.4C301.3,84 299.9,88.3 297,91.4 294.1,94.5 290,96 284.6,96 279.2,96 275.1,94.5 272.2,91.4 269.3,88.3 267.9,84 267.9,78.4L267.9,41.6C267.9,36 269.3,31.7 272.2,28.6 275.1,25.5 279.2,24 284.6,24z M309.8,25L343.8,25 343.8,35 332.3,35 332.3,95 321.3,95 321.3,35 309.8,35 309.8,25z M353.1,25L383.1,25 383.1,35 364.1,35 364.1,53.5 379.2,53.5 379.2,63.5 364.1,63.5 364.1,85 383.1,85 383.1,95 353.1,95 353.1,25z M393.4,25L404.4,25 404.4,54.5 418.4,25 429.4,25 416.3,50.7 429.6,95 418.1,95 408.8,63.8 404.4,72.7 404.4,95 393.4,95 393.4,25z"/>
      </svg>
      <div class="sub">Local Server <span class="pill"><?= $isWin ? 'Windows' : 'Linux' ?></span></div>
    </div>
  </header>

  <div class="grid">
    <div class="card">
      <div class="label">PHP</div>
      <div class="val"><?= PHP_VERSION ?></div>
      <div class="note"><?= $h(PHP_SAPI) ?> · <?= count($ext) ?> eklenti</div>
      <div class="links"><a class="btn" href="/?phpinfo=1">phpinfo()</a></div>
    </div>
    <div class="card">
      <div class="label">Web sunucu</div>
      <div class="val"><span class="dot ok"></span><?= $h(explode(' ', $_SERVER['SERVER_SOFTWARE'] ?? 'Web sunucu')[0]) ?></div>
      <div class="note"><?= $h($host) ?> · <?= $https ? 'HTTPS' : 'HTTP' ?></div>
    </div>
    <div class="card">
      <div class="label">MariaDB / MySQL · port <?= $dbPort ?></div>
      <div class="val"><span class="dot <?= $db['ok'] ? 'ok' : ($db['ok'] === null ? 'warn' : '') ?>"></span><?= $db['ok'] ? $h($db['ver']) : ($db['ok'] === null ? 'Çalışıyor' : 'Bağlanılamadı') ?></div>
      <div class="note"><?= $db['ok'] ? 'root kullanıcısı · şifresiz' : $h($db['msg']) ?></div>
      <div class="links">
        <?php if ($hasPma): ?><a class="btn" href="/phpmyadmin/">phpMyAdmin</a><?php endif; ?>
        <?php if ($hasAdminer): ?><a class="btn" href="/adminer/go.php?db=mysql">Adminer</a><?php endif; ?>
      </div>
    </div>
    <?php if ($pg !== null): ?>
    <div class="card">
      <div class="label">PostgreSQL · port <?= $pgPort ?></div>
      <div class="val"><span class="dot <?= $pg['ok'] ? 'ok' : 'warn' ?>"></span><?= $pg['ok'] ? $h($pg['ver']) : 'Çalışıyor' ?></div>
      <div class="note"><?= $pg['ok'] ? 'postgres kullanıcısı · şifresiz' : $h($pg['msg']) ?></div>
      <?php if ($hasAdminer): ?><div class="links"><a class="btn" href="/adminer/go.php?db=pgsql">Adminer</a></div><?php endif; ?>
    </div>
    <?php endif; ?>
    <div class="card">
      <div class="label">Test e-posta · Mailpit</div>
      <div class="val"><span class="dot <?= $mailUp ? 'ok' : 'warn' ?>"></span>SMTP 127.0.0.1:<?= $smtpPort ?></div>
      <div class="note"><?= $mailUp ? 'Giden e-postalar burada toplanır' : 'Mailpit çalışmıyor' ?></div>
      <div class="links"><a class="btn" href="http://<?= $h($hostOnly) ?>:<?= $mailPort ?>/" target="_blank" rel="noopener">Gelen kutusu</a></div>
    </div>
  </div>

  <h2>Projeler <code><?= $h($root) ?></code></h2>
  <?php if (!$projects): ?>
    <div class="empty">Henüz proje yok.<br>Belge kökünün içine bir klasör ekleyin: <code>GITHUB/proje1</code> → <a href="/GITHUB/proje1/">localhost/GITHUB/proje1</a></div>
  <?php else: ?>
  <div class="grid">
    <?php foreach ($projects as $p): $abs = "$root/$p"; $subs = dn_dirs($abs); ?>
      <div class="card proj">
        <a class="name" href="<?= $h(dn_link($p, $abs)) ?>"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M10 4l2 2h8a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h6z"/></svg><?= $h($p) ?></a>
        <?php if ($subs): ?>
        <div class="chips">
          <?php foreach (array_slice($subs, 0, 24) as $s): ?>
            <a href="<?= $h(dn_link("$p/$s", "$abs/$s")) ?>"><?= $h($s) ?></a>
          <?php endforeach; ?>
          <?php if (count($subs) > 24): ?><span class="note">+<?= count($subs) - 24 ?> daha</span><?php endif; ?>
        </div>
        <?php endif; ?>
      </div>
    <?php endforeach; ?>
  </div>
  <?php endif; ?>

  <h2>PHP</h2>
  <details class="card">
    <summary>Yüklü eklentiler (<?= count($ext) ?>)</summary>
    <div class="ext"><?= $h(implode(' · ', $ext)) ?></div>
  </details>

  <footer>
    <span>DEVNANOTEK Local Server</span>
    <a href="https://devnanotek.net" target="_blank" rel="noopener">devnanotek.net</a>
  </footer>
</div>
</body>
</html>
