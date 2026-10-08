<?php
/**
 * DEVNANOTEK — Adminer'a tek tıkla giriş: go.php?db=mysql | pgsql | sqlite
 * BU DOSYA OTOMATİK ÜRETİLİR. Yalnız bu bilgisayardan açılır.
 */
function dn_env(string $k, string $def): string {
    $v = getenv($k);
    if ($v === false || $v === '') $v = $_SERVER[$k] ?? $_SERVER['REDIRECT_' . $k] ?? $def;
    return (string)$v;
}
$targets = [
    'mysql' => ['server', '127.0.0.1:' . dn_env('DEVNANOTEK_DB_PORT', '3306'), 'root', ''],
    'pgsql' => ['pgsql', '127.0.0.1:' . dn_env('DEVNANOTEK_PG_PORT', '5432'), 'postgres', 'postgres'],
    'sqlite' => ['sqlite', '', '', ''],
];
$k = $_GET['db'] ?? 'mysql';
if (!isset($targets[$k])) $k = 'mysql';
[$driver, $server, $user, $db] = $targets[$k];
if ($k === 'sqlite') { header('Location: ./?sqlite=&username=&db='); exit; }
?><!doctype html>
<html lang="tr"><head><meta charset="utf-8"><title>Adminer · DEVNANOTEK</title></head>
<body style="font:14px system-ui,Segoe UI,sans-serif;padding:24px">
<form id="f" method="post" action="./">
  <input type="hidden" name="auth[driver]" value="<?= htmlspecialchars($driver) ?>">
  <input type="hidden" name="auth[server]" value="<?= htmlspecialchars($server) ?>">
  <input type="hidden" name="auth[username]" value="<?= htmlspecialchars($user) ?>">
  <input type="hidden" name="auth[password]" value="">
  <input type="hidden" name="auth[db]" value="<?= htmlspecialchars($db) ?>">
  <input type="hidden" name="token" id="token" value="">
  <noscript><button type="submit">Adminer'a gir</button></noscript>
</form>
<script>
// Adminer giriş formu CSRF anahtarı ister: önce giriş sayfasından anahtar alınır, sonra form gönderilir
fetch('./', { credentials: 'same-origin' })
  .then(function (r) { return r.text(); })
  .then(function (t) {
    var m = t.match(/name=['"]token['"] value=['"]([^'"]+)['"]/);
    if (m) document.getElementById('token').value = m[1];
    document.getElementById('f').submit();
  })
  .catch(function () { document.getElementById('f').submit(); });
</script>
Adminer açılıyor…
</body></html>
